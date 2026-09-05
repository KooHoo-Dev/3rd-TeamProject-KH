using System;
using Character;
using UnityEngine;

namespace TerrainCollapse
{
    /// <summary>
    /// 플레이어 루트의 단일 조정자
    /// 입력, 이동 계산, 충돌, 지형 상태, 생명주기는 각각 별도 시스템에 위임하고 실행 순서와 외부 계약만 유지
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
    [RequireComponent(typeof(FallingTerrainInteraction), typeof(PlayerInventory))]
    public sealed class PlayerController : MonoBehaviour, IPlayerController
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
        [SerializeField, Min(0f)] private float movementTolerance = 0.0001f;
        [SerializeField, Range(0f, 0.25f)] private float blockingNormalDotThreshold = 0.01f;
        [SerializeField, Min(0f)] private float depenetrationPadding = 0.001f;
        [SerializeField, Range(1, 8)] private int depenetrationIterations = 4;

        private Rigidbody2D body;
        private BoxCollider2D box;
        private FallingTerrainInteraction fallingTerrainInteraction;
        private PlayerInventory inventory;
        private PlayerInputReader inputReader;
        private PlayerMovement movement;
        private PlayerCollisionMove collisionMove;
        private TerrainPenetrationResolver penetrationResolver;
        private PlayerTerrainState terrainState;
        private PlayerLifeCycle lifeCycle;
        private bool isWaitingForTerrainSnapshot;
        private Vector2 terrainSnapshotWaitingPosition;


        public bool IsBuried => terrainState.IsBuried;
        public bool IsClimbing => terrainState.IsClimbing;
        public bool IsDead => lifeCycle.IsDead;
        public bool IsGrounded => collisionMove.IsGrounded;
        public Vector2 Velocity => movement.Velocity;
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
            if (collisionMask.value == 0) collisionMask = LayerMask.GetMask("Terrain");
            body = GetComponent<Rigidbody2D>();
            box = GetComponent<BoxCollider2D>();
            fallingTerrainInteraction = GetComponent<FallingTerrainInteraction>();
            inventory = GetComponent<PlayerInventory>();
            if (terrainManager == null) terrainManager = FindFirstObjectByType<TerrainManager>();

            body.bodyType = RigidbodyType2D.Kinematic;
            body.useFullKinematicContacts = true;
            body.gravityScale = 0f;
            body.freezeRotation = true;

