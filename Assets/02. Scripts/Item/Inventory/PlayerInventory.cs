using System;
using System.Collections.Generic;
using UnityEngine;

namespace TerrainCollapse
{
    // 플레이어가 보유한 ItemID별 수량 관리

    public sealed class PlayerInventory : MonoBehaviour, IItemInventory
    {
        private readonly Dictionary<int, int> quantities = new();

        public event Action<InventoryItemChange> ItemChanged;

        public IReadOnlyDictionary<int, int> Quantities => quantities;

        public int GetQuantity(int itemID)
        {
            return quantities.TryGetValue(itemID, out int quantity)
                ? quantity
                : 0;
        }

        public bool Has(int itemID, int quantity)
        {
            return quantity > 0 && GetQuantity(itemID) >= quantity;
        }

        public bool TryAdd(int itemID, int quantity)
        {
            if (quantity <= 0) return false;

            int previousQuantity = GetQuantity(itemID);

            if (previousQuantity > int.MaxValue - quantity)
                return false;

            int currentQuantity = previousQuantity + quantity;
            quantities[itemID] = currentQuantity;

            ItemChanged?.Invoke(new InventoryItemChange(
                itemID,
                previousQuantity,
                currentQuantity));

            return true;
        }

        public bool TryRemove(int itemID, int quantity)
        {
            if (quantity <= 0) return false;

            int previousQuantity = GetQuantity(itemID);

            if (previousQuantity < quantity) return false;

            int currentQuantity = previousQuantity - quantity;

            if (currentQuantity == 0) quantities.Remove(itemID);
            else quantities[itemID] = currentQuantity;

            ItemChanged?.Invoke(new InventoryItemChange(
                itemID,
                previousQuantity,
                currentQuantity));

            return true;
        }

        public void ReplaceWithSnapshot(IEnumerable<KeyValuePair<int, int>> snapshot)
        {
            Dictionary<int, int> replacement = new();
            if (snapshot != null)
            {
                foreach (KeyValuePair<int, int> item in snapshot)
                    if (item.Key >= 0 && item.Value > 0)
                        replacement[item.Key] = item.Value;
            }

            HashSet<int> changedItemIDs = new(quantities.Keys);
            changedItemIDs.UnionWith(replacement.Keys);

            foreach (int itemID in changedItemIDs)
            {
                int previous = GetQuantity(itemID);
                int current = replacement.TryGetValue(itemID, out int value) ? value : 0;
                if (previous == current) continue;

                if (current == 0) quantities.Remove(itemID);
                else quantities[itemID] = current;
                ItemChanged?.Invoke(new InventoryItemChange(itemID, previous, current));
            }
        }
    }
}
