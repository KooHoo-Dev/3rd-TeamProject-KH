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
        private int authoritativeMaxWeight = -1;

        public event Action<InventoryItemChange> ItemChanged;

        public IReadOnlyDictionary<int, int> Quantities => quantities;
        public int CurrentWeight => currentWeight;
        public int MaxWeight => authoritativeMaxWeight >= 0
            ? authoritativeMaxWeight
            : maxWeight;

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
            if (addedWeight > MaxWeight - currentWeight)
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

        /// <summary>
        /// 서버가 확정한 인벤토리 전체 상태를 적용한다.
        /// 로컬 무게 제한은 서버 스냅샷을 거절하는 조건으로 사용하지 않는다.
        /// </summary>
        public void ReplaceWithSnapshot(
            IEnumerable<KeyValuePair<int, int>> snapshot,
            int serverCurrentWeight,
            int serverMaxWeight)
        {
            Dictionary<int, int> replacement = new();
            if (snapshot != null)
            {
                foreach (KeyValuePair<int, int> item in snapshot)
                    if (item.Key >= 0 && item.Value > 0)
                        replacement[item.Key] = item.Value;
            }

            Dictionary<int, int> previousQuantities = new(quantities);
            HashSet<int> changedItemIDs = new(quantities.Keys);
            changedItemIDs.UnionWith(replacement.Keys);

            quantities.Clear();
            currentWeight = 0;

            foreach (KeyValuePair<int, int> item in replacement)
            {
                quantities[item.Key] = item.Value;

                if (TryGetItemWeight(item.Key, out int itemWeight) == false)
                    continue;

                long weight = (long)itemWeight * item.Value;
                currentWeight = (int)Math.Min(int.MaxValue, currentWeight + weight);
            }

            // 서버와 동일한 Items.tsv를 사용하므로 재계산값과 일치해야 합니다.
            // UI는 서버가 확정한 값을 사용해 클라이언트 데이터 불일치도 드러냅니다.
            if (serverMaxWeight > 0)
            {
                currentWeight = Mathf.Max(0, serverCurrentWeight);
                authoritativeMaxWeight = serverMaxWeight;
            }

            foreach (int itemID in changedItemIDs)
            {
                int previous = previousQuantities.TryGetValue(itemID, out int oldValue)
                    ? oldValue
                    : 0;
                int current = GetQuantity(itemID);
                if (previous == current) continue;

                ItemChanged?.Invoke(new InventoryItemChange(itemID, previous, current));
            }
        }
    }
}
