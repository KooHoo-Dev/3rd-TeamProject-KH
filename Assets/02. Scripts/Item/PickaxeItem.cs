using UnityEngine;

namespace TerrainCollapse
{
    public class PickaxeItem : Item
    {
        public override void Use(Vector3 ownerPos, Vector3 targetPos)
        {
            // 0. ItemDef 검증
            if (Def == null || Def.PickaxeData.HasValue == false) return;

            PickaxeInfo stats = Def.PickaxeData.Value;
            float range = stats.Range;
            int digPower = stats.DigPower;

            // 1. TerrainManager 검증
            TerrainManager terrainManager = FindFirstObjectByType<TerrainManager>();
            if (terrainManager == null || terrainManager.IsReady == false) return;

            // 2. 좌표 변환
            if (terrainManager.TryWorldToCell(ownerPos, out Vector2Int startCell) == false) return;

            Vector3 direction = (targetPos - ownerPos);
            float distance = direction.magnitude;
            if (distance < 0.001f) return;

            direction.Normalize();
            float effectiveDistance = Mathf.Min(distance, range);
            Vector3 clampedTargetPos = ownerPos + direction * effectiveDistance;

            if (terrainManager.TryWorldToCell(clampedTargetPos, out Vector2Int targetCell) == false) return;

            // 3. 브레센험 레이캐스팅으로 가장 가까운 타일 좌표 스냅
            if (TryGetNearestTargetCell(terrainManager, startCell, targetCell, out Vector2Int snappedCell) == false) return;

            // 4. TerrainManager를 통해 굴착 진행 (DigPower 수치만큼 반복 또는 전용 메서드 호출)
            ExecuteDigPower(terrainManager, snappedCell, digPower);
        }

        private void ExecuteDigPower(TerrainManager manager, Vector2Int cell, int digPower)
        {
            // 곡괭이의 DigPower(채굴력)만큼 데미지를 줍니다.
            for (int i = 0; i < digPower; i++)
            {
                TerrainRemoveResult result = manager.RequestRemoveCell(cell);

                // 채굴 불가능 타일이거나 이미 파괴(Removed) 및 빈 셀(EmptyCell)이면 멈춤
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