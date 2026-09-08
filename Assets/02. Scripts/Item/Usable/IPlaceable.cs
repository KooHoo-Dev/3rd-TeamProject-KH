using UnityEngine;

namespace TerrainCollapse
{
    /// <summary>
    /// PlaceableItemBase&lt;TPlaceable&gt;가 제네릭하게 Use()를 처리할 수 있도록,
    /// "설치되는 오브젝트"가 갖춰야 하는 최소 계약. MineExplosive, TorchLight가 구현한다.
    /// </summary>
    public interface IPlaceable
    {
        /// <summary>설치 위치(및 알고 있다면 타일 셀)를 받아 스스로를 배치한다.</summary>
        void Place(Vector3 position, Vector2Int? cell);
    }
}