using System;
using UnityEngine;

namespace Character
{
    // 플레이어의 최대 체력과 현재 체력 및 사망 상태

    public sealed class PlayerHp : MonoBehaviour, IDamageable
    {
        [SerializeField, Min(1)] private int maxHp = 100;

        private int currentHp;
        private bool isDead;

        public int MaxHp => maxHp;
        public int CurrentHp => currentHp;
        public bool IsDead => isDead;

        public event Action<int, int> HpChanged;
        public event Action<DamageInfo> DamageTaken;
        public event Action Died;

        private void Awake()
        {
            maxHp = Mathf.Max(1, maxHp);
            currentHp = maxHp;
        }

        public void TakeDamage(DamageInfo damageInfo)
        {
            if (isDead) return;
            if (damageInfo.Amount <= 0) return;

            int previousHp = currentHp;
            currentHp = Mathf.Clamp(currentHp - damageInfo.Amount, 0, maxHp);

            string sourceName = damageInfo.Source == null
                ? "None"
                : damageInfo.Source.name;
            Debug.Log(
                $"[Player Hp] Damage: type={damageInfo.Type}, amount={damageInfo.Amount}, " +
                $"hp={previousHp}→{currentHp}/{maxHp}, source={sourceName}",
                this);

            DamageTaken?.Invoke(damageInfo);
            HpChanged?.Invoke(currentHp, maxHp);

            if (currentHp > 0) return;

            isDead = true;
            Died?.Invoke();
        }

        public void RestoreHp(int amount)
        {
            if (isDead || amount <= 0) return;

            int previousHp = currentHp;
            currentHp = Mathf.Clamp(currentHp + amount, 0, maxHp);

            if (currentHp != previousHp) HpChanged?.Invoke(currentHp, maxHp);
        }

        public void ResetHp()
        {
            isDead = false;
            currentHp = maxHp;

            HpChanged?.Invoke(currentHp, maxHp);
        }
    }
}
