using System;
using System.Globalization;
using System.Text;
using TMPro;
using TerrainCollapse.Networking;
using UnityEngine;

namespace TerrainCollapse
{
    /// <summary>서버가 집계한 실시간 골드 순위를 하나의 TMP 텍스트에 줄 단위로 표시</summary>
    public sealed class GoldRankingView : MonoBehaviour
    {
        [SerializeField] private RoomClient roomClient;
        [SerializeField, Range(100f, 200f)] private float localPlayerSizePercent = 135f;

        private TMP_Text rankingText;

        private void Awake()
        {
            roomClient ??= FindFirstObjectByType<RoomClient>();
            rankingText = GetComponent<TMP_Text>();
        }

        private void OnEnable()
        {
            if (roomClient != null) roomClient.GoldRankingChanged += Render;
            Render(roomClient?.CurrentGoldRanking);
        }

        private void OnDisable()
        {
            if (roomClient != null) roomClient.GoldRankingChanged -= Render;
        }

        private void Render(GoldRankingMessage message)
        {
            GoldRankingPlayerDto[] players = message?.Players ?? Array.Empty<GoldRankingPlayerDto>();
            StringBuilder builder = new();

            foreach (GoldRankingPlayerDto player in players)
            {
                if (player == null) continue;
                bool isLocalPlayer = string.Equals(
                    player.PlayerID,
                    roomClient?.LocalPlayerID,
                    StringComparison.Ordinal);
                if (builder.Length > 0) builder.AppendLine();
                if (isLocalPlayer)
                    builder.Append("<size=")
                        .Append(localPlayerSizePercent.ToString("0", CultureInfo.InvariantCulture))
                        .Append("%>▶ ");

                string nickName = string.IsNullOrWhiteSpace(player.NickName)
                    ? player.PlayerID
                    : player.NickName;
                string color = ColorUtility.ToHtmlStringRGB(PlayerColorPalette.GetColor(
                    GetPlayerColorIndex(player.PlayerID)));

                builder.Append("<color=#")
                    .Append(color)
                    .Append('>')
                    .Append(EscapeRichText(nickName))
                    .Append("</color>")
                    .Append(' ')
                    .Append(Math.Max(0L, player.Gold).ToString("N0"))
                    .Append("G (")
                    .Append(Math.Max(0L, player.ExchangeValue).ToString("N0"))
                    .Append("G)");
                if (isLocalPlayer) builder.Append("</size>");
            }

            rankingText.text = builder.ToString();
        }

        private int GetPlayerColorIndex(string playerId)
        {
            if (roomClient == null) return 0;
            if (string.Equals(playerId, roomClient.LocalPlayerID, StringComparison.Ordinal))
                return roomClient.LocalPlayerColorIndex;

            foreach (RemotePlayerView remotePlayer in roomClient.RemotePlayerViews)
                if (string.Equals(remotePlayer?.PlayerId, playerId, StringComparison.Ordinal))
                    return remotePlayer.ColorIndex;

            return 0;
        }

        private static string EscapeRichText(string value)
            => value?.Replace("<", "&lt;").Replace(">", "&gt;") ?? string.Empty;
    }
}
