using System.Collections.Generic;
using UnityEngine;

namespace TerrainCollapse
{
    // 인벤토리 수량 차감과 필드 아이템 생성을 연결

    public sealed class PlayerItemDropService
    {
        private readonly ItemDropSpawner itemDropSpawner;

        public PlayerItemDropService(ItemDropSpawner itemDropSpawner)
        {
            this.itemDropSpawner = itemDropSpawner;
        }

        public bool TryDrop(
            IItemInventory inventory,
            int itemID,
            int quantity,
            Vector2 worldPosition,
            float pickupDelay)
        {
            if (inventory == null || quantity <= 0) return false;
            if (inventory.TryRemove(itemID, quantity) == false) return false;

            if (itemDropSpawner != null &&
                itemDropSpawner.TrySpawn(itemID, quantity, worldPosition, pickupDelay, out _))
                return true;

            inventory.TryAdd(itemID, quantity);
            return false;
        }
    }

    public sealed class PlayerDeathDropService
    {
        private const float ITEM_DROP_CHANCE = 0.5f;
        private const float GOLD_DROP_RATIO = 0.5f;

        private readonly ItemDropSpawner itemDropSpawner;
        private readonly float dropSpreadRadius;
        private readonly float pickupDelay;

        public PlayerDeathDropService(
            ItemDropSpawner itemDropSpawner,
            float dropSpreadRadius,
            float pickupDelay)
        {
            this.itemDropSpawner = itemDropSpawner;
            this.dropSpreadRadius = dropSpreadRadius;
            this.pickupDelay = pickupDelay;
        }

        public void DropInventory(IItemInventory inventory, Vector2 deathPosition)
        {
            if (inventory == null) return;
            if (DataManager.Instance == null) return;

            List<KeyValuePair<int, int>> items = new(inventory.Quantities);

            foreach (KeyValuePair<int, int> item in items)
            {
                if (item.Value <= 0) continue;

                if (DataManager.Instance.ItemTable.TryGet(item.Key, out ItemDef itemDef) == false)
                {
                    Debug.LogWarning(
                        $"[Death Drop] Item data missing: itemID={item.Key}");

                    continue;
                }

                if (itemDef.Type == ItemType.Gold)
                {
                    DropGold(inventory, item.Key, item.Value, deathPosition);
                    continue;
                }

                DropNormalItem(inventory, item.Key, item.Value, deathPosition);
            }
        }

        private void DropNormalItem(
            IItemInventory inventory,
            int itemID,
            int quantity,
            Vector2 deathPosition)
        {
            bool isDropped = Random.value < ITEM_DROP_CHANCE;

            if (isDropped)
            {
                TryDrop(inventory, itemID, quantity, deathPosition);
                return;
            }

            inventory.TryRemove(itemID, quantity);
        }

        private void DropGold(
            IItemInventory inventory,
            int goldItemID,
            int goldAmount,
            Vector2 deathPosition)
        {
            int dropAmount = Mathf.FloorToInt(goldAmount * GOLD_DROP_RATIO);

            if (dropAmount > 0)
                TryDrop(inventory, goldItemID, dropAmount, deathPosition);

            int remainingGold = inventory.GetQuantity(goldItemID);
            if (remainingGold > 0)
                inventory.TryRemove(goldItemID, remainingGold);
        }

        private void TryDrop(
            IItemInventory inventory,
            int itemID,
            int quantity,
            Vector2 deathPosition)
        {
            if (inventory.TryRemove(itemID, quantity) == false)
                return;

            Vector2 dropPosition =
                deathPosition + new Vector2(Random.Range(-dropSpreadRadius, dropSpreadRadius), 0f);

            if (itemDropSpawner?.TrySpawn(itemID, quantity, dropPosition, pickupDelay, out _) == true)
                return;

            inventory.TryAdd(itemID, quantity);
        }
    }
}
