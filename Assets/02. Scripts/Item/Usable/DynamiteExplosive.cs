using System.Collections;
using Character;
using UnityEngine;
using TerrainCollapse.Networking;

namespace TerrainCollapse
{
    /// <summary>
    /// DynamiteItem.Use()에 의해 투척되어 생성되는 실제 폭발물.
    /// 착탄 지점까지 이동 후 퓨즈 시간만큼 대기하다가 폭발한다.
    /// 폭발 결과 처리(이펙트/지형 채굴/플레이어 피해)는 ExplosiveBase를 상속받아 재사용한다.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class DynamiteExplosive : ExplosiveBase
    {
        [Header("Explosive Setting Asset")]
        [SerializeField] private ExplosiveData data; // SO 데이터 참조

        [Header("Launch Physics")]
        [SerializeField, Min(0f)] private float launchClearance = 0.05f;

        [Header("Visual Feedback - Blink")]
        [SerializeField] private float maxBlinkInterval = 0.4f; // 퓨즈 시작 시점 깜빡임 간격
        [SerializeField] private float minBlinkInterval = 0.05f; // 폭발 직전 깜빡임 간격
        [SerializeField] private Color blinkColor = Color.red;   // 깜빡일 때 색상

        private string projectileID;
        private bool isLocalOwner;
        private bool isServerControlled;

        private Rigidbody2D rb;
        private Collider2D projectileCollider;
        private SpriteRenderer spriteRenderer;
        private Color originalColor;
        private AudioSource tickingSource;

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            projectileCollider = GetComponent<Collider2D>();
            spriteRenderer = GetComponent<SpriteRenderer>();
            originalColor = spriteRenderer.color;

            // 타일 가장자리에서 빠르게 날아갈 때 Discrete 충돌이 관통/과도한 보정으로
            // 이어지는 것을 막는다.
            if (rb != null)
                rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        public void Launch(Vector3 ownerPos, Vector3 direction)
        {
            Vector2 launchDirection = ((Vector2)direction).normalized;
            transform.position = GetSeparatedLaunchPosition(ownerPos, launchDirection);
            IgnoreLocalPlayerCollisions();

            // 방향 벡터로 물리 속도 부여
            if (rb != null)
            {
                rb.linearVelocity = launchDirection * data.ThrowSpeed;
            }

            StartCoroutine(FuseRoutine());
            StartCoroutine(BlinkRoutine(data.FuseTime));
            StartTickingSound();
        }

        public void LaunchFromServer(DynamiteThrownMessage message, bool isOwner)
        {
            projectileID = message.ProjectileID;
            isLocalOwner = isOwner;
            isServerControlled = true;

            Vector2 launchDirection = new(message.DirectionX, message.DirectionY);
            if (launchDirection.sqrMagnitude < 0.0001f)
                launchDirection = Vector2.right;
            else
                launchDirection.Normalize();

            transform.position = GetSeparatedLaunchPosition(
                new Vector2(message.StartX, message.StartY),
                launchDirection);
            IgnoreLocalPlayerCollisions();

            if (rb != null)
            {
                rb.simulated = true;
                rb.linearVelocity = launchDirection * message.ThrowSpeed;
            }

            StartCoroutine(NetworkFuseRoutine(message.FuseTime));
            StartCoroutine(BlinkRoutine(message.FuseTime));
            StartTickingSound();
        }

        // 프리팹의 Collider2D offset까지 반영해, 콜라이더 뒤쪽 끝이 플레이어 중심보다
        // launchClearance만큼 발사 방향 앞으로 오도록 배치한다.
        private Vector2 GetSeparatedLaunchPosition(
            Vector2 ownerPosition,
            Vector2 launchDirection)
        {
            if (projectileCollider == null)
                return ownerPosition + launchDirection * launchClearance;

            // bounds는 현재 transform 위치를 기준으로 계산되므로, 우선 owner 위치에 둔다.
            transform.position = ownerPosition;
            Bounds bounds = projectileCollider.bounds;
            Vector2 colliderCenterOffset =
                (Vector2)bounds.center - ownerPosition;
            Vector2 extents = bounds.extents;
            float forwardExtent =
                Mathf.Abs(launchDirection.x) * extents.x +
                Mathf.Abs(launchDirection.y) * extents.y;
            float distance = Mathf.Max(
                launchClearance,
                forwardExtent - Vector2.Dot(colliderCenterOffset, launchDirection) +
                launchClearance);

            return ownerPosition + launchDirection * distance;
        }

        // 투사체는 타일에는 부딪혀 튕기지만, 플레이어 물리로 밀려서는 안 된다.
        // 각 클라이언트에는 로컬 플레이어만 실물 Collider2D를 가지므로 해당 콜라이더만 무시한다.
        private void IgnoreLocalPlayerCollisions()
        {
            if (projectileCollider == null)
                return;

            PlayerController localPlayer = FindFirstObjectByType<PlayerController>();
            if (localPlayer == null)
                return;

            foreach (Collider2D playerCollider in
                     localPlayer.GetComponentsInChildren<Collider2D>(true))
            {
                if (playerCollider != null)
                    Physics2D.IgnoreCollision(projectileCollider, playerCollider, true);
            }
        }

        public void ExplodeFromServer(Vector2 explosionPosition, float explosionRadius)
        {
            isServerControlled = false;

            StopTickingSound();
            StopAllCoroutines();

            transform.position = explosionPosition;

            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
                rb.simulated = false;
            }

            Detonate(explosionRadius, data.Power);

            Destroy(gameObject);
        }

        private IEnumerator FuseRoutine()
        {
            // 퓨즈 타임 동안 계속 날아가거나 굴러감
            yield return new WaitForSeconds(data.FuseTime);

            Explode();
        }

        private IEnumerator NetworkFuseRoutine(float fuseTime)
        {
            yield return new WaitForSeconds(fuseTime);

            if (isServerControlled == false)
                yield break;

            // 다른 클라이언트는 서버 exploded 메시지를 기다림
            if (isLocalOwner == false)
                yield break;

            RoomClient.Instance?.RequestDynamiteExplosion(projectileID, transform.position);
        }

        // 퓨즈 시간에 비례해 깜빡이는 간격이 점점 짧아지는 코루틴
        private IEnumerator BlinkRoutine(float fuseTime)
        {
            float elapsed = 0f;
            bool isBlinkOn = false;

            while (elapsed < fuseTime)
            {
                float progress = elapsed / fuseTime; // 0(시작) ~ 1(폭발 직전)

                // 제곱을 줘서 후반부로 갈수록 더 급격하게 빨라지도록 처리
                float easedProgress = progress * progress;
                float currentInterval = Mathf.Lerp(maxBlinkInterval, minBlinkInterval, easedProgress);

                isBlinkOn = !isBlinkOn;
                spriteRenderer.color = isBlinkOn ? blinkColor : originalColor;

                yield return new WaitForSeconds(currentInterval);
                elapsed += currentInterval;
            }
        }

        private void Explode()
        {
            StopTickingSound();
            Detonate(data.Radius, data.Power);

            Destroy(gameObject);
        }

        private void StartTickingSound()
        {
            StopTickingSound();
            tickingSource = GameAudio.GameSound.PlayLoop(
                GameAudio.SoundCue.DynamiteTick,
                transform);
        }

        private void StopTickingSound()
        {
            if (tickingSource == null) return;
            GameAudio.GameSound.StopLoop(tickingSource);
            tickingSource = null;
        }
    }
}
