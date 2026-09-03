using TerrainCollapse;
using UnityEngine;

namespace Character
{
    // Static 지형 이탈 또는 잡기 상태 해제 시점 높이와 착지 또는 다시 잡기 상태 진입 시점 높이 차이를 공통 체력 피해로 변환

    [RequireComponent(typeof(Rigidbody2D), typeof(Collider2D), typeof(PlayerHp))]
    public sealed class FallDamageDetector : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float safeFallDistanceInTiles = 2.5f;
        [SerializeField, Min(0f)] private float damagePerTile = 1f;
        [SerializeField, Min(0f)] private float climbingDamageMultiplier = 0.5f;

        private readonly struct GroundContact
        {
            public readonly bool IsGrounded;
            public readonly bool IsStatic;
            public readonly float Height;

            public GroundContact(bool isGrounded, bool isStatic, float height)
            {
                IsGrounded = isGrounded;
                IsStatic = isStatic;
                Height = height;
            }
        }

        private readonly RaycastHit2D[] groundHits = new RaycastHit2D[8];

        private Collider2D bodyCollider;
        private IDamageable damageable;
        private TerrainManager terrainManager;
        private IPlayerController testPlayerController;
        private float lastStaticSupportY;
        private float? fallStartY;
        private bool wasGrounded;
        private bool wasOnStaticGround;
        private bool wasClimbing;

        private void Awake()
        {
            bodyCollider = GetComponent<Collider2D>();
            damageable = GetComponent<IDamageable>();
            testPlayerController = GetComponent(typeof(IPlayerController)) as IPlayerController;
            terrainManager = testPlayerController.TerrainManager;
            wasClimbing = testPlayerController.IsClimbing;

            GroundContact ground = GetGroundContact();
            wasGrounded = ground.IsGrounded;
            wasOnStaticGround = ground.IsStatic;

            if (ground.IsStatic) lastStaticSupportY = ground.Height;
        }

        private void FixedUpdate()
        {
            GroundContact ground = GetGroundContact();
            bool isClimbing = testPlayerController.IsClimbing;

            if (wasClimbing == false && isClimbing)
            {
                // 잡기 시작 지점을 착지점으로 보고 현재 낙하 피해를 절반만 적용
                if (fallStartY.HasValue)
                    ApplyFallDamage(
                        fallStartY.Value,
                        bodyCollider.bounds.min.y,
                        climbingDamageMultiplier);

                fallStartY = null;
            }
            else if (wasClimbing && isClimbing == false)
            {
                // 잡기 해제 지점을 새로운 낙하 시작점으로 저장
                fallStartY = ground.IsGrounded
                    ? null
                    : bodyCollider.bounds.min.y;
            }
            else if (isClimbing == false)
            {
                // 마지막 Static 지형에서 발이 떨어진 높이를 낙하 시작점으로 저장
                if (wasOnStaticGround && ground.IsGrounded == false)
                    fallStartY = lastStaticSupportY;

                // 낙하 추적 중 새로운 지면에 착지하면 두 지면의 높이 차이로 피해 계산
                if (fallStartY.HasValue && wasGrounded == false && ground.IsGrounded)
                {
                    ApplyFallDamage(fallStartY.Value, ground.Height);
                    fallStartY = null;
                }
            }

            if (ground.IsStatic) lastStaticSupportY = ground.Height;

            wasGrounded = ground.IsGrounded;
            wasOnStaticGround = ground.IsStatic;
            wasClimbing = isClimbing;
        }

        private GroundContact GetGroundContact()
        {
            bool isGrounded = false;
            bool isStatic = false;
            float height = float.NegativeInfinity;
            float groundCheckDistance = Mathf.Max(0.001f, testPlayerController.CollisionSkin);
            LayerMask groundMask = testPlayerController.CollisionMask;

            ContactFilter2D filter = new ContactFilter2D
            {
                useLayerMask = true,
                layerMask = groundMask,
                useTriggers = false
            };

            int hitCount = bodyCollider.Cast(Vector2.down, filter, groundHits, groundCheckDistance);

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit2D hit = groundHits[i];
                Collider2D hitCollider = hit.collider;
                if (hitCollider == null || hitCollider == bodyCollider) continue;

                isGrounded = true;
                height = Mathf.Max(height, hit.point.y);

                Rigidbody2D hitBody = hitCollider.attachedRigidbody;
                if (hitBody == null || hitBody.bodyType == RigidbodyType2D.Static)
                    isStatic = true;
            }

            return new GroundContact(isGrounded, isStatic, height);
        }

        private void ApplyFallDamage(
            float startY,
            float landingY,
            float damageMultiplier = 1f)
        {
            float cellSize = terrainManager.CellSize;
            float fallenTileCount = Mathf.Max(0f, startY - landingY) / cellSize;

            if (fallenTileCount <= safeFallDistanceInTiles) return;

            int baseDamage = Mathf.CeilToInt(
                (fallenTileCount - safeFallDistanceInTiles) * damagePerTile);
            int damage = Mathf.CeilToInt(baseDamage * damageMultiplier);

            if (damage <= 0) return;

            damageable.TakeDamage(new DamageInfo(damage, DamageType.Fall, null));
        }
    }
}
