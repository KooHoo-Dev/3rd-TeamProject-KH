#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using TerrainCollapse;

namespace Character
{
    // 에디터에서 붕괴 상호작용을 시험하기 위한 개발용 2D 플레이어 제어

    /// <summary>Kinematic Rigidbody를 직접 이동시키는 붕괴 테스트용 플레이어</summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D), typeof(SpriteRenderer))]
    public sealed class TestPlayerController : MonoBehaviour, IFallingTerrainEscapeTarget
    {
        [SerializeField] private float moveSpeed = 6f;
        [SerializeField] private float jumpSpeed = 8f;
        [SerializeField] private float climbSpeed = 3f; // ★ [추가] 매달리기/등반 이동 속도
        [SerializeField] private float gravity = 24f;
        [SerializeField] private float collisionSkin = 0.02f;
        [SerializeField] private float escapeProbeStep = 0.05f;
        [SerializeField, Min(0.01f)] private float escapePushSpeed = 4f;
        [SerializeField] private LayerMask collisionMask = ~0;
        [SerializeField] private TerrainManager terrainManager;

        [Header("Burial")] [SerializeField] private float burialDamageInterval = 1f;
        [SerializeField] private float burialDamagePerTick = 1f;
        [SerializeField] private int normalSortingOrder = 2;
        [SerializeField] private int buriedSortingOrder = -1;

        private readonly RaycastHit2D[] castHits = new RaycastHit2D[16];
        private readonly Collider2D[] overlapHits = new Collider2D[16];
        private readonly Dictionary<long, FallingTerrainContactResult> resolvedCollapses = new();
        private Rigidbody2D body;
        private BoxCollider2D box;
        private SpriteRenderer spriteRenderer;
        private Vector2 velocity;
        private bool grounded;
        private bool buried;
        private bool isEscaping;
        private float burialDamageElapsed;
        private float escapeTargetBodyX;
        private Vector2 burialLockedBodyPosition;
        private Coroutine burialRecheckRoutine;
        private bool IsClimb = false;

        public bool IsBuried => buried; // 현재 지형에 매몰된 상태 여부
        public event Action<bool> BurialStateChanged; // 매몰 상태 진입 및 해제 알림
        public event Action<float> BurialDamageRequested; // 향후 체력 시스템이 구독할 매몰 지속 피해 요청

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            box = GetComponent<BoxCollider2D>();
            spriteRenderer = GetComponent<SpriteRenderer>();

            if (terrainManager == null) terrainManager = FindFirstObjectByType<TerrainManager>();

            body.bodyType = RigidbodyType2D.Kinematic;
            body.useFullKinematicContacts = true;
            body.gravityScale = 0f;
            body.freezeRotation = true;
            spriteRenderer.sortingOrder = normalSortingOrder;
        }

        private void OnEnable()
        {
            if (terrainManager != null) terrainManager.TerrainChanged += HandleTerrainChanged;
        }

        private void OnDisable()
        {
            if (terrainManager != null) terrainManager.TerrainChanged -= HandleTerrainChanged;
        }

