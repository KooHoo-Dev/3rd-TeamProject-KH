namespace TerrainCollapse
{
    /// <summary>
    /// 설치형 횃불 아이템. 설치 로직 전체는 PlaceableItemBase&lt;TorchLight&gt;가 담당하므로,
    /// 여기서는 prefab 타입만 지정하면 된다.
    /// </summary>
    public class TorchItem : PlaceableItemBase<TorchLight>
    {
    }
}