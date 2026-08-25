using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TerrainCollapse
{
    /// <summary>
    /// Runtime 2D character controller for the TerrainCollapse world.
    /// This component is intentionally independent from TestPlayerController.
    /// It uses TerrainManager only for grid queries and collapse escape handling.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
    public sealed class CharacterController2D : MonoBehaviour, IFallingTerrainEscapeTarget
    {
        [Header("Movement")]
        [SerializeField, Min(0f)] private float moveSpeed = 6f;
        [SerializeField, Min(0f)] private float acceleration = 45f;
        [SerializeField, Min(0f)] private float deceleration = 60f;
        [SerializeField, Min(0f)] private float jumpSpeed = 9f;
        [SerializeField, Min(0f)] private float gravity = 26f;
        [SerializeField, Min(0f)] private float maxFallSpeed = 18f;
        [SerializeField, Range(0f, 0.5f)] private float coyoteTime = 0.1f;
        [SerializeField, Range(0f, 0.3f)] private float jumpBufferTime = 0.12f;

        [Header("Collision")]
        [SerializeField] private LayerMask collisionMask = ~0;
        [SerializeField, Min(0f)] private float collisionSkin = 0.02f;
        [SerializeField, Min(0f)] private float groundCheckDistance = 0.05f;
        [SerializeField, Min(0f)] private float maxSlopeAngle = 50f;

        [Header("Terrain Collapse")]
        [SerializeField] private TerrainManager terrainManager;
        [SerializeField, Min(0.001f)] private float escapeProbeStep = 0.05f;
        [SerializeField, Min(0f)] private float escapeProbeDistance = 0.5f;
        [SerializeField, Min(0.01f)] private float escapeSpeed = 5f;
        [SerializeField] private int normalSortingOrder = 2;
        [SerializeField] private int buriedSortingOrder = -1;

        private readonly RaycastHit2D[] castHits = new RaycastHit2D[16];
        private readonly Collider2D[] overlapHits = new Collider2D[16];
        private readonly System.Collections.Generic.Dictionary<long, FallingTerrainContactResult> resolvedCollapses = new();

        private Rigidbody2D body;
        private BoxCollider2D box;
        private SpriteRenderer spriteRenderer;
        private Vector2 velocity;
        private float lastGroundedTime = float.NegativeInfinity;
        private float lastJumpPressedTime = float.NegativeInfinity;
        private bool grounded;
        private bool buried;
        private bool escaping;
        private float escapeTargetX;
        private Vector2 buriedPosition;
        private FallingChunk followedFallingChunk;
        private Vector3 followedChunkLocalPosition;
        private Coroutine buriedRecheckRoutine;

        public bool IsGrounded => grounded;
        public bool IsBuried => buried;
        public Vector2 Velocity => velocity;

        public event Action<bool> GroundedChanged;
        public event Action<bool> BurialStateChanged;
        public event Action<float> BurialDamageRequested;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            box = GetComponent<BoxCollider2D>();
            spriteRenderer = GetComponent<SpriteRenderer>();

            if (terrainManager == null)
                terrainManager = FindFirstObjectByType<TerrainManager>();

            body.bodyType = RigidbodyType2D.Kinematic;
            body.useFullKinematicContacts = true;
            body.gravityScale = 0f;
            body.freezeRotation = true;

            if (spriteRenderer != null)
                spriteRenderer.sortingOrder = normalSortingOrder;
        }

        private void OnEnable()
        {
            if (terrainManager != null)
                terrainManager.TerrainChanged += HandleTerrainChanged;
        }

        private void OnDisable()
        {
            if (terrainManager != null)
                terrainManager.TerrainChanged -= HandleTerrainChanged;

            if (buriedRecheckRoutine != null)
            {
                StopCoroutine(buriedRecheckRoutine);
                buriedRecheckRoutine = null;
            }
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
                lastJumpPressedTime = Time.time;
        }

        private void FixedUpdate()
        {
            if (buried)
            {
                LockBurialPosition();
                return;
            }

            if (escaping)
            {
                TickEscapeMovement();
                return;
            }

            float input = ReadHorizontalInput();
            float targetSpeed = input * moveSpeed;
            float rate = Mathf.Abs(targetSpeed) > Mathf.Abs(velocity.x)
                ? acceleration
                : deceleration;

            velocity.x = Mathf.MoveTowards(
                velocity.x,
                targetSpeed,
                rate * Time.fixedDeltaTime);

            UpdateGrounded();

            if (Time.time - lastJumpPressedTime <= jumpBufferTime &&
                Time.time - lastGroundedTime <= coyoteTime)
            {
                velocity.y = jumpSpeed;
                lastJumpPressedTime = float.NegativeInfinity;
                grounded = false;
                RaiseGroundedChanged(false);
            }

            velocity.y = Mathf.Max(
                velocity.y - gravity * Time.fixedDeltaTime,
                -maxFallSpeed);

            Vector2 position = body.position;
            position += ResolveAxisMovement(
                Vector2.right,
                velocity.x * Time.fixedDeltaTime,
                ref velocity.x);

            bool wasGrounded = grounded;
            grounded = false;

            position += ResolveAxisMovement(
                Vector2.up,
                velocity.y * Time.fixedDeltaTime,
                ref velocity.y);

            if (!wasGrounded && grounded)
                lastGroundedTime = Time.time;

            body.MovePosition(position);
        }

        private float ReadHorizontalInput()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return 0f;

            return (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f) -
                   (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1f : 0f);
        }

        private Vector2 ResolveAxisMovement(
            Vector2 axis,
            float distance,
            ref float axisVelocity)
        {
            if (Mathf.Approximately(distance, 0f))
                return Vector2.zero;

            Vector2 direction = axis * Mathf.Sign(distance);
            float requestedDistance = Mathf.Abs(distance);

            ContactFilter2D filter = new ContactFilter2D
            {
                useLayerMask = true,
                layerMask = collisionMask,
                useTriggers = false
            };

            int hitCount = box.Cast(
                direction,
                filter,
                castHits,
                requestedDistance + collisionSkin);

            float allowedDistance = requestedDistance;

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit2D hit = castHits[i];
                if (hit.collider == null || hit.collider == box)
                    continue;

                if (axis == Vector2.up && hit.normal.y < Mathf.Cos(maxSlopeAngle * Mathf.Deg2Rad))
                    continue;

                allowedDistance = Mathf.Min(
                    allowedDistance,
                    Mathf.Max(0f, hit.distance - collisionSkin));
            }

            if (allowedDistance + Mathf.Epsilon < requestedDistance)
            {
                if (direction.y < 0f)
                {
                    grounded = true;
                    lastGroundedTime = Time.time;
                    RaiseGroundedChanged(true);
                }

                axisVelocity = 0f;
            }

            return direction * allowedDistance;
        }

        private void UpdateGrounded()
        {
            ContactFilter2D filter = new ContactFilter2D
            {
                useLayerMask = true,
                layerMask = collisionMask,
                useTriggers = false
            };

            int hitCount = box.Cast(
                Vector2.down,
                filter,
                castHits,
                groundCheckDistance + collisionSkin);

            bool newGrounded = false;
            float minimumNormalY = Mathf.Cos(maxSlopeAngle * Mathf.Deg2Rad);

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit2D hit = castHits[i];
                if (hit.collider != null &&
                    hit.collider != box &&
                    hit.normal.y >= minimumNormalY)
                {
                    newGrounded = true;
                    break;
                }
            }

            if (newGrounded)
                lastGroundedTime = Time.time;

            if (grounded != newGrounded)
            {
                grounded = newGrounded;
                RaiseGroundedChanged(grounded);
            }
        }

        private void TickEscapeMovement()
        {
            Vector2 position = body.position;
            position.x = Mathf.MoveTowards(
                position.x,
                escapeTargetX,
                escapeSpeed * Time.fixedDeltaTime);

            velocity.x = 0f;
            velocity.y = Mathf.Max(
                velocity.y - gravity * Time.fixedDeltaTime,
                -maxFallSpeed);

            position += ResolveAxisMovement(
                Vector2.up,
                velocity.y * Time.fixedDeltaTime,
                ref velocity.y);

            body.MovePosition(position);

            if (Mathf.Abs(position.x - escapeTargetX) <= Mathf.Max(collisionSkin, 0.001f))
            {
                escaping = false;
                UpdateGrounded();
            }
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            RememberFallingChunk(collision);
        }

        private void OnCollisionStay2D(Collision2D collision)
        {
            RememberFallingChunk(collision);
        }

        private void RememberFallingChunk(Collision2D collision)
        {
            if (collision == null || collision.rigidbody == null)
                return;

            FallingChunk chunk = collision.rigidbody.GetComponent<FallingChunk>();
            if (chunk != null && !chunk.IsComplete)
                followedFallingChunk = chunk;
        }

        public FallingTerrainContactResult ResolveFallingTerrainContact(
            Bounds terrainBounds,
            float terrainCellSize,
            long collapseId)
        {
            if (resolvedCollapses.TryGetValue(collapseId, out FallingTerrainContactResult cached))
                return cached;

            if (!TryRegisterFallingChunkContact(terrainBounds, collapseId))
                return FallingTerrainContactResult.Ignored;

            // FallingChunk가 이 메서드를 호출하는 시점은 이미 실제 충돌이
            // 발생한 뒤다. 따라서 Chunk 중심이 플레이어 중심보다 위에 있는지를
            // 기준으로 매몰을 판정하면, 큰 Chunk / 낮은 충돌면 / 경계 접촉에서
            // 정상적인 매몰을 놓칠 수 있다.
            //
            // 여기서는 실제 플레이어 Collider와 해당 붕괴 Chunk가 접촉하고 있는지
            // 를 기준으로 판정한다. 충돌 이벤트가 호출된 직후이므로 아주 작은
            // skin까지 허용한다.
            if (!IsFallingChunkContactingPlayer(followedFallingChunk))
                return FallingTerrainContactResult.Ignored;

            if (!buried && TryFindEscape(terrainBounds, terrainCellSize))
            {
                resolvedCollapses[collapseId] = FallingTerrainContactResult.Escaped;
                return FallingTerrainContactResult.Escaped;
            }

            EnterBuriedState();
            resolvedCollapses[collapseId] = FallingTerrainContactResult.NoEscapeSpace;
            return FallingTerrainContactResult.NoEscapeSpace;
        }

        private bool TryRegisterFallingChunkContact(Bounds terrainBounds, long collapseId)
        {
            if (followedFallingChunk != null &&
                followedFallingChunk.CollapseId == collapseId &&
                !followedFallingChunk.IsComplete)
            {
                return true;
            }

            FallingChunk[] chunks = FindObjectsOfType<FallingChunk>();
            FallingChunk closest = null;
            float closestDistance = float.PositiveInfinity;

            for (int i = 0; i < chunks.Length; i++)
            {
                FallingChunk chunk = chunks[i];
                if (chunk == null ||
                    chunk.IsComplete ||
                    chunk.CollapseId != collapseId)
                    continue;

                Bounds chunkBounds = chunk.WorldBounds;
                if (!chunkBounds.Intersects(box.bounds))
                {
                    // 충돌 콜백 직후에는 Bounds가 아주 미세하게 떨어져 보일 수
                    // 있으므로 skin만큼 확장해서 한 번 더 확인한다.
                    chunkBounds.Expand(collisionSkin * 2f);
                    if (!chunkBounds.Intersects(box.bounds))
                        continue;
                }

                float distance = Vector2.SqrMagnitude(
                    (Vector2)chunkBounds.center - (Vector2)box.bounds.center);

                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closest = chunk;
                }
            }

            if (closest == null)
                return false;

            followedFallingChunk = closest;
            followedChunkLocalPosition =
                followedFallingChunk.transform.InverseTransformPoint(body.position);

            return true;
        }

        private bool IsFallingChunkContactingPlayer(FallingChunk chunk)
        {
            if (chunk == null || chunk.ShapeCollider == null || box == null)
                return false;

            ColliderDistance2D distance = Physics2D.Distance(
                box,
                chunk.ShapeCollider);

            return distance.isOverlapped || distance.distance <= collisionSkin;
        }

        private bool TryFindEscape(Bounds chunkBounds, float cellSize)
        {
            if (terrainManager == null || terrainManager.IsReady == false)
                return false;

            if (!terrainManager.TryWorldToCell(box.bounds.center, out Vector2Int playerCell))
                return false;

            int preferredDirection = transform.position.x <= chunkBounds.center.x ? -1 : 1;

            if (TryEscapeToCell(playerCell + Vector2Int.right * preferredDirection))
                return true;

            if (TryEscapeToCell(playerCell + Vector2Int.left * preferredDirection))
                return true;

            // If the immediate cell is not enough, probe within half a tile.
            float maximumDistance = Mathf.Min(
                Mathf.Max(0f, escapeProbeDistance),
                Mathf.Max(cellSize * 0.5f, 0f));

            int probeCount = Mathf.Max(
                1,
                Mathf.CeilToInt(maximumDistance / escapeProbeStep));

            for (int i = 1; i <= probeCount; i++)
            {
                float distance = Mathf.Min(i * escapeProbeStep, maximumDistance);

                if (TryMoveToEmptyPhysicalSpace(-distance) ||
                    TryMoveToEmptyPhysicalSpace(distance))
                    return true;
            }

            return false;
        }

        private bool TryEscapeToCell(Vector2Int cell)
        {
            if (terrainManager.HasTerrain(cell))
                return false;

            if (!terrainManager.TryCellToWorldCenter(cell, out Vector3 center))
                return false;

            float offsetX = center.x - box.bounds.center.x;
            Bounds candidate = box.bounds;
            candidate.center += Vector3.right * offsetX;

            if (!IsGridSpaceEmpty(candidate) || !IsPhysicalSpaceEmpty(offsetX))
                return false;

            escapeTargetX = body.position.x + offsetX;
            escaping = true;
            velocity.x = 0f;
            return true;
        }

        private bool TryMoveToEmptyPhysicalSpace(float offsetX)
        {
            if (!IsPhysicalSpaceEmpty(offsetX))
                return false;

            body.position += Vector2.right * offsetX;
            Physics2D.SyncTransforms();
            return IsGridSpaceEmpty(box.bounds);
        }

        private bool IsPhysicalSpaceEmpty(float offsetX)
        {
            Vector2 center = (Vector2)box.bounds.center + Vector2.right * offsetX;
            Vector2 size = (Vector2)box.bounds.size -
                           Vector2.one * Mathf.Min(collisionSkin, Mathf.Min(box.bounds.size.x, box.bounds.size.y) * 0.5f);

            ContactFilter2D filter = new ContactFilter2D
            {
                useLayerMask = true,
                layerMask = collisionMask,
                useTriggers = false
            };

            int count = Physics2D.OverlapBox(
                center,
                size,
                0f,
                filter,
                overlapHits);

            for (int i = 0; i < count; i++)
            {
                Collider2D hit = overlapHits[i];
                if (hit != null && hit != box)
                    return false;
            }

            return true;
        }

        private bool IsGridSpaceEmpty(Bounds bounds)
        {
            if (terrainManager == null)
                return false;

            Vector3 inset = Vector3.one * collisionSkin;

            if (!terrainManager.TryWorldToCell(bounds.min + inset, out Vector2Int min) ||
                !terrainManager.TryWorldToCell(bounds.max - inset, out Vector2Int max))
                return false;

            for (int y = min.y; y <= max.y; y++)
            {
                for (int x = min.x; x <= max.x; x++)
                {
                    if (terrainManager.HasTerrain(new Vector2Int(x, y)))
                        return false;
                }
            }

            return true;
        }

        private void EnterBuriedState()
        {
            if (buried)
                return;

            buried = true;
            escaping = false;
            grounded = false;
            velocity = Vector2.zero;

            // 매몰 순간의 실제 월드 위치를 기본 고정 위치로 보존한다.
            // Grid Cell 중심으로 강제로 옮기면 플레이어 Collider가 이미 편입된
            // 지형 Collider 안으로 들어갈 수 있으므로, 매몰 판정 자체가 발생한
            // 위치를 그대로 기준점으로 사용한다.
            buriedPosition = body.position;

            // 매몰을 발생시킨 낙하 Chunk가 아직 존재한다면 그 Chunk의 좌표계를
            // 기준으로 플레이어를 붙인다. 이후 Chunk가 움직이는 동안 플레이어도
            // 동일한 이동/회전을 따라가며, Chunk가 Grid에 편입될 때는 마지막
            // 월드 위치를 유지한다.
            AttachToFollowedFallingChunk();
            LockBurialPosition();

            if (spriteRenderer != null)
                spriteRenderer.sortingOrder = buriedSortingOrder;

            BurialStateChanged?.Invoke(true);
        }

        private void ExitBuriedState()
        {
            if (!buried)
                return;

            buried = false;
            velocity = Vector2.zero;
            followedFallingChunk = null;

            if (spriteRenderer != null)
                spriteRenderer.sortingOrder = normalSortingOrder;

            BurialStateChanged?.Invoke(false);
        }

        private void LockBurialPosition()
        {
            if (followedFallingChunk != null)
            {
                if (followedFallingChunk.IsComplete)
                {
                    // Chunk가 정착/소멸 단계에 들어가면 더 이상 따라가지 않는다.
                    // 마지막 프레임의 위치를 buriedPosition으로 확정한다.
                    buriedPosition = body.position;
                    followedFallingChunk = null;
                }
                else
                {
                    buriedPosition = followedFallingChunk.transform.TransformPoint(
                        followedChunkLocalPosition);
                    body.position = buriedPosition;
                }
            }
            else
            {
                body.position = buriedPosition;
            }

            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
        }

        private void AttachToFollowedFallingChunk()
        {
            if (followedFallingChunk == null || followedFallingChunk.IsComplete)
                return;

            followedChunkLocalPosition = followedFallingChunk.transform.InverseTransformPoint(body.position);
        }

        private void HandleTerrainChanged(TerrainChangeReason _)
        {
            if (!buried)
                return;

            if (buriedRecheckRoutine != null)
                StopCoroutine(buriedRecheckRoutine);

            buriedRecheckRoutine = StartCoroutine(RecheckBurialAfterTerrainRefresh());
        }

        private IEnumerator RecheckBurialAfterTerrainRefresh()
        {
            yield return new WaitForEndOfFrame();
            Physics2D.SyncTransforms();

            if (IsGridSpaceEmpty(box.bounds) && IsPhysicalSpaceEmpty(0f))
            {
                ExitBuriedState();
            }
            else
            {
                float maximumDistance = terrainManager != null
                    ? terrainManager.CellSize * 0.5f
                    : 0f;

                int probeCount = Mathf.Max(
                    1,
                    Mathf.CeilToInt(maximumDistance / escapeProbeStep));

                for (int i = 1; i <= probeCount; i++)
                {
                    float distance = Mathf.Min(i * escapeProbeStep, maximumDistance);

                    if (TryMoveToEmptyPhysicalSpace(-distance) ||
                        TryMoveToEmptyPhysicalSpace(distance))
                    {
                        ExitBuriedState();
                        break;
                    }

                    // 탈출 공간을 찾지 못한 경우에도 probing 과정에서
                    // 캐릭터가 매몰 고정 위치를 벗어나지 않도록 복원한다.
                    LockBurialPosition();
                }
            }

            if (buried)
                LockBurialPosition();

            buriedRecheckRoutine = null;
        }

        private void RaiseGroundedChanged(bool value)
        {
            GroundedChanged?.Invoke(value);
        }
    }
}