        private void FixedUpdate()
        {
            if (buried)
            {
                LockBurialPosition();
                TickBurialDamage();

                return;
            }

            if (terrainManager != null && terrainManager.IsReady &&
                IsGridSpaceEmpty(box.bounds) == false)
            {
                Debug.LogWarning(
                    "[Terrain Collapse] Occupied terrain overlap detected before player movement",
                    this);
                EnterBuriedState();

                return;
            }

            if (isEscaping)
            {
                TickEscapeMovement();

                return;
            }

            Keyboard keyboard = Keyboard.current;

            // ★ [추가] W 키를 누르면 매달리기(등반) 모드 진입 판정
            if (keyboard != null && keyboard.wKey.isPressed)
            {
                IsClimb = true;
            }

            // ★ [추가] 등반 상태일 때의 이동 처리
            if (IsClimb)
            {
                TickClimbMovement(keyboard);
                return;
            }

            // --- 기존 좌우 이동 및 점프 로직 ---
            float input = keyboard == null
                ? 0f
                : (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
            velocity.x = input * moveSpeed;

            if (keyboard != null && keyboard.spaceKey.isPressed && grounded) velocity.y = jumpSpeed;

            velocity.y -= gravity * Time.fixedDeltaTime;

            Vector2 position = body.position;
            position += ResolveMovement(Vector2.right, velocity.x * Time.fixedDeltaTime, ref velocity.x);
            grounded = false;
            position += ResolveMovement(Vector2.up, velocity.y * Time.fixedDeltaTime, ref velocity.y);
            body.MovePosition(position);
        }

        /// <summary>★ [추가] 배경에 매달린(등반) 상태에서의 자유 이동 처리</summary>
        private void TickClimbMovement(Keyboard keyboard)
        {
            if (keyboard == null) return;

            // Space 키 입력 시 매달리기를 해제하고 위로 점프
            if (keyboard.spaceKey.isPressed)
            {
                IsClimb = false;
                velocity.y = jumpSpeed;
                return;
            }

            // 상하좌우 입력을 받아 자유롭게 이동 (중력 무시)
            float inputX = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
            float inputY = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);

            velocity.x = inputX * climbSpeed;
            velocity.y = inputY * climbSpeed;

            Vector2 position = body.position;
            position += ResolveMovement(Vector2.right, velocity.x * Time.fixedDeltaTime, ref velocity.x);
            position += ResolveMovement(Vector2.up, velocity.y * Time.fixedDeltaTime, ref velocity.y);

            body.MovePosition(position);

            // 지면에 닿은 상태에서 S(아래) 키를 누르면 매달리기 해제
            if (grounded && inputY < 0f)
            {
                IsClimb = false;
            }
        }

        /// <summary>일반 입력을 중단하고 인접 셀 중앙까지 플레이어를 점진적으로 이동</summary>
        private void TickEscapeMovement()
        {
            Vector2 position = body.position;
            position.x = Mathf.MoveTowards(
                position.x,
                escapeTargetBodyX,
                escapePushSpeed * Time.fixedDeltaTime);

            grounded = false;
            velocity.x = 0f;
            velocity.y -= gravity * Time.fixedDeltaTime;
            position += ResolveMovement(Vector2.up, velocity.y * Time.fixedDeltaTime, ref velocity.y);
            body.MovePosition(position);

            if (Mathf.Approximately(position.x, escapeTargetBodyX)) isEscaping = false;
        }

        private Vector2 ResolveMovement(Vector2 axis, float distance, ref float axisVelocity)
        {
            if (Mathf.Approximately(distance, 0f)) return Vector2.zero;

            Vector2 direction = axis * Mathf.Sign(distance);
            float requestedDistance = Mathf.Abs(distance);
            ContactFilter2D filter = new ContactFilter2D
            {
                useLayerMask = true,
                layerMask = collisionMask,
                useTriggers = false
            };

            int hitCount = box.Cast(direction, filter, castHits, requestedDistance + collisionSkin);
            float allowedDistance = requestedDistance;

            for (int i = 0; i < hitCount; i++)
            {
                if (castHits[i].collider == box) continue;

                allowedDistance = Mathf.Min(allowedDistance,
                    Mathf.Max(0f, castHits[i].distance - collisionSkin));
            }

            if (allowedDistance + Mathf.Epsilon < requestedDistance)
            {
                if (direction.y < 0f) grounded = true;

                axisVelocity = 0f;
            }

            return direction * allowedDistance;
        }

