using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace TerrainCollapse
{
    /// <summary>
    /// 한 줄의 아이콘 슬롯과 골드를 표시한다.
    /// 아이템 획득과 골드 계산은 인벤토리 시스템의 책임이다.
    /// </summary>
    public sealed class InventoryView : MonoBehaviour
    {
        [SerializeField] private InventorySlotView[] slotViews;
        [SerializeField] private TMP_Text goldText;

        public void Render(
            IReadOnlyList<InventorySlotDisplayData> items,
            int gold)
        {
            SetGold(gold);

            if (slotViews == null) return;

            int itemCount = items == null ? 0 : items.Count;

            for (int index = 0; index < slotViews.Length; index++)
            {
                InventorySlotView slotView = slotViews[index];
                if (slotView == null) continue;

                if (index < itemCount)
                {
                    slotView.Render(items[index]);
                    continue;
                }

                slotView.Clear();
            }
        }

        private void SetGold(int gold)
        {
            if (goldText == null) return;

            goldText.text = $"GOLD  {Mathf.Max(0, gold):N0}";
        }
    }
}
