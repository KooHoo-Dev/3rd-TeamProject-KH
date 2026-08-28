using UnityEngine;
using UnityEngine.Serialization;

namespace TerrainCollapse
{
    public class PickaxeItem : Item
    {
        [Header("Preview Settings")]
        [SerializeField] private LineRenderer lineRenderer; // 캐릭터 ~ 타일 연결선
        [SerializeField] private GameObject tileHighlight; // 타일 하이라이트
        
        private GameObject highlightInstance;

        public override void Init(ItemDef def)
        {
            base.Init(def);
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
            TerrainManager terrainManager = FindFirstObjectByType<TerrainManager>();

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

        public override void Use(Vector3 ownerPos, Vector3 targetPos)
        {
            TerrainManager terrainManager = FindFirstObjectByType<TerrainManager>();

            // Preview와 동일한 로직으로 스냅 셀 획득
            if (TryGetTargetCell(terrainManager, ownerPos, targetPos, out Vector2Int targetCell, out _))
            {
                ExecuteDigPower(terrainManager, targetCell, Def.PickaxeData.Value.DigPower);
            }
        }

        /// <summary>
        /// [공통 로직] 매몰 상태 검사 및 마우스 방향 스냅 타일 계산
        /// </summary>
        private bool TryGetTargetCell(TerrainManager manager, Vector3 ownerPos, Vector3 targetPos, out Vector2Int targetCell, out Vector3 worldPos)
        {
            targetCell = default;
            worldPos = default;

            if (manager.TryWorldToCell(ownerPos, out Vector2Int startCell) == false) return false;

            // 1. 매몰(Buried) 상태 체크
            if (manager.HasTerrain(startCell))
            {
                targetCell = startCell;
                manager.TryCellToWorldCenter(targetCell, out worldPos);
                return true;
            }

            // 2. 사거리 계산 및 DDA 스냅
            Vector3 direction = (targetPos - ownerPos);
            float distance = direction.magnitude;
            if (distance < 0.001f) return false;

            direction.Normalize();
            float effectiveDistance = Mathf.Min(distance, Def.PickaxeData.Value.Range);
            Vector3 clampedTargetPos = ownerPos + direction * effectiveDistance;

            if (manager.TryWorldToCell(clampedTargetPos, out Vector2Int endCell) == false) return false;

            if (TryGetNearestTargetCell(manager, startCell, endCell, out targetCell))
            {
                manager.TryCellToWorldCenter(targetCell, out worldPos);
                return true;
            }

            return false;
        }

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
                        snappedCell = horizNeighbor;
                        return true;
                    }
                }

                if (moveX) { err -= dy; currX += sx; }
                if (moveY) { err += dx; currY += sy; }
            }
            return false;
        }
    }
}