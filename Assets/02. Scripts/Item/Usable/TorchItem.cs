using UnityEngine;

namespace TerrainCollapse
{
    /// <summary>
    /// 설치형 횃불 아이템. 설치 로직 전체는 PlaceableItemBase&lt;TorchLight&gt;가 담당하고,
    /// 여기서는 프리팹 필드만 직접 들고 있는다.
    /// (제네릭 베이스에 필드를 두지 않는 이유는 PlaceableItemBase.cs 상단 주석 참고)
    /// </summary>
    public class TorchItem : PlaceableItemBase<TorchLight>
    {
        [Header("Torch Settings")]
        [SerializeField] private TorchLight torchPrefab; // 설치되는 실제 횃불 프리팹

        protected override TorchLight Prefab => torchPrefab;
    }
}