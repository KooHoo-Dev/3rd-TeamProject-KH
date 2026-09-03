using System;
using System.Collections.Generic;
using UnityEngine;

namespace TerrainCollapse
{
    // 플레이어가 보유한 ItemID별 수량 관리

    public sealed class PlayerInventory : MonoBehaviour, IItemInventory
    {
        [SerializeField, Min(0)] private int maxWeight = 1000;

        private readonly Dictionary<int, int> quantities = new();
        private int currentWeight;

        public event Action<InventoryItemChange> ItemChanged;

        public IReadOnlyDictionary<int, int> Quantities => quantities;
        public int CurrentWeight => currentWeight;
        public int MaxWeight => maxWeight;

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
            if (TryGetItemWeight(itemID, out int itemWeight) == false) return false;

            int previousQuantity = GetQuantity(itemID);

            if (previousQuantity > int.MaxValue - quantity)
                return false;

            long addedWeight = (long)itemWeight * quantity;
            if (addedWeight > maxWeight - currentWeight)
                return false;

            int currentQuantity = previousQuantity + quantity;
            quantities[itemID] = currentQuantity;
            currentWeight += (int)addedWeight;

            ItemChanged?.Invoke(new InventoryItemChange(
                itemID,
                previousQuantity,
                currentQuantity));

            return true;
        }

        public bool TryRemove(int itemID, int quantity)
        {
            if (quantity <= 0) return false;
            if (TryGetItemWeight(itemID, out int itemWeight) == false) return false;

            int previousQuantity = GetQuantity(itemID);

            if (previousQuantity < quantity) return false;

            int currentQuantity = previousQuantity - quantity;

            if (currentQuantity == 0) quantities.Remove(itemID);
            else quantities[itemID] = currentQuantity;

            currentWeight -= itemWeight * quantity;

            ItemChanged?.Invoke(new InventoryItemChange(
                itemID,
                previousQuantity,
                currentQuantity));

            return true;
        }

        private static bool TryGetItemWeight(int itemID, out int itemWeight)
        {
            itemWeight = 0;

            if (DataManager.Instance == null ||
                DataManager.Instance.ItemTable.TryGet(itemID, out ItemDef itemDef) == false)
            {
                Debug.LogWarning($"[Inventory] Item data missing: item={itemID}");

                return false;
            }

            if (itemDef.Weight < 0)
            {
                Debug.LogWarning($"[Inventory] Item weight must not be negative: item={itemID}");

                return false;
            }

            itemWeight = itemDef.Weight;

            return true;
        }
    }
}
