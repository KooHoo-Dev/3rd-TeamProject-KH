using System;

namespace TerrainCollapse.Networking
{
    [Serializable]
    public sealed class PlayerStateDto
    {
        public string Id { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float VelocityX { get; set; }
        public float VelocityY { get; set; }
        public bool IsGrounded { get; set; }
        public bool IsClimbing { get; set; }
        public bool IsBuried { get; set; }
        public bool IsDead { get; set; }
    }

    [Serializable]
    public sealed class PlayerActionMessage
    {
        public string Type { get; set; }
        public string PlayerID { get; set; }
        public string Action { get; set; }
        public float DirectionX { get; set; }
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
        public NetworkUser User { get; set; }
    }
}
