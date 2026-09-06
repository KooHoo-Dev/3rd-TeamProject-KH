using System;

namespace TerrainCollapse.Networking
{
    [Serializable]
    public sealed class PlayerHealthStateDto
    {
        public string PlayerID { get; set; }
        public int CurrentHealth { get; set; }
        public int MaxHealth { get; set; }
        public bool IsDead { get; set; }
    }

    [Serializable]
    public sealed class PlayerHealthSnapshotMessage
    {
        public string Type { get; set; }
        public PlayerHealthStateDto[] Players { get; set; } = Array.Empty<PlayerHealthStateDto>();
    }

    [Serializable]
    public sealed class PlayerHealthChangedMessage
    {
        public string Type { get; set; }
        public PlayerHealthStateDto Player { get; set; }
        public string DamageType { get; set; }
    }

    [Serializable]
    public sealed class PlayerDamageRequest
    {
        public string Type { get; set; } = NetworkMessageTypes.PlayerDamage;
        public string RequestId { get; set; }
        public string DamageType { get; set; }
        public int Amount { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
    }

    [Serializable]
    public sealed class PlayerHealRequest
    {
        public string Type { get; set; } = NetworkMessageTypes.PlayerHeal;
        public string RequestId { get; set; }
        public int Amount { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
    }

    [Serializable]
    public sealed class PlayerDiedMessage
    {
        public string Type { get; set; }
        public PlayerHealthStateDto Player { get; set; }
    }

    [Serializable]
    public sealed class PlayerRespawnedMessage
    {
        public string Type { get; set; }
        public PlayerHealthStateDto Player { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
    }

    [Serializable]
    public sealed class PlayerRespawnRequest
    {
        public string Type { get; set; } = NetworkMessageTypes.PlayerRespawnRequest;
        public string RequestId { get; set; }
    }
}
