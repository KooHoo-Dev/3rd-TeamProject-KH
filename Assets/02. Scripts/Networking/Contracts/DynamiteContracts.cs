using System;

namespace TerrainCollapse.Networking
{
    [Serializable]
    public sealed class DynamiteThrowRequest
    {
        public string Type { get; set; } = NetworkMessageTypes.DynamiteThrow;
        public string RequestId { get; set; }

        public int ItemID { get; set; }

        public float StartX { get; set; }
        public float StartY { get; set; }

        public float DirectionX { get; set; }
        public float DirectionY { get; set; }
    }

    [Serializable]
    public sealed class DynamiteThrownMessage
    {
        public string Type { get; set; }

        public string ProjectileID { get; set; }
        public string OwnerPlayerID { get; set; }
        public int ItemID { get; set; }

        public float StartX { get; set; }
        public float StartY { get; set; }

        public float DirectionX { get; set; }
        public float DirectionY { get; set; }

        public long StartedAtUnixMilliseconds { get; set; }

        public float ThrowSpeed { get; set; }
        public float FuseTime { get; set; }
        public float ExplosionRadius { get; set; }

        public bool IsMine { get; set; }
        public int CellX { get; set; }
        public int CellY { get; set; }
        public float ArmDelay { get; set; }
        public float DetectionRadius { get; set; }
    }

    [Serializable]
    public sealed class DynamiteExplodeRequest
    {
        public string Type { get; set; } =
            NetworkMessageTypes.DynamiteExplodeRequest;

        public string RequestId { get; set; }
        public string ProjectileID { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
    }

    [Serializable]
    public sealed class DynamiteExplodedMessage
    {
        public string Type { get; set; }
        public string RequestId { get; set; }
        public string ProjectileID { get; set; }
        public string OwnerPlayerID { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float ExplosionRadius { get; set; }
        public bool IsMine { get; set; }
    }
}
