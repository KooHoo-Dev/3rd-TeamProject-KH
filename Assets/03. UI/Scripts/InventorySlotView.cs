using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TerrainCollapse
{
    /// <summary>아이콘만 표시하는 정사각형 인벤토리 슬롯</summary>
    public sealed class InventorySlotView : MonoBehaviour
    {
        [SerializeField] private Image iconImage;
        [SerializeField] private Image focusImage;
        [SerializeField] private Image holdFillImage;
        [SerializeField] private TMP_Text quantityText;

        [Header("쿨다운 표시")]
        [SerializeField] private Image cooldownFillImage;

        // InventoryView가 슬롯 생성/바인딩 시 한 번 흘려준다. 아이템 사용/쿨다운 판정의
        // 유일한 소스는 ItemController이므로 슬롯은 여기 값을 그대로 조회만 한다.
        private ItemController itemController;

        public int ItemID { get; private set; } = -1;

        public void SetItemController(ItemController controller) => itemController = controller;

        public void Render(InventorySlotDisplayData data)
        {
            ItemID = data.ItemID;

            SetIcon(data.Icon);
            SetQuantity(data.Quantity);
        }

        public void Clear()
        {
            ItemID = -1;

            SetIcon(null);
            SetQuantity(0);
            SetFocused(false);
            SetHoldProgress(0f);
            SetCooldownFill(0f);
        }

        private void Update()
        {
            UpdateCooldownVisual();
        }

        private void UpdateCooldownVisual()
        {
            if (ItemID < 0)
            {
                SetCooldownFill(0f);
                return;
            }

            float remaining = itemController.GetItemCooldownRemaining(ItemID);
            if (remaining <= 0f)
            {
                SetCooldownFill(0f);
                return;
            }

            float duration = itemController.GetItemCooldownDuration(ItemID);
            float ratio = duration > 0f ? Mathf.Clamp01(remaining / duration) : 0f;
            SetCooldownFill(ratio);
        }

        private void SetCooldownFill(float ratio)
        {
            cooldownFillImage.fillAmount = ratio;
            cooldownFillImage.enabled = ratio > 0f;
        }

        private void SetIcon(Sprite icon)
        {
            if (iconImage == null) return;

            iconImage.sprite = icon;
            iconImage.enabled = icon == null ? false : true;
        }

        private void SetQuantity(int quantity)
        {
            if (quantityText == null) return;

            quantityText.text = quantity > 0 ? quantity.ToString() : "0";
        }

        public void SetFocused(bool isFocused)
        {
            if (focusImage == null) return;

            focusImage.enabled = isFocused;
        }

        public void SetHoldProgress(float progress)
        {
            if (holdFillImage == null) return;

            holdFillImage.fillAmount = Mathf.Clamp01(progress);
            holdFillImage.enabled = holdFillImage.fillAmount > 0f;
        }
    }
}