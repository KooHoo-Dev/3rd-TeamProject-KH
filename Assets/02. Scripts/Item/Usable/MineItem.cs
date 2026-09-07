using UnityEngine;

namespace TerrainCollapse
{
    /// <summary>
    /// DynamiteItem을 참고해 만든 설치형 폭발물 아이템.
    /// 투척/궤적 미리보기 없이, Use() 호출 시 설치자가 현재 서 있는 타일에 그대로 지뢰를 설치한다.
    /// </summary>
    public class MineItem : Item
    {
        [Header("Mine Settings")]
        [SerializeField] private MineExplosive minePrefab; // 설치되는 실제 지뢰 프리팹

        // 좌클릭(사용) 시 ItemController에 의해 호출됨 -> 설치자 발밑 타일에 지뢰 설치
        public override void Use(Vector3 ownerPos, Vector3 targetPos)
        {
            Vector3 placePosition = ResolvePlacementPosition(ownerPos);

            MineExplosive mine = Instantiate(minePrefab, placePosition, Quaternion.identity);
            mine.Place(placePosition);
        }

        // 설치자가 서 있는 타일의 중심 좌표를 계산.
        private Vector3 ResolvePlacementPosition(Vector3 ownerPos)
        {
            TerrainManager terrainManager = FindFirstObjectByType<TerrainManager>();

            terrainManager.TryWorldToCell(ownerPos, out Vector2Int cell);
            terrainManager.TryCellToWorldCenter(cell, out Vector3 cellCenter);

            return cellCenter;
        }
    }
}