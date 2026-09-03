#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TerrainCollapse
{
    /// <summary>
    /// 상점 UI(Shop UI)를 코드만으로 조립하는 에디터 전용 빌더.
    /// 빈 GameObject에 붙인 뒤 컴포넌트 우클릭 메뉴("1. Build UI" / "2. Clear UI")로 실행한다.
    ///
    /// 화면 시안에는 상점 UI 자체가 없어 하단 소지품(인벤토리) 바의 위치만 기준으로 삼았다.
    /// 시안 기준 인벤토리 바는 화면 하단에서 위로 약 140px 지점까지 차지하므로,
    /// 상점 패널 하단을 그보다 위인 170px 지점에서 시작시켜 절대 겹치지 않게 했다.
    ///
    /// 카드가 실제 구매 목록으로 동작하도록 ShopItemRowView(순수 UI 전달용, 인벤토리/골드 등은 모름)만
    /// 템플릿 카드에 부착한다. 인벤토리 접근이나 가격 계산 같은 진짜 게임 로직은 여전히 여기 없다.
    /// </summary>
    public sealed class ShopUIBuilder : MonoBehaviour
    {
        private const string RootName = "Shop UI";
        private static readonly Vector2 ReferenceResolution = new(1920f, 1080f);

        // 하단 소지품 UI(인벤토리 바)와 절대 겹치지 않기 위한 여유 공간(화면 하단 기준)
        private const float BOTTOM_SAFE_MARGIN = 170f;

        [SerializeField] private TMP_FontAsset font; // 한글 폰트 교체용. 비워두면 TMP 기본 폰트 사용

        // 색상은 모두 코드에서 지정 (외부 에셋 참조 금지)
        private static readonly Color32 COLOR_DIMMER = new(0, 0, 0, 150);
        private static readonly Color32 COLOR_ACCENT_TEAL = new(45, 199, 214, 255);
        private static readonly Color32 COLOR_PANEL_BG = new(10, 40, 50, 235);
        private static readonly Color32 COLOR_ROW_BG = new(18, 58, 70, 255);
        private static readonly Color32 COLOR_ICON_PLACEHOLDER = new(214, 90, 40, 255);
        private static readonly Color32 COLOR_BUTTON = new(35, 150, 168, 255);
        private static readonly Color32 COLOR_GOLD_TEXT = new(255, 214, 74, 255);
        private static readonly Color32 COLOR_NOTIFICATION_TEXT = new(255, 120, 120, 255);

        #region 헬퍼 메서드

        private GameObject NewUI(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        private GameObject NewPanel(string name, Transform parent, Vector2 size)
        {
            GameObject go = NewUI(name, parent);

            Image image = go.AddComponent<Image>();
            image.color = Color.white; // 기본색. 호출부에서 용도에 맞게 재설정한다.

            RectTransform rect = (RectTransform)go.transform;
            rect.sizeDelta = size;

            return go;
        }

        private TMP_Text NewText(string name, Transform parent, string text, float size, TextAlignmentOptions align)
        {
            GameObject go = NewUI(name, parent);

            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.alignment = align;
            tmp.color = Color.white;
            tmp.raycastTarget = false;

            if (font != null) tmp.font = font;

            return tmp;
        }

        private Button NewButton(string name, Transform parent, string label, out TMP_Text labelText)
        {
            GameObject go = NewPanel(name, parent, new Vector2(120f, 48f));

            Image image = go.GetComponent<Image>();
            image.color = COLOR_BUTTON;

            Button button = go.AddComponent<Button>();

            labelText = NewText($"{name}Label", go.transform, label, 24f, TextAlignmentOptions.Center);
            Stretch((RectTransform)labelText.transform, 0f);

            return button;
        }

        private void Stretch(RectTransform rect, float padding)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
        }

        #endregion

        #region Canvas 생성

        private GameObject CreateCanvas()
        {
            GameObject root = new GameObject(RootName, typeof(RectTransform));

            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.matchWidthOrHeight = 0.5f;

            root.AddComponent<GraphicRaycaster>();

            return root;
        }

        #endregion

        #region 팝업 배경 (Dimmer + 패널 프레임)

        // Dimmer + ShopPanel(테두리) + ShopPanelBackground(내부 채움)를 만들고,
        // 내부 채움 위에 헤더/목록/닫기버튼/알림 영역을 이어서 구성한다.
        private GameObject CreatePopupRoot(Transform canvas)
        {
            GameObject popupRoot = NewUI("PopupRoot", canvas);
            Stretch((RectTransform)popupRoot.transform, 0f);

            CreateDimmer(popupRoot.transform);

            Transform shopPanelBackground = CreateShopPanelFrame(popupRoot.transform);

            CreateHeader(shopPanelBackground);
            CreateCloseButton(shopPanelBackground);
            CreateItemList(shopPanelBackground);
            CreateFooter(shopPanelBackground);

            return popupRoot;
        }

        // 화면 전체를 덮는 반투명 검정 - 상점 팝업 뒤 배경을 어둡게 처리
        private void CreateDimmer(Transform parent)
        {
            GameObject dimmer = NewPanel("Dimmer", parent, Vector2.zero);
            dimmer.GetComponent<Image>().color = COLOR_DIMMER;
            Stretch((RectTransform)dimmer.transform, 0f);
        }

        // 하단 인벤토리 바(화면 하단 기준 ~140px)와 겹치지 않도록 BOTTOM_SAFE_MARGIN만큼 띄워서 배치.
        // 바깥쪽(ShopPanel, 테두리색) 안에 안쪽(ShopPanelBackground, 채움색)을 6px 인셋으로 넣어
        // 시안 속 HP/미니맵/인벤토리 패널과 같은 "테두리 있는 패널" 느낌을 낸다.
        private Transform CreateShopPanelFrame(Transform parent)
        {
            GameObject shopPanel = NewPanel("ShopPanel", parent, new Vector2(900f, 700f));
            shopPanel.GetComponent<Image>().color = COLOR_ACCENT_TEAL;

            RectTransform panelRect = (RectTransform)shopPanel.transform;
            panelRect.anchorMin = new Vector2(0.5f, 0f);
            panelRect.anchorMax = new Vector2(0.5f, 0f);
            panelRect.pivot = new Vector2(0.5f, 0f);
            panelRect.anchoredPosition = new Vector2(0f, BOTTOM_SAFE_MARGIN);

            GameObject background = NewPanel("ShopPanelBackground", shopPanel.transform, Vector2.zero);
            background.GetComponent<Image>().color = COLOR_PANEL_BG;
            Stretch((RectTransform)background.transform, 6f);

            return background.transform;
        }

        #endregion

        #region 상단 헤더 영역 (제목 + 골드 표시)

        private void CreateHeader(Transform shopPanelBackground)
        {
            GameObject header = NewUI("Header", shopPanelBackground);

            RectTransform headerRect = (RectTransform)header.transform;
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.sizeDelta = new Vector2(0f, 88f);
            headerRect.anchoredPosition = new Vector2(0f, -8f);

            TMP_Text titleText = NewText("TitleText", header.transform, "상점", 40f, TextAlignmentOptions.Left);
            RectTransform titleRect = (RectTransform)titleText.transform;
            titleRect.anchorMin = new Vector2(0f, 0.5f);
            titleRect.anchorMax = new Vector2(0f, 0.5f);
            titleRect.pivot = new Vector2(0f, 0.5f);
            titleRect.sizeDelta = new Vector2(300f, 60f);
            titleRect.anchoredPosition = new Vector2(28f, 0f);

            TMP_Text goldText = NewText("GoldText", header.transform, "GOLD  0", 30f, TextAlignmentOptions.Right);
            goldText.color = COLOR_GOLD_TEXT;
            RectTransform goldRect = (RectTransform)goldText.transform;
            goldRect.anchorMin = new Vector2(1f, 0.5f);
            goldRect.anchorMax = new Vector2(1f, 0.5f);
            goldRect.pivot = new Vector2(1f, 0.5f);
            goldRect.sizeDelta = new Vector2(260f, 50f);
            goldRect.anchoredPosition = new Vector2(-92f, 0f); // 우측 닫기 버튼과 안 겹치도록 여백
        }

        // 상점 패널 우측 상단 고정 닫기 버튼 (헤더 높이와 무관하게 패널 코너에 붙음)
        private void CreateCloseButton(Transform shopPanelBackground)
        {
            Button closeButton = NewButton("CloseButton", shopPanelBackground, "X", out TMP_Text label);
            label.fontSize = 28f;

            RectTransform rect = (RectTransform)closeButton.transform;
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(48f, 48f);
            rect.anchoredPosition = new Vector2(-16f, -16f);
        }

        #endregion

        #region 아이템 목록 영역

        // 실제 아이템 개수만큼 카드가 늘어나는 건 런타임의 ShopView가 담당한다.
        // 여기서는 ShopView가 복제해서 쓸 "템플릿 카드" 하나만 만들어 비활성 상태로 남겨둔다.
        private void CreateItemList(Transform shopPanelBackground)
        {
            GameObject listPanel = NewUI("ItemListPanel", shopPanelBackground);

            RectTransform listRect = (RectTransform)listPanel.transform;
            listRect.anchorMin = new Vector2(0.5f, 0.5f);
            listRect.anchorMax = new Vector2(0.5f, 0.5f);
            listRect.pivot = new Vector2(0.5f, 0.5f);
            listRect.sizeDelta = new Vector2(840f, 480f);
            listRect.anchoredPosition = new Vector2(0f, -28f); // 헤더 높이만큼 아래로 보정

            VerticalLayoutGroup layout = listPanel.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 12f;
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.UpperCenter;

            CreateItemRowTemplate(listPanel.transform);
        }

        // 아이템 카드 템플릿: 아이콘 자리 + 이름/가격 + 구매 버튼.
        // ShopItemRowView를 부착해 ShopView가 목록 개수만큼 Instantiate해서 쓸 수 있게 한다.
        // 템플릿 자체는 화면에 안 보이도록 마지막에 비활성화한다.
        private void CreateItemRowTemplate(Transform parent)
        {
            GameObject row = NewPanel("ItemRowTemplate", parent, new Vector2(0f, 90f));
            row.GetComponent<Image>().color = COLOR_ROW_BG;

            RectTransform rowRect = (RectTransform)row.transform;
            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(1f, 1f);
            rowRect.pivot = new Vector2(0.5f, 1f);
            // sizeDelta.y = 90f는 위 NewPanel 호출에서 이미 지정됨 (VerticalLayoutGroup이 폭은 자동 조정)

            GameObject icon = NewPanel("IconImage", row.transform, new Vector2(64f, 64f));
            Image iconImage = icon.GetComponent<Image>();
            iconImage.color = COLOR_ICON_PLACEHOLDER; // 스프라이트 없을 때 대비한 색 블록. 실제 아이콘은 런타임에 채워짐
            RectTransform iconRect = (RectTransform)icon.transform;
            iconRect.anchorMin = new Vector2(0f, 0.5f);
            iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0f, 0.5f);
            iconRect.anchoredPosition = new Vector2(16f, 0f);

            GameObject info = NewUI("InfoContainer", row.transform);
            RectTransform infoRect = (RectTransform)info.transform;
            infoRect.anchorMin = new Vector2(0f, 0.5f);
            infoRect.anchorMax = new Vector2(0f, 0.5f);
            infoRect.pivot = new Vector2(0f, 0.5f);
            infoRect.sizeDelta = new Vector2(420f, 72f);
            infoRect.anchoredPosition = new Vector2(96f, 0f);

            VerticalLayoutGroup infoLayout = info.AddComponent<VerticalLayoutGroup>();
            infoLayout.spacing = 4f;
            infoLayout.childControlWidth = true;
            infoLayout.childControlHeight = true;
            infoLayout.childForceExpandWidth = true;
            infoLayout.childForceExpandHeight = false;
            infoLayout.childAlignment = TextAnchor.MiddleLeft;

            TMP_Text nameText = NewText("NameText", info.transform, "아이템 이름", 26f, TextAlignmentOptions.Left);
            RectTransform nameRect = (RectTransform)nameText.transform;
            nameRect.sizeDelta = new Vector2(0f, 34f);

            TMP_Text priceText = NewText("PriceText", info.transform, "0 Gold", 20f, TextAlignmentOptions.Left);
            priceText.color = COLOR_GOLD_TEXT;
            RectTransform priceRect = (RectTransform)priceText.transform;
            priceRect.sizeDelta = new Vector2(0f, 28f);

            Button buyButton = NewButton("BuyButton", row.transform, "구매", out TMP_Text buyLabel);
            RectTransform buyRect = (RectTransform)buyButton.transform;
            buyRect.anchorMin = new Vector2(1f, 0.5f);
            buyRect.anchorMax = new Vector2(1f, 0.5f);
            buyRect.pivot = new Vector2(1f, 0.5f);
            buyRect.sizeDelta = new Vector2(120f, 56f);
            buyRect.anchoredPosition = new Vector2(-16f, 0f);

            ShopItemRowView rowView = row.AddComponent<ShopItemRowView>();
            rowView.Configure(iconImage, nameText, priceText, buyButton);

            row.SetActive(false); // 템플릿은 항상 비활성 - ShopView가 복제한 사본만 화면에 보인다
        }

        #endregion

        #region 하단 알림 영역

        // 구매 실패("골드 부족" 등) 안내가 잠깐 떴다가 사라지는 자리. 평소엔 숨겨져 있는다.
        private void CreateFooter(Transform shopPanelBackground)
        {
            GameObject footer = NewPanel("FooterNotificationPanel", shopPanelBackground, new Vector2(760f, 48f));
            footer.GetComponent<Image>().color = COLOR_PANEL_BG;

            RectTransform footerRect = (RectTransform)footer.transform;
            footerRect.anchorMin = new Vector2(0.5f, 0f);
            footerRect.anchorMax = new Vector2(0.5f, 0f);
            footerRect.pivot = new Vector2(0.5f, 0f);
            footerRect.anchoredPosition = new Vector2(0f, 16f);

            TMP_Text notiText = NewText("NotificationText", footer.transform, "구매 실패 안내 자리", 22f, TextAlignmentOptions.Center);
            notiText.color = COLOR_NOTIFICATION_TEXT;
            Stretch((RectTransform)notiText.transform, 4f);

            footer.SetActive(false); // 평소엔 숨김 - ShopView 등이 필요할 때 활성화
        }

        #endregion

        #region Build / Clear 진입점

        [ContextMenu("1. Build UI")]
        private void BuildUI()
        {
            if (GameObject.Find(RootName) != null)
            {
                Debug.LogWarning($"[{nameof(ShopUIBuilder)}] 이미 '{RootName}' 오브젝트가 존재합니다. 중복 생성을 막기 위해 중단합니다.");
                return;
            }

            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                Debug.LogWarning($"[{nameof(ShopUIBuilder)}] 씬에 EventSystem이 없습니다. 버튼 입력이 동작하려면 EventSystem을 추가하세요.");
            }

            GameObject root = CreateCanvas();
            Undo.RegisterCreatedObjectUndo(root, "Create Shop UI");

            GameObject popupRoot = CreatePopupRoot(root.transform);
            popupRoot.SetActive(false); // 평소엔 숨김 - 상점 진입 시에만 활성화

            EditorSceneManager.MarkSceneDirty(gameObject.scene);
            Debug.Log($"[{nameof(ShopUIBuilder)}] '{RootName}' 생성 완료.");
        }

        [ContextMenu("2. Clear UI")]
        private void ClearUI()
        {
            GameObject existing = GameObject.Find(RootName);

            if (existing == null)
            {
                Debug.Log($"[{nameof(ShopUIBuilder)}] 지울 '{RootName}' 오브젝트가 없습니다.");
                return;
            }

            Undo.DestroyObjectImmediate(existing);
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
            Debug.Log($"[{nameof(ShopUIBuilder)}] '{RootName}' 오브젝트를 삭제했습니다.");
        }

        #endregion
    }
}
#endif