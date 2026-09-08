using System.Collections.Generic;
using UnityEngine;

namespace TerrainCollapse
{
    /// <summary>
    /// "이 타일 셀에 T가 이미 설치돼 있는가"를 추적하는 타입별(제네릭) 레지스트리.
    /// GridOccupancy&lt;MineExplosive&gt;, GridOccupancy&lt;TorchLight&gt;처럼
    /// 설치 가능한 오브젝트 타입마다 서로 독립된 점유 테이블을 갖는다.
    /// (즉 같은 타일에 지뢰 하나 + 횃불 하나가 동시에 있는 것은 허용된다.)
    /// </summary>
    public static class GridOccupancy<T> where T : Component
    {
        private static readonly Dictionary<Vector2Int, T> cellToInstance = new();
        private static readonly Dictionary<T, Vector2Int> instanceToCell = new();

        public static bool IsOccupied(Vector2Int cell) => cellToInstance.ContainsKey(cell);

        public static void Register(Vector2Int cell, T instance)
        {
            cellToInstance[cell] = instance;
            instanceToCell[instance] = cell;
        }

        /// <summary>파괴/해제 시 호출. 등록된 셀 정보를 찾아 정리한다.</summary>
        public static void Unregister(T instance)
        {
            if (instanceToCell.TryGetValue(instance, out Vector2Int cell) == false)
                return;

            // 같은 셀에 이미 다른 인스턴스가 재등록된 상태라면 그건 건드리지 않는다.
            if (cellToInstance.TryGetValue(cell, out T registered) && ReferenceEquals(registered, instance))
                cellToInstance.Remove(cell);

            instanceToCell.Remove(instance);
        }
    }
}