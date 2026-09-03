using UnityEngine;

namespace TerrainCollapse
{
    /// <summary>Static Terrain의 실제 침투만 최소 분리 벡터로 복구</summary>
    public sealed class TerrainPenetrationResolver
    {
        private readonly Rigidbody2D body;
        private readonly BoxCollider2D box;
        private readonly LayerMask mask;
        private readonly float tolerance;
        private readonly float padding;
        private readonly int iterations;
        private readonly Collider2D[] overlapHits = new Collider2D[16];

        public TerrainPenetrationResolver(Rigidbody2D body, BoxCollider2D box, LayerMask mask, float tolerance, float padding, int iterations)
        { this.body = body; this.box = box; this.mask = mask; this.tolerance = tolerance; this.padding = padding; this.iterations = iterations; }

        public Vector2 Resolve(Vector2 position)
        {
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                int count = Physics2D.OverlapCollider(box, CreateFilter(), overlapHits);
                bool found = false;
                ColliderDistance2D deepest = default;
                for (int i = 0; i < count; i++)
                {
                    Collider2D other = overlapHits[i];
                    if (ShouldIgnore(other) || !IsStaticTerrainCollider(other)) continue;
                    ColliderDistance2D distance = Physics2D.Distance(box, other);
                    if (!distance.isValid || !distance.isOverlapped) continue;
                    if (!found || distance.distance < deepest.distance) { found = true; deepest = distance; }
                }
                if (!found) break;
                Vector2 correction = deepest.normal * deepest.distance;
                if (correction.sqrMagnitude <= tolerance * tolerance) break;
                if (padding > 0f) correction += correction.normalized * padding;
                // FallingChunk는 Dynamic/Kinematic Rigidbody이므로 여기서 보정하지 않음
                body.position += correction;
                // 실제 침투를 보정한 경우에만 다음 overlap query를 동기화
                Physics2D.SyncTransforms();
            }
            return body.position;
        }

        private ContactFilter2D CreateFilter() => new ContactFilter2D { useLayerMask = true, layerMask = mask, useTriggers = false };
        private bool ShouldIgnore(Collider2D other) => other == null || other == box || Physics2D.GetIgnoreCollision(box, other);
        private static bool IsStaticTerrainCollider(Collider2D other) => other.attachedRigidbody == null || other.attachedRigidbody.bodyType == RigidbodyType2D.Static;
    }
}
