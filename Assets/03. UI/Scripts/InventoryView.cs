using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

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
        [SerializeField] private Image weightFillImage;
        [SerializeField] private TMP_Text weightText;

        [Header("포커스된 슬롯 변경을 액티브 아이템에 반영")]
        [SerializeField] private ItemController itemController;

        private const float SCROLL_THRESHOLD = 4.2f; // 휠 입력 한 단계로 판정할 델타 기준값

        private int focusedSlotIndex;
        private float scrollFocusAccumulator;
        private MinimapView minimapView;

        // ItemController에 마지막으로 통지한 itemID (중복 호출 방지)
        private int notifiedActiveItemID = int.MinValue;

        public int FocusedSlotIndex => focusedSlotIndex;
        public int FocusedItemID => GetFocusedSlot()?.ItemID ?? -1;

        public void SetHoldProgress(float progress)
        {
            GetFocusedSlot()?.SetHoldProgress(progress);
        }

        private void Awake()
        {
            minimapView ??= FindFirstObjectByType<MinimapView>();
        }

        private void Update()
        {
            if (slotViews == null || slotViews.Length == 0) return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.digit1Key.wasPressedThisFrame) SetFocusedSlot(0);
                else if (keyboard.digit2Key.wasPressedThisFrame) SetFocusedSlot(1);
                else if (keyboard.digit3Key.wasPressedThisFrame) SetFocusedSlot(2);
                else if (keyboard.digit4Key.wasPressedThisFrame) SetFocusedSlot(3);
                else if (keyboard.digit5Key.wasPressedThisFrame) SetFocusedSlot(4);
                else if (keyboard.digit6Key.wasPressedThisFrame) SetFocusedSlot(5);
                else if (keyboard.digit7Key.wasPressedThisFrame) SetFocusedSlot(6);
                else if (keyboard.digit8Key.wasPressedThisFrame) SetFocusedSlot(7);
                else if (keyboard.digit9Key.wasPressedThisFrame) SetFocusedSlot(8);
                else if (keyboard.digit0Key.wasPressedThisFrame) SetFocusedSlot(9);
            }

            Mouse mouse = Mouse.current;
            if (mouse == null) return;
            if (minimapView.IsFullMapOpen) return;

            scrollFocusAccumulator += Mathf.Clamp(mouse.scroll.ReadValue().y, -1f, 1f);

            while (Mathf.Abs(scrollFocusAccumulator) >= SCROLL_THRESHOLD)
            {
                int scrollDirection = scrollFocusAccumulator > 0f ? 1 : -1;
                scrollFocusAccumulator -= scrollDirection * SCROLL_THRESHOLD;
                MoveFocus(scrollDirection > 0 ? -1 : 1);
            }
        }

        public void Render(
            IReadOnlyList<InventorySlotDisplayData> items,
            int gold)
        {
            SetGold(gold);
            RenderItems(items);
        }

        public void Render(
            IReadOnlyList<InventorySlotDisplayData> items,
            int gold,
            int currentWeight,
            int maxWeight)
        {
            SetGold(gold);
            SetWeight(currentWeight, maxWeight);
            RenderItems(items);
        }

        public void RenderItems(IReadOnlyList<InventorySlotDisplayData> items)
        {
            if (slotViews == null || slotViews.Length == 0) return;

            int itemCount = items?.Count ?? 0;

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

            SetFocusedSlot(Mathf.Clamp(focusedSlotIndex, 0, slotViews.Length - 1));
        }

        public void SetGold(int gold)
        {
            if (goldText == null) return;

            goldText.text = $"{Mathf.Max(0, gold):N0} 골드";
        }

        public void SetWeight(int currentWeight, int maxWeight)
        {
            int safeMaxWeight = Mathf.Max(1, maxWeight);
            int safeCurrentWeight = Mathf.Clamp(currentWeight, 0, safeMaxWeight);

            if (weightFillImage != null)
                weightFillImage.fillAmount = (float)safeCurrentWeight / safeMaxWeight;

            if (weightText != null)
                weightText.text = $"{safeCurrentWeight} / {safeMaxWeight}";
        }

        private void MoveFocus(int direction)
        {
            int nextIndex = (focusedSlotIndex + direction + slotViews.Length) % slotViews.Length;
            SetFocusedSlot(nextIndex);
        }

        private void SetFocusedSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= slotViews.Length) return;

            focusedSlotIndex = slotIndex;

            for (int index = 0; index < slotViews.Length; index++)
            {
                InventorySlotView slotView = slotViews[index];
                if (slotView == null) continue;

                slotView.SetFocused(index == focusedSlotIndex);
                if (index != focusedSlotIndex)
                    slotView.SetHoldProgress(0f);
            }

            NotifyActiveItemChanged();
        }

        // 포커스된 슬롯의 itemID가 이전과 달라졌을 때만 ItemController에 반영.
        // 보유 여부/타입 적합성(곡괭이, 환전용 등) 검증은 ItemController.SetActiveItem 내부에서 처리한다.
        private void NotifyActiveItemChanged()
        {
            if (itemController == null) return;

            int itemID = GetFocusedSlot()?.ItemID ?? -1;
            if (itemID == notifiedActiveItemID) return;

            notifiedActiveItemID = itemID;

            if (itemID < 0) itemController.ClearActiveItem();
            else itemController.SetActiveItem(itemID);
        }

        private InventorySlotView GetFocusedSlot()
        {
            if (slotViews == null || focusedSlotIndex < 0 || focusedSlotIndex >= slotViews.Length)
                return null;

            return slotViews[focusedSlotIndex];
        }
    }
}
