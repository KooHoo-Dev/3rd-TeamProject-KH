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
        private bool isInSpawnArea;
        private float dropThroughTargetY;
        private bool isDroppingThroughSpawnPlatform;
        private float climbThroughPlatformTargetY = float.NegativeInfinity;
        private bool isClimbingThroughSpawnPlatform;


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
        public bool IsInSpawnArea => isInSpawnArea;
        public bool IsDroppingThroughSpawnPlatform => isDroppingThroughSpawnPlatform;

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
            if (lifeCycle.IsDead || terrainState.IsInteractionLocked || terrainState.IsEscaping) return;
            if (inputReader.CurrentInput.ClimbTogglePressed && !isInSpawnArea) ToggleClimbState();
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

            UpdateSpawnAreaState();

            if (isDroppingThroughSpawnPlatform && body.position.y <= dropThroughTargetY)
                isDroppingThroughSpawnPlatform = false;
            bool isDroppingThroughPlatform = isDroppingThroughSpawnPlatform;

            // 플랫폼을 통과하는 동안 침투 보정기가 같은 경계로 되밀지 않게 한다.
            bool isPassingThroughSpawnPlatform =
                isDroppingThroughPlatform || isClimbingThroughSpawnPlatform;
            Vector2 position = isPassingThroughSpawnPlatform
                ? body.position
                : penetrationResolver.Resolve(body.position);
            collisionMove.IgnoreDownwardCollisions = isDroppingThroughPlatform;
            collisionMove.IgnoreUpwardCollisions = isClimbingThroughSpawnPlatform;
            movement.UpdateVelocity(
                inputReader.CurrentInput,
                terrainState.IsClimbing,
                collisionMove.IsGrounded,
                Time.fixedDeltaTime);
            CollisionMoveResult result = collisionMove.Move(position, movement.Velocity, Time.fixedDeltaTime);
            body.MovePosition(result.Position);
            movement.SetVelocity(result.Velocity);
            collisionMove.IgnoreDownwardCollisions = false;
            collisionMove.IgnoreUpwardCollisions = false;
        }

        /// <summary>
        /// 서버 지형 스냅샷의 스폰 안전 구역에 진입하면 등반과 아이템 사용을 해제한다.
        /// 실제 서버도 같은 구역 좌표로 등반 상태를 무효화한다.
        /// </summary>
        private void UpdateSpawnAreaState()
        {
            RoomClient roomClient = RoomClient.Instance;
            isInSpawnArea = roomClient != null && roomClient.IsInSpawnArea(body.position);

            UpdateClimbThroughSpawnPlatform();
            if (isInSpawnArea && terrainState.IsClimbing && isClimbingThroughSpawnPlatform == false)
                CancelClimbing();

            PlayerInputData input = inputReader.CurrentInput;
            if (roomClient == null || roomClient.IsGameplayActive == false ||
                isInSpawnArea == false ||
                isDroppingThroughSpawnPlatform ||
                input.JumpPressed == false || input.MoveY >= -0.01f ||
                TryGetPlatformBelowFeet(out Vector2Int platformCell) == false)
                return;

            BeginDropThroughSpawnPlatform(platformCell);
        }

        private void BeginDropThroughSpawnPlatform(Vector2Int platformCell)
        {
            // 스폰 플랫폼의 하단을 완전히 지날 때까지 아래 충돌을 무시한다.
            // 아래 한 칸은 생성기에서 비워 둬서 일반 지형을 통과하지 않는다.
            if (terrainManager.TryCellToWorldCenter(platformCell, out Vector3 platformCenter))
            {
                dropThroughTargetY = platformCenter.y - terrainManager.CellSize * 0.5f -
                                     box.bounds.extents.y - collisionSkin;
            }
            else dropThroughTargetY = float.NegativeInfinity;

            isDroppingThroughSpawnPlatform = true;
            collisionMove.ClearGrounded();
        }

        private void UpdateClimbThroughSpawnPlatform()
        {
            if (terrainState.IsClimbing == false || terrainManager == null || terrainManager.CellSize <= 0f)
            {
                isClimbingThroughSpawnPlatform = false;
                climbThroughPlatformTargetY = float.NegativeInfinity;
                return;
            }

            if (TryGetSpawnPlatformAboveHead(out Vector2Int platformCell) &&
                terrainManager.TryCellToWorldCenter(platformCell, out Vector3 platformCenter))
            {
                // 몸 전체가 플랫폼 상단보다 위로 올라갈 때까지 통과 상태를 유지한다.
                climbThroughPlatformTargetY = platformCenter.y + terrainManager.CellSize * 0.5f +
                                              box.bounds.extents.y + collisionSkin;
            }

            isClimbingThroughSpawnPlatform =
                body.position.y < climbThroughPlatformTargetY;
        }

        private bool TryGetSpawnPlatformAboveHead(out Vector2Int cell)
        {
            cell = default;
            float cellSize = terrainManager.CellSize;
            float headY = box.bounds.max.y + collisionSkin;

            // 머리 바로 위부터 한 칸 앞까지 확인해, 플랫폼 하단에 닿기 전부터 통과를 시작한다.
            for (float offset = 0f; offset <= cellSize; offset += cellSize * 0.5f)
            {
                Vector3 probe = new(body.position.x, headY + offset, 0f);
                if (terrainManager.TryWorldToCell(probe, out Vector2Int candidate) &&
                    terrainManager.TryGetTileType(candidate, out TerrainTileType type) &&
                    type == TerrainTileType.SpawnPlatform)
                {
                    cell = candidate;
                    return true;
                }
            }

            return false;
        }

        private bool TryGetPlatformBelowFeet(out Vector2Int cell)
        {
            cell = default;
            if (terrainManager == null || terrainManager.CellSize <= 0f) return false;

            // 현재 발과 플랫폼 사이의 작은 간격에서도 미리 감지해야 Cast가
            // 플랫폼 상단에서 먼저 멈추지 않는다.
            Vector3 probe = new(
                body.position.x,
                box.bounds.min.y - collisionSkin - terrainManager.CellSize * 0.5f,
                0f);
            return terrainManager.TryWorldToCell(probe, out cell) &&
                   terrainManager.TryGetTileType(cell, out TerrainTileType type) &&
                   type == TerrainTileType.SpawnPlatform;
        }

        private bool TryGetSpawnPlatformInsideBody(out Vector2Int cell)
        {
            cell = default;
            if (terrainManager == null) return false;

            Bounds bounds = box.bounds;
            // 몸 안에 플랫폼 타일이 겹쳐진 경우만 잡는다. 플랫폼 위로 완전히 올라온
            // 정상 상태에서는 이 조건이 성립하지 않아, 다시 아래로 떨어뜨리지 않는다.
            float[] xProbes = { bounds.min.x + collisionSkin, bounds.center.x, bounds.max.x - collisionSkin };
            float[] yProbes = { bounds.min.y + collisionSkin, bounds.center.y, bounds.max.y - collisionSkin };
            foreach (float x in xProbes)
            foreach (float y in yProbes)
            {
                if (terrainManager.TryWorldToCell(new Vector3(x, y, 0f), out Vector2Int candidate) &&
                    terrainManager.TryGetTileType(candidate, out TerrainTileType type) &&
                    type == TerrainTileType.SpawnPlatform)
                {
                    cell = candidate;
                    return true;
                }
            }

            return false;
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
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            Physics2D.SyncTransforms();
            movement.Stop();
            collisionMove.ClearGrounded();
            terrainState.ResetState();
            fallingTerrainInteraction.ResetState();
            fallingTerrainInteraction.enabled = true;
            lifeCycle.Respawn();
            if (isWaitingForTerrainSnapshot)
                terrainSnapshotWaitingPosition = spawnPosition;
            Debug.Log("[Terrain Collapse] Player respawned", this);
        }

        public void ToggleClimbState()
        {
            if (isInSpawnArea && !terrainState.IsClimbing) return;
            if (terrainState.IsClimbing)
                CancelClimbing();
            else
            {
                terrainState.StartClimbing();
                ClimbStateChanged?.Invoke(true);
            }
            movement.Stop();
            Debug.Log($"[PlayerController] Climb State: {terrainState.IsClimbing}", this);
        }

        public void CancelClimbing()
        {
            if (!terrainState.IsClimbing) return;

            // 플랫폼 내부에서 잡기를 풀면 침투 보정에 의해 플랫폼 위에 다시
            // 고정될 수 있다. 이 경우에는 하향 점프와 동일하게 하단까지 통과시킨다.
            Vector2Int platformCell = default;
            bool dropThroughPlatform = TryGetSpawnPlatformInsideBody(out platformCell);
            terrainState.StopClimbing();
            ClimbStateChanged?.Invoke(false);
            if (dropThroughPlatform) BeginDropThroughSpawnPlatform(platformCell);
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