        /// <summary>위에서 내려오는 덩어리 충돌 처리</summary>
        public FallingTerrainContactResult ResolveFallingTerrainContact(FallingChunk fallingChunk)
        {
            long collapseId = fallingChunk.CollapseId;

            // 같은 붕괴에서는 최초 판정 결과를 재사용해 반복 밀림 방지
            if (resolvedCollapses.TryGetValue(collapseId, out FallingTerrainContactResult resolved))
                return resolved;

            if (buried)
            {
                resolvedCollapses[collapseId] = FallingTerrainContactResult.NoEscapeSpace;
                return FallingTerrainContactResult.NoEscapeSpace;
            }

            Bounds playerBounds = box.bounds;
            Bounds chunkBounds = fallingChunk.WorldBounds;

            // 위에서 충돌했는지 확인
            bool struckFromAbove = chunkBounds.center.y > playerBounds.center.y &&
                                   chunkBounds.min.y <= playerBounds.max.y + collisionSkin;

            if (struckFromAbove == false) return FallingTerrainContactResult.Ignored;

            // ★ [핵심 수정] 캐릭터 바로 아래에 바닥(지형 또는 물리 콜라이더)이 있는지 확인합니다.
            bool hasGroundBelow = HasSolidGroundBelow();

            // 아래가 비어있다면(공중, 점프 중, 등반 중 등) 매몰되지 않고 낙하 지형과 함께 아래로 밀립니다.
            if (!hasGroundBelow)
            {
                // 등반 중이었다면 매달리기가 풀리면서 같이 떨어지도록 처리
                if (IsClimb)
                {
                    IsClimb = false;
                }

                // 공중 충돌 시에는 붕괴 처리를 유예하고 무시(또는 자연스러운 하강)합니다.
                return FallingTerrainContactResult.Ignored;
            }

            // --- 아래에 바닥이 존재하여 '압사/매몰' 위험이 있을 때만 탈출 및 매몰 판정 수행 ---

            if (terrainManager != null &&
                terrainManager.TryWorldToCell(playerBounds.center, out Vector2Int playerCell))
            {
                int preferredDirection = transform.position.x <= chunkBounds.center.x ? -1 : 1;
                Vector2Int preferredCell = GetNearestCellInDirection(
                    playerCell,
                    playerBounds.center.x,
                    preferredDirection);
                Vector2Int oppositeCell = GetNearestCellInDirection(
                    playerCell,
                    playerBounds.center.x,
                    -preferredDirection);

                // 좌/우 옆 셀로 탈출 시도
                if (TryEscapeToCell(preferredCell, fallingChunk) ||
                    TryEscapeToCell(oppositeCell, fallingChunk))
                {
                    velocity.x = 0f;
                    resolvedCollapses[collapseId] = FallingTerrainContactResult.Escaped;

                    return FallingTerrainContactResult.Escaped;
                }
            }

            // 좌/우로도 탈출할 공간이 없고 아래도 바닥으로 막혀있을 때 비로소 매몰 처리
            EnterBuriedState();
            resolvedCollapses[collapseId] = FallingTerrainContactResult.NoEscapeSpace;

            return FallingTerrainContactResult.NoEscapeSpace;
        }

        /// <summary>★ [추가] 캐릭터 바로 아래에 지형이나 물리 충돌체(지면)가 있는지 검사</summary>
        private bool HasSolidGroundBelow()
        {
            // 이미 착지 상태라면 true
            if (grounded) return true;

            // Raycast/BoxCast를 이용해 바로 아래쪽(collisionSkin 범위 내) 지면 존재 여부 확인
            ContactFilter2D filter = new ContactFilter2D
            {
                useLayerMask = true,
                layerMask = collisionMask,
                useTriggers = false
            };

            int hitCount = box.Cast(Vector2.down, filter, castHits, collisionSkin * 2f);

            for (int i = 0; i < hitCount; i++)
            {
                if (castHits[i].collider != null && castHits[i].collider != box)
                {
                    return true; // 아래에 지면 존재
                }
            }

            return false; // 공중에 떠 있음 (바닥 비어있음)
        }

        /// <summary>지정 방향에서 반 타일 이내에 있는 가장 가까운 셀 중앙 좌표 계산</summary>
        private Vector2Int GetNearestCellInDirection(
            Vector2Int playerCell,
            float playerCenterX,
            int direction)
        {
            if (terrainManager.TryCellToWorldCenter(playerCell, out Vector3 currentCellCenter))
            {
                bool isCurrentCenterInDirection = direction < 0
                    ? currentCellCenter.x < playerCenterX - collisionSkin
                    : currentCellCenter.x > playerCenterX + collisionSkin;

                if (isCurrentCenterInDirection) return playerCell;
            }

            return playerCell + Vector2Int.right * direction;
        }

