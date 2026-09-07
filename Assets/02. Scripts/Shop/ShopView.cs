using System;
using System.Collections;
using System.Collections.Generic;
using TerrainCollapse.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TerrainCollapse
{
    // 상점 UI. 목록(ShopListing) 개수만큼 rowTemplate을 복제해서 카드로 보여준다.
    // - itemListContent/rowTemplate: 카드형 구매 목록
    // - undoButton: 이번 방문에서 산 것을 선입후출로 되돌리는 버튼 (판매 기능 대체)
    // - notificationText: 구매/되돌리기 실패 시 잠깐 떴다가 사라지는 알림 (코루틴 기반)
    public sealed class ShopView : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot; // 비워두면 이 GameObject 자체를 켜고 끔
        [SerializeField] private Button pickaxeTabButton;
        [SerializeField] private Button itemTabButton;
        [SerializeField] private RectTransform itemListContent; // 카드가 배치될 부모 (ShopUIBuilder의 ItemListPanel)
        [SerializeField] private ShopItemRowView rowTemplate; // 씬에서는 비활성 상태로 존재하는 카드 템플릿
        [SerializeField] private Button undoButton;
        [SerializeField] private TMP_Text goldText;
        [SerializeField] private TMP_Text notificationText;
        [SerializeField, Min(0.1f)] private float notificationDuration = 2f;

        // 기본값은 곡괭이 탭 - 상점을 열 때마다 이 값으로 리셋된다.
        private ShopCategory currentCategory = ShopCategory.Pickaxe;

        private ShopService shopService;
        private IItemInventory inventory;
        private PickaxeInventory pickaxeInventory;
        private IReadOnlyList<ShopListing> listings = Array.Empty<ShopListing>();
        private readonly List<ShopItemRowView> spawnedRows = new();
        private Coroutine notificationRoutine;
        private RoomClient roomClient;

        private GameObject Root => panelRoot != null ? panelRoot : gameObject;

        public void Bind(ShopService service, IItemInventory playerInventory, PickaxeInventory playerPickaxeInventory)
        {
            if (inventory != null)
                inventory.ItemChanged -= HandleInventoryChanged;

            shopService = service;
            inventory = playerInventory;
            pickaxeInventory = playerPickaxeInventory;

            if (roomClient != null)
                roomClient.ShopNotificationReceived -= ShowNotification;
            roomClient = RoomClient.Instance;
            if (roomClient != null)
                roomClient.ShopNotificationReceived += ShowNotification;

            if (inventory != null)
                inventory.ItemChanged += HandleInventoryChanged;

            RefreshGold();
        }

        private void OnDestroy()
        {
            if (inventory != null)
                inventory.ItemChanged -= HandleInventoryChanged;
            if (roomClient != null)
                roomClient.ShopNotificationReceived -= ShowNotification;
        }

        public void BeginSession()
        {
            if (TryGetServerClient(out RoomClient roomClient))
            {
                if (roomClient.RequestShop(ShopActions.Begin) == false)
                    ShowNotification("상점 이용 요청을 보내지 못했습니다.");
                return;
            }

            shopService?.BeginSession();
        }

        private void Awake()
        {
            notificationText.gameObject.SetActive(false);

            // 템플릿 자체는 화면에 안 보여야 함 (복제본만 활성화해서 사용)
            rowTemplate.gameObject.SetActive(false);
            undoButton.onClick.AddListener(HandleUndoClicked);
            pickaxeTabButton.onClick.AddListener(() => SetCategory(ShopCategory.Pickaxe));
            itemTabButton.onClick.AddListener(() => SetCategory(ShopCategory.Item));

            Hide();
        }

        public void Show()
        {
            Root.SetActive(true);
            RefreshGold();
            SetCategory(ShopCategory.Pickaxe); // 상점에 들어올 때마다 기본값은 곡괭이 탭
        }

        public void Hide()
        {
            Root.SetActive(false);
        }

        // 탭 전환: 현재 카테고리를 바꾸고 탭 버튼 활성/비활성 표시를 갱신한 뒤 목록을 다시 그린다.
        private void SetCategory(ShopCategory category)
        {
            currentCategory = category;

            // 지금 선택된 탭의 버튼은 눌러도 소용없게(=비활성) 만들어서 "여기 있다"는 표시로 사용한다.
            pickaxeTabButton.interactable = category != ShopCategory.Pickaxe;
            itemTabButton.interactable = category != ShopCategory.Item;

            RefreshListing();
        }

        // 목록을 다시 조회해서 현재 탭(currentCategory)에 해당하는 것만 카드로 찍어낸다.
        // 곡괭이 탭은 추가로 "보유 곡괭이보다 채굴력이 낮은" 곡괭이를 잠근다.
        private void RefreshListing()
        {
            listings = shopService.GetListings();

            ClearSpawnedRows();

            // 보유 곡괭이가 없으면 아무 것도 잠그지 않는다 (비교 대상이 없으므로).
            int currentDigPower = pickaxeInventory != null && pickaxeInventory.CurrentDef != null
                ? pickaxeInventory.CurrentDef.PickaxeData?.DigPower ?? int.MinValue
                : int.MinValue;

            foreach (ShopListing listing in listings)
            {
                if (listing.Category != currentCategory) continue;

                bool isLocked = listing.Category == ShopCategory.Pickaxe && listing.DigPower <= currentDigPower;

                ShopItemRowView row = Instantiate(rowTemplate, itemListContent);
                row.gameObject.SetActive(true);
                row.Bind(listing, HandlePurchaseRequested, isLocked);
                spawnedRows.Add(row);
            }
        }

        private void ClearSpawnedRows()
        {
            foreach (ShopItemRowView row in spawnedRows)
            {
                if (row != null) Destroy(row.gameObject);
            }

            spawnedRows.Clear();
        }

        // 카드의 구매 버튼이 눌렸을 때 ShopItemRowView가 호출하는 콜백
        private void HandlePurchaseRequested(ShopListing listing)
        {
            if (TryGetServerClient(out RoomClient roomClient))
            {
                if (roomClient.RequestShop(ShopActions.Purchase, listing.ItemID) == false)
                    ShowNotification("구매 요청을 보내지 못했습니다.");
                return;
            }

            ShopPurchaseResult result = shopService.TryPurchase(inventory, listing.ItemID, 1);

            if (result.Success)
            {
                Debug.Log($"[Shop] 구매: {result.ItemName} (-{result.SpentGold} Gold)");
                RefreshListing();
                return;
            }

            ShowNotification(GetPurchaseFailMessage(result.FailReason));
        }

        // 되돌리기 버튼 클릭 - 이번 방문에서 가장 최근에 산 것부터 선입후출로 되돌린다.
        private void HandleUndoClicked()
        {
            if (TryGetServerClient(out RoomClient roomClient))
            {
                if (roomClient.RequestShop(ShopActions.Undo) == false)
                    ShowNotification("구매 되돌리기 요청을 보내지 못했습니다.");
                return;
            }

            ShopUndoResult result = shopService.TryUndoLastPurchase(inventory);

            if (result.Success)
            {
                Debug.Log($"[Shop] 되돌리기: {result.ItemName} (+{result.RefundedGold} Gold)");
                ShowNotification($"{result.ItemName} 구매를 되돌렸습니다. (+{result.RefundedGold} Gold)");
                return;
            }

            ShowNotification(GetUndoFailMessage(result.FailReason));
        }

        private static bool TryGetServerClient(out RoomClient roomClient)
        {
            roomClient = RoomClient.Instance;
            return roomClient != null && roomClient.IsConnected;
        }

        private void HandleInventoryChanged(InventoryItemChange change)
        {
            if (TryGetGoldItemID(out int goldItemID) && change.ItemID == goldItemID)
                RefreshGold();
        }

        private void RefreshGold()
        {
            if (goldText == null || inventory == null || TryGetGoldItemID(out int goldItemID) == false)
                return;

            goldText.text = $"{inventory.GetQuantity(goldItemID):N0} 골드";
        }

        private static bool TryGetGoldItemID(out int goldItemID)
        {
            goldItemID = 0;
            if (DataManager.Instance == null ||
                DataManager.Instance.ItemTable.TryGetGold(out ItemDef goldItem) == false)
                return false;

            goldItemID = goldItem.ID;
            return true;
        }

        private static string GetPurchaseFailMessage(ShopPurchaseFailReason reason) => reason switch
        {
            ShopPurchaseFailReason.InsufficientGold => "골드가 부족합니다.",
            ShopPurchaseFailReason.InventoryRejected => "인벤토리 공간이 부족해 구매할 수 없습니다.",
            ShopPurchaseFailReason.NotForSale => "판매하지 않는 상품입니다.",
            _ => "구매할 수 없습니다."
        };

        private static string GetUndoFailMessage(ShopUndoFailReason reason) => reason switch
        {
            ShopUndoFailReason.NothingToUndo => "되돌릴 구매 내역이 없습니다.",
            ShopUndoFailReason.ItemUnavailable => "이미 사용되었거나 없어져 되돌릴 수 없습니다.",
            _ => "되돌릴 수 없습니다."
        };

        private void ShowNotification(string message)
        {
            // 알림이 표시되는 도중 새 알림이 또 들어오면 이전 타이머를 멈추고 새로 띄웁니다.
            if (notificationRoutine != null)
                StopCoroutine(notificationRoutine);

            notificationRoutine = StartCoroutine(ShowNotificationRoutine(message));
        }

        private IEnumerator ShowNotificationRoutine(string message)
        {
            notificationText.text = message;
            notificationText.gameObject.SetActive(true);

            yield return new WaitForSeconds(notificationDuration);

            notificationText.gameObject.SetActive(false);
            notificationRoutine = null;
        }
    }
}
