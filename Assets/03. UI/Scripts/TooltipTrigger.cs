using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TerrainCollapse
{
    /// <summary>UI 오브젝트 호버 시 현재 아이템의 Tooltip 표시를 요청한다.</summary>
    public sealed class TooltipTrigger : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
    {
        [SerializeField] private int itemID = -1;

        private InventorySlotView inventorySlot;
        private PickaxeSlotView pickaxeSlot;
        private ShopItemRowView shopItemRow;

        private void Awake()
        {
            // Trigger는 아이콘 자식에 붙고, 실제 아이템 데이터는 슬롯/행 부모가 가진다.
            inventorySlot = GetComponentInParent<InventorySlotView>();
            pickaxeSlot = GetComponentInParent<PickaxeSlotView>();
            shopItemRow = GetComponentInParent<ShopItemRowView>();

            // 슬롯 배경 Image가 Raycast를 받지 않으면 포인터 이벤트가 전혀 오지 않는다.
            // TooltipTrigger가 달린 슬롯은 항상 자신을 UI Raycast 대상으로 사용한다.
            if (TryGetComponent(out Graphic graphic))
                graphic.raycastTarget = true;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            int resolvedItemID = ResolveItemID();
            if (resolvedItemID < 0) return;

            ItemTooltipView.Instance?.Show(this, resolvedItemID, eventData.position);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            ItemTooltipView.Instance?.Hide(this);
        }

        public void OnPointerMove(PointerEventData eventData)
        {
            ItemTooltipView.Instance?.SetScreenPosition(eventData.position);
        }

        private int ResolveItemID()
        {
            if (inventorySlot != null) return inventorySlot.ItemID;
            if (pickaxeSlot != null) return pickaxeSlot.ItemID;
            if (shopItemRow != null) return shopItemRow.ItemID;

            return itemID;
        }
    }
}
