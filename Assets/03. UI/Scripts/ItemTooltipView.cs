using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TerrainCollapse
{
    /// <summary>하나의 Tooltip 프리팹을 재사용하며 아이템 테이블 데이터를 화면에 표시한다.</summary>
    public sealed class ItemTooltipView : MonoBehaviour
    {
        public static ItemTooltipView Instance { get; private set; }

        [Header("Text References")]
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text typeText;
        [SerializeField] private TMP_Text descriptionText;
        [SerializeField] private Image iconImage;

        [Header("Position")]
        [SerializeField, Min(0f)] private float pointerPadding = 16f;

        private RectTransform tooltipRect;
        private Canvas rootCanvas;
        private TooltipTrigger activeTrigger;

        private void Awake()
        {
            Instance = this;
            tooltipRect = transform as RectTransform;
            rootCanvas = GetComponentInParent<Canvas>()?.rootCanvas;

            nameText ??= FindText("NameText");
            typeText ??= FindText("TypeText");
            descriptionText ??= FindText("DescriptionText");
            iconImage ??= FindImage("Icon");

            foreach (Graphic graphic in GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = false;

            gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Show(TooltipTrigger trigger, int itemID, Vector2 screenPosition)
        {
            if (DataManager.Instance == null ||
                DataManager.Instance.ItemTable.TryGet(itemID, out ItemDef itemDef) == false)
                return;

            activeTrigger = trigger;
            nameText.text = itemDef.Name;
            typeText.text = GetTypeText(itemDef.Type);
            descriptionText.text = BuildDescription(itemDef);

            iconImage.sprite = itemDef.Sprite;
            iconImage.enabled = itemDef.Sprite != null;

            gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            SetScreenPosition(screenPosition);
        }

        public void Hide(TooltipTrigger trigger)
        {
            if (activeTrigger != trigger) return;

            Hide();
        }

        /// <summary>현재 호버 대상과 무관하게 Tooltip을 즉시 닫는다.</summary>
        public void Hide()
        {
            activeTrigger = null;
            gameObject.SetActive(false);
        }

        public void SetScreenPosition(Vector2 screenPosition)
        {
            if (tooltipRect == null || rootCanvas == null) return;

            Camera uiCamera = rootCanvas != null &&
                              rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? rootCanvas.worldCamera
                : null;

            // Tooltip의 실제 화면 크기를 구해 화면 경계와 직접 비교한다.
            Vector3[] corners = new Vector3[4];
            tooltipRect.GetWorldCorners(corners);
            float width = Vector2.Distance(
                RectTransformUtility.WorldToScreenPoint(uiCamera, corners[0]),
                RectTransformUtility.WorldToScreenPoint(uiCamera, corners[3]));
            float height = Vector2.Distance(
                RectTransformUtility.WorldToScreenPoint(uiCamera, corners[0]),
                RectTransformUtility.WorldToScreenPoint(uiCamera, corners[1]));

            Rect screenBounds = rootCanvas.pixelRect;
            bool placeRight = screenPosition.x + pointerPadding + width <= screenBounds.xMax;
            bool placeBelow = screenPosition.y - pointerPadding - height >= screenBounds.yMin;

            // 기본은 커서 우하단. 해당 방향에 전체 Tooltip이 들어가지 않을 때만
            // 아래 -> 위, 오른쪽 -> 왼쪽 순서로 뒤집는다.
            Vector2 targetScreenPosition = new(
                screenPosition.x + (placeRight ? 1f : -1f) * (pointerPadding + width * 0.5f),
                screenPosition.y + (placeBelow ? -1f : 1f) * (pointerPadding + height * 0.5f));

            // 부모의 앵커/피벗 설정과 무관하게 화면상의 실제 위치를 직접 지정한다.
            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(
                    tooltipRect.parent as RectTransform, targetScreenPosition, uiCamera, out Vector3 worldPosition))
            {
                tooltipRect.position = worldPosition;
            }
        }

        private TMP_Text FindText(string objectName)
        {
            foreach (TMP_Text text in GetComponentsInChildren<TMP_Text>(true))
                if (text.name == objectName) return text;

            return null;
        }

        private Image FindImage(string objectName)
        {
            foreach (Image image in GetComponentsInChildren<Image>(true))
                if (image.name == objectName) return image;

            return null;
        }

        private static string GetTypeText(ItemType itemType)
        {
            return itemType switch
            {
                ItemType.Pickaxe => "곡괭이",
                ItemType.Usable => "사용 아이템",
                ItemType.Exchange => "자원",
                ItemType.Gold => "골드",
                _ => string.Empty
            };
        }

        private static string BuildDescription(ItemDef itemDef)
        {
            List<string> lines = new() { $"무게: {itemDef.Weight}" };

            if (itemDef.Type == ItemType.Exchange)
            {
                lines.Add($"판매 가격: {itemDef.Price:N0}G");
            }
            else if (itemDef.Type == ItemType.Pickaxe && itemDef.PickaxeData.HasValue)
            {
                PickaxeInfo pickaxe = itemDef.PickaxeData.Value;
                lines.Add($"채굴력: {pickaxe.DigPower}");
                lines.Add($"사거리: {pickaxe.Range:0.#}");
                lines.Add($"수용량: +{itemDef.InventoryCapacityBonus}");
            }
            else if (itemDef.Type == ItemType.Usable &&
                     string.IsNullOrWhiteSpace(itemDef.Description) == false)
            {
                lines.Add(itemDef.Description);
            }

            return string.Join("\n", lines);
        }
    }
}
