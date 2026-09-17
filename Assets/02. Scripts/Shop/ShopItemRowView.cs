using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TerrainCollapse
{
    /// <summary>
    /// 상점 아이템 카드 한 줄. ShopListing 데이터를 화면에 반영하고,
    /// 구매 버튼이 눌리면 바인딩된 콜백에게 "이 아이템을 사겠다"는 신호만 전달한다.
    /// 인벤토리/골드 등 실제 구매 처리는 전혀 모른다 (ShopView/ShopService의 책임).
    /// </summary>
    public sealed class ShopItemRowView : MonoBehaviour
    {
        [SerializeField] private Image iconImage;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text priceText;
        [SerializeField] private Button buyButton;
        [SerializeField] private CanvasGroup canvasGroup;

        private ShopListing listing;
        private Action<ShopListing> onBuyClicked;
        private bool hasListing;
        private FontStyles originalPriceFontStyle;
        private bool cachedOriginalPriceStyle;

        public int ItemID => hasListing ? listing.ItemID : -1;

        // ShopUIBuilder 등 화면 생성 코드가 자식 참조를 넘겨줄 때 사용 (에디터 타임 배선용)
        public void Configure(Image icon, TMP_Text name, TMP_Text price, Button buy, CanvasGroup group)
        {
            iconImage = icon;
            nameText = name;
            priceText = price;
            buyButton = buy;
            canvasGroup = group;
        }

        private void Awake()
        {
            if (priceText != null)
            {
                originalPriceFontStyle = priceText.fontStyle;
                cachedOriginalPriceStyle = true;
            }
            if (buyButton != null)
                buyButton.onClick.AddListener(HandleBuyClicked);
        }

        public void Bind(
            ShopListing shopListing,
            Action<ShopListing> buyCallback,
            bool isLocked = false,
            int discountedPrice = -1)
        {
            listing = shopListing;
            hasListing = true;
            onBuyClicked = buyCallback;

            if (nameText != null) nameText.text = listing.Name;

            bool hasDiscount = discountedPrice >= 0 && discountedPrice < listing.Price;
            if (priceText != null)
            {
                if (cachedOriginalPriceStyle == false)
                {
                    originalPriceFontStyle = priceText.fontStyle;
                    cachedOriginalPriceStyle = true;
                }

                priceText.fontStyle = originalPriceFontStyle;
                priceText.text = hasDiscount
                    ? $"<color=#E14B4B><s>{listing.Price:N0} 골드</s></color> → {discountedPrice:N0} 골드"
                    : $"{listing.Price:N0} 골드";
            }

            if (iconImage != null)
            {
                iconImage.sprite = listing.Icon;
                iconImage.enabled = listing.Icon != null;

                // 상점 카드 전체가 아닌 아이콘 위에서만 Tooltip을 표시한다.
                if (iconImage.TryGetComponent<TooltipTrigger>(out _) == false)
                    iconImage.gameObject.AddComponent<TooltipTrigger>();
            }

            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f;
                canvasGroup.interactable = true;
                canvasGroup.blocksRaycasts = true;
            }

            if (buyButton != null)
                buyButton.interactable = isLocked == false;
        }

        private void HandleBuyClicked()
        {
            onBuyClicked?.Invoke(listing);
        }
    }
}
