using System;
using System.Collections.Generic;
using UnityEngine;

namespace TerrainCollapse
{
    // ItemID와 수량을 필드 아이템으로 생성

    public sealed class ItemDropSpawner : MonoBehaviour
    {
        private const string DROP_PREFAB_KEY = "WorldItemDrop";

        private WorldItemDrop dropPrefab;
        private Transform dropRoot;
        private readonly Dictionary<string, WorldItemDrop> networkDrops = new();

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

        public bool TrySpawnNetworkDrop(
            string dropID,
            int itemID,
            int quantity,
            Vector2 worldPosition,
            long pickupAvailableAtUnixMilliseconds,
            out WorldItemDrop itemDrop)
        {
            itemDrop = null;
            if (string.IsNullOrWhiteSpace(dropID) || quantity <= 0) return false;

            if (networkDrops.TryGetValue(dropID, out WorldItemDrop existing) && existing != null)
            {
                itemDrop = existing;
                return true;
            }

            if (DataManager.Instance == null ||
                DataManager.Instance.ItemTable.TryGet(itemID, out ItemDef itemDef) == false ||
                dropPrefab == null)
                return false;

            itemDrop = Instantiate(dropPrefab, worldPosition, Quaternion.identity, dropRoot);
            float pickupDelay = Mathf.Max(
                0f,
                (pickupAvailableAtUnixMilliseconds -
                 DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) / 1000f);
            itemDrop.Initialize(dropID, itemDef, quantity, pickupDelay);
            networkDrops[dropID] = itemDrop;
            return true;
        }

        public bool RemoveNetworkDrop(string dropID)
        {
            if (string.IsNullOrWhiteSpace(dropID) ||
                networkDrops.Remove(dropID, out WorldItemDrop itemDrop) == false)
                return false;

            if (itemDrop != null) Destroy(itemDrop.gameObject);
            return true;
        }

        public void ReplaceNetworkDrops(IEnumerable<Networking.WorldItemDropDto> drops)
        {
            foreach (WorldItemDrop itemDrop in networkDrops.Values)
                if (itemDrop != null) Destroy(itemDrop.gameObject);
            networkDrops.Clear();

            if (drops == null) return;
            foreach (Networking.WorldItemDropDto drop in drops)
            {
                if (drop == null) continue;
                TrySpawnNetworkDrop(
                    drop.DropID,
                    drop.ItemID,
                    drop.Quantity,
                    new Vector2(drop.X, drop.Y),
                    drop.PickupAvailableAtUnixMilliseconds,
                    out _);
            }
        }
    }
}
