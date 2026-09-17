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
        private bool isLocalChainOwner = true;
        private bool isLocallyOwnedPlacementApplied;
        private readonly HashSet<long> locallyOwnedCollapseIds = new();

        public event System.Action<IReadOnlyList<List<Vector2Int>>> CollapseStartRequested;

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
            terrainManager.AuthoritativeBatchApplied += HandleAuthoritativeBatchApplied;
            collapseManager.RebuildCompleted += HandleRebuildCompleted;
        }

        private void UnSubscribe()
        {
            terrainManager.TerrainChanged -= HandleTerrainChanged;
            terrainManager.AuthoritativeBatchApplied -= HandleAuthoritativeBatchApplied;
            collapseManager.RebuildCompleted -= HandleRebuildCompleted;
        }

        private void HandleAuthoritativeBatchApplied(TerrainChangeBatch batch)
        {
            if (batch == null) return;

            // CollapseID마다 소유자를 유지한다. 이전에는 가장 마지막에 수신한 배치의
            // IsPlacementOwner 하나만 저장해서, 다른 플레이어의 배치가 내 연쇄의
            // 소유권을 덮어썼다.
            isLocallyOwnedPlacementApplied = false;
            if (batch.CollapseId > 0)
            {
                isLocallyOwnedPlacementApplied =
                    locallyOwnedCollapseIds.Remove(batch.CollapseId);
                if (isLocallyOwnedPlacementApplied)
                    isLocalChainOwner = true;
                return;
            }

            // 내가 진행 중인 낙하가 있을 때는 다른 플레이어의 일반 배치가
            // 현재 연쇄의 소유권을 바꾸면 안 된다.
            if (locallyOwnedCollapseIds.Count == 0)
                isLocalChainOwner = batch.IsPlacementOwner;
        }

        /// <summary>서버가 승인한 낙하의 소유자를 CollapseID별로 기록한다.</summary>
        public void NotifyCollapseStarted(long collapseId, bool isPlacementOwner)
        {
            if (collapseId <= 0 || isPlacementOwner == false) return;

            locallyOwnedCollapseIds.Add(collapseId);
            isLocalChainOwner = true;
        }

        /// <summary>취소되었거나 새 Snapshot을 받으며 끝난 낙하 소유 상태를 제거한다.</summary>
        public void NotifyCollapsesCancelled(IReadOnlyList<long> collapseIds)
        {
            if (collapseIds == null) return;

            bool removedLocalCollapse = false;
            foreach (long collapseId in collapseIds)
                removedLocalCollapse |= locallyOwnedCollapseIds.Remove(collapseId);

            // 다른 플레이어의 취소 메시지는 내 연쇄 소유 상태에 영향을 주지 않는다.
            if (removedLocalCollapse && locallyOwnedCollapseIds.Count == 0)
                isLocalChainOwner = false;
        }

        public void ResetAuthoritativeChain()
        {
            locallyOwnedCollapseIds.Clear();
            isLocallyOwnedPlacementApplied = false;
            isLocalChainOwner = true;
            chainCount = 0;
        }

        private void HandleTerrainChanged(TerrainChangeReason reason)
        {
            // 서버가 확정한 낙하 배치는 이미 진행 중인 연쇄의 한 단계다.
            // 이를 일반 AuthoritativeBatch처럼 취급하면 매 단계마다 chainCount가 0으로
            // 돌아가 maximumChainCount가 절대 소진되지 않는다.
            if (reason == TerrainChangeReason.CollapsePlacement)
            {
                if (isLocallyOwnedPlacementApplied)
                    ContinueAuthoritativeChain();
                return;
            }

            // 다른 플레이어의 일반 지형 변경이 내 낙하 청크가 끝나기 전에 도착할 수 있다.
            // 이때 연쇄 상태를 새 붕괴로 초기화하면 다인 환경에서 제한 횟수가 다시 0이 된다.
            if (reason == TerrainChangeReason.AuthoritativeBatch &&
                collapseManager.HasActiveChunks)
                return;

            chainCount = 0;
            if (reason != TerrainChangeReason.AuthoritativeBatch)
                isLocalChainOwner = true;

            ReleaseFloatingGroups();
        }

        private void ContinueAuthoritativeChain()
        {
            // 낙하 완료 뒤의 연쇄 탐색은 서버가 최종 타일 배치를 확정한 이후에만 한다.
            // 물리 청크가 끝난 직후에는 아직 서버 원본/최종 위치가 동기화되지 않았다.
            if (RoomClient.Instance?.IsAuthoritativeTerrainMode != true ||
                isLocalChainOwner == false)
                return;

            if (chainCount >= maximumChainCount)
            {
                Debug.LogError($"[Terrain Collapse] 연쇄 붕괴가 {maximumChainCount}회를 초과해 중단합니다.");
                return;
            }

            if (ReleaseFloatingGroups()) chainCount++;
        }

        private void HandleRebuildCompleted(long _)
        {
            // 권위 모드에서는 이 시점에 아직 낙하 배치 요청의 서버 결과가 없다.
            // 서버 확정 전 재검사하면 같은 그룹을 원본 좌표 기준으로 다시 요청할 수 있다.
            if (RoomClient.Instance?.IsAuthoritativeTerrainMode == true) return;

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
                
                if (RoomClient.Instance?.IsAuthoritativeTerrainMode == true)
                {
                    if (isLocalChainOwner && groups.Count > 0)
                        CollapseStartRequested?.Invoke(groups);
                }
                else
                    foreach (List<Vector2Int> group in groups)
                        collapseManager.CreateChunk(group, isLocalChainOwner);

                return groups.Count > 0;
            }
            finally
            {
                isCheckingGroups = false;
            }
        }
    }
}
