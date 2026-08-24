using System.Collections.Generic;
using UnityEngine;

namespace TerrainCollapse
{
    // 외부 지형 변경과 Chunk별 재배치 완료 이후 연쇄 붕괴 재검사 순서 조정

    public sealed class TerrainCollapseCoordinator : MonoBehaviour
    {
        [SerializeField] private TerrainManager terrainManager;
        [SerializeField] private CollapseManager collapseManager;
        [SerializeField] private int maximumChainCount = 32;

        private bool isCheckingGroups;
        private int chainCount;

        public void Configure(TerrainManager terrain, CollapseManager collapse)
        {
            terrainManager = terrain;
            collapseManager = collapse;
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            UnSubscribe();
        }

        private void Subscribe()
        {
            terrainManager.TerrainChanged += HandleTerrainChanged;
            collapseManager.RebuildCompleted += HandleRebuildCompleted;
        }

        private void UnSubscribe()
        {
            terrainManager.TerrainChanged -= HandleTerrainChanged;
            collapseManager.RebuildCompleted -= HandleRebuildCompleted;
        }

        private void HandleTerrainChanged(TerrainChangeReason reason)
        {
            if (reason == TerrainChangeReason.CollapsePlacement) return;

            chainCount = 0;
            ReleaseFloatingGroups();
        }

        private void HandleRebuildCompleted(long _)
        {
            if (chainCount >= maximumChainCount)
            {
                Debug.LogError($"[Terrain Collapse] 연쇄 붕괴가 {maximumChainCount}회를 초과해 중단합니다.");
                
                return;
            }

            bool hasNewGroups = ReleaseFloatingGroups();
            if (hasNewGroups) chainCount++;
            else if (collapseManager.HasActiveChunks == false) chainCount = 0;
        }

        private bool ReleaseFloatingGroups()
        {
            if (isCheckingGroups) return false;

            isCheckingGroups = true;
            try
            {
                List<List<Vector2Int>> groups = terrainManager.FindFloatingGroups();
                
                foreach (List<Vector2Int> group in groups)
                    collapseManager.CreateChunk(group);

                return groups.Count > 0;
            }
            finally
            {
                isCheckingGroups = false;
            }
        }
    }
}
