using System.Text;
using UnityEngine;

namespace TerrainCollapse
{
    // 플레이어의 지상 진입을 감지해 보유 자원을 자동 판매

    [RequireComponent(typeof(PlayerInventory))]
    public sealed class PlayerSellController : MonoBehaviour
    {
        private PlayerInventory inventory;
        private IPlayerController playerController;
        private ItemSaleService itemSaleService;
        private bool wasInSellArea;

        private void Awake()
        {
            inventory = GetComponent<PlayerInventory>();
            playerController = GetComponent(typeof(IPlayerController)) as IPlayerController;

            if (playerController == null)
            {
                Debug.LogError("[Sell] IPlayerController가 없어 자동 판매를 비활성화합니다.", this);
                enabled = false;
            }
        }

        private void Start()
        {
            if (DataManager.Instance == null)
            {
                Debug.LogWarning("[Sell] DataManager가 준비되지 않아 자동 판매를 비활성화합니다.", this);
                enabled = false;
                return;
            }

            itemSaleService = new ItemSaleService(DataManager.Instance.ItemTable);
            wasInSellArea = IsInSellArea();
        }

        private void Update()
        {
            if (playerController.IsDead || itemSaleService == null) return;

            bool isInSellArea = IsInSellArea();

            if (wasInSellArea == false && isInSellArea)
            {
                RoomClient roomClient = RoomClient.Instance;
                if (roomClient != null && roomClient.IsConnected)
                {
                    if (roomClient.RequestInventorySell() == false)
                        Debug.LogWarning("[Sell] 서버에 자동 판매 요청을 보내지 못했습니다.", this);
                }
                else
                {
                    ItemSaleResult result = itemSaleService.SellAllExchangeItems(inventory);
                    if (result.EarnedGold > 0) LogSaleResult(result);
                }
            }

            wasInSellArea = isInSellArea;
        }

        private bool IsInSellArea()
        {
            RoomClient roomClient = RoomClient.Instance;
            if (roomClient != null && roomClient.IsAuthoritativeTerrainMode)
                return roomClient.IsLocalPlayerInSpawnArea;

            // 로컬 테스트 맵은 기존 지상 진입 규칙을 유지
            return playerController.CurrentPosition.y >= 0f;
        }

        private void LogSaleResult(ItemSaleResult result)
        {
            StringBuilder message = new("[Sell] ");

            for (int i = 0; i < result.SoldItems.Count; i++)
            {
                ItemSaleResult.SoldItem item = result.SoldItems[i];
                if (i > 0) message.Append(", ");

                message.Append($"{item.Name} x{item.Quantity} (+{item.EarnedGold} Gold)");
            }

            message.Append($" | Total +{result.EarnedGold} Gold");
            Debug.Log(message, this);
        }
    }
}
