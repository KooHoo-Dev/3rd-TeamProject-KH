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
        [SerializeField] private TMP_Text quantityText;

        public int ItemID { get; private set; } = -1;

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
    }
}
