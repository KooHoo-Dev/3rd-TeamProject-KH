using UnityEngine;

namespace TerrainCollapse
{
    // ItemID와 수량을 필드 아이템으로 생성

    public sealed class ItemDropSpawner : MonoBehaviour
    {
        private const string DROP_PREFAB_KEY = "WorldItemDrop";

        private WorldItemDrop dropPrefab;
        private Transform dropRoot;

        private void Awake()
        {
            dropPrefab = Resources.Load<WorldItemDrop>(DROP_PREFAB_KEY);

            GameObject root = new("WorldItemDrops");
            dropRoot = root.transform;
        }

        public bool TrySpawn(
            int itemID,
            int quantity,
            Vector2 worldPosition,
            float pickupDelay,
            out WorldItemDrop itemDrop)
        {
            itemDrop = null;

            if (quantity <= 0) return false;

            if (DataManager.Instance == null ||
                DataManager.Instance.ItemTable.TryGet(itemID, out ItemDef itemDef) == false)
            {
                Debug.LogWarning($"[Item Drop] Item data missing: item={itemID}");
                return false;
            }

            if (dropPrefab == null)
            {
                Debug.LogWarning($"[Item Drop] Drop prefab missing: {DROP_PREFAB_KEY}");
                return false;
            }

            itemDrop = Instantiate(dropPrefab, worldPosition, Quaternion.identity, dropRoot);
            itemDrop.Initialize(itemDef, quantity, pickupDelay);

            return true;
        }
    }
}
