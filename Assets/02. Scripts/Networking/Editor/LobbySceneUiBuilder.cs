#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace TerrainCollapse.Editor
{
    /// <summary>로비 씬에 편집 가능한 기본 UGUI 계층을 생성한다.</summary>
    public static class LobbySceneUiBuilder
    {
        private const string RootName = "Lobby UI";
        [MenuItem("Terrain Collapse/Lobby/Build Main Menu UI")]
        private static void BuildMainMenu() => Build(false);
        [MenuItem("Terrain Collapse/Lobby/Build Room Lobby UI")]
        private static void BuildRoomLobby() => Build(true);

        private static void Build(bool roomScene)
        {
            LobbyFlowController controller = Object.FindFirstObjectByType<LobbyFlowController>();
            if (controller == null) { Debug.LogError("Open a lobby scene first."); return; }
            GameObject oldRoot = GameObject.Find(RootName);
            if (oldRoot != null) Object.DestroyImmediate(oldRoot);
            if (Object.FindFirstObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            GameObject root = new(RootName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            root.AddComponent<Image>().color = new Color(0.04f, 0.08f, 0.13f, 1f);
            RectTransform panel = CreatePanel(root.transform, roomScene ? "Room Lobby" : "Main Menu");
            SerializedObject serialized = new(controller);
            serialized.FindProperty("isRoomScene").boolValue = roomScene;
            if (roomScene) BuildRoom(panel, serialized); else BuildMain(panel, serialized);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
        }

        private static void BuildMain(RectTransform panel, SerializedObject serialized)
        {
            CreateText(panel, "TERRAIN COLLAPSE", 42f, new Vector2(0f, 180f));
            TMP_InputField nickName = CreateInput(panel, "Nickname (1-16 characters)", new Vector2(0f, 80f));
            Button create = CreateButton(panel, "Create Room", new Vector2(0f, -20f));
            Button join = CreateButton(panel, "Join Room", new Vector2(0f, -95f));
            TMP_Text status = CreateText(panel, string.Empty, 20f, new Vector2(0f, -230f));
            GameObject popup = CreateJoinPopup(panel, out TMP_InputField roomCode, out Button confirm, out Button cancel);
            Set(serialized, "nickNameInput", nickName); Set(serialized, "roomCodeInput", roomCode);
            Set(serialized, "createRoomButton", create); Set(serialized, "joinRoomButton", join); Set(serialized, "statusText", status);
            Set(serialized, "roomCodePopup", popup); Set(serialized, "confirmJoinButton", confirm); Set(serialized, "cancelJoinButton", cancel);
        }

        private static GameObject CreateJoinPopup(Transform parent, out TMP_InputField roomCode, out Button confirm, out Button cancel)
        {
            GameObject popup = new("Join Room Popup", typeof(RectTransform), typeof(Image));
            popup.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)popup.transform;
            rect.sizeDelta = new Vector2(500f, 280f);
            popup.GetComponent<Image>().color = new Color(0.08f, 0.13f, 0.20f, 1f);
            CreateText(popup.transform, "JOIN ROOM", 30f, new Vector2(0f, 85f));
            roomCode = CreateInput(popup.transform, "6-digit room code", new Vector2(0f, 10f));
            roomCode.characterLimit = 6;
            roomCode.contentType = TMP_InputField.ContentType.IntegerNumber;
            confirm = CreateButton(popup.transform, "Join", new Vector2(-85f, -85f));
            cancel = CreateButton(popup.transform, "Cancel", new Vector2(85f, -85f));
            ((RectTransform)confirm.transform).sizeDelta = new Vector2(150f, 48f);
            ((RectTransform)cancel.transform).sizeDelta = new Vector2(150f, 48f);
            popup.SetActive(false);
            return popup;
        }

        private static void BuildRoom(RectTransform panel, SerializedObject serialized)
        {
            CreateText(panel, "ROOM LOBBY", 42f, new Vector2(0f, 190f));
            TMP_Text roomInfo = CreateText(panel, "ROOM CODE: ------  (0/4)", 24f, new Vector2(0f, 125f));
            LobbyPlayerSlotView[] slots = CreatePlayerSlots(panel);
            Button copy = CreateButton(panel, "Copy Code", new Vector2(0f, -125f));
            Button start = CreateButton(panel, "Start Game", new Vector2(0f, -180f));
            ((RectTransform)copy.transform).sizeDelta = new Vector2(220f, 44f);
            TMP_Text status = CreateText(panel, string.Empty, 20f, new Vector2(0f, -220f));
            Set(serialized, "roomInfoText", roomInfo); Set(serialized, "copyRoomCodeButton", copy);
            Set(serialized, "startGameButton", start); Set(serialized, "statusText", status);
            SerializedProperty slotProperty = serialized.FindProperty("playerSlots");
            slotProperty.arraySize = slots.Length;
            for (int i = 0; i < slots.Length; i++) slotProperty.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];
        }

        private static LobbyPlayerSlotView[] CreatePlayerSlots(Transform parent)
        {
            Sprite localSprite = LoadCharacterSprite("Assets/03. UI/Scenes/LocalPlayer.prefab");
            Sprite remoteSprite = LoadCharacterSprite("Assets/01. Scenes/RemotePlayer.prefab");
            Color[] playerColors =
            {
                new Color(0.93f, 0.23f, 0.23f),
                new Color(0.96f, 0.80f, 0.16f),
                new Color(0.25f, 0.78f, 0.35f),
                new Color(0.24f, 0.52f, 0.95f),
            };
            LobbyPlayerSlotView[] slots = new LobbyPlayerSlotView[4];
            for (int i = 0; i < slots.Length; i++)
            {
                GameObject slot = new($"Player Slot {i + 1}", typeof(RectTransform), typeof(Image), typeof(LobbyPlayerSlotView));
                slot.transform.SetParent(parent, false);
                RectTransform rect = (RectTransform)slot.transform;
                rect.sizeDelta = new Vector2(135f, 180f);
                rect.anchoredPosition = new Vector2(-225f + 150f * i, 0f);
                slot.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);

                GameObject avatar = new("Avatar", typeof(RectTransform), typeof(Image));
                avatar.transform.SetParent(slot.transform, false);
                RectTransform avatarRect = (RectTransform)avatar.transform;
                avatarRect.sizeDelta = new Vector2(90f, 105f);
                avatarRect.anchoredPosition = new Vector2(0f, 25f);
                Image avatarImage = avatar.GetComponent<Image>();
                avatarImage.preserveAspect = true;
                GameObject label = new("Nickname", typeof(RectTransform), typeof(TextMeshProUGUI));
                label.transform.SetParent(slot.transform, false);
                RectTransform labelRect = (RectTransform)label.transform;
                labelRect.sizeDelta = new Vector2(125f, 42f);
                labelRect.anchoredPosition = new Vector2(0f, -58f);
                TMP_Text labelText = label.GetComponent<TextMeshProUGUI>();
                labelText.alignment = TextAlignmentOptions.Center;
                labelText.fontSize = 18f;
                labelText.text = "Waiting...";
                SerializedObject slotSerialized = new(slot.GetComponent<LobbyPlayerSlotView>());
                slotSerialized.FindProperty("avatarImage").objectReferenceValue = avatarImage;
                slotSerialized.FindProperty("nickNameText").objectReferenceValue = labelText;
                slotSerialized.FindProperty("localPlayerSprite").objectReferenceValue = localSprite;
                slotSerialized.FindProperty("remotePlayerSprite").objectReferenceValue = remoteSprite;
                slotSerialized.FindProperty("playerColor").colorValue = playerColors[i];
                slotSerialized.ApplyModifiedPropertiesWithoutUndo();
                slots[i] = slot.GetComponent<LobbyPlayerSlotView>();
            }
            return slots;
        }

        private static Sprite LoadCharacterSprite(string prefabPath)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            return prefab?.GetComponentInChildren<SpriteRenderer>(true)?.sprite;
        }

        private static void Set(SerializedObject serialized, string property, Object value) => serialized.FindProperty(property).objectReferenceValue = value;
        private static RectTransform CreatePanel(Transform parent, string name)
        {
            GameObject target = new(name, typeof(RectTransform), typeof(Image)); target.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)target.transform; rect.sizeDelta = new Vector2(640f, 560f);
            target.GetComponent<Image>().color = new Color(0.10f, 0.18f, 0.26f, 0.98f); return rect;
        }
        private static TMP_Text CreateText(Transform parent, string value, float fontSize, Vector2 position)
        {
            GameObject target = new("Text", typeof(RectTransform), typeof(TextMeshProUGUI)); target.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)target.transform; rect.sizeDelta = new Vector2(560f, 150f); rect.anchoredPosition = position;
            TextMeshProUGUI text = target.GetComponent<TextMeshProUGUI>(); text.text = value; text.fontSize = fontSize; text.alignment = TextAlignmentOptions.Center; text.color = Color.white; text.raycastTarget = false; return text;
        }
        private static TMP_InputField CreateInput(Transform parent, string placeholder, Vector2 position)
        {
            GameObject target = new("Input", typeof(RectTransform), typeof(Image), typeof(TMP_InputField)); target.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)target.transform; rect.sizeDelta = new Vector2(460f, 58f); rect.anchoredPosition = position;
            target.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);
            TMP_Text text = CreateText(target.transform, string.Empty, 24f, Vector2.zero); TMP_Text hint = CreateText(target.transform, placeholder, 20f, Vector2.zero);
            Stretch((RectTransform)text.transform, new Vector2(16f, 0f)); Stretch((RectTransform)hint.transform, new Vector2(16f, 0f));
            text.alignment = hint.alignment = TextAlignmentOptions.MidlineLeft; hint.color = new Color(1f, 1f, 1f, 0.45f);
            TMP_InputField input = target.GetComponent<TMP_InputField>(); input.textComponent = text; input.placeholder = hint; return input;
        }
        private static Button CreateButton(Transform parent, string label, Vector2 position)
        {
            GameObject target = new("Button", typeof(RectTransform), typeof(Image), typeof(Button)); target.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)target.transform; rect.sizeDelta = new Vector2(300f, 54f); rect.anchoredPosition = position;
            target.GetComponent<Image>().color = new Color(0.16f, 0.55f, 0.72f, 1f);
            TMP_Text text = CreateText(target.transform, label, 24f, Vector2.zero); Stretch((RectTransform)text.transform, Vector2.zero); text.alignment = TextAlignmentOptions.Center; return target.GetComponent<Button>();
        }
        private static void Stretch(RectTransform rect, Vector2 padding) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = padding; rect.offsetMax = -padding; }
    }
}
#endif
