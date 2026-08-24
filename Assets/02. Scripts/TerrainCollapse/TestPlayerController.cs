#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TerrainCollapse
{
    // 에디터에서 붕괴 상호작용을 시험하기 위한 개발용 2D 플레이어 제어

    /// <summary>Kinematic Rigidbody를 직접 이동시키는 붕괴 테스트용 플레이어</summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D), typeof(SpriteRenderer))]
    public sealed class TestPlayerController : MonoBehaviour, IFallingTerrainEscapeTarget
    {
        [SerializeField] private float moveSpeed = 6f;
        [SerializeField] private float jumpSpeed = 8f;
        [SerializeField] private float gravity = 24f;
        [SerializeField] private float collisionSkin = 0.02f;
        [SerializeField] private float escapeProbeStep = 0.05f;
        [SerializeField] private LayerMask collisionMask = ~0;
        [SerializeField] private TerrainManager terrainManager;
        
        [Header("Burial")]
        [SerializeField] private float burialDamageInterval = 1f;
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
        private float burialDamageElapsed;
        private Coroutine burialRecheckRoutine;
        
        public bool IsBuried => buried;                   // 현재 지형에 매몰된 상태 여부
        public event Action<bool> BurialStateChanged;     // 매몰 상태 진입 및 해제 알림
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
                TickBurialDamage();
                
                return;
            }

            Keyboard keyboard = Keyboard.current;
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

        /// <summary>위에서 내려오는 덩어리와 겹치지 않는 가장 가까운 좌우 위치 탐색</summary>
        public FallingTerrainContactResult ResolveFallingTerrainContact(
            Bounds terrainBounds,
            float terrainCellSize,
            long collapseId)
        {
            // 같은 붕괴에서는 최초 판정 결과를 재사용해 반복 밀림 방지
            if (resolvedCollapses.TryGetValue(collapseId, out FallingTerrainContactResult resolved))
                return resolved;

            if (buried)
            {
                resolvedCollapses[collapseId] = FallingTerrainContactResult.NoEscapeSpace;
                return FallingTerrainContactResult.NoEscapeSpace;
            }

            Bounds playerBounds = box.bounds;
            Bounds chunkBounds = terrainBounds;
            bool struckFromAbove = chunkBounds.center.y > playerBounds.center.y &&
                                   chunkBounds.min.y <= playerBounds.max.y + collisionSkin;
            
            if (struckFromAbove == false) return FallingTerrainContactResult.Ignored;

            float maximumDistance = terrainCellSize * 0.5f;
            int probeCount = Mathf.Max(1, Mathf.CeilToInt(maximumDistance / escapeProbeStep));
            
            for (int probe = 1; probe <= probeCount; probe++)
            {
                float distance = Mathf.Min(probe * escapeProbeStep, maximumDistance);
                int preferredDirection = transform.position.x <= chunkBounds.center.x ? -1 : 1;
                
                if (TryEscapeFallingPath(preferredDirection * distance, chunkBounds) ||
                    TryEscapeFallingPath(-preferredDirection * distance, chunkBounds))
                {
                    velocity.x = 0f;
                    resolvedCollapses[collapseId] = FallingTerrainContactResult.Escaped;

                    return FallingTerrainContactResult.Escaped;
                }
            }

            EnterBuriedState();
            resolvedCollapses[collapseId] = FallingTerrainContactResult.NoEscapeSpace;

            return FallingTerrainContactResult.NoEscapeSpace;
        }

        /// <summary>후보 위치가 낙하 영역을 완전히 벗어나고 비어 있을 때만 이동</summary>
        private bool TryEscapeFallingPath(float offsetX, Bounds terrainBounds)
        {
            Bounds candidate = box.bounds;
            candidate.center += Vector3.right * offsetX;
            bool outsideFallingPath = candidate.max.x <= terrainBounds.min.x + collisionSkin ||
                                      candidate.min.x >= terrainBounds.max.x - collisionSkin;

            return outsideFallingPath && TryMoveToEmptySpace(offsetX);
        }

        private bool TryMoveToEmptySpace(float offsetX)
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

            body.position += Vector2.right * offsetX;
            Physics2D.SyncTransforms();
            
            return true;
        }

        /// <summary>이동을 중단하고 지형 뒤로 표시하는 매몰 상태 진입</summary>
        private void EnterBuriedState()
        {
            if (buried) return;
            
            buried = true;
            grounded = false;
            velocity = Vector2.zero;
            burialDamageElapsed = 0f;
            spriteRenderer.sortingOrder = buriedSortingOrder;
            BurialStateChanged?.Invoke(true);
            
            Debug.Log("[Terrain Collapse] Player buried", this);
        }

        /// <summary>이동과 일반 표시 순서를 복원하는 매몰 상태 해제</summary>
        private void ExitBuriedState()
        {
            if (buried == false) return;
            
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

            if (IsGridSpaceEmpty(box.bounds) || TryFindNearbyBurialExit())
                ExitBuriedState();

            burialRecheckRoutine = null;
        }

        /// <summary>현재 위치에서 반 타일 범위 내 좌우 탈출 공간 탐색</summary>
        private bool TryFindNearbyBurialExit()
        {
            float maximumDistance = terrainManager.CellSize * 0.5f;
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
                    if (terrainManager.HasTerrain(new Vector2Int(x, y))) return false;

            return true;
        }
    }
}
#endif
