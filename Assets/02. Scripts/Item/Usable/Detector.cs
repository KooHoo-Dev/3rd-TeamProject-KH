using System.Collections;
using System.Collections.Generic;
using TerrainCollapse.Networking;
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

        [Header("음파 링 이펙트")]
        [SerializeField] private DetectionWaveRing waveRingPrefab;        // 중심에서 퍼져나가는 소나 링 이펙트
        [SerializeField] private Color waveRingColor;

        private Coroutine waveCoroutine;

        public override bool Use(Vector3 ownerPos, Vector3 targetPos)
        {
            TerrainManager terrainManager = FindFirstObjectByType<TerrainManager>();

            float cellSize = terrainManager.CellSize;
            if (cellSize <= 0f) return false;

            if (terrainManager.TryWorldToCell(ownerPos, out Vector2Int centerCell) == false)
                return false;

            List<(Vector3 worldPos, float distance)> found = ScanResources(terrainManager, ownerPos, centerCell, cellSize);

            PlayDetectionWave(ownerPos, found);

            return true;
        }

        public void ShowConfirmedResults(Vector3 ownerPos, GridCoord[] cells)
        {
            TerrainManager terrainManager = FindFirstObjectByType<TerrainManager>();
            if (terrainManager == null) return;

            List<(Vector3 worldPos, float distance)> found = new();

            if (cells != null)
            {
                foreach (GridCoord cell in cells)
                    if (terrainManager.TryCellToWorldCenter(new Vector2Int(cell.X, cell.Y), out Vector3 worldPos))
                        found.Add((worldPos, Vector3.Distance(ownerPos, worldPos)));

                found.Sort((a, b) => a.distance.CompareTo(b.distance));
            }

            // 자원이 없어도 "탐지했지만 없었다"는 피드백은 필요하므로
            // 메시지 출력과 음파 재생을 분리한다. return으로 코루틴 생성을 막지 않는다.
            if (found.Count == 0)
                RoomClient.Instance?.ShowLocalSystemMessage("범위 내에 탐지된 자원이 없습니다.");

            PlayDetectionWave(ownerPos, found);
        }

        /// <summary>
        /// 음파 링 재생을 시작한다. found가 비어 있어도 항상 실행되어야 한다.
        /// (마커 배치 유무와 음파 발생 유무는 서로 독립적인 관심사)
        /// </summary>
        private void PlayDetectionWave(Vector3 ownerPos, List<(Vector3 worldPos, float distance)> found)
        {
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

            // 음파 링은 found 유무와 무관하게 항상 재생된다.
            SpawnWaveRing(ownerPos, waveDuration);

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

        private void SpawnWaveRing(Vector3 ownerPos, float waveDuration)
        {
            DetectionWaveRing ring = Instantiate(waveRingPrefab, ownerPos, Quaternion.identity);
            ring.Play(waveDuration, detectRadius, waveRingColor);
        }

        private void SpawnMarker(Vector3 worldPos)
        {
            worldPos.z = -2f;
            GameObject marker = Instantiate(resourceMarkerPrefab, worldPos, Quaternion.identity);
            Destroy(marker, markerLifetime);
        }
    }
}