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
}
