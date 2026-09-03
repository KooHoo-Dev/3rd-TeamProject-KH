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
            // 현재 서버에는 플레이어가 아이템을 버리는 명령이 없다.
            if (RoomClient.Instance != null) return false;
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
        private const float ITEM_DROP_CHANCE = 1f;
        private const float GOLD_DROP_RATIO = 1f;

        private sealed class DeathLootPlan
        {
            public List<TerrainCellLootEntry> LootEntries { get; } = new();
            public List<TerrainCellLootEntry> RemoveEntries { get; } = new();
        }

        public bool TryStoreInventory(
            IItemInventory inventory,
            TerrainManager terrainManager,
            Vector2Int deathCell)
        {
            // 사망 드롭과 DeathLoot 지형은 아직 서버 권위 계약에 포함되지 않는다.
            if (RoomClient.Instance != null) return false;
            if (inventory == null || terrainManager == null) return false;
            if (DataManager.Instance == null) return false;

            DeathLootPlan plan = CreateLootPlan(inventory);

            if (plan.RemoveEntries.Count == 0) return false;

            // 실제 보관할 항목이 있을 때만 타일 생성 가능 여부 검사
            if (plan.LootEntries.Count > 0 &&
                terrainManager.CanCreateDeathLootCell(deathCell) == false)
                return false;

            if (TryRemoveEntries(inventory, plan.RemoveEntries) == false)
                return false;

            // 드롭 확률 판정 결과 보관할 항목이 없으면 전부 삭제 후 종료
            if (plan.LootEntries.Count == 0) return false;

            if (terrainManager.TryCreateDeathLootCell(deathCell, plan.LootEntries))
                return true;

            RestoreEntries(inventory, plan.RemoveEntries);

            return false;
        }

        private static DeathLootPlan CreateLootPlan(IItemInventory inventory)
        {
            DeathLootPlan plan = new();
            List<KeyValuePair<int, int>> items = new(inventory.Quantities);

            foreach (KeyValuePair<int, int> item in items)
            {
                if (item.Value <= 0) continue;

                if (DataManager.Instance.ItemTable.TryGet(item.Key, out ItemDef itemDef) == false)
                    continue;

                // 드롭 여부와 관계없이 사망 시 기존 수량은 모두 소모
                plan.RemoveEntries.Add(new TerrainCellLootEntry(item.Key, item.Value));

                if (itemDef.Type == ItemType.Gold)
                {
                    int droppedGold = Mathf.FloorToInt(item.Value * GOLD_DROP_RATIO);

                    if (droppedGold > 0)
                        plan.LootEntries.Add(new TerrainCellLootEntry(item.Key, droppedGold));

                    continue;
                }

                if (Random.value < ITEM_DROP_CHANCE)
                    plan.LootEntries.Add(new TerrainCellLootEntry(item.Key, item.Value));

            }

            return plan;
        }

        private static bool TryRemoveEntries(
            IItemInventory inventory,
            IReadOnlyList<TerrainCellLootEntry> entries)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                TerrainCellLootEntry entry = entries[i];

                if (inventory.TryRemove(entry.ItemID, entry.Quantity))
                    continue;

                for (int index = 0; index < i; index++)
                {
                    TerrainCellLootEntry restored = entries[index];
                    inventory.TryAdd(restored.ItemID, restored.Quantity);
                }

                return false;
            }

            return true;
        }

        private static void RestoreEntries(
            IItemInventory inventory,
            IReadOnlyList<TerrainCellLootEntry> entries)
        {
            foreach (TerrainCellLootEntry entry in entries)
                inventory.TryAdd(entry.ItemID, entry.Quantity);
        }

    }
}
