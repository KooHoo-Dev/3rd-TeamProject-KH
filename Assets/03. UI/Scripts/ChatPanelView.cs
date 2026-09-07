using System.Collections.Generic;
using System.Text;
using TMPro;
using TerrainCollapse.Networking;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace TerrainCollapse
{
    public sealed class ChatPanelView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public enum ChatDisplayMode
        {
            InGame,
            Lobby,
        }

        private const int MaximumStoredMessages = 100;
        private const string InputHint = "Enter 또는 T를 눌러 채팅하기";

        [SerializeField] private ChatDisplayMode displayMode;
        [SerializeField] private RoomClient roomClient;
        private LobbySocketClient lobbyClient;
        [SerializeField] private TMP_Text messagesText;
        [SerializeField] private TMP_InputField inputField;
        [SerializeField] private ScrollRect messagesScrollRect;

        private ChatPanelFader panelFader;
        private readonly Queue<string> messages = new();
        private bool isTyping;
        private bool scrollToBottom;

        private void Awake()
        {
            roomClient ??= FindFirstObjectByType<RoomClient>();
            lobbyClient = LobbySession.Instance?.LobbyClient;
            panelFader = GetComponent<ChatPanelFader>();
            if (messagesText != null) messagesText.text = string.Empty;
            SetInputHint();
        }

        private void OnEnable()
        {
            if (displayMode == ChatDisplayMode.Lobby)
            {
                if (lobbyClient == null) return;
                lobbyClient.ChatMessageReceived += HandleLobbyChatMessage;
                lobbyClient.SystemMessageReceived += HandleLobbySystemMessage;
                return;
            }
            if (roomClient == null) return;
            roomClient.ChatMessageReceived += HandleChatMessage;
            roomClient.SystemMessageReceived += HandleSystemMessage;
        }

        private void OnDisable()
        {
            CloseInput();
            ChatInputState.IsPointerOverChat = false;
            if (displayMode == ChatDisplayMode.Lobby)
            {
                if (lobbyClient != null)
                {
                    lobbyClient.ChatMessageReceived -= HandleLobbyChatMessage;
                    lobbyClient.SystemMessageReceived -= HandleLobbySystemMessage;
                }
                return;
            }
            if (roomClient == null) return;
            roomClient.ChatMessageReceived -= HandleChatMessage;
            roomClient.SystemMessageReceived -= HandleSystemMessage;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            ChatInputState.IsPointerOverChat = true;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            ChatInputState.IsPointerOverChat = false;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            bool enter = keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame;

            if (!isTyping)
            {
                if (enter || keyboard.tKey.wasPressedThisFrame) OpenInput();
                return;
            }

            if (keyboard.escapeKey.wasPressedThisFrame) CloseInput();
            else if (enter) Submit();
        }

        private void LateUpdate()
        {
            if (!scrollToBottom || messagesScrollRect == null) return;

            Canvas.ForceUpdateCanvases();
            messagesScrollRect.verticalNormalizedPosition = 0f;
            scrollToBottom = false;
        }

        private void OpenInput()
        {
            if (inputField == null) return;
            isTyping = true;
            ChatInputState.IsTyping = true;
            inputField.interactable = true;
            inputField.text = string.Empty;
            inputField.Select();
            inputField.ActivateInputField();
            panelFader?.SetInputActive(true);
        }

        private void Submit()
        {
            string text = inputField?.text?.Trim();
            if (string.IsNullOrWhiteSpace(text) == false)
            {
                if (displayMode == ChatDisplayMode.Lobby) lobbyClient?.SendChat(text);
                else roomClient?.SendChat(text);
            }
            CloseInput();
        }

        private void CloseInput()
        {
            isTyping = false;
            ChatInputState.IsTyping = false;
            if (inputField != null)
            {
                inputField.DeactivateInputField();
                SetInputHint();
            }
            panelFader?.SetInputActive(false);
        }

        private void SetInputHint()
        {
            if (inputField == null) return;
            inputField.text = InputHint;
            inputField.interactable = false;
        }

        private void HandleChatMessage(ChatBroadcastMessage message)
        {
            if (message == null) return;
            int index = LobbySession.Instance?.GetPlayerColorIndex(message.ClientID) ?? 0;
            string color = ColorUtility.ToHtmlStringRGB(PlayerColorPalette.GetColor(index));
            string prefix = displayMode == ChatDisplayMode.Lobby
                ? "[대기실]"
                : $"[{FormatTime(message.ElapsedSeconds)}]";

            Append($"{prefix} <color=#{color}>{Escape(message.NickName)} : {Escape(message.Text)}</color>");
        }

        private void HandleSystemMessage(ChatSystemMessage message)
        {
            if (message == null) return;
            string prefix = displayMode == ChatDisplayMode.Lobby
                ? "[대기실] [시스템]"
                : $"[{FormatTime(message.ElapsedSeconds)}] [시스템]";

            Append($"{prefix} <color=#FFFFFF>{Escape(message.Text)}</color>");
        }

        private void HandleLobbyChatMessage(LobbyChatMessage message)
        {
            if (message == null) return;
            int index = LobbySession.Instance?.GetPlayerColorIndex(message.ClientID) ?? 0;
            string color = ColorUtility.ToHtmlStringRGB(PlayerColorPalette.GetColor(index));
            Append($"[대기실] <color=#{color}>{Escape(message.NickName)} : {Escape(message.Text)}</color>");
        }

        private void HandleLobbySystemMessage(LobbySystemMessage message)
        {
            if (message != null) Append($"[대기실] [시스템] <color=#FFFFFF>{Escape(message.Text)}</color>");
        }

        private void Append(string line)
        {
            messages.Enqueue(line);
            while (messages.Count > MaximumStoredMessages) messages.Dequeue();
            StringBuilder builder = new();
            foreach (string message in messages) builder.AppendLine(message);
            if (messagesText != null) messagesText.text = builder.ToString();
            scrollToBottom = true;
            panelFader?.NotifyMessageReceived();
        }

        private static string FormatTime(int seconds)
        {
            seconds = Mathf.Max(0, seconds);
            return $"{seconds / 60:00}:{seconds % 60:00}";
        }

        private static string Escape(string value) => value?.Replace("<", "&lt;").Replace(">", "&gt;") ?? string.Empty;
    }
}
