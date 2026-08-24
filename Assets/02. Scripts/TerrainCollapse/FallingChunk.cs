using System.Collections.Generic;
using UnityEngine;

namespace TerrainCollapse
{
    // 붕괴 Chunk의 물리 낙하, 지면 접촉, 안정화 판정과 플레이어 매몰과 탈출 처리

    /// <summary>낙하 지형과 캐릭터 접촉 처리 결과</summary>
    public enum FallingTerrainContactResult
    {
        Ignored,
        Escaped,
        NoEscapeSpace
    }
    
    /// <summary>낙하 지형 접촉 시 탈출 또는 매몰 여부를 결정하는 대상</summary>
    public interface IFallingTerrainEscapeTarget
    {
        FallingTerrainContactResult ResolveFallingTerrainContact(
            Bounds terrainBounds,
            float terrainCellSize,
            long collapseId);
    }

    /// <summary>커스텀 Grid 셀 묶음의 물리 낙하와 안정화 상태를 관리</summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(CompositeCollider2D))]
    public sealed class FallingChunk : MonoBehaviour
    {
        [SerializeField] private float linearVelocityThreshold = 0.1f;
        [SerializeField] private float angularVelocityThreshold = 1f;
        [SerializeField] private float settleDuration = 0.3f;
        [SerializeField] private float groundContactGraceDuration = 0.15f;
        [SerializeField] private float maximumSettleWait = 2f;
        [SerializeField] private float maximumLifetime = 15f;
        [SerializeField] private float forcedSettleVelocityThreshold = 0.5f;
        [SerializeField, Range(0f, 1f)] private float minimumSupportingNormalY = 0.8f;
        [SerializeField] private float minimumSupportContactWidth = 0.02f;

        private readonly struct TerrainContactInfo
        {
            public readonly int Count;
            public readonly float MinX;
            public readonly float MaxX;

            public TerrainContactInfo(int count, float minX, float maxX)
            {
                Count = count;
                MinX = minX;
                MaxX = maxX;
            }
        }

        private readonly List<Vector2Int> localCells = new();
        private readonly List<TerrainTileType> cellTypes = new();
        private readonly List<int> cellDurabilities = new();
        private readonly Dictionary<Collider2D, TerrainContactInfo> terrainContactInfos = new();
        private Rigidbody2D body;
        private CompositeCollider2D compositeCollider;
        private PhysicsMaterial2D frictionlessMaterial;
        private float cellSize;
        private float stableTime;
        private float timeSinceFirstGroundContact;
        private float lastGroundContactTime = float.NegativeInfinity;
        private float lifetime;
        private float minimumWorldY;
        private bool hasTouchedGround;
        private bool settled;
        private bool expired;
        private long collapseId;

        public bool IsSettled => settled;
        public bool IsExpired => expired;
        public bool IsComplete => settled || expired;
        public IReadOnlyList<Vector2Int> LocalCells => localCells;
        public IReadOnlyList<TerrainTileType> CellTypes => cellTypes;
        public IReadOnlyList<int> CellDurabilities => cellDurabilities;
        public Collider2D ShapeCollider => compositeCollider;
        public Bounds WorldBounds => compositeCollider.bounds;
        public float CellSize => cellSize;
        public long CollapseId => collapseId;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            compositeCollider = GetComponent<CompositeCollider2D>();
            frictionlessMaterial = new PhysicsMaterial2D("Falling Chunk Frictionless")
            {
                friction = 0f,
                bounciness = 0f
            };
            compositeCollider.sharedMaterial = frictionlessMaterial;
        }

        public void Configure(
            IReadOnlyList<Vector2Int> cells,
            IReadOnlyList<TerrainTileType> types,
            IReadOnlyList<int> durabilities,
            float size,
            long id,
            float minimumY)
        {
            localCells.Clear();
            localCells.AddRange(cells);
            cellTypes.Clear();
            cellTypes.AddRange(types);
            cellDurabilities.Clear();
            cellDurabilities.AddRange(durabilities);
            cellSize = size;
            collapseId = id;
            minimumWorldY = minimumY;
        }

        public Vector3 GetLocalCellCenterWorld(Vector2Int localCell)
            => transform.TransformPoint(new Vector3((localCell.x + 0.5f) * cellSize,
                (localCell.y + 0.5f) * cellSize, 0f));

        private void OnDestroy()
        {
            Destroy(frictionlessMaterial);
        }

