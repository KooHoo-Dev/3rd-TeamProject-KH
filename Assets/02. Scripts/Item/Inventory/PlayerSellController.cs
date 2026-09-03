using System.Text;
using UnityEngine;

namespace TerrainCollapse
{
    // 플레이어의 지상 진입을 감지해 보유 자원을 자동 판매

    [RequireComponent(typeof(PlayerInventory), typeof(TestPlayerController))]
    public sealed class PlayerSellController : MonoBehaviour
    {
        private PlayerInventory inventory;
        private TestPlayerController playerController;
        private RoomClient roomClient;
        private ItemSaleService itemSaleService;
        private bool wasInSellArea;

        private void Awake()
        {
            inventory = GetComponent<PlayerInventory>();
            playerController = GetComponent<TestPlayerController>();
            roomClient = RoomClient.Instance ?? FindFirstObjectByType<RoomClient>();
        }

        private void Start()
        {
            itemSaleService = new ItemSaleService(DataManager.Instance.ItemTable);
            wasInSellArea = IsInSellArea();
        }

        private void Update()
        {
            if (playerController.IsDead || itemSaleService == null) return;
            // 현재 서버에는 판매/골드 변경 명령이 없으므로 멀티플레이에서는 판매하지 않는다.
            if (roomClient != null) return;

            bool isInSellArea = IsInSellArea();

            if (wasInSellArea == false && isInSellArea)
            {
                ItemSaleResult result = itemSaleService.SellAllExchangeItems(inventory);

                if (result.EarnedGold > 0) LogSaleResult(result);
            }

            wasInSellArea = isInSellArea;
        }

        private bool IsInSellArea()
        {
            roomClient ??= RoomClient.Instance ?? FindFirstObjectByType<RoomClient>();

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
