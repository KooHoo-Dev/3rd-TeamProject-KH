using UnityEngine;

namespace TerrainCollapse
{
    // 개발 테스트용-나중에 삭제: 플레이어 인벤토리에 초기 골드 지급

    [RequireComponent(typeof(PlayerInventory))]
    public sealed class PlayerInventoryDevelopmentInitializer : MonoBehaviour
    {
        [SerializeField, Min(0)] private int initialGold = 500;

        private void Start()
        {
            if (initialGold <= 0) return;

            if (DataManager.Instance == null ||
                DataManager.Instance.ItemTable.TryGetGold(out ItemDef goldItem) == false)
            {
                Debug.LogWarning("[Inventory Development] Gold item data is missing.");
                return;
            }

            PlayerInventory inventory = GetComponent<PlayerInventory>();
            inventory.TryAdd(goldItem.ID, initialGold);
            inventory.TryAdd(52,100);
        }
    }
}
