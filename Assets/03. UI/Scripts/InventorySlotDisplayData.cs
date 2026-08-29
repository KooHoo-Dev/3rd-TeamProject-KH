using UnityEngine;

namespace TerrainCollapse
{
    /// <summary>인벤토리 시스템이 UI에 전달하는 단일 표시 항목</summary>
    public readonly struct InventorySlotDisplayData
    {
        public int ItemID { get; }
        public Sprite Icon { get; }
        public int Quantity { get; }

        public InventorySlotDisplayData(Sprite icon)
            : this(-1, icon, 1)
        {
        }

        public InventorySlotDisplayData(int item, Sprite icon, int quantity)
        {
            ItemID = item;
            Icon = icon;
            Quantity = quantity;
        }
    }
}
