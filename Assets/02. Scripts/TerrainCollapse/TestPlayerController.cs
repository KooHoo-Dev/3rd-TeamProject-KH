using System;
using Character;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TerrainCollapse
{
    /// <summary>Kinematic Rigidbody를 직접 이동시키는 붕괴 테스트용 플레이어 (공중 충돌 후 착지 탈출 완전 보정)</summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D), typeof(SpriteRenderer))]
    [RequireComponent(typeof(FallingTerrainInteraction))]
    [RequireComponent(typeof(PlayerInventory))]
    public sealed class TestPlayerController : MonoBehaviour, IFallingTerrainCharacter
    {
        [Header("Movement Settings")]
        [SerializeField] private float moveSpeed = 6f;
        [SerializeField] private float climbSpeed = 3f;
        [SerializeField] private float jumpSpeed = 8f;
        [SerializeField] private float gravity = 24f;
        [SerializeField] private float collisionSkin = 0.02f;
        [SerializeField] private LayerMask collisionMask;
        [SerializeField] private TerrainManager terrainManager;
        
        [Header("Terrain Interaction Visual Settings")]
        [SerializeField] private int normalSortingOrder = 2;
        [SerializeField] private int buriedSortingOrder = -1;

        private readonly RaycastHit2D[] castHits = new RaycastHit2D[16];
        
        private Rigidbody2D body;
        private BoxCollider2D box;
        private SpriteRenderer spriteRenderer;
        private FallingTerrainInteraction fallingTerrainInteraction;
        private PlayerHp playerHp;
        private PlayerInventory inventory;
        
        private Vector2 velocity;
        private bool grounded;
        private bool buried;
        private bool terrainInteractionLocked;
        private bool isEscaping;
        private bool isClimbing;
        private bool isDead;
        private bool isDeadFalling;
        private bool isWaitingForTerrainSnapshot;

        private float escapePushSpeed;
        private float escapeTargetBodyX;
        private Vector2 terrainInteractionLockedPosition;
        
        public bool IsBuried => buried;                   
        public bool IsClimbing => isClimbing;
        public bool IsDead => isDead;
        public Vector2 CurrentPosition => body.position;
        public Bounds CollisionBounds => box.bounds;
        public Vector2 Position => body.position;
        public TerrainManager TerrainManager => terrainManager;
        public float CollisionSkin => collisionSkin;
        public LayerMask CollisionMask => collisionMask;
        public IItemInventory Inventory => inventory;
        
        public event Action<bool> ClimbStateChanged;

        private void Awake()
        {
            if (collisionMask.value == 0)
                collisionMask = LayerMask.GetMask("Terrain");

            body = GetComponent<Rigidbody2D>();
            box = GetComponent<BoxCollider2D>();
            spriteRenderer = GetComponent<SpriteRenderer>();
            fallingTerrainInteraction = GetComponent<FallingTerrainInteraction>();
            playerHp = GetComponent<PlayerHp>();
            inventory = GetComponent<PlayerInventory>();
            isDead = playerHp.IsDead;
            
            if (terrainManager == null) terrainManager = FindFirstObjectByType<TerrainManager>();
            
            body.bodyType = RigidbodyType2D.Kinematic;
            body.useFullKinematicContacts = true;
            body.gravityScale = 0f;
            body.freezeRotation = true;
            spriteRenderer.sortingOrder = normalSortingOrder;
        }

        private void Update()
        {
            if (isWaitingForTerrainSnapshot || isDead || terrainInteractionLocked || isEscaping) return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && (keyboard.leftShiftKey.wasPressedThisFrame || keyboard.rightShiftKey.wasPressedThisFrame))
            {
                ToggleClimbState();
            }

            // 테스트용: K로 플레이어 즉사
            if (keyboard != null && keyboard.kKey.wasPressedThisFrame)
                playerHp.TakeDamage(new DamageInfo(999, DamageType.None, null));
        }

        private void FixedUpdate()
        {
            if (isWaitingForTerrainSnapshot)
            {
                SetPosition(terrainInteractionLockedPosition);
                return;
            }

            if (isDead)
            {
                if (isDeadFalling) TickDeadFall();
                return;
            }
            if (buried) return;

            if (terrainInteractionLocked)
            {
                SetPosition(terrainInteractionLockedPosition);
                return;
            }

            if (isEscaping)
            {
                TickEscapeMovement();
                return;
            }

            Keyboard keyboard = isDead ? null : Keyboard.current;

            float inputX = keyboard == null ? 0f : (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
            float inputY = keyboard == null ? 0f : (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);

            if (isClimbing && isDead == false)
            {
                velocity.x = inputX * climbSpeed;
                velocity.y = inputY * climbSpeed;
            }
            else
            {
                velocity.x = isDead ? 0f : inputX * moveSpeed;

                if (isDead == false && keyboard != null && keyboard.spaceKey.isPressed && grounded)
                {
                    velocity.y = jumpSpeed;
                }

                velocity.y -= gravity * Time.fixedDeltaTime;
            }

            Vector2 position = body.position;
            float deltaX = velocity.x * Time.fixedDeltaTime;
            float deltaY = velocity.y * Time.fixedDeltaTime;

            // 1. 현재 위치에서 X축 검사 후 허용된 X 이동을 중간 위치에 반영
            position += ResolveMovement(position, Vector2.right, deltaX, ref velocity.x);
            // 2. 접지 상태 초기화
            grounded = false;
            // 3. X 이동이 적용된 위치에서 Y축 검사 후 허용된 Y 이동을 반영
            position += ResolveMovement(position, Vector2.up, deltaY, ref velocity.y);
            // 4. 최종 위치를 한 번만 적용
            body.MovePosition(position);
        }

        private void TickDeadFall()
        {
            velocity.x = 0f;
            velocity.y -= gravity * Time.fixedDeltaTime;

            Vector2 position = body.position;
            float deltaY = velocity.y * Time.fixedDeltaTime;

            grounded = false;

            Vector2 movement =
                ResolveMovement(position, Vector2.up, deltaY, ref velocity.y);

            position += movement;
            body.MovePosition(position);

            // Static 지형에 닿아 수직 이동이 막히면 낙하 종료
            if (grounded || Mathf.Approximately(velocity.y, 0f))
            {
                velocity = Vector2.zero;
                isDeadFalling = false;
            }
        }

        /// <summary>사망 상태로 전환하고 매몰/탈출/등반/이동 등 이전 플레이 상태를 모두 정리
        /// 리스폰 타이밍과 체력 복구는 RespawnController가 담당</summary>
        public void EnterDeadState(bool isPositionLocked = false)
        {
            if (isDead) return;

            bool diedWhileBuried = buried;
            isDead = true;
            isDeadFalling = isPositionLocked == false && diedWhileBuried == false;
            
            fallingTerrainInteraction.ResetState();
            fallingTerrainInteraction.enabled = false;
            isEscaping = false;
            isClimbing = false;
            terrainInteractionLocked = false;
            grounded = false;

            // 매몰 또는 사망 보관 타일 생성: 위치 고정
            if (diedWhileBuried || isPositionLocked)
            {
                velocity = Vector2.zero;
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
            }
            // 공중 사망: 수평 이동만 제거하고 수직 속도는 유지
            else velocity.x = 0f;

            ClimbStateChanged?.Invoke(false);

            Debug.Log("[Terrain Collapse] Player died", this);
        }

        /// <summary>주어진 위치에서 이동/충돌 관련 잔여 상태를 완전히 초기화하며 재시작
        /// 체력 복구는 호출부(TestPlayerRespawnController)에서 PlayerHp.ResetHp()로 처리</summary>
        public void Respawn(Vector2 spawnPosition)
        {
            body.position = spawnPosition;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            Physics2D.SyncTransforms();

            velocity = Vector2.zero;
            grounded = true;
            isEscaping = false;
            isClimbing = false;
            terrainInteractionLocked = false;
            buried = false;
            spriteRenderer.sortingOrder = normalSortingOrder;
            fallingTerrainInteraction.ResetState();
            fallingTerrainInteraction.enabled = true;

            isDead = false;
            isDeadFalling = false;

            Debug.Log("[Terrain Collapse] Player respawned", this);
        }

        /// <summary>서버 지형 스냅샷을 받기 전 플레이어 입력과 이동을 고정</summary>
        public void SetWaitingForTerrainSnapshot(bool isWaiting)
        {
            isWaitingForTerrainSnapshot = isWaiting;
            StopControlledMovement();
            grounded = false;

            if (isWaiting == false) return;

            terrainInteractionLockedPosition = body.position;
            SetPosition(terrainInteractionLockedPosition);
        }

        public void ToggleClimbState()
        {
            isClimbing = !isClimbing;
            velocity = Vector2.zero;
            ClimbStateChanged?.Invoke(isClimbing);
            Debug.Log($"[TestPlayerController] Climb State: {isClimbing}", this);
        }

        public void CancelClimbing()
        {
            if (isClimbing == false) return;

            isClimbing = false;
            ClimbStateChanged?.Invoke(false);
        }

        public void ApplyFallingVelocity(Vector2 fallingVelocity)
        {
            velocity = fallingVelocity;
            grounded = false;
            isEscaping = false;
        }

        public void StartTerrainEscape(float targetX, float speed)
        {
            escapeTargetBodyX = targetX;
            escapePushSpeed = speed;
            isEscaping = true;
        }

        public void StopTerrainEscape() => isEscaping = false;

        public void SetTerrainInteractionLocked(bool isLocked, Vector2 lockedPosition)
        {
            terrainInteractionLocked = isLocked;

            if (isLocked == false) return;

            terrainInteractionLockedPosition = lockedPosition;
            StopControlledMovement();
            grounded = false;
            SetPosition(lockedPosition);
        }

        public void SetTerrainBuried(bool isTerrainBuried, Vector2 lockedPosition)
        {
            buried = isTerrainBuried;
            terrainInteractionLocked = false;

            if (isTerrainBuried)
            {
                StopControlledMovement();
                grounded = false;
                SetPosition(lockedPosition);
                spriteRenderer.sortingOrder = buriedSortingOrder;
                return;
            }

            spriteRenderer.sortingOrder = normalSortingOrder;
        }

        public void SetPosition(Vector2 position)
        {
            body.position = position;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
        }

        private void TickEscapeMovement()
        {
            Vector2 position = body.position;

            float deltaX = Mathf.MoveTowards(position.x, escapeTargetBodyX, escapePushSpeed * Time.fixedDeltaTime) -
                           position.x;

            grounded = false;

            velocity.y -= gravity * Time.fixedDeltaTime;

            // X축 탈출 이동 검사 후 이동
            Vector2 horizontalMovement = ResolveMovement(position, Vector2.right, deltaX, ref velocity.x);
            position += horizontalMovement;

            // X 이동이 반영된 위치에서 Y축 검사 후 이동
            Vector2 verticalMovement = ResolveMovement(position, Vector2.up, velocity.y * Time.fixedDeltaTime, ref velocity.y);
            position += verticalMovement;

            body.MovePosition(position);
        }

        private void StopControlledMovement()
        {
            isEscaping = false;
            isClimbing = false;
            velocity = Vector2.zero;
        }

        private Vector2 ResolveMovement(Vector2 startPosition, Vector2 axis, float distance, ref float axisVelocity)
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

            // Rigidbody 위치와 실제 Collider 중심 사이의 거리 차이
            Vector2 centerOffset = (Vector2)box.bounds.center - body.position;

            // 전달받은 가상 시작 위치를 기준으로 캐스트 중심 계산
            Vector2 castCenter = startPosition + centerOffset;
            Vector2 castSize = box.bounds.size;

            // 수평 이동은 바닥/천장, 수직 이동은 벽 접촉이 방해하지 않도록 직교축에 skin 적용
            if (Mathf.Abs(axis.x) > 0f)
                castSize.y = Mathf.Max(0.001f, castSize.y - collisionSkin * 2f);
            else
                castSize.x = Mathf.Max(0.001f, castSize.x - collisionSkin * 2f);

            int hitCount = Physics2D.BoxCast(castCenter,
                castSize,
                body.rotation,
                direction,
                filter,
                castHits,
                requestedDistance + collisionSkin);
            float allowedDistance = requestedDistance;
            
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit2D hit = castHits[i];

                if (hit.collider == null || hit.collider == box ||
                    Physics2D.GetIgnoreCollision(box, hit.collider)) continue;

                float availableDistance = Mathf.Max(0f, hit.distance - collisionSkin);

                allowedDistance = Mathf.Min(allowedDistance, availableDistance);
            }

            bool wasBlocked = allowedDistance + Mathf.Epsilon < requestedDistance;

            if (wasBlocked)
            {
                if (direction.y < 0f) grounded = true;

                axisVelocity = 0f;
            }
            
            return direction * allowedDistance;
        }
    }
}
