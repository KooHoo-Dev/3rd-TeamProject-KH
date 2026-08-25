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
        // 실제 타일 형상을 확인하기 위해 FallingChunk 객체 자체를 전달
        FallingTerrainContactResult ResolveFallingTerrainContact(FallingChunk fallingChunk);
    }

    /// <summary>커스텀 Grid 셀 묶음의 물리 낙하와 안정화 상태를 관리</summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(CompositeCollider2D))]
    public sealed class FallingChunk : MonoBehaviour
    {
        [SerializeField] private float linearVelocityThreshold = 0.1f;
        [SerializeField] private float angularVelocityThreshold = 1f;
        [SerializeField] private float settleDuration = 0.3f;
        [SerializeField] private float groundContactGraceDuration = 0.15f;
        [SerializeField] private float maximumLifetime = 15f;
        // 정적 지형 접촉 후 강제 안정화 판정에 필요한 유지 시간
        [SerializeField] private float positionStabilityDuration = 0.25f;
        // 강제 안정화 판정 시 허용되는 타일 크기 대비 위치 오차 비율
        [SerializeField, Range(0f, 1f)] private float positionStabilityRatio = 0.02f;
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
        private readonly HashSet<Collider2D> staticTerrainContacts = new();
        private Rigidbody2D body;
        private CompositeCollider2D compositeCollider;
        private PhysicsMaterial2D frictionlessMaterial;
        private float cellSize;
        private float stableTime;
        private float positionStableTime;
        private float lastGroundContactTime = float.NegativeInfinity;
        private float lifetime;
        private float minimumWorldY;
        private bool isTrackingStablePosition;
        private bool settled;
        private bool expired;
        private long collapseId;
        private Vector2 stablePositionOrigin;

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

            // 접점 개수와 지지 폭을 충족하는 일반 안정화용 지형 접촉 여부
            bool hasStableTerrainContact = HasStableTerrainContact();
            // 접촉 방향과 무관하게 정적 지형에 닿아 있는 강제 안정화용 접촉 여부
            bool hasStaticTerrainContact = staticTerrainContacts.Count > 0;
            bool recentGround = hasStableTerrainContact ||
                                Time.time - lastGroundContactTime <= groundContactGraceDuration;
            bool slow = body.IsSleeping() ||
                        (body.linearVelocity.sqrMagnitude <= linearVelocityThreshold * linearVelocityThreshold &&
                         Mathf.Abs(body.angularVelocity) <= angularVelocityThreshold);

            // 정상적인 지형 지지와 저속 상태가 유지될 때 일반 안정화 시간 누적
            stableTime = recentGround && slow
                ? stableTime + Time.fixedDeltaTime
                : Mathf.Max(0f, stableTime - Time.fixedDeltaTime);

            // 정적 지형 접촉 중 위치 변화가 허용 범위 이내일 때 강제 안정화 판정
            bool isForceSettled = IsPositionStable(hasStaticTerrainContact);

            // 일반 안정화 시간과 강제 안정화 조건을 모두 충족하지 않으면 안정화 보류
            if (stableTime < settleDuration && isForceSettled == false) return;

            settled = true;
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
            body.bodyType = RigidbodyType2D.Kinematic;
            Debug.Log($"[Terrain Collapse] Chunk Settled: {name}" +
                      (isForceSettled ? " (position stability fallback)" : string.Empty));
        }

        /// <summary>정적 지형 접촉 중 타일 크기 비례 허용 범위 내 위치 유지 시간 확인</summary>
        private bool IsPositionStable(bool hasStaticTerrainContact)
        {
            if (hasStaticTerrainContact == false)
            {
                isTrackingStablePosition = false;
                positionStableTime = 0f;

                return false;
            }

            Vector2 position = body.position;
            float tolerance = cellSize * positionStabilityRatio;

            if (isTrackingStablePosition == false ||
                (position - stablePositionOrigin).sqrMagnitude > tolerance * tolerance)
            {
                stablePositionOrigin = position;
                positionStableTime = 0f;
                isTrackingStablePosition = true;

                return false;
            }

            positionStableTime += Time.fixedDeltaTime;

            return positionStableTime >= positionStabilityDuration;
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
                    target.ResolveFallingTerrainContact(this);

                    // 판정 이후 캐릭터가 Chunk를 받치지 않도록 현재 붕괴 동안 충돌 해제
                    Physics2D.IgnoreCollision(compositeCollider, collision.collider, true);
                    staticTerrainContacts.Remove(collision.collider);
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

            if (isStaticTerrain && collision.contactCount > 0)
                staticTerrainContacts.Add(collision.collider);
            else staticTerrainContacts.Remove(collision.collider);
            
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

            if (HasStableTerrainContact()) lastGroundContactTime = Time.time;
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

            staticTerrainContacts.Remove(collision.collider);
            terrainContactInfos.Remove(collision.collider);

            if (hasStableTerrainContact && HasStableTerrainContact() == false)
                lastGroundContactTime = Time.time;
        }
    }
}
