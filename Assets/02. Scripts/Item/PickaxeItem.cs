using UnityEngine;

namespace TerrainCollapse
{
    public class PickaxeItem : Item
    {
        public override void Use(Vector3 ownerPos, Vector3 targetPos)
        {
            // ItemDef 데이터 검증
            if (Def == null || Def.PickaxeData.HasValue == false) return;

            PickaxeInfo stats = Def.PickaxeData.Value;
            float range = stats.Range;
            int digPower = stats.DigPower;

            // TerrainManager 준비 상태 검증
            TerrainManager terrainManager = FindFirstObjectByType<TerrainManager>();
            if (terrainManager == null || terrainManager.IsReady == false) return;

            // 캐릭터 위치를 그리드 셀로 변환
            if (terrainManager.TryWorldToCell(ownerPos, out Vector2Int startCell) == false) return;

            // 캐릭터가 서 있는 좌표(startCell)에 타일이 존재하는 경우, 
            // 방향/거리와 상관없이 자신이 갇혀 있는 해당 셀을 즉시 채굴 대상으로 지정
            if (terrainManager.HasTerrain(startCell))
            {
                ExecuteDigPower(terrainManager, startCell, digPower);
                return;
            }

            // 매몰 상태가 아닌 일반 상태일 때만 마우스 방향 타겟 계산
            Vector3 direction = (targetPos - ownerPos);
            float distance = direction.magnitude;
            
            // 마우스와 캐릭터 위치가 거의 동일하면 조기 리턴
            if (distance < 0.001f) return;

            direction.Normalize();
            float effectiveDistance = Mathf.Min(distance, range);
            Vector3 clampedTargetPos = ownerPos + direction * effectiveDistance;

            if (terrainManager.TryWorldToCell(clampedTargetPos, out Vector2Int targetCell) == false) return;

            // 캐릭터 외부의 가장 가까운 타일 스냅 탐색
            if (TryGetNearestTargetCell(terrainManager, startCell, targetCell, out Vector2Int snappedCell))
            {
                ExecuteDigPower(terrainManager, snappedCell, digPower);
            }
        }

        private void ExecuteDigPower(TerrainManager manager, Vector2Int cell, int digPower)
        {
            for (int i = 0; i < digPower; i++)
            {
                TerrainRemoveResult result = manager.RequestRemoveCell(cell);

                // 채굴 불가능 타일, 파괴 완료, 또는 빈 셀인 경우 루프 종료
                if (result == TerrainRemoveResult.NotMineable || 
                    result == TerrainRemoveResult.EmptyCell || 
                    result == TerrainRemoveResult.Removed)
                {
                    break;
                }
            }
        }

        private bool TryGetNearestTargetCell(
            TerrainManager manager, 
            Vector2Int start, 
            Vector2Int end, 
            out Vector2Int snappedCell)
        {
            snappedCell = start;

            int x0 = start.x, y0 = start.y;
            int x1 = end.x, y1 = end.y;

            int dx = Mathf.Abs(x1 - x0);
            int dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            int currX = x0;
            int currY = y0;

            while (true)
            {
                Vector2Int currentCell = new Vector2Int(currX, currY);

                // startCell은 위에서 이미 매몰 검사를 마쳤으므로, 외부 타일을 탐색
                if (currentCell != start && manager.HasTerrain(currentCell))
                {
                    snappedCell = currentCell;
                    return true;
                }

                if (currX == x1 && currY == y1) break;

                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; currX += sx; }
                if (e2 < dx) { err += dx; currY += sy; }
            }

            return false;
        }
    }
}