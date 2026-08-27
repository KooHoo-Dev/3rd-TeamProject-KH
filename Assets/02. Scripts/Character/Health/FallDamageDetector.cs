using UnityEngine;

namespace Character
{
    // 플레이어의 착지 직전 낙하 속도를 공통 체력 피해로 변환

    [RequireComponent(typeof(Rigidbody2D), typeof(Collider2D), typeof(PlayerHp))]
    public sealed class FallDamageDetector : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float minimumDamageSpeed = 10f;
        [SerializeField, Min(0f)] private float damagePerSpeed = 1f;
        [SerializeField, Min(0.001f)] private float groundCheckDistance = 0.05f;
        [SerializeField] private LayerMask groundMask = ~0;

        private readonly RaycastHit2D[] groundHits = new RaycastHit2D[8];

        private Rigidbody2D body;
        private Collider2D bodyCollider;
        private IDamageable damageable;
        private Vector2 previousPosition;
        private float maximumFallingSpeed;
        private bool wasGrounded;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            bodyCollider = GetComponent<Collider2D>();
            damageable = GetComponent<IDamageable>();
            previousPosition = body.position;
            wasGrounded = IsGrounded();
        }

        private void FixedUpdate()
        {
            float fallingSpeed = Mathf.Max(0f,
                (previousPosition.y - body.position.y) / Time.fixedDeltaTime);
            previousPosition = body.position;

            bool isGrounded = IsGrounded();

            if (isGrounded == false)
                maximumFallingSpeed = Mathf.Max(maximumFallingSpeed, fallingSpeed);
            else if (wasGrounded == false)
            {
                ApplyFallDamage();
                maximumFallingSpeed = 0f;
            }

            wasGrounded = isGrounded;
        }

        private bool IsGrounded()
        {
            ContactFilter2D filter = new ContactFilter2D
            {
                useLayerMask = true,
                layerMask = groundMask,
                useTriggers = false
            };

            int hitCount = bodyCollider.Cast(Vector2.down, filter, groundHits, groundCheckDistance);

            for (int i = 0; i < hitCount; i++)
            {
                Collider2D hit = groundHits[i].collider;
                if (hit != null && hit != bodyCollider) return true;
            }

            return false;
        }

        private void ApplyFallDamage()
        {
            if (maximumFallingSpeed < minimumDamageSpeed) return;

            int damage = Mathf.CeilToInt(
                (maximumFallingSpeed - minimumDamageSpeed) * damagePerSpeed);

            if (damage <= 0) return;

            damageable.TakeDamage(new DamageInfo(damage, DamageType.Fall, null));
        }
    }
}
