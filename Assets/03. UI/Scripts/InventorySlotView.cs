using UnityEngine;
using UnityEngine.UI;

namespace TerrainCollapse
{
    /// <summary>아이콘만 표시하는 정사각형 인벤토리 슬롯</summary>
    public sealed class InventorySlotView : MonoBehaviour
    {
        [SerializeField] private Image iconImage;

        public void Render(InventorySlotDisplayData data)
        {
            SetIcon(data.Icon);
        }

        public void Clear()
        {
            SetIcon(null);
        }

        private void SetIcon(Sprite icon)
        {
            if (iconImage == null) return;

            iconImage.sprite = icon;
            iconImage.enabled = icon == null ? false : true;
        }
    }
}
