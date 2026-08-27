#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TerrainCollapse
{
    /// <summary>Kinematic Rigidbody를 직접 이동시키는 붕괴 테스트용 플레이어 (공중 충돌 후 착지 탈출 완전 보정)</summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D), typeof(SpriteRenderer))]
    public sealed class TestPlayerController : MonoBehaviour, IFallingTerrainEscapeTarget
    {
        [Header("Movement Settings")]
        [SerializeField] private float moveSpeed = 6f;
        [SerializeField] private float climbSpeed = 3f;
        [SerializeField] private float jumpSpeed = 8f;
        [SerializeField] private float gravity = 24f;
        [SerializeField] private float collisionSkin = 0.02f;
        [SerializeField] private float escapeProbeStep = 0.05f;
        [SerializeField, Min(0.01f)] private float escapePushSpeed = 4f;
        [SerializeField] private LayerMask collisionMask = ~0;
        [SerializeField] private TerrainManager terrainManager;
        [SerializeField, Min(0f)] private float escapeXOffset = 0.5f;
        
        [Header("Burial Settings")]
        [SerializeField] private float burialDamageInterval = 1f;
        [SerializeField] private int burialDamagePerTick = 1;
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
        private bool isClimbing;

        private float burialDamageElapsed;
        private float escapeTargetBodyX;
        private Vector2 burialLockedBodyPosition;
        private Coroutine burialRecheckRoutine;
        
        public bool IsBuried => buried;                   
        public bool IsClimbing => isClimbing;
        public TerrainManager TerrainManager => terrainManager;
        
        public event Action<bool> BurialStateChanged;     
        public event Action<int> BurialDamageRequested;
        public event Action<bool> ClimbStateChanged;      

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

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.leftShiftKey.wasPressedThisFrame || keyboard.rightShiftKey.wasPressedThisFrame))
            {
                ToggleClimbState();
            }
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
                Debug.LogWarning("[Terrain Collapse] Occupied terrain overlap detected before player movement", this);
                EnterBuriedState();
                return;
            }

            if (isEscaping)
            {
                TickEscapeMovement();
                return;
            }

            Keyboard keyboard = Keyboard.current;
            
            float inputX = keyboard == null ? 0f : (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
            float inputY = keyboard == null ? 0f : (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);

            if (isClimbing)
            {
                velocity.x = inputX * climbSpeed;
                velocity.y = inputY * climbSpeed;
            }
            else
            {
                velocity.x = inputX * moveSpeed;
                
                if (keyboard != null && keyboard.spaceKey.isPressed && grounded)
                {
                    velocity.y = jumpSpeed;
                }
                
                velocity.y -= gravity * Time.fixedDeltaTime;
            }

            Vector2 position = body.position;
            position += ResolveMovement(Vector2.right, velocity.x * Time.fixedDeltaTime, ref velocity.x);
            grounded = false;
            position += ResolveMovement(Vector2.up, velocity.y * Time.fixedDeltaTime, ref velocity.y);
            body.MovePosition(position);
        }

        public void ToggleClimbState()
        {
            isClimbing = !isClimbing;
            velocity = Vector2.zero;
            ClimbStateChanged?.Invoke(isClimbing);
            Debug.Log($"[TestPlayerController] Climb State: {isClimbing}", this);
        }

        private void TickEscapeMovement()
        {
            Vector2 position = body.position;
            position.x = Mathf.MoveTowards(
                position.x,
                escapeTargetBodyX,
                escapePushSpeed * Time.fixedDeltaTime);

            grounded = false;
            velocity.x = 0f;
            
            if (isClimbing == false)
            {
                velocity.y -= gravity * Time.fixedDeltaTime;
            }
            
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

        /// <summary>상단 지형 낙하 충돌 판정 (공중 충돌 후 착지 탈출 완전 보정)</summary>
        public FallingTerrainContactResult ResolveFallingTerrainContact(FallingChunk fallingChunk)
        {
            if (fallingChunk == null || terrainManager == null || terrainManager.IsReady == false)
                return FallingTerrainContactResult.Ignored;

            // OnCollisionEnter/Stay가 같은 붕괴에 대해 여러 번 호출되어도 한 번만 판정한다.
            if (resolvedCollapses.TryGetValue(fallingChunk.CollapseId, out FallingTerrainContactResult previousResult))
                return previousResult;

            // 이미 낙하가 끝난 Chunk는 탈출 판정 대상이 아니다.
            if (fallingChunk.IsComplete)
            {
                resolvedCollapses[fallingChunk.CollapseId] = FallingTerrainContactResult.Ignored;
                return FallingTerrainContactResult.Ignored;
            }

            // Chunk의 실제 타일 형상이 플레이어 위쪽을 덮는 경우에만 낙하에 휘말린 것으로 본다.
            // 단순한 옆면 접촉이나 이미 Chunk 아래로 지나간 접촉은 무시한다.
            if (IsInFallingTerrainPath(box.bounds, fallingChunk) == false)
            {
                resolvedCollapses[fallingChunk.CollapseId] = FallingTerrainContactResult.Ignored;
                return FallingTerrainContactResult.Ignored;
            }

            // 등반 중이어도 낙하에 휘말린 순간부터는 등반 상태를 해제하고 추락시킨다.
            isClimbing = false;
            ClimbStateChanged?.Invoke(false);

            // Kinematic Player가 Chunk의 낙하를 즉시 따라가도록 현재 낙하 속도를 한 번 상속한다.
            Vector2 fallingVelocity = fallingChunk.BeforeVelocity;
            velocity.x = fallingVelocity.x;
            velocity.y = Mathf.Min(fallingVelocity.y, 0f);

            grounded = false;
            isEscaping = false;

            float oneTile = terrainManager.CellSize;
            if (oneTile > 0f)
            {
                // 플레이어가 타일 경계에서 0.25타일 정도만 옆으로 치우쳐 있는 경우,
                // 무조건 인접 타일의 중심으로 한 칸 이동하려 하면 오히려 현재 서 있던
                // 안전한 타일을 놓치게 된다.
                // 따라서 먼저 현재 타일 안에서 최소 이동량으로 Chunk의 수직 투영을
                // 벗어나는 위치를 찾고, 그 다음에 양 옆으로 최대 한 타일까지 탐색한다.
                //
                // offset=0도 후보에 포함한다. 현재 위치 자체가 이미 안전하면 이동하지
                // 않고도 '회피 성공'으로 처리할 수 있다.
                float probeStep = Mathf.Max(0.005f, Mathf.Min(escapeProbeStep, oneTile * 0.25f));
                float bestOffset = 0f;
                float bestDistance = float.PositiveInfinity;
                bool foundEscape = false;
                float maximumEscapeDistance = oneTile * escapeXOffset;

                int probeCount = Mathf.Max(1, Mathf.CeilToInt(maximumEscapeDistance / probeStep));
                for (int probe = 0; probe <= probeCount; probe++)
                {
                    float distance = Mathf.Min(probe * probeStep, maximumEscapeDistance);

                    // 같은 거리에서는 왼쪽을 먼저 선택해 결과를 결정적으로 만든다.
                    float leftOffset = -distance;
                    Bounds leftCandidate = box.bounds;
                    leftCandidate.center += Vector3.right * leftOffset;

                    bool leftSafe =
                        IsGridSpaceEmpty(leftCandidate) &&
                        IsPhysicalSpaceEmpty(leftOffset) &&
                        IsInFallingTerrainPath(leftCandidate, fallingChunk) == false;

                    if (leftSafe && distance < bestDistance)
                    {
                        bestOffset = leftOffset;
                        bestDistance = distance;
                        foundEscape = true;
                    }

                    // distance=0일 때는 동일 후보를 중복 검사하지 않는다.
                    if (probe == 0) continue;

                    float rightOffset = distance;
                    Bounds rightCandidate = box.bounds;
                    rightCandidate.center += Vector3.right * rightOffset;

                    bool rightSafe =
                        IsGridSpaceEmpty(rightCandidate) &&
                        IsPhysicalSpaceEmpty(rightOffset) &&
                        IsInFallingTerrainPath(rightCandidate, fallingChunk) == false;

                    if (rightSafe && distance < bestDistance)
                    {
                        bestOffset = rightOffset;
                        bestDistance = distance;
                        foundEscape = true;
                    }
                }

                if (foundEscape)
                {
                    // 이미 안전한 위치(offset=0)라면 굳이 움직이지 않는다.
                    escapeTargetBodyX = body.position.x + bestOffset;
                    isEscaping = Mathf.Approximately(bestOffset, 0f) == false;

                    resolvedCollapses[fallingChunk.CollapseId] = FallingTerrainContactResult.Escaped;
                    return FallingTerrainContactResult.Escaped;
                }
            }

            // 현재 타일 안에서 조금 움직이는 것조차 불가능하고 양 옆 한 타일까지
            // 안전한 공간이 없다면 현재 위치를 강제로 옮기지 않는다.
            // Chunk가 계속 추락하고 지면에 닿아 플레이어 공간을 차지하면 FixedUpdate의
            // 일반 매몰 판정이 샌드위치/생매장 상태로 전환한다.
            resolvedCollapses[fallingChunk.CollapseId] = FallingTerrainContactResult.NoEscapeSpace;
            return FallingTerrainContactResult.NoEscapeSpace;
        }
        
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

        private void EnterBuriedState()
        {
            if (buried) return;

            buried = true;
            isEscaping = false;
            isClimbing = false;
            grounded = false;
            velocity = Vector2.zero;
            burialLockedBodyPosition = body.position;
            LockBurialPosition();
            burialDamageElapsed = 0f;
            spriteRenderer.sortingOrder = buriedSortingOrder;
            BurialStateChanged?.Invoke(true);
            
            Debug.Log("[Terrain Collapse] Player buried", this);
        }

        private void LockBurialPosition()
        {
            body.position = burialLockedBodyPosition;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
        }

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

        private void HandleTerrainChanged(TerrainChangeReason _)
        {
            if (buried == false) return;
            
            if (burialRecheckRoutine != null) StopCoroutine(burialRecheckRoutine);
            
            burialRecheckRoutine = StartCoroutine(RecheckBurialAfterTerrainRefresh());
        }

        private IEnumerator RecheckBurialAfterTerrainRefresh()
        {
            yield return new WaitForEndOfFrame();
            
            Physics2D.SyncTransforms();

            if (IsCurrentSpaceEmpty() || TryFindNearbyBurialExit())
                ExitBuriedState();

            burialRecheckRoutine = null;
        }

        private bool IsCurrentSpaceEmpty()
            => IsGridSpaceEmpty(box.bounds) && IsPhysicalSpaceEmpty(0f);

        private bool TryFindNearbyBurialExit()
        {
            float maximumDistance = terrainManager.CellSize * escapeXOffset;
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

        private bool IsGridSpaceEmpty(Bounds bounds)
        {
            Vector3 inset = Vector3.one * collisionSkin;
            if (terrainManager.TryWorldToCell(bounds.min + inset, out Vector2Int min) == false)
                return false;
            if (terrainManager.TryWorldToCell(bounds.max - inset, out Vector2Int max) == false)
                return false;

            for (int y = min.y; y <= max.y; y++)
                for (int x = min.x; x <= max.x; x++)
                    if (terrainManager.HasTerrain(new Vector2Int(x, y))) return false;

            return true;
        }
    }
}
#endif
