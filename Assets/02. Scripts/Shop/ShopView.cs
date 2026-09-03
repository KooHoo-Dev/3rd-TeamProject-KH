using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace TerrainCollapse
{
    // 디자인은 고려하지 않은 상점 UI 플레이스홀더.
    // - listingText: "[1] 다이너마이트 - 30 Gold" 같은 목록을 텍스트로만 표시
    // - notificationText: 구매 불가 시 잠깐 떴다가 사라지는 알림 플레이스홀더 (코루틴 기반)
    public sealed class ShopView : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot; // 비워두면 이 GameObject 자체를 켜고 끔
        [SerializeField] private TMP_Text listingText;
        [SerializeField] private TMP_Text notificationText;
        [SerializeField, Min(0.1f)] private float notificationDuration = 2f;

        private ShopService shopService;
        private IItemInventory inventory;
        private IReadOnlyList<ShopListing> listings = Array.Empty<ShopListing>();
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

        private void Update()
        {
            if (Root.activeSelf == false) return;
            if (shopService == null || inventory == null) return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            // 목록의 1~5번째 항목을 숫자키 1~5에 대응 (플레이스홀더 수준의 임시 입력)
            TryPurchaseByKey(keyboard.digit1Key, 0);
            TryPurchaseByKey(keyboard.digit2Key, 1);
            TryPurchaseByKey(keyboard.digit3Key, 2);
            TryPurchaseByKey(keyboard.digit4Key, 3);
            TryPurchaseByKey(keyboard.digit5Key, 4);
        }

        // wasPressedThisFrame(엣지 트리거)만 사용하므로 키를 누르고 있어도 한 번만 구매가 실행됩니다.
        private void TryPurchaseByKey(ButtonControl key, int listingIndex)
        {
            if (key.wasPressedThisFrame == false) return;
            if (listingIndex >= listings.Count) return;

            ShopListing listing = listings[listingIndex];
            ShopPurchaseResult result = shopService.TryPurchase(inventory, listing.ItemID, 1);

            if (result.Success)
            {
                Debug.Log($"[Shop] 구매: {result.ItemName} (-{result.SpentGold} Gold)");
                RefreshListing();
                return;
            }

            ShowPurchaseFailedNotification(result.FailReason);
        }

        private void RefreshListing()
        {
            listings = shopService.GetListings();

            if (listingText == null) return;

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < listings.Count; i++)
            {
                ShopListing listing = listings[i];
                sb.AppendLine($"[{i + 1}] {listing.Name} - {listing.Price} Gold");
            }

            listingText.text = sb.Length > 0 ? sb.ToString() : "판매 중인 상품이 없습니다.";
        }

        private void ShowPurchaseFailedNotification(ShopPurchaseFailReason reason)
        {
            if (notificationText == null) return;

            string message = reason switch
            {
                ShopPurchaseFailReason.InsufficientGold => "골드가 부족합니다.",
                ShopPurchaseFailReason.InventoryRejected => "인벤토리 공간이 부족해 구매할 수 없습니다.",
                ShopPurchaseFailReason.NotForSale => "판매하지 않는 상품입니다.",
                _ => "구매할 수 없습니다."
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
