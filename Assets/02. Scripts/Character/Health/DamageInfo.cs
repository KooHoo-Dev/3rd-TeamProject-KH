using UnityEngine;

namespace Character
{
    // 피해량과 피해 종류 및 발생 주체 전달 정보

    public enum DamageType
    {
        None,
        Burial,
        Fall,
        FallingChunk,
        Explosion,
        Debug,
    }

    public readonly struct DamageInfo
    {
        public int Amount { get; }
        public DamageType Type { get; }
        public GameObject Source { get; }

        public DamageInfo(int amount, DamageType type, GameObject source)
        {
            Amount = amount;
            Type = type;
            Source = source;
        }
    }
}
