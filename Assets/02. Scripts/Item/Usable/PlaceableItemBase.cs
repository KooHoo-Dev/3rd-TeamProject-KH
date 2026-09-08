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
        /// (ownerPos로 폴백하지 않는다 - 실패 시 호출 측은 설치를 진행해서는 안 된다.)
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
    /// 프리팹 하나를 설치자가 서 있는 타일에 놓는 게 전부인 설치형 아이템을 위한 제네릭 베이스.
    /// MineItem, TorchItem처럼 "프리팹 null 체크 -> 셀 계산 -> 중복 설치 체크 ->
    /// Instantiate -> Place()" 흐름이 완전히 같은 경우, 이 클래스를 상속하면
    /// Use()를 다시 작성할 필요 없이 Prefab 프로퍼티만 구현하면 된다.
    /// </summary>
    /// <typeparam name="TPlaceable">
    /// 실제로 설치되는 컴포넌트 타입. Component이면서 IPlaceable을 구현해야 하고,
    /// 중복 설치 판정은 GridOccupancy&lt;TPlaceable&gt;를 통해 타입별로 독립적으로 관리된다.
    /// </typeparam>
    public abstract class PlaceableItemBase<TPlaceable> : PlaceableItemBase
        where TPlaceable : Component, IPlaceable
    {
        /// <summary>
        /// 설치되는 실제 프리팹. 구체 클래스가 자기 타입의 [SerializeField] 필드를 직접 들고
        /// 이 프로퍼티로 반환하기만 하면 된다 (예: MineItem -> minePrefab, TorchItem -> torchPrefab).
        /// </summary>
        protected abstract TPlaceable Prefab { get; }

        // 좌클릭(사용) 시 ItemController에 의해 호출됨 -> 설치자가 서 있는 타일에 Prefab 설치
        public override bool Use(Vector3 ownerPos, Vector3 targetPos)
        {
            TPlaceable prefab = Prefab;

            if (prefab == null)
            {
                Debug.LogWarning($"[{GetType().Name}] prefab이 설정되지 않아 설치할 수 없습니다.");
                return false;
            }

            // 설치 가능한 타일을 찾지 못하면 그대로 설치 실패 처리한다 (ownerPos 폴백 없음).
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