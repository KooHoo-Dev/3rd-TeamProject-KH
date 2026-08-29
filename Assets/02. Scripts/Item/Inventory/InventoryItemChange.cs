namespace TerrainCollapse
{
    // 아이템 보유 수량 변경 정보

    public readonly struct InventoryItemChange
    {
        public int ItemID { get; }
        public int PreviousQuantity { get; }
        public int CurrentQuantity { get; }
        public int Delta => CurrentQuantity - PreviousQuantity;

        public InventoryItemChange(
            int itemID,
            int previousQuantity,
            int currentQuantity)
        {
            ItemID = itemID;
            PreviousQuantity = previousQuantity;
            CurrentQuantity = currentQuantity;
        }
    }
}
