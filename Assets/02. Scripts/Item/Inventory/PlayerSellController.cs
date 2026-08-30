using System.Text;
using UnityEngine;

namespace TerrainCollapse
{
    // 플레이어의 지상 진입을 감지해 보유 자원을 자동 판매

    [RequireComponent(typeof(PlayerInventory), typeof(TestPlayerController))]
    public sealed class PlayerSellController : MonoBehaviour
    {
        [SerializeField] private float sellAreaMinimumY = 0f;

        private PlayerInventory inventory;
        private TestPlayerController playerController;
        private ItemSaleService itemSaleService;
        private bool wasBelowSellArea;

        private void Awake()
        {
            inventory = GetComponent<PlayerInventory>();
            playerController = GetComponent<TestPlayerController>();
        }

        private void Start()
        {
            itemSaleService = new ItemSaleService(DataManager.Instance.ItemTable);
            wasBelowSellArea = playerController.CurrentPosition.y < sellAreaMinimumY;
        }

        private void Update()
        {
            if (playerController.IsDead || itemSaleService == null) return;

            bool isInSellArea = playerController.CurrentPosition.y >= sellAreaMinimumY;

            if (wasBelowSellArea && isInSellArea)
            {
                ItemSaleResult result = itemSaleService.SellAllExchangeItems(inventory);

                if (result.EarnedGold > 0) LogSaleResult(result);
            }

            wasBelowSellArea = isInSellArea == false;
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
