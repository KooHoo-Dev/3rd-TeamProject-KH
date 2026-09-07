using System.Collections.Generic;
using Character;
using UnityEngine;

namespace TerrainCollapse
{
    /// <summary>
    /// DynamiteExplosive / MineExplosive 등 모든 폭발물의 공통 기반 클래스.
    /// 실제 폭발 결과 처리는 상속받아 그대로 재사용한다.
    /// </summary>
    public abstract class ExplosiveBase : MonoBehaviour
    {
        [Header("Explosion Detection")]
        [SerializeField] protected LayerMask damageableLayerMask = ~0;

        [Header("Visual Feedback - Explosion")]
        [SerializeField] protected ExplosionEffect explosionEffectPrefab;

        /// <summary>
        /// 현재 위치를 중심으로 이펙트 생성 + 지형 채굴 + 플레이어 피해를 처리한다.
        /// 각 서브클래스는 자신의 트리거 조건이 충족됐을 때 이 메서드만 호출하면 된다.
        /// </summary>
        protected void Detonate(float radius, int power)
        {
            Vector3 position = transform.position;

            SpawnExplosionEffect(position, radius);

            TerrainManager terrainManager = FindFirstObjectByType<TerrainManager>();
            if (terrainManager != null &&
                terrainManager.TryWorldToCell(position, out Vector2Int centerCell))
            {
                DigTilesInRadius(terrainManager, centerCell, radius, power);
            }

            DamagePlayersInRadius(position, radius, power);
        }

        // 폭발 이펙트 프리팹을 착탄 지점에 생성
        private void SpawnExplosionEffect(Vector3 position, float radius)
        {
            if (explosionEffectPrefab == null)
                return;

            ExplosionEffect effect = Instantiate(explosionEffectPrefab, position, Quaternion.identity);
            effect.SetExplosionRadius(radius);
        }

        // 중심 셀 기준 폭발 반경 내 모든 타일에 채굴 명령 호출
        private void DigTilesInRadius(TerrainManager manager, Vector2Int centerCell, float radius, int power)
        {
            int cellRadius = Mathf.CeilToInt(radius);
            float radiusSqr = (radius + 0.01f) * (radius + 0.01f);

            // 1. 폭발 반경 내 대상 타일 셀들을 수집
            List<Vector2Int> targetCells = new List<Vector2Int>();

            for (int dx = -cellRadius; dx <= cellRadius; dx++)
            {
                for (int dy = -cellRadius; dy <= cellRadius; dy++)
                {
                    Vector2Int offset = new Vector2Int(dx, dy);
                    if (offset.sqrMagnitude > radiusSqr) continue;

                    targetCells.Add(new Vector2Int(centerCell.x + dx, centerCell.y + dy));
                }
            }

            // 2. 맨 위 타일부터 지우기 위해 Y좌표 내림차순 정렬
            targetCells.Sort((a, b) => b.y.CompareTo(a.y));

            // 3. 위쪽 타일부터 순서대로 채굴 처리
            foreach (Vector2Int cell in targetCells)
            {
                for (int i = 0; i < power; i++)
                {
                    TerrainRemoveResult result = manager.RequestRemoveCell(cell);
                    if (result == TerrainRemoveResult.NotMineable ||
                        result == TerrainRemoveResult.EmptyCell ||
                        result == TerrainRemoveResult.Removed)
                    {
                        break;
                    }
                }
            }
        }

        // 폭발 반경 내 모든 플레이어(IDamageable)에게 피해 API 호출
        private void DamagePlayersInRadius(Vector3 center, float radius, int power)
        {
            Collider2D[] hits = Physics2D.OverlapCircleAll(center, radius, damageableLayerMask);

            foreach (Collider2D hit in hits)
            {
                if (hit.TryGetComponent<IDamageable>(out IDamageable damageable) == false) continue;

                damageable.TakeDamage(new DamageInfo(power, DamageType.Explosion, gameObject));
            }
        }
    }
}