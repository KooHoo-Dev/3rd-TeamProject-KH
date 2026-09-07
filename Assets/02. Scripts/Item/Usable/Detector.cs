using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TerrainCollapse
{
    public class Detector : Item
    {
        [Header("탐지 설정")]
        [SerializeField, Min(0.1f)] private float detectRadius = 8f;      // 탐지 반경 (월드 단위)
        [SerializeField, Min(0.1f)] private float waveSpeed = 6f;         // 파동이 퍼져나가는 속도 (단위/초)
        [SerializeField] private GameObject resourceMarkerPrefab;         // 발견된 자원 위치 마커
        [SerializeField, Min(0.1f)] private float markerLifetime = 4f;    // 마커 유지 시간(초)

        private Coroutine waveCoroutine;

        public override void OnUpdatePreview(Vector3 ownerPos, Vector3 targetPos)
        {
            
        }

        public override void Use(Vector3 ownerPos, Vector3 targetPos)
        {
            TerrainManager terrainManager = FindFirstObjectByType<TerrainManager>();
            if (terrainManager == null || terrainManager.IsReady == false)
            {
                Debug.LogWarning("[Detector] TerrainManager가 준비되지 않아 사용할 수 없습니다.");
                return;
            }

            float cellSize = terrainManager.CellSize;
            if (cellSize <= 0f) return;

            if (terrainManager.TryWorldToCell(ownerPos, out Vector2Int centerCell) == false)
                return;

            List<(Vector3 worldPos, float distance)> found = ScanResources(terrainManager, ownerPos, centerCell, cellSize);

            if (waveCoroutine != null)
                StopCoroutine(waveCoroutine);

            waveCoroutine = StartCoroutine(RunDetectionWave(ownerPos, found));
        }

        private List<(Vector3, float)> ScanResources(
            TerrainManager terrainManager, Vector3 ownerPos, Vector2Int centerCell, float cellSize)
        {
            List<(Vector3, float)> found = new();
            int cellRadius = Mathf.Max(1, Mathf.CeilToInt(detectRadius / cellSize));

            for (int offsetY = -cellRadius; offsetY <= cellRadius; offsetY++)
            {
                for (int offsetX = -cellRadius; offsetX <= cellRadius; offsetX++)
                {
                    Vector2Int cell = centerCell + new Vector2Int(offsetX, offsetY);

                    if (terrainManager.HasTerrain(cell) == false) continue;
                    if (terrainManager.TryGetResourceID(cell, out int resourceID) == false || resourceID <= 0) continue;
                    if (terrainManager.TryCellToWorldCenter(cell, out Vector3 worldPos) == false) continue;

                    float distance = Vector3.Distance(ownerPos, worldPos);
                    if (distance > detectRadius) continue;

                    found.Add((worldPos, distance));
                }
            }

            // 가까운 순으로 정렬해야 링이 퍼지는 순서와 마커 등장 순서가 일치함
            found.Sort((a, b) => a.Item2.CompareTo(b.Item2));
            return found;
        }

        private IEnumerator RunDetectionWave(Vector3 ownerPos, List<(Vector3 worldPos, float distance)> found)
        {
            float waveDuration = detectRadius / waveSpeed;

            float elapsed = 0f;
            int nextIndex = 0;

            while (elapsed < waveDuration)
            {
                elapsed += Time.deltaTime;
                float currentWaveDistance = elapsed * waveSpeed;

                while (nextIndex < found.Count && found[nextIndex].distance <= currentWaveDistance)
                {
                    SpawnMarker(found[nextIndex].worldPos);
                    nextIndex++;
                }

                yield return null;
            }

            // 혹시 프레임 스텝 오차로 못 띄운 마커가 남아있다면 마무리 처리
            while (nextIndex < found.Count)
            {
                SpawnMarker(found[nextIndex].worldPos);
                nextIndex++;
            }

            Debug.Log($"[Detector] 반경 {detectRadius} 내 자원 {found.Count}개를 발견했습니다.");
            waveCoroutine = null;
        }

        private void SpawnMarker(Vector3 worldPos)
        {
            GameObject marker = Instantiate(resourceMarkerPrefab, worldPos, Quaternion.identity);
            Destroy(marker, markerLifetime);
        }
    }
}