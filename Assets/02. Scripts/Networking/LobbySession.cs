using System;
using System.Collections.Generic;
using TerrainCollapse.Networking;
using UnityEngine;

namespace TerrainCollapse
{
    /// <summary>메인/대기방/인게임 씬 사이에서 로그인과 방 선택 결과를 보존한다.</summary>
    public sealed class LobbySession : MonoBehaviour
    {
        public static LobbySession Instance { get; private set; }

        public string ServerUrl { get; private set; }
        public string NickName { get; private set; }
        public string RoomCode { get; private set; }
        public string HostToken { get; private set; }
        public string ClientId { get; private set; }
        public bool IsHost => string.IsNullOrEmpty(HostToken) == false;
        public bool HasRoom => string.IsNullOrEmpty(RoomCode) == false;
        public LobbySocketClient LobbyClient { get; private set; }

        private readonly List<string> orderedPlayerClientIds = new();

        public static LobbySession Ensure(string defaultServerUrl = null)
        {
            if (Instance != null) return Instance;

            GameObject root = new("LobbySession");
            Instance = root.AddComponent<LobbySession>();
            Instance.Initialize(defaultServerUrl);
            DontDestroyOnLoad(root);
            return Instance;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            Initialize(null);
            LobbyClient = GetComponent<LobbySocketClient>() ?? gameObject.AddComponent<LobbySocketClient>();
            DontDestroyOnLoad(gameObject);
        }

        private void Initialize(string defaultServerUrl)
        {
            if (string.IsNullOrWhiteSpace(ClientId))
                ClientId = Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(ServerUrl) == false || string.IsNullOrWhiteSpace(defaultServerUrl))
                return;
            ServerUrl = defaultServerUrl.TrimEnd('/');
        }

        public void Configure(string serverUrl, string nickName, string roomCode, string hostToken)
        {
            ServerUrl = serverUrl.TrimEnd('/');
            NickName = nickName.Trim();
            RoomCode = roomCode.Trim();
            HostToken = hostToken;
        }

        public void UpdatePlayers(IReadOnlyList<LobbyPlayerInfo> players)
        {
            orderedPlayerClientIds.Clear();
            if (players == null) return;

            for (int i = 0; i < players.Count; i++)
            {
                string clientId = players[i]?.ClientID;
                if (string.IsNullOrWhiteSpace(clientId) == false)
                    orderedPlayerClientIds.Add(clientId);
            }
        }

        public int GetPlayerColorIndex(string clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId)) return 0;

            int index = orderedPlayerClientIds.IndexOf(clientId);
            return index >= 0 ? index : 0;
        }

        public string GetWebSocketBaseUrl()
        {
            if (string.IsNullOrWhiteSpace(ServerUrl)) return string.Empty;
            if (ServerUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return "wss://" + ServerUrl.Substring(8);
            if (ServerUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                return "ws://" + ServerUrl.Substring(7);
            return ServerUrl;
        }

        public static void ApplyTo(RoomClient roomClient)
        {
            if (Instance?.HasRoom != true || roomClient == null) return;
            roomClient.ConfigureConnection(
                Instance.GetWebSocketBaseUrl(),
                Instance.RoomCode,
                Instance.NickName);
        }
    }
}
