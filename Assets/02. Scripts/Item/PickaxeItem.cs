using UnityEngine;

namespace TerrainCollapse
{
    public class PickaxeItem : Item
    {
        [Header("Preview Settings")]
        [SerializeField] private LineRenderer lineRenderer; // 캐릭터 ~ 타일 연결선
        [SerializeField] private GameObject tileHighlight; // 타일 하이라이트
        
        [SerializeField] private TerrainManager pendingTerrainManager;
        private TerrainManager terrainManager;
        private Vector2Int pendingTargetCell;
        private bool hasPendingDig;

        private GameObject highlightInstance;

        public override void Init(ItemDef def)
        {
            base.Init(def);

            GetTerrainManager();

            if (highlightInstance == null)
            {
                highlightInstance = Instantiate(tileHighlight, transform);
                highlightInstance.SetActive(false);
            }

            void SetupLineRenderer()
            {
                if (lineRenderer == null)
                    lineRenderer = gameObject.AddComponent<LineRenderer>();

                // 1. 점 개수 및 공간 좌표계 설정
                lineRenderer.positionCount = 2;
                lineRenderer.useWorldSpace = true;

                // 2. 두께 및 정렬 레이어 설정
                lineRenderer.startWidth = 0.05f;
                lineRenderer.endWidth = 0.05f;
                lineRenderer.sortingLayerName = "Foreground"; // 프로젝트의 레이어 이름으로 지정
                lineRenderer.sortingOrder = 10;

                // 3. 머티리얼 및 색상 설정
                lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
                lineRenderer.startColor = new Color(0f, 1f, 1f, 0.8f); // Cyan (반투명)
                lineRenderer.endColor = new Color(0f, 1f, 1f, 0.3f);

                // 4. 기본 비활성화
                lineRenderer.enabled = false;
            }
            SetupLineRenderer();
        }

        // 매 프레임 ItemController에 의해 호출됨
        public override void OnUpdatePreview(Vector3 ownerPos, Vector3 targetPos)
        {
            GetTerrainManager();

            // 1. 공통 스냅 셀 계산
            if (TryGetTargetCell(terrainManager, ownerPos, targetPos, out Vector2Int targetCell,
                    out Vector3 targetWorldPos))
            {
                // 2. 하이라이트 표시
                highlightInstance.transform.position = targetWorldPos;
                highlightInstance.SetActive(true);

                // 3. 표시선(LineRenderer) 그린다면 업데이트
                lineRenderer.enabled = true;
                lineRenderer.SetPosition(0, ownerPos);
                lineRenderer.SetPosition(1, targetWorldPos);
            }
            else
            {
                OnClearPreview();
            }
        }

        public override void OnClearPreview()
        {
            highlightInstance.SetActive(false);
            lineRenderer.enabled = false;
        }

        public override bool Use(Vector3 ownerPos, Vector3 targetPos)
        {
            GetTerrainManager();

            if (TryGetTargetCell(terrainManager, ownerPos, targetPos, out Vector2Int targetCell, out _))
            {
                ExecuteDigPower(terrainManager, targetCell, Def.PickaxeData.Value.DigPower);
            }

            return true;
        }

        /// <summary>
        /// [공통 로직] 매몰 상태 검사 및 마우스 방향 스냅 타일 계산
        /// 사거리 판정은 서버와 동일하게 "타일 중심 ~ 플레이어 위치" 단순 거리 비교로 수행한다.
        /// (DDA 시야 스냅은 편의 기능이므로 그대로 유지, 다만 탐색 자체는 사거리로 클램프하지 않는다.)
        /// </summary>
        private bool TryGetTargetCell(TerrainManager manager, Vector3 ownerPos, Vector3 targetPos, out Vector2Int targetCell, out Vector3 worldPos)
        {
            targetCell = default;
            worldPos = default;

            if (manager.TryWorldToCell(ownerPos, out Vector2Int startCell) == false) return false;

            // 1. 매몰(Buried) 상태 체크 - 플레이어가 서 있는 타일이므로 거리 판정 불필요
            if (manager.HasTerrain(startCell))
            {
                if (manager.IsCellMineable(startCell) == false) return false;

                targetCell = startCell;
                manager.TryCellToWorldCenter(targetCell, out worldPos);
                return true;
            }

            // 2. 시야(DDA) 스냅 - 편의 기능: 클릭 방향에서 가장 가까운 채굴 가능 타일을 찾는다.
            //    더 이상 사거리로 클램프한 위치를 사용하지 않고, 실제 클릭/타겟 위치까지 시야를 검사한다.
            if (manager.TryWorldToCell(targetPos, out Vector2Int endCell) == false) return false;

            if (TryGetNearestTargetCell(manager, startCell, endCell, out targetCell) == false)
                return false;

            if (manager.TryCellToWorldCenter(targetCell, out worldPos) == false)
            {
                targetCell = default;
                return false;
            }

            // 3. 최종 사거리 판정 - 서버 로직과 동일: 타일 중심 ~ 플레이어 위치 단순 거리 비교
            float allowedRange = Def.PickaxeData.Value.Range;
            if (Vector3.Distance(ownerPos, worldPos) > allowedRange)
            {
                targetCell = default;
                worldPos = default;
                return false;
            }

            return true;
        }

