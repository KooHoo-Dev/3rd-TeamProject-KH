using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameUI
{
    /// <summary>아이콘, 이름, 수량만 표시하는 인벤토리 슬롯</summary>
    public sealed class InventorySlotView : MonoBehaviour
    {
        [SerializeField] private Image iconImage;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text amountText;

        public void Render(InventorySlotDisplayData data)
        {
            SetIcon(data.Icon);
            SetName(data.DisplayName);
            SetAmount(data.Amount);
        }

        public void Clear()
        {
            SetIcon(null);
            SetName(string.Empty);
            SetAmount(0);
        }

        private void SetIcon(Sprite icon)
        {
            if (iconImage == null) return;

            iconImage.sprite = icon;
            iconImage.enabled = icon == null ? false : true;
        }

        private void SetName(string displayName)
        {
            if (nameText == null) return;

            nameText.text = displayName ?? string.Empty;
        }

        private void SetAmount(int amount)
        {
            if (amountText == null) return;

            amountText.text = amount > 0 ? amount.ToString() : string.Empty;
        }
    }
}
