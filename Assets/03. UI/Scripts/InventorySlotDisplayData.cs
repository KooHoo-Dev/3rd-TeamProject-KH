using UnityEngine;

namespace GameUI
{
    /// <summary>인벤토리 시스템이 UI에 전달하는 단일 표시 항목</summary>
    public readonly struct InventorySlotDisplayData
    {
        public Sprite Icon { get; }
        public string DisplayName { get; }
        public int Amount { get; }

        public InventorySlotDisplayData(Sprite icon, string displayName, int amount)
        {
            Icon = icon;
            DisplayName = displayName ?? string.Empty;
            Amount = Mathf.Max(0, amount);
        }
    }
}
