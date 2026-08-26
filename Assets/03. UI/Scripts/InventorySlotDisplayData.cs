using UnityEngine;

namespace TerrainCollapse
{
    /// <summary>인벤토리 시스템이 UI에 전달하는 단일 표시 항목</summary>
    public readonly struct InventorySlotDisplayData
    {
        public Sprite Icon { get; }

        public InventorySlotDisplayData(Sprite icon)
        {
            Icon = icon;
        }
    }
}