        /// <summary>지정 셀이 비어 있으면 플레이어 중심을 해당 셀의 X축 중앙으로 이동</summary>
        private bool TryEscapeToCell(Vector2Int cell, FallingChunk fallingChunk)
        {
            if (terrainManager.HasTerrain(cell)) return false;
            if (terrainManager.TryCellToWorldCenter(cell, out Vector3 cellCenter) == false) return false;

            float offsetX = cellCenter.x - box.bounds.center.x;
            Bounds candidate = box.bounds;
            candidate.center += Vector3.right * offsetX;

            if (IsGridSpaceEmpty(candidate) == false) return false;
            if (IsPhysicalSpaceEmpty(offsetX) == false) return false;
            if (IsInFallingTerrainPath(candidate, fallingChunk)) return false; // 붕괴 지형이 낙하할 위치라면 탈출을 거부

            escapeTargetBodyX = body.position.x + offsetX;
            isEscaping = true;

            return true;
        }

        /// <summary>후보 영역이 붕괴 Chunk의 실제 타일 하강 경로와 겹치는지 확인</summary>
        private bool IsInFallingTerrainPath(Bounds candidate, FallingChunk fallingChunk)
        {
            float candidateMinX = candidate.min.x + collisionSkin;
            float candidateMaxX = candidate.max.x - collisionSkin;
            float candidateMinY = candidate.min.y + collisionSkin;
            float halfCellSize = fallingChunk.CellSize * 0.5f;

            foreach (Vector2Int localCell in fallingChunk.LocalCells)
            {
                Vector3 tileCenter = fallingChunk.GetLocalCellCenterWorld(localCell);
                float tileMinX = tileCenter.x - halfCellSize;
                float tileMaxX = tileCenter.x + halfCellSize;
                float tileMaxY = tileCenter.y + halfCellSize;

                if (tileMaxY <= candidateMinY) continue;
                if (candidateMaxX <= tileMinX || candidateMinX >= tileMaxX) continue;

                return true;
            }

            return false;
        }

        private bool TryMoveToEmptySpace(float offsetX)
        {
            if (IsPhysicalSpaceEmpty(offsetX) == false) return false;

            body.position += Vector2.right * offsetX;
            Physics2D.SyncTransforms();

            return true;
        }

        private bool IsPhysicalSpaceEmpty(float offsetX)
        {
            Vector2 candidateCenter = (Vector2)box.bounds.center + Vector2.right * offsetX;

            ContactFilter2D filter = new ContactFilter2D
            {
                useLayerMask = true,
                layerMask = collisionMask,
                useTriggers = false
            };

            Vector2 probeSize = (Vector2)box.bounds.size - Vector2.one * collisionSkin;
            int count = Physics2D.OverlapBox(candidateCenter, probeSize, 0f, filter, overlapHits);

            for (int i = 0; i < count; i++)
            {
                Collider2D hit = overlapHits[i];

                if (hit == null || hit == box) continue;

                return false;
            }

            return true;
        }

        /// <summary>이동을 중단하고 지형 뒤로 표시하는 매몰 상태 진입</summary>
        private void EnterBuriedState()
        {
            if (buried) return;

            buried = true;
            IsClimb = false; // ★ 매몰 시 매달리기 해제
            isEscaping = false;
            grounded = false;
            velocity = Vector2.zero;
            burialLockedBodyPosition = body.position;
            LockBurialPosition();
            burialDamageElapsed = 0f;
            spriteRenderer.sortingOrder = buriedSortingOrder;
            BurialStateChanged?.Invoke(true);

            Debug.Log("[Terrain Collapse] Player buried", this);
        }

        /// <summary>매몰 직전 이동 예약과 외부 물리 영향으로부터 플레이어 위치 고정</summary>
        private void LockBurialPosition()
        {
            body.position = burialLockedBodyPosition;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
        }

