using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace TerrainCollapse
{
    // 상점 UI. 목록(ShopListing) 개수만큼 rowTemplate을 복제해서 카드로 보여준다.
    // - itemListContent/rowTemplate: 카드형 구매 목록
    // - notificationText: 구매 불가 시 잠깐 떴다가 사라지는 알림 (코루틴 기반)
    public sealed class ShopView : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot; // 비워두면 이 GameObject 자체를 켜고 끔
        [SerializeField] private RectTransform itemListContent; // 카드가 배치될 부모 (ShopUIBuilder의 ItemListPanel)
        [SerializeField] private ShopItemRowView rowTemplate; // 씬에서는 비활성 상태로 존재하는 카드 템플릿
        [SerializeField] private TMP_Text notificationText;
        [SerializeField, Min(0.1f)] private float notificationDuration = 2f;

        private ShopService shopService;
        private IItemInventory inventory;
        private IReadOnlyList<ShopListing> listings = Array.Empty<ShopListing>();
        private readonly List<ShopItemRowView> spawnedRows = new();
        private Coroutine notificationRoutine;

        private GameObject Root => panelRoot != null ? panelRoot : gameObject;

        public void Bind(ShopService service, IItemInventory playerInventory)
        {
            shopService = service;
            inventory = playerInventory;
        }

        private void Awake()
        {
            if (notificationText != null)
                notificationText.gameObject.SetActive(false);

            // 템플릿 자체는 화면에 안 보여야 함 (복제본만 활성화해서 사용)
            if (rowTemplate != null)
                rowTemplate.gameObject.SetActive(false);

            Hide();
        }

        public void Show()
        {
            Root.SetActive(true);
            RefreshListing();
        }

        public void Hide()
        {
            Root.SetActive(false);
        }

        // 목록을 다시 조회해서 카드들을 새로 찍어낸다. 개수는 shopService.GetListings() 결과에 그대로 따라간다.
        private void RefreshListing()
        {
            listings = shopService.GetListings();

            ClearSpawnedRows();

            foreach (ShopListing listing in listings)
            {
                ShopItemRowView row = Instantiate(rowTemplate, itemListContent);
                row.gameObject.SetActive(true);
                row.Bind(listing, HandlePurchaseRequested);
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
            ShopPurchaseResult result = shopService.TryPurchase(inventory, listing.ItemID, 1);

            if (result.Success)
            {
                Debug.Log($"[Shop] 구매: {result.ItemName} (-{result.SpentGold} Gold)");
                RefreshListing();
                return;
            }

            ShowPurchaseFailedNotification(result.FailReason);
        }

        private void ShowPurchaseFailedNotification(ShopPurchaseFailReason reason)
        {
            string message = reason switch
            {
                ShopPurchaseFailReason.InsufficientGold => "Not Enough Money",
                ShopPurchaseFailReason.InventoryRejected => "Not Enough Inventory",
                ShopPurchaseFailReason.NotForSale => "Not For Sale",
                ShopPurchaseFailReason.ServerAuthorityUnsupported => "Unavailable In Multiplayer",
                _ => "Can Not Buy."
            };

            // 알림이 표시되는 도중 새 실패가 또 들어오면 이전 타이머를 멈추고 새로 띄웁니다.
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
