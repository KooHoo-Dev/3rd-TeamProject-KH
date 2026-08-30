using UnityEngine;

namespace TerrainCollapse
{
    // 자원 타일 파괴 이벤트를 필드 아이템 드롭으로 변환

    [RequireComponent(typeof(TerrainManager))]
    [RequireComponent(typeof(ItemDropSpawner))]
    public sealed class TerrainResourceDropSpawner : MonoBehaviour
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

        private void HandleCellDestroyed(TerrainCellDestroyedInfo destroyedInfo)
        {
            if (destroyedInfo.HasResource == false) return;

            TerrainDataManager terrainDataManager = TerrainDataManager.Instance;
            terrainDataManager.EnsureLoaded();

            if (terrainDataManager.Resources.TryGet(
                    destroyedInfo.ResourceID,
                    out TerrainResourceDef resourceDef) == false)
            {
                Debug.LogWarning(
                    $"[Terrain Resource] Drop data missing: resource={destroyedInfo.ResourceID}");
                return;
            }

            itemDropSpawner.TrySpawn(
                resourceDef.DropItemID,
                resourceDef.DropCount,
                destroyedInfo.WorldPosition,
                0f,
                out _);
        }
    }
}