        /// <summary>이동과 일반 표시 순서를 복원하는 매몰 상태 해제</summary>
        private void ExitBuriedState()
        {
            if (buried == false) return;
            if (IsCurrentSpaceEmpty() == false) return;

            buried = false;
            burialDamageElapsed = 0f;
            spriteRenderer.sortingOrder = normalSortingOrder;
            BurialStateChanged?.Invoke(false);

            Debug.Log("[Terrain Collapse] Player escaped burial", this);
        }

        /// <summary>설정된 주기마다 체력 시스템으로 전달할 피해 이벤트 발생</summary>
        private void TickBurialDamage()
        {
            if (burialDamageInterval <= 0f) return;

            burialDamageElapsed += Time.fixedDeltaTime;

            while (burialDamageElapsed >= burialDamageInterval)
            {
                burialDamageElapsed -= burialDamageInterval;
                BurialDamageRequested?.Invoke(burialDamagePerTick);
            }
        }

        /// <summary>채굴 또는 서버 확정 변경 이후 매몰 해제 가능 여부 재검사 예약</summary>
        private void HandleTerrainChanged(TerrainChangeReason _)
        {
            if (buried == false) return;

            if (burialRecheckRoutine != null) StopCoroutine(burialRecheckRoutine);

            burialRecheckRoutine = StartCoroutine(RecheckBurialAfterTerrainRefresh());
        }

        /// <summary>지형 Collider 갱신 완료 후 현재 위치와 주변 탈출 공간 재검사</summary>
        private IEnumerator RecheckBurialAfterTerrainRefresh()
        {
            yield return new WaitForEndOfFrame();

            Physics2D.SyncTransforms();

            if (IsCurrentSpaceEmpty() || TryFindNearbyBurialExit())
                ExitBuriedState();

            burialRecheckRoutine = null;
        }

        /// <summary>현재 플레이어 영역에 Grid 지형과 물리 Collider가 모두 없는지 확인</summary>
        private bool IsCurrentSpaceEmpty()
            => IsGridSpaceEmpty(box.bounds) && IsPhysicalSpaceEmpty(0f);

        /// <summary>현재 위치에서 반 타일 범위 내 좌우 탈출 공간 탐색</summary>
        private bool TryFindNearbyBurialExit()
        {
            float maximumDistance = terrainManager.CellSize * 0.4f;
            int probeCount = Mathf.Max(1, Mathf.CeilToInt(maximumDistance / escapeProbeStep));

            for (int probe = 1; probe <= probeCount; probe++)
            {
                float distance = Mathf.Min(probe * escapeProbeStep, maximumDistance);

                if (TryMoveToGridSpace(-distance) || TryMoveToGridSpace(distance)) return true;
            }

            return false;
        }

        private bool TryMoveToGridSpace(float offsetX)
        {
            Bounds candidate = box.bounds;
            candidate.center += Vector3.right * offsetX;

            if (IsGridSpaceEmpty(candidate) == false) return false;
            if (TryMoveToEmptySpace(offsetX) == false) return false;

            return true;
        }

        /// <summary>주어진 영역과 겹치는 점유 Grid 셀 존재 여부 확인</summary>
        private bool IsGridSpaceEmpty(Bounds bounds)
        {
            Vector3 inset = Vector3.one * collisionSkin;
            if (terrainManager.TryWorldToCell(bounds.min + inset, out Vector2Int min) == false)
                return false;
            if (terrainManager.TryWorldToCell(bounds.max - inset, out Vector2Int max) == false)
                return false;

            for (int y = min.y; y <= max.y; y++)
            for (int x = min.x; x <= max.x; x++)
                if (terrainManager.HasTerrain(new Vector2Int(x, y)))
                    return false;

            return true;
        }

        public FallingTerrainContactResult ResolveFallingTerrainContact(Bounds terrainBounds, float terrainCellSize, long collapseId)
        {
            throw new NotImplementedException();
        }
    }
}
#endif