#if UNITY_EDITOR
using UnityEngine;

namespace TerrainCollapse
{
    // 서버 미구현 단계에서 좌표 제안을 즉시 확정 Batch로 되돌리는 에디터 전용 테스트 권위자

    public sealed class TestTerrainAuthority : MonoBehaviour
    {
        [SerializeField] private CollapseManager collapseManager;
        [SerializeField] private TerrainManager terrainManager;

        private void OnEnable()
        {
            if (collapseManager != null)
                collapseManager.PlacementProposed += HandlePlacementProposed;
        }

        private void OnDisable()
        {
            if (collapseManager != null)
                collapseManager.PlacementProposed -= HandlePlacementProposed;
        }

        /// <summary>로컬 좌표 제안을 현재 Revision 기준의 서버 확정 Batch처럼 즉시 적용</summary>
        private void HandlePlacementProposed(TerrainPlacementProposal proposal)
        {
            // 네트워크 씬에서는 RoomClient가 서버에 제안하고 서버 Batch만 적용한다.
            if (RoomClient.Instance != null) return;

            uint baseRevision = terrainManager.Revision;
            var batch = new TerrainChangeBatch(
                proposal.CollapseId,
                baseRevision,
                baseRevision + 1,
                proposal.Changes);

            if (terrainManager.ApplyAuthoritativeBatch(
                    batch,
                    TerrainChangeReason.CollapsePlacement,
                    out string error) == false)
                Debug.LogError($"[Terrain Collapse Test Authority] Batch 적용 실패: {error}", this);
        }
    }
}
#endif
