#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Character;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TerrainCollapse
{
    /// <summary>
    /// Kinematic Rigidbody를 직접 이동시키는 붕괴 테스트용 플레이어.
    /// 축 분리 이동 + 실제 Collider Cast + Static Terrain 침투 보정을 사용한다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
    [RequireComponent(typeof(FallingTerrainInteraction))]
    [RequireComponent(typeof(PlayerInventory))]
    public sealed class TestPlayerController : MonoBehaviour, IPlayerController
    {
        [Header("Movement Settings")]
        [SerializeField] private float moveSpeed = 6f;
        [SerializeField] private float climbSpeed = 3f;
        [SerializeField] private float jumpSpeed = 8f;
        [SerializeField] private float gravity = 24f;
        [SerializeField] private float collisionSkin = 0.02f;
        [SerializeField] private LayerMask collisionMask;
        [SerializeField] private TerrainManager terrainManager;

        [Header("Collision Recovery Settings")]

        // requestedDistance와 allowedDistance 사이의 극소 오차 때문에
        // 불필요하게 velocity가 0이 되는 것을 방지한다.
        [SerializeField]
        [Min(0f)]
        private float movementTolerance = 0.0001f;

        // 이동 방향을 실제로 가로막는 normal인지 판정하기 위한 최소 내적값.
        // 바닥 normal과 수평 이동 방향처럼 직교하는 접촉은 무시한다.
        [SerializeField]
        [Range(0f, 0.25f)]
        private float blockingNormalDotThreshold = 0.01f;

        // 이미 Static Terrain 내부에 들어간 경우 표면 바로 바깥까지
        // 아주 조금 더 밀어내 재침투를 방지한다.
        [SerializeField]
        [Min(0f)]
        private float depenetrationPadding = 0.001f;

        // 여러 타일의 모서리에 동시에 겹쳤을 경우를 대비한 반복 횟수.
        [SerializeField]
        [Range(1, 8)]
        private int depenetrationIterations = 4;

        private readonly List<RaycastHit2D> castHits =
            new List<RaycastHit2D>(16);

        private readonly Collider2D[] overlapHits =
            new Collider2D[16];

        private Rigidbody2D body;
        private BoxCollider2D box;
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
        public bool IsGrounded => grounded;
        public Vector2 Velocity => velocity;

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
            fallingTerrainInteraction = GetComponent<FallingTerrainInteraction>();
            playerHp = GetComponent<PlayerHp>();
            inventory = GetComponent<PlayerInventory>();

            isDead = playerHp.IsDead;

            if (terrainManager == null)
                terrainManager = FindFirstObjectByType<TerrainManager>();

            body.bodyType = RigidbodyType2D.Kinematic;
            body.useFullKinematicContacts = true;
            body.gravityScale = 0f;
            body.freezeRotation = true;
        }

        private void Update()
        {
            if (isWaitingForTerrainSnapshot ||
                isDead ||
                terrainInteractionLocked ||
                isEscaping)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;

            if (keyboard != null &&
                (keyboard.leftShiftKey.wasPressedThisFrame ||
                 keyboard.rightShiftKey.wasPressedThisFrame))
            {
                ToggleClimbState();
            }

            // 테스트용: K로 플레이어 즉사
            if (keyboard != null &&
                keyboard.kKey.wasPressedThisFrame)
            {
                playerHp.TakeDamage(
                    new DamageInfo(
                        999,
                        DamageType.None,
                        null));
            }
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
                if (isDeadFalling)
                    TickDeadFall();

                return;
            }

            if (buried)
                return;

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

            /*
             * 핵심 보정 1
             *
             * 정상 플레이 중 Static Terrain 안에 미세 침투가 생겼다면
             * 이동 Cast를 실행하기 전에 먼저 해소한다.
             *
             * 시작 중첩 상태에서 Cast가 distance == 0으로 고정되고
             * X/Y 양축이 모두 차단되는 영구 끼임을 방지한다.
             */
            Vector2 position = ResolveStaticTerrainPenetration();

            Keyboard keyboard = Keyboard.current;

            float inputX =
                keyboard == null
                    ? 0f
                    : (keyboard.dKey.isPressed ? 1f : 0f) -
                      (keyboard.aKey.isPressed ? 1f : 0f);

            float inputY =
                keyboard == null
                    ? 0f
                    : (keyboard.wKey.isPressed ? 1f : 0f) -
                      (keyboard.sKey.isPressed ? 1f : 0f);

            if (isClimbing)
            {
                velocity.x = inputX * climbSpeed;
                velocity.y = inputY * climbSpeed;
            }
            else
            {
                velocity.x = inputX * moveSpeed;

                if (keyboard != null &&
                    keyboard.spaceKey.isPressed &&
                    grounded)
                {
                    velocity.y = jumpSpeed;
                }

                velocity.y -= gravity * Time.fixedDeltaTime;
            }

            float deltaX =
                velocity.x * Time.fixedDeltaTime;

            float deltaY =
                velocity.y * Time.fixedDeltaTime;

            /*
             * X → Y 순으로 축 분리 이동.
             *
             * X 이동 결과를 가상 위치에 적용한 뒤
             * 그 위치를 기준으로 Y Cast를 수행한다.
             */
            position += ResolveMovement(
                position,
                Vector2.right,
                deltaX,
                ref velocity.x);

            grounded = false;

            position += ResolveMovement(
                position,
                Vector2.up,
                deltaY,
                ref velocity.y);

            body.MovePosition(position);
        }

        private void TickDeadFall()
        {
            velocity.x = 0f;
            velocity.y -= gravity * Time.fixedDeltaTime;

            Vector2 position = body.position;

            float deltaY =
                velocity.y * Time.fixedDeltaTime;

            grounded = false;

            Vector2 movement =
                ResolveMovement(
                    position,
                    Vector2.up,
                    deltaY,
                    ref velocity.y);

            position += movement;

            body.MovePosition(position);

            // Static 지형에 닿아 수직 이동이 막히면 낙하 종료
            if (grounded ||
                Mathf.Approximately(velocity.y, 0f))
            {
                velocity = Vector2.zero;
                isDeadFalling = false;
            }
        }

        /// <summary>
        /// 사망 상태로 전환하고
        /// 매몰/탈출/등반/이동 등 이전 플레이 상태를 모두 정리한다.
        /// 리스폰 타이밍과 체력 복구는 RespawnController가 담당한다.
        /// </summary>
        public void EnterDeadState(bool isPositionLocked = false)
        {
            if (isDead)
                return;

            bool diedWhileBuried = buried;

            isDead = true;

            isDeadFalling =
                isPositionLocked == false &&
                diedWhileBuried == false;

            fallingTerrainInteraction.ResetState();
            fallingTerrainInteraction.enabled = false;

            isEscaping = false;
            isClimbing = false;
            terrainInteractionLocked = false;
            grounded = false;

            // 매몰 또는 사망 보관 타일 생성:
            // 위치 고정
            if (diedWhileBuried ||
                isPositionLocked)
            {
                velocity = Vector2.zero;

                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
            }
            // 공중 사망:
            // 수평 이동만 제거하고 수직 속도는 유지
            else
            {
                velocity.x = 0f;
            }

            ClimbStateChanged?.Invoke(false);

            Debug.Log(
                "[Terrain Collapse] Player died",
                this);
        }

        /// <summary>
        /// 주어진 위치에서 이동/충돌 관련 잔여 상태를
        /// 완전히 초기화하며 재시작한다.
        /// 체력 복구는 호출부에서 PlayerHp.ResetHp()로 처리한다.
        /// </summary>
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

            fallingTerrainInteraction.ResetState();
            fallingTerrainInteraction.enabled = true;

            isDead = false;
            isDeadFalling = false;

            Debug.Log(
                "[Terrain Collapse] Player respawned",
                this);
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

            Debug.Log(
                $"[TestPlayerController] Climb State: {isClimbing}",
                this);
        }

        public void CancelClimbing()
        {
            if (isClimbing == false)
                return;

            isClimbing = false;

            ClimbStateChanged?.Invoke(false);
        }

        public void ApplyFallingVelocity(
            Vector2 fallingVelocity)
        {
            velocity = fallingVelocity;

            grounded = false;
            isEscaping = false;
        }

        public void StartTerrainEscape(
            float targetX,
            float speed)
        {
            escapeTargetBodyX = targetX;
            escapePushSpeed = speed;

            isEscaping = true;
        }

        public void StopTerrainEscape()
        {
            isEscaping = false;
        }

        public void SetTerrainInteractionLocked(
            bool isLocked,
            Vector2 lockedPosition)
        {
            terrainInteractionLocked = isLocked;

            if (isLocked == false)
                return;

            terrainInteractionLockedPosition =
                lockedPosition;

            StopControlledMovement();

            grounded = false;

            SetPosition(lockedPosition);
        }

        public void SetTerrainBuried(
            bool isTerrainBuried,
            Vector2 lockedPosition)
        {
            buried = isTerrainBuried;
            terrainInteractionLocked = false;

            if (isTerrainBuried)
            {
                StopControlledMovement();

                grounded = false;

                SetPosition(lockedPosition);

                return;
            }
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

            float deltaX =
                Mathf.MoveTowards(
                    position.x,
                    escapeTargetBodyX,
                    escapePushSpeed * Time.fixedDeltaTime) -
                position.x;

            grounded = false;

            velocity.y -=
                gravity * Time.fixedDeltaTime;

            // X축 탈출 이동
            Vector2 horizontalMovement =
                ResolveMovement(
                    position,
                    Vector2.right,
                    deltaX,
                    ref velocity.x);

            position += horizontalMovement;

            // X 이동이 반영된 위치에서 Y축 이동
            Vector2 verticalMovement =
                ResolveMovement(
                    position,
                    Vector2.up,
                    velocity.y * Time.fixedDeltaTime,
                    ref velocity.y);

            position += verticalMovement;

            body.MovePosition(position);
        }

        private void StopControlledMovement()
        {
            isEscaping = false;
            isClimbing = false;

            velocity = Vector2.zero;
        }

        /// <summary>
        /// 현재 플레이어가 Static Terrain 안에 이미 미세하게
        /// 침투한 상태라면 최소 분리 벡터를 이용해 밖으로 꺼낸다.
        ///
        /// FallingChunk와 같은 Dynamic/Kinematic Rigidbody는
        /// FallingTerrainInteraction이 별도로 처리하므로 여기서는
        /// 의도적으로 건드리지 않는다.
        /// </summary>
        private Vector2 ResolveStaticTerrainPenetration()
        {
            ContactFilter2D filter =
                CreateCollisionFilter();

            for (int iteration = 0;
                 iteration < depenetrationIterations;
                 iteration++)
            {
                int overlapCount =
                    Physics2D.OverlapCollider(
                        box,
                        filter,
                        overlapHits);

                bool foundPenetration = false;

                ColliderDistance2D deepestDistance =
                    default(ColliderDistance2D);

                float deepestSignedDistance = 0f;

                for (int i = 0;
                     i < overlapCount;
                     i++)
                {
                    Collider2D other =
                        overlapHits[i];

                    if (ShouldIgnoreCollider(other))
                        continue;

                    /*
                     * FallingChunk 등 움직이는 지형에는
                     * 이 보정을 적용하지 않는다.
                     *
                     * 해당 접촉은 FallingTerrainInteraction의
                     * 매몰/탈출 로직에 맡긴다.
                     */
                    if (IsStaticTerrainCollider(other) == false)
                        continue;

                    ColliderDistance2D distance =
                        Physics2D.Distance(
                            box,
                            other);

                    if (distance.isValid == false ||
                        distance.isOverlapped == false)
                    {
                        continue;
                    }

                    /*
                     * distance는 overlap 상태에서 음수다.
                     * 가장 음수가 큰 것 = 가장 깊은 침투.
                     */
                    if (foundPenetration == false ||
                        distance.distance <
                        deepestSignedDistance)
                    {
                        foundPenetration = true;

                        deepestSignedDistance =
                            distance.distance;

                        deepestDistance =
                            distance;
                    }
                }

                if (foundPenetration == false)
                    break;

                /*
                 * Unity ColliderDistance2D 규칙:
                 *
                 * normal * distance가
                 * overlap을 해소하기 위해 Collider A를
                 * 이동시킬 최소 벡터가 된다.
                 */
                Vector2 correction =
                    deepestDistance.normal *
                    deepestDistance.distance;

                float toleranceSquared =
                    movementTolerance *
                    movementTolerance;

                if (correction.sqrMagnitude <=
                    toleranceSquared)
                {
                    break;
                }

                if (depenetrationPadding > 0f)
                {
                    correction +=
                        correction.normalized *
                        depenetrationPadding;
                }

                /*
                 * 여기서는 이미 잘못 겹친 상태를
                 * 복구하는 것이 목적이므로 MovePosition이 아니라
                 * 즉시 Rigidbody 위치를 보정한다.
                 */
                body.position += correction;

                /*
                 * 다음 반복의 overlap query가
                 * 방금 수정한 위치를 확실히 기준으로 사용하도록
                 * 물리 Transform을 동기화한다.
                 *
                 * 이 호출은 penetration이 실제 발생한 경우에만
                 * 실행되므로 정상 플레이에서는 비용이 없다.
                 */
                Physics2D.SyncTransforms();
            }

            return body.position;
        }

        /// <summary>
        /// 한 축의 이동량을 충돌 가능한 범위까지 제한한다.
        ///
        /// 기존의 축소 BoxCast 대신 실제 BoxCollider2D 자체를
        /// 지정 위치에서 Cast한다.
        /// </summary>
        private Vector2 ResolveMovement(
            Vector2 startPosition,
            Vector2 axis,
            float distance,
            ref float axisVelocity)
        {
            if (Mathf.Approximately(distance, 0f))
                return Vector2.zero;

            Vector2 direction =
                axis * Mathf.Sign(distance);

            float requestedDistance =
                Mathf.Abs(distance);

            ContactFilter2D filter =
                CreateCollisionFilter();

            castHits.Clear();

            /*
             * 핵심 보정 2
             *
             * 실제 BoxCollider2D 형상을 그대로 Cast한다.
             *
             * 기존 코드처럼
             *
             *   castSize.y -= collisionSkin * 2
             *   castSize.x -= collisionSkin * 2
             *
             * 를 하지 않는다.
             *
             * 따라서 검사 형상과 실제 Collider 형상이 달라져
             * 모서리만 Terrain 안으로 들어가는 상황을 방지한다.
             *
             * startPosition은 Collider 중심이 아니라
             * 이 BoxCollider가 붙어 있는 Rigidbody2D의 가상 위치다.
             */
            int hitCount =
                box.Cast(
                    startPosition,
                    body.rotation,
                    direction,
                    filter,
                    castHits,
                    requestedDistance + collisionSkin,
                    true);

            float allowedDistance =
                requestedDistance;

            bool foundBlockingHit = false;

            Vector2 blockingNormal =
                Vector2.zero;

            for (int i = 0;
                 i < hitCount;
                 i++)
            {
                RaycastHit2D hit =
                    castHits[i];

                if (ShouldIgnoreCollider(hit.collider))
                    continue;

                /*
                 * 핵심 보정 3
                 *
                 * 이동 방향을 실제로 가로막는 면만 처리한다.
                 *
                 * 예:
                 *
                 * 오른쪽 이동:
                 *   벽 normal = left
                 *   Dot(left, left) = 1 → 차단
                 *
                 * 오른쪽 이동 중 바닥:
                 *   바닥 normal = up
                 *   Dot(up, left) = 0 → 무시
                 *
                 * 따라서 실제 Collider 크기를 줄이지 않고도
                 * 바닥이 수평 이동을 막는 문제를 방지한다.
                 */
                float blockingDot =
                    Vector2.Dot(
                        hit.normal,
                        -direction);

                if (blockingDot <=
                    blockingNormalDotThreshold)
                {
                    continue;
                }

                float availableDistance =
                    Mathf.Max(
                        0f,
                        hit.distance -
                        collisionSkin);

                if (availableDistance <
                    allowedDistance)
                {
                    allowedDistance =
                        availableDistance;

                    blockingNormal =
                        hit.normal;

                    foundBlockingHit = true;
                }
            }

            /*
             * Mathf.Epsilon은 물리 이동 오차 판정용으로
             * 지나치게 작으므로 실제 tolerance를 사용한다.
             */
            bool wasBlocked =
                foundBlockingHit &&
                allowedDistance <
                requestedDistance -
                movementTolerance;

            if (wasBlocked)
            {
                /*
                 * 아래 방향 이동이 실제 바닥 성분을 가진 normal에
                 * 막힌 경우에만 grounded로 인정한다.
                 *
                 * 벽/모서리 접촉을 단순히
                 * "아래로 가다 막힘"이라는 이유로
                 * grounded 처리하지 않는다.
                 */
                if (direction.y < 0f &&
                    blockingNormal.y > 0.5f)
                {
                    grounded = true;
                }

                axisVelocity = 0f;
            }

            return direction *
                   allowedDistance;
        }

        private ContactFilter2D CreateCollisionFilter()
        {
            return new ContactFilter2D
            {
                useLayerMask = true,
                layerMask = collisionMask,
                useTriggers = false
            };
        }

        private bool ShouldIgnoreCollider(
            Collider2D other)
        {
            if (other == null)
                return true;

            if (other == box)
                return true;

            if (Physics2D.GetIgnoreCollision(
                    box,
                    other))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Static Terrain에 대해서만 자동 침투 복구를 수행한다.
        /// Rigidbody가 없으면 2D Physics상 Static Collider로 취급한다.
        /// </summary>
        private static bool IsStaticTerrainCollider(
            Collider2D other)
        {
            Rigidbody2D otherBody =
                other.attachedRigidbody;

            if (otherBody == null)
                return true;

            return otherBody.bodyType ==
                   RigidbodyType2D.Static;
        }
    }
}
