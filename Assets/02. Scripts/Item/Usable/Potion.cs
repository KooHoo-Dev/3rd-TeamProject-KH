using UnityEngine;
using Character;

namespace TerrainCollapse
{
    public class Potion : Item
    {
        [Header("회복 설정")]
        [SerializeField, Min(1)] private int healAmount = 30;

        [Header("힐 이펙트 (녹십자)")]
        [SerializeField] private GameObject healEffectPrefab;
        [SerializeField] private int minEffectCount = 3;
        [SerializeField] private int maxEffectCount = 5;
        [SerializeField] private float spawnRadius = 0.6f;
        [SerializeField] private float effectDuration = 1.2f;
        [SerializeField] private float floatSpeed = 0.8f;
        [SerializeField] private float maxSpawnStagger = 0.15f;

        public override bool Use(Vector3 ownerPos, Vector3 targetPos)
        {
            PlayerHp playerHp = FindFirstObjectByType<PlayerHp>();
            if (playerHp == null)
            {
                Debug.LogWarning("[Potion] PlayerHp를 찾을 수 없어 사용할 수 없습니다.");
                return false;
            }

            if (playerHp.IsDead)
                return false;

            playerHp.RestoreHp(healAmount);
            SpawnHealEffects(ownerPos, playerHp.transform);
            
            return true;
        }

        public void PlayConfirmedEffect(Vector3 ownerPos)
        {
            PlayerHp playerHp = FindFirstObjectByType<PlayerHp>();
            if (playerHp != null) SpawnHealEffects(ownerPos, playerHp.transform);
        }

        private void SpawnHealEffects(Vector3 centerPos, Transform followTarget)
        {
            int effectCount = Random.Range(minEffectCount, maxEffectCount);

            for (int i = 0; i < effectCount; i++)
            {
                Vector2 randomOffset = Random.insideUnitCircle * spawnRadius;
                Vector3 spawnPos = centerPos + new Vector3(randomOffset.x, Mathf.Abs(randomOffset.y) * 0.5f, 0f);
                float delay = Random.Range(0f, maxSpawnStagger);

                GameObject effect = Instantiate(healEffectPrefab, spawnPos, Quaternion.identity);

                // 플레이어를 부모로 붙이되 월드 위치는 그대로 유지 -> 이후 로컬 좌표만으로 "부모 기준 상승"이 성립
                if (followTarget != null)
                    effect.transform.SetParent(followTarget, true);

                if (effect.TryGetComponent(out HealPopupEffect popupEffect))
                    popupEffect.Play(effectDuration, floatSpeed, delay);
            }
        }
    }
}
