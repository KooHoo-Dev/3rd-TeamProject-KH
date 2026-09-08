using UnityEngine;

namespace TerrainCollapse
{
    /// <summary>
    /// 설치형(placeable) 아이템 공통 베이스. "설치자가 서 있는 타일 셀 + 그 중심 월드 좌표를
    /// 계산"하는 로직만 담당한다. Use()를 완전히 커스텀하게 구현해야 하는 설치형 아이템이
    /// 생기면 이 클래스를 직접 상속하면 되고, 대부분의 경우엔 아래 제네릭 버전인
    /// PlaceableItemBase&lt;TPlaceable&gt;을 상속하는 편이 더 간단하다.
    /// </summary>
    public abstract class PlaceableItemBase : Item
    {
        /// <summary>
        /// 설치자가 서 있는 타일의 셀 좌표와 중심 월드 좌표를 계산한다.
        /// TerrainManager를 못 찾거나, 셀 변환에 실패하거나, 셀의 월드 중심 좌표를
        /// 얻지 못하는 경우 전부 "설치 실패"로 보고 false를 반환한다.
        /// </summary>
        protected bool TryResolvePlacement(Vector3 ownerPos, out Vector2Int cell, out Vector3 placePosition)
        {
            cell = default;
            placePosition = default;

            TerrainManager terrainManager = FindFirstObjectByType<TerrainManager>();
            if (terrainManager == null)
                return false;

            if (terrainManager.TryWorldToCell(ownerPos, out cell) == false)
                return false;

            if (terrainManager.TryCellToWorldCenter(cell, out placePosition) == false)
                return false;

            return true;
        }
    }

    /// <summary>
    /// 프리팹 하나를 설치자 타일에 놓는 게 전부인 설치형 아이템을 위한 제네릭 베이스.
    /// MineItem, TorchItem처럼 "프리팹 null 체크 -> 셀 계산 -> 중복 설치 체크 ->
    /// Instantiate -> Place()" 흐름이 완전히 같은 경우, 이 클래스를 상속하면
    /// Use()를 다시 작성할 필요 없이 prefab 필드만 채우면 된다.
    /// </summary>
    /// <typeparam name="TPlaceable">
    /// 실제로 설치되는 컴포넌트 타입. Component이면서 IPlaceable을 구현해야 하고,
    /// 중복 설치 판정은 GridOccupancy&lt;TPlaceable&gt;를 통해 타입별로 독립적으로 관리된다.
    /// </typeparam>
    public abstract class PlaceableItemBase<TPlaceable> : PlaceableItemBase
        where TPlaceable : Component, IPlaceable
    {
        [Header("Placement Settings")]
        [SerializeField] private TPlaceable prefab; // 설치되는 실제 오브젝트 프리팹

        // 좌클릭(사용) 시 ItemController에 의해 호출됨 -> 설치자 발밑 타일에 prefab 설치
        public override bool Use(Vector3 ownerPos, Vector3 targetPos)
        {
            if (prefab == null)
            {
                Debug.LogWarning($"[{GetType().Name}] prefab이 설정되지 않아 설치할 수 없습니다.");
                return false;
            }

            // 설치 가능한 타일을 찾지 못하면 그대로 설치 실패 처리한다
            if (TryResolvePlacement(ownerPos, out Vector2Int cell, out Vector3 placePosition) == false)
            {
                Debug.LogWarning($"[{GetType().Name}] 설치할 타일을 찾지 못해 설치할 수 없습니다.");
                return false;
            }

            if (GridOccupancy<TPlaceable>.IsOccupied(cell))
            {
                Debug.LogWarning($"[{GetType().Name}] 이미 설치된 타일에는 설치할 수 없습니다.");
                return false;
            }

            TPlaceable instance = Instantiate(prefab, placePosition, Quaternion.identity);
            instance.Place(placePosition, cell);

            return true;
        }
    }
}