#if UNITY_EDITOR
using TerrainCollapse;
using UnityEngine;

namespace Character
{
    // 매몰 피해 요청을 공통 체력 피해로 변환

    [RequireComponent(typeof(TestPlayerController), typeof(PlayerHp))]
    public sealed class BurialDamageAdapter : MonoBehaviour
    {
        private TestPlayerController playerController;
        private IDamageable damageable;

        private void Awake()
        {
            playerController = GetComponent<TestPlayerController>();
            damageable = GetComponent<IDamageable>();
        }

        private void OnEnable()
        {
            playerController.BurialDamageRequested += HandleBurialDamage;
        }

        private void OnDisable()
        {
            playerController.BurialDamageRequested -= HandleBurialDamage;
        }

        private void HandleBurialDamage(int amount)
        {
            damageable.TakeDamage(new DamageInfo(amount, DamageType.Burial, gameObject));
        }
    }
}
#endif
