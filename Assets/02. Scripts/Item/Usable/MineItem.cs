using UnityEngine;

namespace TerrainCollapse
{
    /// <summary>
    /// 설치형 폭발물 아이템. 설치 로직 전체(셀 계산/중복 방지/Instantiate)는
    /// PlaceableItemBase&lt;MineExplosive&gt;가 담당하고, 여기서는 프리팹 필드만 직접 들고 있는다.
    /// (제네릭 베이스에 필드를 두지 않는 이유는 PlaceableItemBase.cs 상단 주석 참고)
    /// </summary>
    public class MineItem : PlaceableItemBase<MineExplosive>
    {
        [Header("Mine Settings")]
        [SerializeField] private MineExplosive minePrefab; // 설치되는 실제 지뢰 프리팹

        protected override MineExplosive Prefab => minePrefab;
    }
}