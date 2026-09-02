using System.Collections;
using Character;
using UnityEngine;

namespace TerrainCollapse
{
    /// <summary>
    /// DynamiteItem.Use()에 의해 투척되어 생성되는 실제 폭발물.
    /// 착탄 지점까지 이동 후 퓨즈 시간만큼 대기하다가 폭발한다.
    /// </summary>
    public class DynamiteExplosive : MonoBehaviour
    {
        [Header("Explosion Detection")]
        [SerializeField] private LayerMask damageableLayerMask = ~0;

        [Header("Flight Settings")] [SerializeField]
        private float throwSpeed; // 투척 속도

        private DynamiteInfo info;
        private Rigidbody2D rb;

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
        }

        public void Launch(Vector3 ownerPos, Vector3 direction, float speed, DynamiteInfo dynamiteInfo)
        {
            transform.position = ownerPos;
            info = dynamiteInfo;
            throwSpeed = speed;

            // 방향 벡터로 물리 속도 부여
            if (rb != null)
            {
                rb.linearVelocity = (Vector2)direction * throwSpeed;
            }

            StartCoroutine(FuseRoutine());
        }

        private IEnumerator FuseRoutine()
        {
            // 퓨즈 타임 동안 계속 날아가거나 굴러감
            yield return new WaitForSeconds(info.FuseTime);

            Explode();
        }

        private void Explode()
        {
            TerrainManager terrainManager = FindFirstObjectByType<TerrainManager>();

            if (terrainManager != null &&
                terrainManager.TryWorldToCell(transform.position, out Vector2Int centerCell))
            {
                DigTilesInRadius(terrainManager, centerCell, info.Radius, info.Power);
            }

            DamagePlayersInRadius(transform.position, info.Radius, info.Power);

            Destroy(gameObject);
        }

        // 자신이 있던 타일을 중심으로 폭발 반경 내 모든 타일에 채굴 명령 호출
        private void DigTilesInRadius(TerrainManager manager, Vector2Int centerCell, float radius, int power)
        {
            int cellRadius = Mathf.CeilToInt(radius);
            float radiusSqr = (radius + 0.01f) * (radius + 0.01f);

            // 1. 폭발 반경 내 대상 타일 셀들을 수집
            System.Collections.Generic.List<Vector2Int> targetCells = new System.Collections.Generic.List<Vector2Int>();

            for (int dx = -cellRadius; dx <= cellRadius; dx++)
            {
                for (int dy = -cellRadius; dy <= cellRadius; dy++)
                {
                    Vector2Int offset = new Vector2Int(dx, dy);
                    if (offset.sqrMagnitude > radiusSqr) continue;

                    targetCells.Add(new Vector2Int(centerCell.x + dx, centerCell.y + dy));
                }
            }

            // 2. 맨 위 타일부터 지우기 위해 Y좌표 내림차순(OrderById/Sort) 정렬
            // (y값이 클수록 지형 그리드 상 높은 위치)
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

                // NOTE: DamageType에 Explosion 항목이 없다면 FallingChunk, Burial 옆에 추가 필요
                damageable.TakeDamage(new DamageInfo(power, DamageType.Explosion, gameObject));
            }
        }
    }
}