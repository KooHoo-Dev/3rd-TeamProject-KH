using System;

namespace TerrainCollapse.Networking
{
    [Serializable]
    public sealed class PlayerStateDto
    {
        public string Id { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
    }

    [Serializable]
    public sealed class PlayerStateMessage
    {
        public string Type { get; set; }
        public PlayerStateDto[] States { get; set; }
    }

    [Serializable]
    public sealed class PlayerJoinedMessage
    {
        public string Type { get; set; }
        public NetworkUser User { get; set; }
    }

    [Serializable]
    public sealed class PlayerLeftMessage
    {
        public string Type { get; set; }
        public string Id { get; set; }
    }
}
