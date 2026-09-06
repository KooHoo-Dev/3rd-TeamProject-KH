using System;
using System.Collections.Generic;

namespace TerrainCollapse.Networking
{
    [Serializable] public sealed class LobbyMessageHeader { public string Type; }
    [Serializable] public sealed class LobbyCreateMessage { public string Type = "lobby.create"; public string NickName; public string ClientID; }
    [Serializable] public sealed class LobbyJoinMessage { public string Type = "lobby.join"; public string RoomCode; public string NickName; public string ClientID; }
    [Serializable] public sealed class LobbyReturnMessage { public string Type = "lobby.return"; public string RoomCode; public string NickName; public string ClientID; }
    [Serializable] public sealed class LobbyStartRequest { public string Type = "lobby.start"; public string HostToken; }
    [Serializable] public sealed class LobbyStateMessage { public string Type; public LobbyRoomInfo Room; public string HostToken; }
    [Serializable] public sealed class LobbyStartedMessage { public string Type; public string RoomCode; }
    [Serializable] public sealed class LobbyErrorMessage { public string Type; public string Code; }
    [Serializable] public sealed class LobbyPlayerInfo { public string ClientID; public string NickName; }
    [Serializable] public sealed class LobbyRoomInfo { public string RoomCode; public string HostClientID; public bool IsStarted; public int MaxPlayers; public List<string> Players; public List<LobbyPlayerInfo> PlayerDetails; }
}
