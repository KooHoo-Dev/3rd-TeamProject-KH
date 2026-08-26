using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameUI
{
    /// <summary>
    /// 아이템 목록, 현재 무게, 최대 무게, 소지금을 표시한다.
    /// 아이템 획득과 무게 계산은 인벤토리 시스템의 책임이다.
    /// </summary>
    public sealed class InventoryView : MonoBehaviour
    {
        [SerializeField] private InventorySlotView[] slotViews;
        [SerializeField] private Image weightFillImage;
        [SerializeField] private TMP_Text weightText;
        [SerializeField] private TMP_Text moneyText;

        public void Render(
            IReadOnlyList<InventorySlotDisplayData> items,
            float currentWeight,
            float maxWeight,
            int money)
        {
            SetSummary(currentWeight, maxWeight, money);

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

        private void SetSummary(float currentWeight, float maxWeight, int money)
        {
            float safeMaxWeight = Mathf.Max(0f, maxWeight);
            float safeCurrentWeight = Mathf.Clamp(currentWeight, 0f, safeMaxWeight);
            float weightRatio = safeMaxWeight > 0f ? safeCurrentWeight / safeMaxWeight : 0f;

            if (weightFillImage == null)
            {
                Debug.LogError("[UI] InventoryView의 Weight Fill Image가 연결되지 않았습니다.", this);
            }
            else
            {
                weightFillImage.fillAmount = weightRatio;
            }

            if (weightText == null)
            {
                Debug.LogError("[UI] InventoryView의 Weight Text가 연결되지 않았습니다.", this);
            }
            else
            {
                weightText.text = $"{safeCurrentWeight:0.#} / {safeMaxWeight:0.#}";
            }

            if (moneyText == null) return;

            moneyText.text = Mathf.Max(0, money).ToString("N0");
        }
    }
}
