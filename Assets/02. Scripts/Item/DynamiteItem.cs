using System.Collections;
using UnityEngine;

namespace TerrainCollapse
{
    /// <summary>
    /// DynamiteItem이 투척(Use) 시 씬에 생성하는 실제 다이너마이트 오브젝트.
    /// 포물선 비행 -> 뇌관(퓨즈) 대기 -> 폭발(범위 채굴) 순으로 동작한다.
    /// </summary>
    public class DynamiteProjectile : MonoBehaviour
    {
        [Header("VFX (Optional)")]
        [SerializeField] private GameObject explosionEffectPrefab;

        public void Launch(
            TerrainManager manager,
            Vector3 startPos,
            Vector3 landingPos,
            Vector2Int landingCell,
            float arcHeight,
            float throwDuration,
            float fuseTime,
            int explosionRadius,
            int explosionPower)
        {
            StartCoroutine(ThrowRoutine(manager, startPos, landingPos, landingCell,
                arcHeight, throwDuration, fuseTime, explosionRadius, explosionPower));
        }

        private IEnumerator ThrowRoutine(
            TerrainManager manager,
            Vector3 startPos,
            Vector3 landingPos,
            Vector2Int landingCell,
            float arcHeight,
            float throwDuration,
            float fuseTime,
            int explosionRadius,
            int explosionPower)
        {
            // 1. 포물선 비행 (DynamiteItem의 프리뷰와 동일한 보간식 사용)
            float elapsed = 0f;
            while (elapsed < throwDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / throwDuration);
                Vector3 point = Vector3.Lerp(startPos, landingPos, t);
                point.y += arcHeight * 4f * t * (1f - t);
                transform.position = point;
                yield return null;
            }
            transform.position = landingPos;

            // 2. 뇌관(퓨즈) 시간 대기
            yield return new WaitForSeconds(fuseTime);

            // 3. 폭발
            Explode(manager, landingCell, explosionRadius, explosionPower);
        }

        private void Explode(TerrainManager manager, Vector2Int center, int radius, int power)
        {
            if (explosionEffectPrefab != null)
            {
                Instantiate(explosionEffectPrefab, transform.position, Quaternion.identity);
            }

            if (manager != null)
            {
                int radiusSqr = radius * radius;
                for (int dx = -radius; dx <= radius; dx++)
                {
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        // 원형 범위만 채굴 (사각 범위를 원하면 이 체크를 제거)
                        if (dx * dx + dy * dy > radiusSqr) continue;

                        Vector2Int cell = new Vector2Int(center.x + dx, center.y + dy);
                        ExecuteDigPower(manager, cell, power);
                    }
                }
            }

            Destroy(gameObject);
        }

        /// <summary>
        /// PickaxeItem.ExecuteDigPower와 동일한 방식으로 한 셀에 채굴 명령을 반복 호출한다.
        /// </summary>
        private void ExecuteDigPower(TerrainManager manager, Vector2Int cell, int digPower)
        {
            for (int i = 0; i < digPower; i++)
            {
                TerrainRemoveResult result = manager.RequestRemoveCell(cell);
                if (result == TerrainRemoveResult.NotMineable ||
                    result == TerrainRemoveResult.EmptyCell ||
                    result == TerrainRemoveResult.Removed) break;
            }
        }
    }
}