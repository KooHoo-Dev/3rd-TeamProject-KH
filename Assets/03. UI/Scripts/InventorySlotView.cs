using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TerrainCollapse
{
    /// <summary>아이콘만 표시하는 정사각형 인벤토리 슬롯</summary>
    public sealed class InventorySlotView : MonoBehaviour
    {
        [SerializeField] private Image iconImage;
        [SerializeField] private TMP_Text quantityText;

        public void Render(InventorySlotDisplayData data)
        {
            SetIcon(data.Icon);
            SetQuantity(data.Quantity);
        }

        public void Clear()
        {
            SetIcon(null);
            SetQuantity(0);
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
    }
}
