using System.Collections.Generic;
using UnityEngine;

namespace TerrainCollapse
{
    // 플레이어 인벤토리 변경을 HUD 인벤토리 슬롯 표시로 변환

    public sealed class PlayerInventoryHudPresenter : MonoBehaviour
    {
        [SerializeField] private PlayerInventory inventory;
        [SerializeField] private InventoryView inventoryView;

        private void Awake()
        {
            inventory ??= FindFirstObjectByType<PlayerInventory>();
        }

        private void OnEnable()
        {
            inventory.ItemChanged += HandleItemChanged;
        }

        private void OnDisable()
        {
            inventory.ItemChanged -= HandleItemChanged;
        }

        private void Start()
        {
            RefreshAll();
        }

        private void HandleItemChanged(InventoryItemChange change)
        {
            RefreshAll();
        }

        private void RefreshAll()
        {
            int goldItemID = DataManager.Instance.ItemTable.TryGetGold(out ItemDef goldItem)
                ? goldItem.ID
                : -1;

            int gold = goldItemID >= 0
                ? inventory.GetQuantity(goldItemID)
                : 0;

            List<int> itemIDs = new(inventory.Quantities.Keys);
            itemIDs.Sort();

            List<InventorySlotDisplayData> items = new(itemIDs.Count);

            foreach (int itemID in itemIDs)
            {
                int quantity = inventory.GetQuantity(itemID);

                if (itemID == goldItemID) continue;
                if (quantity <= 0) continue;
                if (DataManager.Instance.ItemTable.TryGet(itemID, out ItemDef itemDef) == false) continue;

                items.Add(new InventorySlotDisplayData(itemID, itemDef.Sprite, quantity));
            }

            inventoryView.Render(
                items,
                gold,
                inventory.CurrentWeight,
                inventory.MaxWeight);
        }
    }
}
