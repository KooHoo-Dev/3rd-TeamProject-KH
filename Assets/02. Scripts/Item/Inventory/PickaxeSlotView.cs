using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TerrainCollapse
{
    /// <summary>
    /// 곡괭이 전용 표시 슬롯.
    /// 선택/포커스 같은 상호작용 없이, PickaxeInventory(전용 창구)가 알려주는 곡괭이
    /// 아이콘/수량만 그대로 보여준다. 직접 인벤토리를 검색하지 않는다.
    /// 기존 InventoryView 옆에 나란히 배치해서 쓰는 용도.
    /// </summary>
    public sealed class PickaxeSlotView : MonoBehaviour
    {
        [SerializeField] private PickaxeInventory pickaxeInventory;
        [SerializeField] private Image iconImage;

        private void OnEnable()
        {
            pickaxeInventory.PickaxeChanged += HandlePickaxeChanged;

            Refresh();
        }

        private void OnDisable()
        {
            pickaxeInventory.PickaxeChanged -= HandlePickaxeChanged;
        }

        private void HandlePickaxeChanged(ItemDef def) => Refresh();

        private void Refresh()
        {
            if (pickaxeInventory.CurrentDef == null)
            {
                SetIcon(null);
                return;
            }

            SetIcon(pickaxeInventory.CurrentDef.Sprite);
        }

        private void SetIcon(Sprite icon)
        {
            if (iconImage == null) return;

            iconImage.sprite = icon;
            iconImage.enabled = icon != null;
        }
    }
}