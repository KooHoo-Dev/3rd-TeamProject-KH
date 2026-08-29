using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

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
        [SerializeField, Min(0f)] private float scrollFocusInterval = 0.1f;

        private int focusedSlotIndex;
        private float nextScrollFocusTime;

        public int FocusedSlotIndex => focusedSlotIndex;
        public int FocusedItemID => GetFocusedSlot()?.ItemID ?? -1;

        public void SetHoldProgress(float progress)
        {
            GetFocusedSlot()?.SetHoldProgress(progress);
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

            float scrollY = mouse.scroll.ReadValue().y;
            if (Mathf.Approximately(scrollY, 0f) || Time.unscaledTime < nextScrollFocusTime) return;

            nextScrollFocusTime = Time.unscaledTime + scrollFocusInterval;
            MoveFocus(scrollY > 0f ? -1 : 1);
        }

        public void Render(
            IReadOnlyList<InventorySlotDisplayData> items,
            int gold)
        {
            SetGold(gold);
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

            goldText.text = $"GOLD  {Mathf.Max(0, gold):N0}";
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
        }

        private InventorySlotView GetFocusedSlot()
        {
            if (slotViews == null || focusedSlotIndex < 0 || focusedSlotIndex >= slotViews.Length)
                return null;

            return slotViews[focusedSlotIndex];
        }
    }
}
