#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TerrainCollapse
{
    /// <summary>TP 검증용 HUD Prefab과 별도 프로토타입 씬을 Unity 직렬화로 생성한다.</summary>
    public static class TPUIPrototypeBuilder
    {
        private const string PrefabFolder = "Assets/03. UI/Prefabs";
        private const string SceneFolder = "Assets/03. UI/Scenes";
        private const string PrefabPath = PrefabFolder + "/TPHud.prefab";
        private const string SourceScenePath = "Assets/01. Scenes/TerrainCollpseTestScene.unity";
        private const string PrototypeScenePath = SceneFolder + "/UIPrototypeScene.unity";
        private const string DefaultFontPath =
            "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
        private const string PreviewIconPath =
            "Assets/02. Scripts/TerrainCollapse/Development/Sprites/OutlinedWhiteSquare.png";

        private static readonly Color PanelColor = new(0.035f, 0.045f, 0.065f, 0.92f);
        private static readonly Color BorderColor = new(0.18f, 0.78f, 0.9f, 0.85f);
        private static readonly Color TrackColor = new(0.12f, 0.14f, 0.18f, 1f);
        private static readonly Color TextColor = new(0.92f, 0.96f, 1f, 1f);

        [MenuItem("Tools/Game UI/Rebuild TP UI Prototype")]
        public static void Build()
        {
            GameObject prefab = BuildHudPrefab();

            CreatePrototypeScene(prefab);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Game UI] TP UI 생성 완료: {PrefabPath}, {PrototypeScenePath}");
        }

        [MenuItem("Tools/Game UI/Rebuild TP HUD Prefab")]
        public static void BuildHudPrefabFromMenu()
        {
            BuildHudPrefab();
        }

        private static GameObject BuildHudPrefab()
        {
            EnsureFolder(PrefabFolder);

            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(DefaultFontPath);
            if (font == null)
                throw new InvalidOperationException($"기본 TMP Font를 찾을 수 없습니다: {DefaultFontPath}");

            Sprite previewIcon = AssetDatabase.LoadAssetAtPath<Sprite>(PreviewIconPath);
            GameObject canvasRoot = CreateHudCanvas(font, previewIcon);
            GameObject prefab = SaveHudPrefab(canvasRoot);
            Object.DestroyImmediate(canvasRoot);

            return prefab;
        }

        private static GameObject SaveHudPrefab(GameObject sourceHud)
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(sourceHud, PrefabPath);
            if (prefab == null)
                throw new InvalidOperationException($"HUD Prefab 생성에 실패했습니다: {PrefabPath}");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Game UI] HUD Prefab 생성 완료: {PrefabPath}");

            return prefab;
        }

        public static void BuildFromCommandLine()
        {
            Build();
        }

        private static GameObject CreateHudCanvas(TMP_FontAsset font, Sprite previewIcon)
        {
            GameObject root = new(
                "TPHud",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));

            SetLayerRecursively(root, LayerMask.NameToLayer("UI"));

            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            HealthBarView healthBarView = CreateHealthBar(root.transform, font);
            InventoryView inventoryView = CreateInventory(root.transform, font);
            RectTransform fullMapDimmer = CreateImage(
                "FullMapDimmer",
                root.transform,
                new Color(0f, 0f, 0f, 0.72f));
            StretchToParent(fullMapDimmer, 0f);
            fullMapDimmer.gameObject.SetActive(false);

            MinimapView minimapView = CreateMinimap(
                root.transform,
                fullMapDimmer.gameObject,
                out TerrainMinimapPresenter presenter);

            HudPrototypeDriver preview = root.AddComponent<HudPrototypeDriver>();
            SetBool(preview, "previewEnabled", false);
            SetObjectReference(preview, "healthBarView", healthBarView);
            SetObjectReference(preview, "inventoryView", inventoryView);
            SetObjectReference(preview, "coalIcon", previewIcon);
            SetObjectReference(preview, "ironOreIcon", previewIcon);
            SetObjectReference(presenter, "minimapView", minimapView);

            PlayerInventoryHudPresenter inventoryPresenter =
                root.AddComponent<PlayerInventoryHudPresenter>();
            SetObjectReference(inventoryPresenter, "inventoryView", inventoryView);

            return root;
        }

        private static HealthBarView CreateHealthBar(Transform parent, TMP_FontAsset font)
        {
            RectTransform panel = CreatePanel("HealthBar", parent);
            SetAnchoredRect(panel, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, 1f), new Vector2(30f, -30f), new Vector2(440f, 110f));

            CreateText("Title", panel, font, "HP", 28f, FontStyles.Bold,
                TextAlignmentOptions.Left, new Vector2(18f, -10f), new Vector2(90f, 36f),
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f));

            RectTransform track = CreateImage("Track", panel, TrackColor);
            SetStretchRect(track, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(18f, 18f), new Vector2(-18f, 58f));

            Image fill = CreateImage("Fill", track, new Color(0.9f, 0.16f, 0.2f, 1f)).GetComponent<Image>();
            StretchToParent((RectTransform)fill.transform, 3f);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = 0;

            TextMeshProUGUI valueText = CreateText("Value", track, font, "75 / 100", 23f,
                FontStyles.Bold, TextAlignmentOptions.Center);
            StretchToParent(valueText.rectTransform, 0f);

            HealthBarView view = panel.gameObject.AddComponent<HealthBarView>();
            SetObjectReference(view, "fillImage", fill);
            SetObjectReference(view, "valueText", valueText);
            return view;
        }

        private static InventoryView CreateInventory(Transform parent, TMP_FontAsset font)
        {
            RectTransform panel = CreatePanel("InventoryBar", parent);
            SetAnchoredRect(panel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0f, 24f), new Vector2(900f, 158f));

            TextMeshProUGUI goldText = CreateText("Gold", panel, font, "GOLD  1,250", 26f,
                FontStyles.Bold, TextAlignmentOptions.Center, new Vector2(0f, -10f),
                new Vector2(420f, 34f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f));
            goldText.color = new Color(1f, 0.82f, 0.25f, 1f);

            RectTransform divider = CreateImage("Divider", panel, BorderColor);
            SetAnchoredRect(divider, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f), new Vector2(0f, -51f), new Vector2(852f, 2f));

            List<InventorySlotView> slots = new();
            for (int index = 0; index < 10; index++)
            {
                RectTransform slot = CreateImage("Slot " + (index + 1), panel,
                    new Color(0.075f, 0.095f, 0.13f, 0.98f));
                SetAnchoredRect(slot, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(0f, 1f), new Vector2(24f + index * 86f, -65f),
                    new Vector2(78f, 78f));
                AddOutline(slot.gameObject, new Color(0.2f, 0.27f, 0.35f, 0.9f), 1f);

                Image icon = CreateImage("Icon", slot, new Color(0.65f, 0.72f, 0.8f, 1f))
                    .GetComponent<Image>();
                StretchToParent((RectTransform)icon.transform, 9f);

                TextMeshProUGUI quantityText = CreateText(
                    "Quantity",
                    slot,
                    font,
                    string.Empty,
                    20f,
                    FontStyles.Bold,
                    TextAlignmentOptions.BottomRight,
                    new Vector2(-7f, 7f),
                    new Vector2(50f, 28f),
                    new Vector2(1f, 0f),
                    new Vector2(1f, 0f),
                    new Vector2(1f, 0f));
                quantityText.color = Color.white;
                AddOutline(quantityText.gameObject, Color.black, 1f);

                Image focusImage = CreateImage("Focus", slot, new Color(0.2f, 0.85f, 1f, 0.2f))
                    .GetComponent<Image>();
                StretchToParent((RectTransform)focusImage.transform, 2f);
                focusImage.enabled = false;
                AddOutline(focusImage.gameObject, new Color(0.3f, 0.9f, 1f, 1f), 2f);

                Image holdFillImage = CreateImage("HoldFill", slot, new Color(0.9f, 0.9f, 0.25f, 0.35f))
                    .GetComponent<Image>();
                StretchToParent((RectTransform)holdFillImage.transform, 2f);
                holdFillImage.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
                holdFillImage.type = Image.Type.Filled;
                holdFillImage.fillMethod = Image.FillMethod.Radial360;
                holdFillImage.fillOrigin = (int)Image.Origin360.Top;
                holdFillImage.fillAmount = 0f;
                holdFillImage.enabled = false;

                InventorySlotView slotView = slot.gameObject.AddComponent<InventorySlotView>();
                SetObjectReference(slotView, "iconImage", icon);
                SetObjectReference(slotView, "focusImage", focusImage);
                SetObjectReference(slotView, "holdFillImage", holdFillImage);
                SetObjectReference(slotView, "quantityText", quantityText);
                slots.Add(slotView);
            }

            InventoryView view = panel.gameObject.AddComponent<InventoryView>();
            SetObjectArray(view, "slotViews", slots);
            SetObjectReference(view, "goldText", goldText);
            return view;
        }

        private static MinimapView CreateMinimap(
            Transform parent,
            GameObject fullMapDimmer,
            out TerrainMinimapPresenter presenter)
        {
            RectTransform mapFrame = CreatePanel("MapFrame", parent);
            SetAnchoredRect(mapFrame, Vector2.one, Vector2.one, Vector2.one,
                new Vector2(-30f, -30f), new Vector2(340f, 340f));
            mapFrame.gameObject.AddComponent<RectMask2D>();

            GameObject mapObject = new(
                "MapImage",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(RawImage));
            mapObject.transform.SetParent(mapFrame, false);
            SetLayerRecursively(mapObject, LayerMask.NameToLayer("UI"));
            RectTransform mapRect = mapObject.GetComponent<RectTransform>();
            StretchToParent(mapRect, 7f);
            RawImage mapImage = mapObject.GetComponent<RawImage>();
            mapImage.color = Color.white;
            mapImage.raycastTarget = false;

            Image marker = CreateImage("PlayerMarker", mapRect, new Color(1f, 0.88f, 0.1f, 1f))
                .GetComponent<Image>();
            marker.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/Resources/UI/MinimapPlayerMarker.png");
            RectTransform markerRect = (RectTransform)marker.transform;
            SetAnchoredRect(markerRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(12f, 12f));
            marker.raycastTarget = false;

            MinimapView view = mapObject.AddComponent<MinimapView>();
            SetObjectReference(view, "mapImage", mapImage);
            SetObjectReference(view, "playerMarker", markerRect);
            SetObjectReference(view, "mapFrame", mapFrame);
            SetObjectReference(view, "fullMapDimmer", fullMapDimmer);

            presenter = mapFrame.gameObject.AddComponent<TerrainMinimapPresenter>();
            SetObjectReference(presenter, "minimapView", view);
            return view;
        }

        private static void CreatePrototypeScene(GameObject prefab)
        {
            string scenePath = AssetDatabase.LoadAssetAtPath<SceneAsset>(
                PrototypeScenePath) != null
                ? PrototypeScenePath
                : SourceScenePath;
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            RemoveExistingHud(scene);

            GameObject instance = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
            if (instance == null)
                throw new InvalidOperationException("TPHud Prefab 인스턴스 생성에 실패했습니다.");

            TerrainCollapse.TerrainMapLoader mapLoader =
                Object.FindFirstObjectByType<TerrainCollapse.TerrainMapLoader>();
            TerrainCollapse.TerrainManager terrainManager =
                Object.FindFirstObjectByType<TerrainCollapse.TerrainManager>();
            GameObject player = GameObject.Find("TestPlayer");
            TerrainMinimapPresenter presenter = instance.GetComponentInChildren<TerrainMinimapPresenter>(true);

            if (mapLoader == null || terrainManager == null)
                throw new InvalidOperationException("원본 테스트 씬에서 지형 시스템을 찾을 수 없습니다.");

            SetObjectReference(presenter, "mapLoader", mapLoader);
            SetObjectReference(presenter, "terrainManager", terrainManager);
            SetObjectReference(presenter, "localPlayer", player != null ? player.transform : null);
            ReconnectPrototypeReferences(player, instance, terrainManager);

            EditorSceneManager.MarkSceneDirty(scene);
            if (EditorSceneManager.SaveScene(scene, PrototypeScenePath) == false)
                throw new InvalidOperationException($"UI 프로토타입 씬 저장에 실패했습니다: {PrototypeScenePath}");
        }

        private static void RemoveExistingHud(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                string assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
                if (assetPath != PrefabPath && root.name != "TPHud") continue;

                Object.DestroyImmediate(root);
            }
        }

        private static void ReconnectPrototypeReferences(
            GameObject player,
            GameObject hud,
            TerrainManager terrainManager)
        {
            if (player == null) return;

            InventoryView inventoryView = hud.GetComponentInChildren<InventoryView>(true);
            PlayerInventory inventory = player.GetComponent<PlayerInventory>();
            PlayerItemDropController itemDropController =
                player.GetComponent<PlayerItemDropController>();
            FallingTerrainInteraction fallingInteraction =
                player.GetComponent<FallingTerrainInteraction>();
            RespawnController respawnController =
                player.GetComponent<RespawnController>();
            TestPlayerController playerController =
                player.GetComponent<TestPlayerController>();

            PlayerInventoryHudPresenter inventoryPresenter =
                hud.GetComponent<PlayerInventoryHudPresenter>();

            SetObjectReference(inventoryPresenter, "inventory", inventory);
            SetObjectReference(inventoryPresenter, "inventoryView", inventoryView);
            SetObjectReference(itemDropController, "inventoryView", inventoryView);
            SetObjectReference(itemDropController, "itemDropSpawner",
                Object.FindFirstObjectByType<ItemDropSpawner>());
            SetObjectReference(fallingInteraction, "terrainManager", terrainManager);
            SetObjectReference(respawnController, "terrainManager", terrainManager);
            SetObjectReference(playerController, "terrainManager", terrainManager);
            SetObjectReference(hud.GetComponent<HudPrototypeDriver>(), "playerHp",
                player.GetComponent<Character.PlayerHp>());
        }

        private static RectTransform CreatePanel(string name, Transform parent)
        {
            RectTransform panel = CreateImage(name, parent, PanelColor);
            AddOutline(panel.gameObject, BorderColor, 2f);
            return panel;
        }

        private static RectTransform CreateImage(string name, Transform parent, Color color)
        {
            GameObject target = new(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            target.transform.SetParent(parent, false);
            SetLayerRecursively(target, LayerMask.NameToLayer("UI"));

            Image image = target.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return target.GetComponent<RectTransform>();
        }

        private static TextMeshProUGUI CreateText(
            string name,
            Transform parent,
            TMP_FontAsset font,
            string value,
            float fontSize,
            FontStyles fontStyle,
            TextAlignmentOptions alignment)
        {
            GameObject target = new(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            target.transform.SetParent(parent, false);
            SetLayerRecursively(target, LayerMask.NameToLayer("UI"));

            TextMeshProUGUI text = target.GetComponent<TextMeshProUGUI>();
            text.font = font;
            text.text = value;
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.alignment = alignment;
            text.color = TextColor;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            return text;
        }

        private static TextMeshProUGUI CreateText(
            string name,
            Transform parent,
            TMP_FontAsset font,
            string value,
            float fontSize,
            FontStyles fontStyle,
            TextAlignmentOptions alignment,
            Vector2 anchoredPosition,
            Vector2 size,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot)
        {
            TextMeshProUGUI text = CreateText(name, parent, font, value, fontSize, fontStyle, alignment);
            SetAnchoredRect(text.rectTransform, anchorMin, anchorMax, pivot, anchoredPosition, size);
            return text;
        }

        private static void AddOutline(GameObject target, Color color, float distance)
        {
            Outline outline = target.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = new Vector2(distance, -distance);
            outline.useGraphicAlpha = true;
        }

        private static void SetAnchoredRect(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;
        }

        private static void SetStretchRect(
            RectTransform rect,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            rect.localScale = Vector3.one;
        }

        private static void StretchToParent(RectTransform rect, float padding)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
            rect.localScale = Vector3.one;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = path.Substring(0, path.LastIndexOf('/'));
            string name = path.Substring(path.LastIndexOf('/') + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        private static void SetObjectReference(Object target, string propertyName, Object value)
        {
            SerializedObject serializedObject = new(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
                throw new MissingFieldException(target.GetType().Name, propertyName);

            property.objectReferenceValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetObjectArray<T>(Object target, string propertyName, IReadOnlyList<T> values)
            where T : Object
        {
            SerializedObject serializedObject = new(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
                throw new MissingFieldException(target.GetType().Name, propertyName);

            property.arraySize = values.Count;
            for (int index = 0; index < values.Count; index++)
                property.GetArrayElementAtIndex(index).objectReferenceValue = values[index];

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetBool(Object target, string propertyName, bool value)
        {
            SerializedObject serializedObject = new(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
                throw new MissingFieldException(target.GetType().Name, propertyName);

            property.boolValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetLayerRecursively(GameObject target, int layer)
        {
            if (layer < 0) return;

            target.layer = layer;
            foreach (Transform child in target.transform)
                SetLayerRecursively(child.gameObject, layer);
        }
    }
}
#endif
