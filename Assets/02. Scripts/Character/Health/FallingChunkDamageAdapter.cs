using TerrainCollapse;
using UnityEngine;

namespace Character
{
    // 낙하 지형의 충돌 및 매몰 피해를 공통 체력 피해로 변환

    [RequireComponent(typeof(Collider2D), typeof(FallingTerrainInteraction), typeof(PlayerHp))]
    public sealed class FallingChunkDamageAdapter : MonoBehaviour
    {
        [Header("Collision Damage")]
        [SerializeField, Min(0f)] private float minimumDamageSpeed = 1f;
        [SerializeField, Min(0f)] private float damagePerSpeed = 1f;

        [Header("Burial Damage")]
        [SerializeField, Min(1)] private int impactDamagePerTile = 1;

        private FallingTerrainInteraction interaction;
        private IDamageable damageable;

        private void Awake()
        {
            interaction = GetComponent<FallingTerrainInteraction>();
            damageable = GetComponent<IDamageable>();
        }

        private void OnEnable()
        {
            interaction.BurialImpactRequested += HandleBurialImpact;
            interaction.BurialDamageRequested += HandleBurialDamage;
        }

        private void OnDisable()
        {
            interaction.BurialImpactRequested -= HandleBurialImpact;
            interaction.BurialDamageRequested -= HandleBurialDamage;
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (collision.collider.TryGetComponent(out FallingChunk fallingChunk) == false) return;

            float fallingSpeed = Mathf.Max(0f, -fallingChunk.BeforeVelocity.y);
            if (fallingSpeed < minimumDamageSpeed) return;

            int damage = Mathf.CeilToInt(
                (fallingSpeed - minimumDamageSpeed) * damagePerSpeed);

            if (damage <= 0) return;

            damageable.TakeDamage(new DamageInfo(
                damage,
                DamageType.FallingChunk,
                fallingChunk.gameObject));
        }

        private void HandleBurialImpact(FallingTerrainImpactInfo impact)
        {
            int damage = impact.TileCount * impactDamagePerTile;

            damageable.TakeDamage(new DamageInfo(
                damage,
                DamageType.FallingChunk,
                impact.Source));
        }

        private void HandleBurialDamage(int amount)
        {
            damageable.TakeDamage(new DamageInfo(amount, DamageType.Burial, gameObject));
        }
    }
}
