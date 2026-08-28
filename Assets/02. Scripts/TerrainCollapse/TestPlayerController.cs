#if UNITY_EDITOR
using System;
using Character;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TerrainCollapse
{
    /// <summary>
    /// 프레임 단위 입력 데이터를 전달하기 위한 구조체
    /// </summary>
    public struct PlayerInputFrame
    {
        public float MoveX;
        public float MoveY;
        public bool JumpPressed;
        public bool ClimbToggled;

        /// <summary>사망 또는 입력 차단 상태에서 사용하는 기본 빈 프레임</summary>
        public static PlayerInputFrame Empty => default;
    }

    /// <summary>Kinematic Rigidbody를 직접 이동시키는 붕괴 테스트용 플레이어 (공중 충돌 후 착지 탈출 완전 보정)</summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D), typeof(SpriteRenderer))]
    [RequireComponent(typeof(FallingTerrainInteraction))]
    public sealed class TestPlayerController : MonoBehaviour, IFallingTerrainCharacter
    {
        [Header("Movement Settings")]
        [SerializeField] private float moveSpeed = 6f;
        [SerializeField] private float climbSpeed = 3f;
        [SerializeField] private float jumpSpeed = 8f;
        [SerializeField] private float gravity = 24f;
        [SerializeField] private float collisionSkin = 0.02f;
        [SerializeField] private LayerMask collisionMask = ~0;
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
        
        private Vector2 velocity;
        private bool grounded;
        private bool buried;
        private bool isEscaping;
        private bool isClimbing;
        private bool isDead;

        private float escapePushSpeed;
        private float escapeTargetBodyX;

        // 프레임별 수집된 입력 정보
        private PlayerInputFrame currentInputFrame;
        
        public bool IsBuried => buried;                   
        public bool IsClimbing => isClimbing;
        public bool IsDead => isDead;
        public Vector2 CurrentPosition => body.position;
        public Bounds CollisionBounds => box.bounds;
        public Vector2 Position => body.position;
        public TerrainManager TerrainManager => terrainManager;
        public float CollisionSkin => collisionSkin;
        public LayerMask CollisionMask => collisionMask;
        
        public event Action<bool> ClimbStateChanged;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            box = GetComponent<BoxCollider2D>();
            spriteRenderer = GetComponent<SpriteRenderer>();
            fallingTerrainInteraction = GetComponent<FallingTerrainInteraction>();
            playerHp = GetComponent<PlayerHp>();
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
            Keyboard keyboard = Keyboard.current;
            
            // 테스트용: K로 플레이어 즉사
            if (keyboard != null && keyboard.kKey.wasPressedThisFrame)
                playerHp.TakeDamage(new DamageInfo(999, DamageType.None, null));

            // 1. 입력 수집 단계 (Input Collection)
            currentInputFrame = GatherInputFrame(keyboard);

            // 2. 토글성 이벤트 입력 처리
            if (currentInputFrame.ClimbToggled)
            {
                ToggleClimbState();
            }
        }

        private void FixedUpdate()
        {
            // 매몰 상태에서는 물리 이동 멈춤
            if (buried) return;

            // 탈출 보정 이동 처리
            if (isEscaping)
            {
                TickEscapeMovement();
                return;
            }

            // 3. 물리 이동 수행 단계 (Execution with InputFrame)
            ExecuteMovement(currentInputFrame);
        }

        /// <summary>
        /// 하드웨어 키보드 입력을 읽어 PlayerInputFrame을 생성합니다.
        /// 사망 상태일 경우 Empty 프레임을 반환합니다.
        /// </summary>
        private PlayerInputFrame GatherInputFrame(Keyboard keyboard)
        {
            if (isDead || keyboard == null)
            {
                return PlayerInputFrame.Empty;
            }

            float inputX = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
            float inputY = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
            bool jump = keyboard.spaceKey.isPressed;
            bool climb = keyboard.leftShiftKey.wasPressedThisFrame || keyboard.rightShiftKey.wasPressedThisFrame;

            return new PlayerInputFrame
            {
                MoveX = inputX,
                MoveY = inputY,
                JumpPressed = jump,
                ClimbToggled = climb
            };
        }

        /// <summary>
        /// 전달받은 InputFrame을 기반으로 이동 속도 계산 및 물리 충돌 해결을 수행합니다.
        /// </summary>
        private void ExecuteMovement(PlayerInputFrame input)
        {
            if (isClimbing)
            {
                velocity.x = input.MoveX * climbSpeed;
                velocity.y = input.MoveY * climbSpeed;
            }
            else
            {
                velocity.x = input.MoveX * moveSpeed;
                
                if (input.JumpPressed && grounded)
                {
                    velocity.y = jumpSpeed;
                }
                
                // 사망 시 input.MoveX/JumpPressed가 default(false/0)가 되므로, 중력만 자연스럽게 적용됨
                velocity.y -= gravity * Time.fixedDeltaTime;
            }

            Vector2 position = body.position;
            position += ResolveMovement(Vector2.right, velocity.x * Time.fixedDeltaTime, ref velocity.x);
            grounded = false;
            position += ResolveMovement(Vector2.up, velocity.y * Time.fixedDeltaTime, ref velocity.y);
            body.MovePosition(position);
        }

        /// <summary>사망 상태로 전환하고 조작 관련 상태 정리 (중력 낙하는 유지)</summary>
        public void EnterDeadState()
        {
            if (isDead) return;

            isDead = true;

            fallingTerrainInteraction.ResetState();
            fallingTerrainInteraction.enabled = false;
            isEscaping = false;
            isClimbing = false;
            grounded = false;

            // 사망 즉시 입력 프레임 초기화 및 X축 속도 제거
            currentInputFrame = PlayerInputFrame.Empty;
            velocity.x = 0f;
            spriteRenderer.sortingOrder = normalSortingOrder;

            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;

            ClimbStateChanged?.Invoke(false);

            Debug.Log("[Terrain Collapse] Player died", this);
        }

        /// <summary>주어진 위치에서 이동/충돌 관련 잔여 상태를 완전히 초기화하며 재시작</summary>
        public void Respawn(Vector2 spawnPosition)
        {
            body.position = spawnPosition;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            Physics2D.SyncTransforms();

            velocity = Vector2.zero;
            grounded = false;
            isEscaping = false;
            isClimbing = false;
            currentInputFrame = PlayerInputFrame.Empty;

            spriteRenderer.sortingOrder = normalSortingOrder;
            fallingTerrainInteraction.ResetState();
            fallingTerrainInteraction.enabled = true;

            isDead = false;

            Debug.Log("[Terrain Collapse] Player respawned", this);
        }

        public void ToggleClimbState()
        {
            if (isDead) return;

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
            isEscaping = Mathf.Approximately(body.position.x, targetX) == false;
        }

        public void SetTerrainBuried(bool isTerrainBuried, Vector2 lockedPosition)
        {
            buried = isTerrainBuried;

            if (isTerrainBuried)
            {
                isEscaping = false;
                isClimbing = false;
                grounded = false;
                velocity = Vector2.zero;
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
    }
}
#endif