            inputReader = new PlayerInputReader();
            movement = new PlayerMovement(moveSpeed, climbSpeed, jumpSpeed, gravity);
            collisionMove = new PlayerCollisionMove(body, box, collisionMask, collisionSkin, movementTolerance, blockingNormalDotThreshold);
            penetrationResolver = new TerrainPenetrationResolver(body, box, collisionMask, movementTolerance, depenetrationPadding, depenetrationIterations);
            terrainState = new PlayerTerrainState();
            lifeCycle = new PlayerLifeCycle();
            if (GetComponent<PlayerHp>().IsDead) lifeCycle.EnterDead(false, false);
        }

        private void Update()
        {
            inputReader.Tick();
            if (isWaitingForTerrainSnapshot || lifeCycle.IsDead ||
                terrainState.IsInteractionLocked || terrainState.IsEscaping)
                return;
            if (inputReader.CurrentInput.ClimbTogglePressed) ToggleClimbState();
        }

        private void FixedUpdate()
        {
            if (isWaitingForTerrainSnapshot)
            {
                SetPosition(terrainSnapshotWaitingPosition);
                return;
            }
            if (lifeCycle.IsDead) { if (lifeCycle.IsDeadFalling) TickDeathFall(); return; }
            if (terrainState.IsBuried) return;
            if (terrainState.IsInteractionLocked) { SetPosition(terrainState.LockedPosition); return; }
            if (terrainState.IsEscaping) { TickEscapeMovement(); return; }

            // Cast 전에 Static Terrain의 미세 침투를 해소해 영구 끼임 방지
            Vector2 position = penetrationResolver.Resolve(body.position);
            movement.UpdateVelocity(inputReader.CurrentInput, terrainState.IsClimbing, collisionMove.IsGrounded, Time.fixedDeltaTime);
            CollisionMoveResult result = collisionMove.Move(position, movement.Velocity, Time.fixedDeltaTime);
            body.MovePosition(result.Position);
            movement.SetVelocity(result.Velocity);
        }

        public void EnterDeadState(bool isPositionLocked = false)
        {
            if (lifeCycle.IsDead) return;
            bool wasBuried = terrainState.IsBuried;
            lifeCycle.EnterDead(isPositionLocked, wasBuried);
            fallingTerrainInteraction.ResetState();
            fallingTerrainInteraction.enabled = false;
            bool wasClimbing = terrainState.IsClimbing;
            terrainState.ResetState();
            collisionMove.ClearGrounded();
            if (wasBuried || isPositionLocked)
            {
                movement.Stop();
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
            }
            else movement.StopHorizontal();
            if (wasClimbing) ClimbStateChanged?.Invoke(false);
            Debug.Log("[Terrain Collapse] Player died", this);
        }

        /// <summary>서버 지형 스냅샷을 받기 전 플레이어 입력과 이동을 고정</summary>
        public void SetWaitingForTerrainSnapshot(bool isWaiting)
        {
            isWaitingForTerrainSnapshot = isWaiting;
            movement.Stop();
            collisionMove.ClearGrounded();

            if (isWaiting == false) return;

            terrainSnapshotWaitingPosition = body.position;
            SetPosition(terrainSnapshotWaitingPosition);
        }

        public void Respawn(Vector2 spawnPosition)
        {
            body.position = spawnPosition;

            if (isWaitingForTerrainSnapshot) terrainSnapshotWaitingPosition = spawnPosition;

            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            Physics2D.SyncTransforms();
            movement.Stop();
            collisionMove.ClearGrounded();
            terrainState.ResetState();
            fallingTerrainInteraction.ResetState();
            fallingTerrainInteraction.enabled = true;
            lifeCycle.Respawn();
            Debug.Log("[Terrain Collapse] Player respawned", this);
        }

        public void ToggleClimbState()
        {
            if (terrainState.IsClimbing) terrainState.StopClimbing(); else terrainState.StartClimbing();
            movement.Stop();
            ClimbStateChanged?.Invoke(terrainState.IsClimbing);
            Debug.Log($"[PlayerController] Climb State: {terrainState.IsClimbing}", this);
        }

        public void CancelClimbing()
        {
            if (!terrainState.IsClimbing) return;
            terrainState.StopClimbing();
            ClimbStateChanged?.Invoke(false);
        }

        public void ApplyFallingVelocity(Vector2 fallingVelocity)
        {
            movement.SetVelocity(fallingVelocity);
            terrainState.StopEscape();
            collisionMove.ClearGrounded();
        }

        public void StartTerrainEscape(float targetX, float speed) => terrainState.StartEscape(targetX, speed);
        public void StopTerrainEscape() => terrainState.StopEscape();

        public void SetTerrainInteractionLocked(bool isLocked, Vector2 lockedPosition)
        {
            terrainState.SetInteractionLocked(isLocked, lockedPosition);
            if (!isLocked) return;
            movement.Stop();
            collisionMove.ClearGrounded();
            SetPosition(lockedPosition);
        }

        public void SetTerrainBuried(bool isTerrainBuried, Vector2 lockedPosition)
        {
            terrainState.SetBuried(isTerrainBuried, lockedPosition);
            if (!isTerrainBuried) return;
            movement.Stop();
            collisionMove.ClearGrounded();
            SetPosition(lockedPosition);
        }

        public void SetPosition(Vector2 position)
        {
            body.position = position;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
        }

        private void TickDeathFall()
        {
            movement.StopHorizontal();
            movement.ApplyGravity(Time.fixedDeltaTime);
            CollisionMoveResult result = collisionMove.MoveVertical(body.position, movement.Velocity, Time.fixedDeltaTime);
            body.MovePosition(result.Position);
            movement.SetVelocity(result.Velocity);
            if (result.Grounded || Mathf.Approximately(result.Velocity.y, 0f)) { movement.Stop(); lifeCycle.StopDeadFall(); }
        }

        private void TickEscapeMovement()
        {
            collisionMove.ClearGrounded();
            movement.ApplyGravity(Time.fixedDeltaTime);
            CollisionMoveResult result = collisionMove.MoveEscape(body.position, movement.Velocity, terrainState.EscapeTargetX, terrainState.EscapeSpeed, Time.fixedDeltaTime);
            body.MovePosition(result.Position);
            movement.SetVelocity(result.Velocity);
        }
    }
}
