using System.Collections.Generic;
using UnityEngine;

namespace TerrainCollapse
{
    // 필드에 생성된 아이템의 종류와 수량 보관

    [RequireComponent(typeof(SpriteRenderer))]
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(CircleCollider2D))]
    public sealed class WorldItemDrop : MonoBehaviour, IFallingTerrainEscapeTarget
    {
        private const float ColliderRadius = 0.45f;
        private const float CollisionSkin = 0.02f;

        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private float gravityScale = 1f;
        [SerializeField, Min(0.01f)] private float escapePushSpeed = 4f;
        [SerializeField, Min(0.01f)] private float escapeSmoothTime = 0.12f;
        [SerializeField, Min(1)] private int escapeSearchCellCount = 256;

        private readonly HashSet<long> resolvedCollapses = new();

        private TerrainManager terrainManager;
        private Rigidbody2D body;
        private CircleCollider2D itemCollider;
        private Vector2 escapeTargetPosition;
        private Vector2 escapeVelocity;
        private bool isEscaping;
        private bool isCollected;

        public int ItemID { get; private set; }
        public int Quantity { get; private set; }

        private void Awake()
        {
            TerrainPhysicsLayers.ConfigureDropCollision();
            gameObject.layer = TerrainPhysicsLayers.ItemDrop;

            if (spriteRenderer == null)
                spriteRenderer = GetComponent<SpriteRenderer>();

            body = GetComponent<Rigidbody2D>();
            if (body == null)
                body = gameObject.AddComponent<Rigidbody2D>();

            body.bodyType = RigidbodyType2D.Dynamic;
            body.gravityScale = gravityScale;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            itemCollider = GetComponent<CircleCollider2D>();
            if (itemCollider == null)
                itemCollider = gameObject.AddComponent<CircleCollider2D>();

            itemCollider.radius = ColliderRadius;
            terrainManager = FindFirstObjectByType<TerrainManager>();
        }

        private void FixedUpdate()
        {
            if (isEscaping == false) return;

            Vector2 position = Vector2.SmoothDamp(
                body.position,
                escapeTargetPosition,
                ref escapeVelocity,
                escapeSmoothTime,
                escapePushSpeed,
                Time.fixedDeltaTime);

            if (Vector2.SqrMagnitude(position - escapeTargetPosition) <= CollisionSkin * CollisionSkin)
                position = escapeTargetPosition;

            body.MovePosition(position);

            if (position == escapeTargetPosition == false) return;

            body.linearVelocity = Vector2.zero;
            escapeVelocity = Vector2.zero;
            body.gravityScale = gravityScale;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            itemCollider.isTrigger = false;
            isEscaping = false;
        }

        public void Initialize(ItemDef itemDef, int quantity)
        {
            ItemID = itemDef.ID;
            Quantity = Mathf.Max(1, quantity);
            if (itemDef.Sprite != null)
            {
                spriteRenderer.sprite = itemDef.Sprite;
                spriteRenderer.color = Color.white;
            }
            else
            {
                spriteRenderer.color = ItemID switch
                {
                    100 => new Color(0.15f, 0.15f, 0.15f),
                    101 => new Color(0.7f, 0.45f, 0.25f),
                    _ => Color.magenta
                };
            }

            gameObject.name = $"WorldItem_{itemDef.Name}_{Quantity}";
        }

        public FallingTerrainContactResult ResolveFallingTerrainContact(FallingChunk fallingChunk)
        {
            if (fallingChunk == null || terrainManager == null || terrainManager.IsReady == false)
                return FallingTerrainContactResult.Ignored;

            if (IsBeingCoveredBy(fallingChunk) == false)
                return FallingTerrainContactResult.Ignored;

            if (resolvedCollapses.Add(fallingChunk.CollapseId) == false)
                return FallingTerrainContactResult.Escaped;

            if (TryFindNearestEscapeTarget(fallingChunk, out Vector2 targetPosition) == false)
                return FallingTerrainContactResult.Ignored;

            escapeTargetPosition = targetPosition;
            escapeVelocity = Vector2.zero;
            isEscaping = true;
            body.linearVelocity = Vector2.zero;
            body.gravityScale = 0f;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            itemCollider.isTrigger = true;

            return FallingTerrainContactResult.Escaped;
        }

        private bool TryFindNearestEscapeTarget(FallingChunk fallingChunk, out Vector2 targetPosition)
        {
            targetPosition = body.position;

            if (terrainManager.TryWorldToCell(body.position, out Vector2Int originCell) == false)
                return false;

            for (int distance = 0; distance <= escapeSearchCellCount; distance++)
            {
                Vector2Int leftCell = originCell + Vector2Int.left * distance;
                bool hasLeftTarget = TryGetEscapeTarget(
                    leftCell,
                    fallingChunk,
                    out Vector2 leftTarget);

                if (distance == 0)
                {
                    if (hasLeftTarget)
                    {
                        targetPosition = leftTarget;
                        return true;
                    }

                    continue;
                }

                Vector2Int rightCell = originCell + Vector2Int.right * distance;
                bool hasRightTarget = TryGetEscapeTarget(
                    rightCell,
                    fallingChunk,
                    out Vector2 rightTarget);

                if (hasLeftTarget == false && hasRightTarget == false) continue;

                if (hasLeftTarget && hasRightTarget)
                {
                    targetPosition = Vector2.SqrMagnitude(body.position - leftTarget) <=
                                     Vector2.SqrMagnitude(body.position - rightTarget)
                        ? leftTarget
                        : rightTarget;
                }
                else targetPosition = hasLeftTarget ? leftTarget : rightTarget;

                return true;
            }

            return false;
        }

        private bool TryGetEscapeTarget(
            Vector2Int cell,
            FallingChunk fallingChunk,
            out Vector2 targetPosition)
        {
            targetPosition = default;

            if (terrainManager.HasTerrain(cell)) return false;
            if (terrainManager.TryCellToWorldCenter(cell, out Vector3 cellCenter) == false) return false;

            Vector2 safeTargetPosition = CalculateSafeTargetPosition(cell, cellCenter);
            Bounds candidate = itemCollider.bounds;
            candidate.center = new Vector3(
                safeTargetPosition.x,
                safeTargetPosition.y,
                candidate.center.z);

            if (IsGridSpaceEmpty(candidate) == false) return false;
            if (IsInFallingTerrainPath(candidate, fallingChunk)) return false;
            if (IsEscapePathClear(safeTargetPosition) == false) return false;

            targetPosition = safeTargetPosition;

            return true;
        }

        private Vector2 CalculateSafeTargetPosition(Vector2Int cell, Vector3 cellCenter)
        {
            float targetY = body.position.y;
            Vector2Int belowCell = cell + Vector2Int.down;

            if (terrainManager.HasTerrain(belowCell))
            {
                float cellBottom = cellCenter.y - terrainManager.CellSize * 0.5f;
                float restingY = cellBottom + itemCollider.bounds.extents.y + CollisionSkin;
                targetY = Mathf.Max(targetY, restingY);
            }

            return new Vector2(cellCenter.x, targetY);
        }

        private bool IsBeingCoveredBy(FallingChunk fallingChunk)
        {
            Bounds itemBounds = itemCollider.bounds;
            float halfCellSize = fallingChunk.CellSize * 0.5f;

            foreach (Vector2Int localCell in fallingChunk.LocalCells)
            {
                Vector3 tileCenter = fallingChunk.GetLocalCellCenterWorld(localCell);
                float tileMinX = tileCenter.x - halfCellSize;
                float tileMaxX = tileCenter.x + halfCellSize;

                if (tileCenter.y <= itemBounds.center.y) continue;
                if (itemBounds.max.x <= tileMinX || itemBounds.min.x >= tileMaxX) continue;

                return true;
            }

            return false;
        }

        private bool IsGridSpaceEmpty(Bounds bounds)
        {
            Vector3 insetMin = bounds.min + Vector3.one * CollisionSkin;
            Vector3 insetMax = bounds.max - Vector3.one * CollisionSkin;

            if (terrainManager.TryWorldToCell(insetMin, out Vector2Int minCell) == false ||
                terrainManager.TryWorldToCell(insetMax, out Vector2Int maxCell) == false)
                return false;

            for (int y = minCell.y; y <= maxCell.y; y++)
            {
                for (int x = minCell.x; x <= maxCell.x; x++)
                {
                    if (terrainManager.HasTerrain(new Vector2Int(x, y)))
                        return false;
                }
            }

            return true;
        }

        private bool IsEscapePathClear(Vector2 targetPosition)
        {
            Vector2 displacement = targetPosition - body.position;
            float distance = displacement.magnitude;
            if (distance <= CollisionSkin) return true;

            Vector2 direction = displacement / distance;
            float probeStep = Mathf.Max(CollisionSkin, terrainManager.CellSize * 0.25f);
            int probeCount = Mathf.CeilToInt(distance / probeStep);

            for (int probe = 1; probe <= probeCount; probe++)
            {
                float probeDistance = Mathf.Min(probe * probeStep, distance);
                Bounds candidate = itemCollider.bounds;
                candidate.center += (Vector3)(direction * probeDistance);

                if (IsGridSpaceEmpty(candidate) == false) return false;
            }

            return true;
        }

        private static bool IsInFallingTerrainPath(Bounds candidate, FallingChunk fallingChunk)
        {
            float halfCellSize = fallingChunk.CellSize * 0.5f;

            foreach (Vector2Int localCell in fallingChunk.LocalCells)
            {
                Vector3 tileCenter = fallingChunk.GetLocalCellCenterWorld(localCell);
                float tileMinX = tileCenter.x - halfCellSize;
                float tileMaxX = tileCenter.x + halfCellSize;
                float tileMaxY = tileCenter.y + halfCellSize;

                if (tileMaxY <= candidate.min.y) continue;
                if (candidate.max.x <= tileMinX || candidate.min.x >= tileMaxX) continue;

                return true;
            }

            return false;
        }

        public bool TryCollect(IItemInventory inventory)
        {
            if (isCollected || inventory == null)
                return false;

            if (inventory.TryAdd(ItemID, Quantity) == false)
                return false;

            isCollected = true;
            Destroy(gameObject);

            return true;
        }
    }
}
