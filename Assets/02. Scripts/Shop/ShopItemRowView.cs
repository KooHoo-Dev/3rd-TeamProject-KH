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

        private ShopListing listing;
        private Action<ShopListing> onBuyClicked;

        // ShopUIBuilder 등 화면 생성 코드가 자식 참조를 넘겨줄 때 사용 (에디터 타임 배선용)
        public void Configure(Image icon, TMP_Text name, TMP_Text price, Button buy)
        {
            iconImage = icon;
            nameText = name;
            priceText = price;
            buyButton = buy;
        }

        private void Awake()
        {
            if (buyButton != null)
                buyButton.onClick.AddListener(HandleBuyClicked);
        }

        public void Bind(ShopListing shopListing, Action<ShopListing> buyCallback)
        {
            listing = shopListing;
            onBuyClicked = buyCallback;

            if (nameText != null) nameText.text = listing.Name;
            if (priceText != null) priceText.text = $"{listing.Price} 골드";

            if (iconImage != null)
            {
                iconImage.sprite = listing.Icon;
                iconImage.enabled = listing.Icon != null;
            }
        }

        private void HandleBuyClicked()
        {
            onBuyClicked?.Invoke(listing);
        }
    }
}