        private void FixedUpdate()
        {
            if (IsComplete) return;

            lifetime += Time.fixedDeltaTime;
            
            if (lifetime >= maximumLifetime || transform.position.y <= minimumWorldY)
            {
                expired = true;
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
                body.bodyType = RigidbodyType2D.Kinematic;
                Debug.LogWarning($"[Terrain Collapse] Chunk Expired: {name}, lifetime={lifetime:F2}");
                
                return;
            }

            bool hasStableTerrainContact = HasStableTerrainContact();
            bool recentGround = hasStableTerrainContact ||
                                Time.time - lastGroundContactTime <= groundContactGraceDuration;
            bool slow = body.IsSleeping() ||
                        (body.linearVelocity.sqrMagnitude <= linearVelocityThreshold * linearVelocityThreshold &&
                         Mathf.Abs(body.angularVelocity) <= angularVelocityThreshold);
            
            stableTime = recentGround && slow
                ? stableTime + Time.fixedDeltaTime
                : Mathf.Max(0f, stableTime - Time.fixedDeltaTime);

            if (hasTouchedGround) timeSinceFirstGroundContact += Time.fixedDeltaTime;
            
            bool forceSettled = hasTouchedGround && recentGround &&
                                timeSinceFirstGroundContact >= maximumSettleWait &&
                                body.linearVelocity.sqrMagnitude <=
                                forcedSettleVelocityThreshold * forcedSettleVelocityThreshold;
            
            if (stableTime < settleDuration && forceSettled == false) return;

            settled = true;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.bodyType = RigidbodyType2D.Kinematic;
            Debug.Log($"[Terrain Collapse] Chunk Settled: {name}" +
                      (forceSettled ? " (contact timeout fallback)" : string.Empty));
        }

        private void OnCollisionEnter2D(Collision2D collision) => HandleCollision(collision);
        private void OnCollisionStay2D(Collision2D collision) => HandleCollision(collision);

        private void HandleCollision(Collision2D collision)
        {
            MonoBehaviour[] behaviours = collision.collider.GetComponents<MonoBehaviour>();
            
            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour is IFallingTerrainEscapeTarget target)
                {
                    target.ResolveFallingTerrainContact(WorldBounds, CellSize, CollapseId);

                    // 판정 이후 캐릭터가 Chunk를 받치지 않도록 현재 붕괴 동안 충돌 해제
                    Physics2D.IgnoreCollision(compositeCollider, collision.collider, true);
                    terrainContactInfos.Remove(collision.collider);

                    return;
                }
            }

            UpdateGroundContact(collision);
        }

        private void UpdateGroundContact(Collision2D collision)
        {
            Rigidbody2D otherBody = collision.rigidbody;
            bool isStaticTerrain = otherBody == null || otherBody.bodyType == RigidbodyType2D.Static;
            int terrainContactCount = 0;
            float minX = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            
            for (int i = 0; isStaticTerrain && i < collision.contactCount; i++)
            {
                ContactPoint2D contact = collision.GetContact(i);
                
                if (contact.normal.y < minimumSupportingNormalY) continue;
                
                terrainContactCount++;
                minX = Mathf.Min(minX, contact.point.x);
                maxX = Mathf.Max(maxX, contact.point.x);
            }

            if (isStaticTerrain && terrainContactCount > 0)
                terrainContactInfos[collision.collider] = new TerrainContactInfo(
                    terrainContactCount, minX, maxX);
            else terrainContactInfos.Remove(collision.collider);

            if (HasStableTerrainContact())
            {
                hasTouchedGround = true;
                lastGroundContactTime = Time.time;
            }
        }

        private bool HasStableTerrainContact()
        {
            int totalContactCount = 0;
            float totalMinX = float.PositiveInfinity;
            float totalMaxX = float.NegativeInfinity;

            foreach (TerrainContactInfo contactInfo in terrainContactInfos.Values)
            {
                totalContactCount += contactInfo.Count;
                totalMinX = Mathf.Min(totalMinX, contactInfo.MinX);
                totalMaxX = Mathf.Max(totalMaxX, contactInfo.MaxX);
            }

            return totalContactCount >= 2 &&
                   totalMaxX - totalMinX >= minimumSupportContactWidth;
        }

        private void OnCollisionExit2D(Collision2D collision)
        {
            bool hasStableTerrainContact = HasStableTerrainContact();

            terrainContactInfos.Remove(collision.collider);

            if (hasStableTerrainContact && HasStableTerrainContact() == false)
                lastGroundContactTime = Time.time;
        }
    }
}
