namespace TerrainCollapse
{
    /// <summary>
    /// 설치형 폭발물 아이템. 설치 로직 전체(프리팹 체크/셀 계산/중복 방지/Instantiate)는
    /// PlaceableItemBase&lt;MineExplosive&gt;가 담당하므로, 여기서는 prefab 타입만 지정하면 된다.
    /// </summary>
    public class MineItem : PlaceableItemBase<MineExplosive>
    {
    }
}