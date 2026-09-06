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
        [SerializeField] private CanvasGroup canvasGroup; // 잠금 시 카드를 흐리게 + 클릭 차단

        private const float LOCKED_ALPHA = 0.4f;

        private ShopListing listing;
        private Action<ShopListing> onBuyClicked;

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
            if (buyButton != null)
                buyButton.onClick.AddListener(HandleBuyClicked);
        }

        public void Bind(ShopListing shopListing, Action<ShopListing> buyCallback, bool isLocked = false)
        {
            listing = shopListing;
            onBuyClicked = buyCallback;

            if (nameText != null) nameText.text = listing.Name;

            if (priceText != null)
                priceText.text = isLocked ? "보유 곡괭이보다 채굴력이 낮음" : $"{listing.Price} Gold";

            if (iconImage != null)
            {
                iconImage.sprite = listing.Icon;
                iconImage.enabled = listing.Icon != null;
            }

            if (canvasGroup != null)
            {
                canvasGroup.alpha = isLocked ? LOCKED_ALPHA : 1f;
                canvasGroup.interactable = isLocked == false;
                canvasGroup.blocksRaycasts = isLocked == false;
            }
        }

        private void HandleBuyClicked()
        {
            onBuyClicked?.Invoke(listing);
        }
    }
}