        private void ExecuteDigPower(TerrainManager manager, Vector2Int cell, int digPower)
        {
            if (digPower <= 0) return;

            RoomClient roomClient = RoomClient.Instance;
            if (roomClient != null && roomClient.IsAuthoritativeTerrainMode)
            {
                // 로컬 지형 변경은 서버의 terrain_batch를 받은 경우에만 적용한다.
                if (roomClient.RequestExcavation(cell, Def.ID))
                    manager.PlayTileHit(cell);
                return;
            }

            // RoomClient가 씬에 있으면 연결/초기 Snapshot 완료 전에도 로컬 확정을 금지한다.
            if (roomClient != null)
            {
                Debug.LogWarning("[PickaxeItem] 서버 지형 동기화가 준비되지 않아 채굴 요청을 보류했습니다.");
                return;
            }

            // RoomClient를 사용하지 않는 기존 오프라인 씬의 동작은 보존한다.
            bool applied = false;
            for (int i = 0; i < digPower; i++)
            {
                TerrainRemoveResult result = manager.RequestRemoveCell(cell);
                applied |= result == TerrainRemoveResult.Damaged || result == TerrainRemoveResult.Removed;
                if (result == TerrainRemoveResult.NotMineable ||
                    result == TerrainRemoveResult.EmptyCell ||
                    result == TerrainRemoveResult.Removed) break;
            }
            if (applied)
                manager.PlayTileHit(cell);
        }

        private bool TryGetNearestTargetCell(TerrainManager manager, Vector2Int start, Vector2Int end, out Vector2Int snappedCell)
        {
            snappedCell = start;
            int x0 = start.x, y0 = start.y, x1 = end.x, y1 = end.y;
            int dx = Mathf.Abs(x1 - x0), dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            int err = dx - dy, currX = x0, currY = y0;

            while (true)
            {
                Vector2Int currentCell = new Vector2Int(currX, currY);
                if (currentCell != start && manager.HasTerrain(currentCell))
                {
                    // 채굴 불가 타일은 시야를 막지만 타깃으로 선택하지 않음
                    if (manager.IsCellMineable(currentCell) == false) return false;

                    snappedCell = currentCell;
                    return true;
                }
                if (currX == x1 && currY == y1) break;

                int e2 = 2 * err;
                bool moveX = e2 > -dy;
                bool moveY = e2 < dx;

                // 대각선 이동인 경우, 코너 컷팅 여부 검사
                if (moveX && moveY)
                {
                    Vector2Int horizNeighbor = new Vector2Int(currX + sx, currY);
                    Vector2Int vertNeighbor  = new Vector2Int(currX, currY + sy);

                    bool horizBlocked = manager.HasTerrain(horizNeighbor);
                    bool vertBlocked  = manager.HasTerrain(vertNeighbor);

                    // 두 이웃이 모두 막혀있다면, 대각선 틈으로 시야가 통하지 않음
                    // → 여기서 멈추고, 막고 있는 타일 중 하나를 타겟으로 반환
                    if (horizBlocked && vertBlocked)
                    {
                        if (manager.IsCellMineable(horizNeighbor))
                        {
                            snappedCell = horizNeighbor;
                            return true;
                        }

                        if (manager.IsCellMineable(vertNeighbor))
                        {
                            snappedCell = vertNeighbor;
                            return true;
                        }

                        return false;
                    }
                }

                if (moveX) { err -= dy; currX += sx; }
                if (moveY) { err += dx; currY += sy; }
            }
            return false;
        }

        public bool PrepareUse(Vector3 ownerPos, Vector3 targetPos)
        {
            hasPendingDig = false;
            pendingTerrainManager = null;

            GetTerrainManager();
            if (terrainManager == null)
                return false;

            if (!TryGetTargetCell(terrainManager, ownerPos, targetPos, out Vector2Int targetCell, out _))
                return false;

            pendingTerrainManager = terrainManager;  // 캐시를 예약 상태로 복사
            pendingTargetCell = targetCell;
            hasPendingDig = true;

            return true;
        }

        public void ExecutePreparedUse(Vector3 currentOwnerPos)
        {
            if (!hasPendingDig ||
                pendingTerrainManager == null)
            {
                return;
            }

            // 이번 Swing의 타격 기회는 여기서 소비
            hasPendingDig = false;

            if (!IsPreparedTargetStillValid(currentOwnerPos))
            {
                pendingTerrainManager = null;
                return;
            }

            ExecuteDigPower(
                pendingTerrainManager,
                pendingTargetCell,
                Def.PickaxeData.Value.DigPower);

            pendingTerrainManager = null;
        }

        /// <summary>
        /// 예약된 타겟이 여전히 유효한지 재검증한다.
        /// 서버 판정과 동일하게 "타일 중심 ~ 현재 플레이어 위치" 단순 거리 비교만으로 판정한다.
        /// (타일 채굴 가능 여부는 실제 실행 시 ExecuteDigPower -> RequestRemoveCell에서 다시 확인되므로
        ///  여기서 전체 TryGetTargetCell을 재계산할 필요는 없다.)
        /// </summary>
        private bool IsPreparedTargetStillValid(Vector3 currentOwnerPos)
        {
            if (pendingTerrainManager == null)
                return false;

            if (!pendingTerrainManager.TryCellToWorldCenter(pendingTargetCell, out Vector3 targetWorldPos))
                return false;

            float allowedRange = Def.PickaxeData.Value.Range;
            return Vector3.Distance(currentOwnerPos, targetWorldPos) <= allowedRange;
        }

        public void CancelPreparedUse()
        {
            hasPendingDig = false;
            pendingTerrainManager = null;
        }

        private void GetTerrainManager()
        {
            if (terrainManager == null)
                terrainManager = FindFirstObjectByType<TerrainManager>();
        }
    }
}
