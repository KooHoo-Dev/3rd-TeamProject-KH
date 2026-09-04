using UnityEngine;

namespace TerrainCollapse
{
    // 지형 셀 파괴 파괴 이벤트를 자원 및 사망 보관 아이템 드롭으로 변환

    [RequireComponent(typeof(TerrainManager))]
    [RequireComponent(typeof(ItemDropSpawner))]
    public sealed class TerrainDropSpawner : MonoBehaviour
    {
        private TerrainManager terrainManager;
        private ItemDropSpawner itemDropSpawner;

        private void Awake()
        {
            terrainManager = GetComponent<TerrainManager>();
            itemDropSpawner = GetComponent<ItemDropSpawner>();
        }

        private void OnEnable()
        {
            terrainManager.CellDestroyed += HandleCellDestroyed;
        }

        private void OnDisable()
        {
            terrainManager.CellDestroyed -= HandleCellDestroyed;
        }

        private void HandleCellDestroyed(
            TerrainCellDestroyedInfo destroyedInfo)
        {
            // 서버 모드의 드롭은 world_item.spawned로만 생성한다.
            if (RoomClient.Instance != null) return;

            if (destroyedInfo.TileType == TerrainTileType.DeathLoot)
            {
                SpawnDeathLoot(destroyedInfo);
                return;
            }

            if (destroyedInfo.HasResource == false)
                return;

            SpawnResourceLoot(destroyedInfo);
        }

        private void SpawnDeathLoot(TerrainCellDestroyedInfo destroyedInfo)
        {
            foreach (TerrainCellLootEntry loot in destroyedInfo.LootEntries)
            {
                if (loot.ItemID <= 0 || loot.Quantity <= 0)
                    continue;

                itemDropSpawner.TrySpawn(loot.ItemID,
                    loot.Quantity,
                    destroyedInfo.WorldPosition,
                    0f,
                    out _);
            }
        }

        private void SpawnResourceLoot(TerrainCellDestroyedInfo destroyedInfo)
        {
            TerrainDataManager terrainDataManager = TerrainDataManager.Instance;
            terrainDataManager.EnsureLoaded();

            if (terrainDataManager.Resources.TryGet(
                    destroyedInfo.ResourceID,
                    out TerrainResourceDef resourceDef) == false)
                return;

            itemDropSpawner.TrySpawn(
                resourceDef.DropItemID,
                resourceDef.DropCount,
                destroyedInfo.WorldPosition,
                0f,
                out _);
        }
    }
}
