using UnityEngine;

namespace TerrainCollapse
{
    /// <summary>
    /// DynamiteItem을 참고해 만든 설치형 폭발물 아이템.
    /// 투척/궤적 미리보기 없이, Use() 호출 시 설치자가 현재 서 있는 타일에 그대로 지뢰를 설치한다.
    /// 이미 지뢰가 설치된 타일에는 중복 설치할 수 없다.
    /// </summary>
    public class MineItem : Item
    {
        [Header("Mine Settings")]
        [SerializeField] private MineExplosive minePrefab; // 설치되는 실제 지뢰 프리팹

        // 좌클릭(사용) 시 ItemController에 의해 호출됨 -> 설치자 발밑 타일에 지뢰 설치
        // 실제로 설치에 성공했을 때만 true를 반환한다 (false면 인벤토리가 소모되지 않는다).
        public override bool Use(Vector3 ownerPos, Vector3 targetPos)
        {
            if (minePrefab == null)
            {
                Debug.LogWarning("[MineItem] minePrefab이 설정되지 않아 설치할 수 없습니다.");
                return false;
            }

            bool hasCell = TryResolvePlacement(ownerPos, out Vector2Int cell, out Vector3 placePosition);

            if (hasCell && MineExplosive.IsCellOccupied(cell))
            {
                Debug.LogWarning("[MineItem] 이미 지뢰가 설치된 타일에는 설치할 수 없습니다.");
                return false;
            }

            MineExplosive mine = Instantiate(minePrefab, placePosition, Quaternion.identity);
            mine.Place(placePosition, hasCell ? cell : (Vector2Int?)null);

            return true;
        }

        // 설치자가 서 있는 타일의 셀 좌표와 중심 월드 좌표를 계산.
        // 지형 정보를 얻지 못하면 ownerPos를 그대로 반환하고 false를 반환한다(중복 설치 판정 제외).
        private bool TryResolvePlacement(Vector3 ownerPos, out Vector2Int cell, out Vector3 placePosition)
        {
            cell = default;
            placePosition = ownerPos;

            TerrainManager terrainManager = FindFirstObjectByType<TerrainManager>();
            if (terrainManager == null)
                return false;

            if (terrainManager.TryWorldToCell(ownerPos, out cell) == false)
                return false;

            if (terrainManager.TryCellToWorldCenter(cell, out Vector3 cellCenter))
                placePosition = cellCenter;

            return true;
        }
    }
}