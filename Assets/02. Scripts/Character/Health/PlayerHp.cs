using System;
using TerrainCollapse;
using UnityEngine;

namespace Character
{
    // 플레이어의 최대 체력과 현재 체력 및 사망 상태

    public sealed class PlayerHp : MonoBehaviour, IDamageable
    {
        [SerializeField, Min(1)] private int maxHp = 100;

        private int currentHp;
        private bool isDead;
        private RoomClient roomClient;

        public int MaxHp => maxHp;
        public int CurrentHp => currentHp;
        public bool IsDead => isDead;
        public bool IsDamageImmune => IsServerSpawnSafeZone; // 서버가 지정한 스폰 안전 구역에서는 모든 피해를 무시

        public event Action<int, int> HpChanged;
        public event Action<DamageInfo> DamageTaken;
        public event Action Died;

        private void Awake()
        {
            maxHp = Mathf.Max(1, maxHp);
            currentHp = maxHp;
            roomClient = RoomClient.Instance ?? FindFirstObjectByType<RoomClient>();
        }

        public void TakeDamage(DamageInfo damageInfo)
        {
            // 서버 연결 중에는 로컬 낙하/매몰/폭발 콜라이더가 체력과 사망을 확정하면 안 된다.
            // 서버가 보낸 player.health_changed / player.died만 ApplyAuthoritativeState로 반영한다.
            // 그렇지 않으면 로컬 사망 보관함 요청이 서버의 IsDead 상태보다 먼저 도착한다.
            roomClient ??= RoomClient.Instance ?? FindFirstObjectByType<RoomClient>();
            if (roomClient != null && roomClient.IsConnected)
                return;

            if (isDead) return;
            if (damageInfo.Amount <= 0) return;
            if (IsDamageImmune) return;

            int previousHp = currentHp;
            currentHp = Mathf.Clamp(currentHp - damageInfo.Amount, 0, maxHp);

            string sourceName = damageInfo.Source == null
                ? "None"
                : damageInfo.Source.name;
            Debug.Log(
                $"[Player Hp] Damage: type={damageInfo.Type}, amount={damageInfo.Amount}, " +
                $"hp={previousHp}→{currentHp}/{maxHp}, source={sourceName}",
                this);

            if (previousHp - currentHp >= 3)
                GameAudio.GameSound.Play(GameAudio.SoundCue.PlayerHurt);
            DamageTaken?.Invoke(damageInfo);
            HpChanged?.Invoke(currentHp, maxHp);

            if (currentHp > 0) return;

            isDead = true;
            GameAudio.GameSound.Play(GameAudio.SoundCue.PlayerDeath);
            Died?.Invoke();
        }

        /// <summary>
        /// 로컬 물리 피해 판정은 유지하되, 서버 연결 중에는 서버에게 보고만 한다.
        /// </summary>
        public void ReportOrApplyDamage(DamageInfo damageInfo)
        {
            if (isDead || damageInfo.Amount <= 0 ||
                (damageInfo.Type != DamageType.Debug && IsDamageImmune))
                return;

            roomClient ??= RoomClient.Instance ?? FindFirstObjectByType<RoomClient>();
            if (roomClient != null && roomClient.IsConnected)
            {
                if (roomClient.RequestPlayerDamage(
                        damageInfo.Type,
                        damageInfo.Amount,
                        transform.position) == false)
                {
                    Debug.LogWarning(
                        $"[Player Hp] Damage request rejected locally: " +
                        $"type={damageInfo.Type}, amount={damageInfo.Amount}, " +
                        $"gameplayActive={roomClient.IsGameplayActive}",
                        this);
                }
                return;
            }

            TakeDamage(damageInfo);
        }

        private bool IsServerSpawnSafeZone
        {
            get
            {
                roomClient ??= RoomClient.Instance ?? FindFirstObjectByType<RoomClient>();

                return roomClient != null && roomClient.IsLocalPlayerInSpawnArea;
            }
        }

        public void RestoreHp(int amount)
        {
            if (isDead || amount <= 0) return;

            roomClient ??= RoomClient.Instance ?? FindFirstObjectByType<RoomClient>();
            if (roomClient != null && roomClient.IsConnected)
            {
                if (roomClient.RequestPlayerHeal(amount, transform.position) == false)
                    Debug.LogWarning(
                        $"[Player Hp] Heal request rejected locally: amount={amount}, " +
                        $"gameplayActive={roomClient.IsGameplayActive}",
                        this);
                return;
            }

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

        /// <summary>서버가 확정한 체력 상태를 로컬 표시 상태에 반영</summary>
        public void ApplyAuthoritativeState(
            int serverCurrentHealth,
            int serverMaxHealth,
            bool serverIsDead,
            bool notifyDeath)
        {
            if (serverMaxHealth <= 0) return;

            bool wasDead = isDead;
            maxHp = serverMaxHealth;
            currentHp = Mathf.Clamp(serverCurrentHealth, 0, maxHp);
            isDead = serverIsDead;
            HpChanged?.Invoke(currentHp, maxHp);

            if (notifyDeath && wasDead == false && isDead)
            {
                GameAudio.GameSound.Play(GameAudio.SoundCue.PlayerDeath);
                Died?.Invoke();
            }
        }
    }
}
