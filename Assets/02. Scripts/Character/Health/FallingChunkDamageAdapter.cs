using TerrainCollapse;
using UnityEngine;

namespace Character
{
    // 낙하 지형의 충돌 속도를 공통 체력 피해로 변환

    [RequireComponent(typeof(Collider2D), typeof(PlayerHp))]
    public sealed class FallingChunkDamageAdapter : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float minimumDamageSpeed = 1f;
        [SerializeField, Min(0f)] private float damagePerSpeed = 1f;

        private IDamageable damageable;

        private void Awake()
        {
            damageable = GetComponent<IDamageable>();
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
    }
}
