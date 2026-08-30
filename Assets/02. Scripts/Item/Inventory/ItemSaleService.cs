using System;
using System.Collections.Generic;

namespace TerrainCollapse
{
    public readonly struct ItemSaleResult
    {
        public readonly struct SoldItem
        {
            public string Name { get; }
            public int Quantity { get; }
            public int EarnedGold { get; }

            public SoldItem(string name, int quantity, int earnedGold)
            {
                Name = name;
                Quantity = quantity;
                EarnedGold = earnedGold;
            }
        }

        public int SoldQuantity { get; }
        public int EarnedGold { get; }
        public IReadOnlyList<SoldItem> SoldItems { get; }

        public ItemSaleResult(
            int soldQuantity,
            int earnedGold,
            IReadOnlyList<SoldItem> soldItems)
        {
            SoldQuantity = soldQuantity;
            EarnedGold = earnedGold;
            SoldItems = soldItems ?? Array.Empty<SoldItem>();
        }
    }

    public sealed class ItemSaleService
    {
        private readonly ItemTable itemTable;

        public ItemSaleService(ItemTable itemTable)
        {
            this.itemTable = itemTable;
        }

        public ItemSaleResult SellAllExchangeItems(IItemInventory inventory)
        {
            if (inventory == null || itemTable == null) return default;
            if (itemTable.TryGetGold(out ItemDef goldItem) == false) return default;

            List<KeyValuePair<int, int>> items = new(inventory.Quantities);
            List<ItemSaleResult.SoldItem> soldItems = new();
            int soldQuantity = 0;
            int earnedGold = 0;

            foreach (KeyValuePair<int, int> item in items)
            {
                if (item.Value <= 0) continue;
                if (itemTable.TryGet(item.Key, out ItemDef itemDef) == false) continue;
                if (itemDef.Type != ItemType.Exchange) continue;
                if (itemDef.Price <= 0) continue;
                if (inventory.TryRemove(item.Key, item.Value) == false) continue;

                int itemGold = item.Value * itemDef.Price;
                soldQuantity += item.Value;
                earnedGold += itemGold;
                soldItems.Add(new ItemSaleResult.SoldItem(
                    itemDef.Name,
                    item.Value,
                    itemGold));
            }

            if (earnedGold > 0) inventory.TryAdd(goldItem.ID, earnedGold);

            return new ItemSaleResult(soldQuantity, earnedGold, soldItems);
        }
    }
}
