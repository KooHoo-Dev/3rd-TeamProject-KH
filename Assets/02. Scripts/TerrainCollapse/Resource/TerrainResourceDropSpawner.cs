using UnityEngine;

namespace TerrainCollapse
{
    // 자원 타일 파괴 이벤트를 필드 아이템 드롭으로 변환

    [RequireComponent(typeof(TerrainManager))]
    public sealed class TerrainResourceDropSpawner : MonoBehaviour
    {
        private const string DropPrefabKey = "WorldItemDrop";

        private TerrainManager terrainManager;
        private WorldItemDrop dropPrefab;
        private Transform dropRoot;

        private void Awake()
        {
            terrainManager = GetComponent<TerrainManager>();
            dropPrefab = UnityEngine.Resources.Load<WorldItemDrop>(DropPrefabKey);

            GameObject root = new("WorldItemDrops");
            dropRoot = root.transform;
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

            if (DataManager.Instance == null ||
                DataManager.Instance.ItemTable.TryGet(resourceDef.DropItemID, out ItemDef itemDef) == false)
            {
                Debug.LogWarning(
                    $"[Terrain Resource] Item data missing: item={resourceDef.DropItemID}");
                return;
            }

            if (dropPrefab == null)
            {
                Debug.LogWarning($"[Terrain Resource] Drop prefab missing: {DropPrefabKey}");
                return;
            }

            WorldItemDrop itemDrop = Instantiate(
                dropPrefab,
                destroyedInfo.WorldPosition,
                Quaternion.identity,
                dropRoot);

            itemDrop.Initialize(itemDef, resourceDef.DropCount);
        }
    }
}
