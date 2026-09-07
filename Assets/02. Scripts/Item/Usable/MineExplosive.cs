using System.Collections;
using UnityEngine;

namespace TerrainCollapse
{
    /// <summary>
    /// MineItem.Use()에 의해 플레이어가 서있던 타일에 설치되는 지뢰.
    /// DynamiteExplosive와 달리 퓨즈 타이머로 터지는 게 아니라,
    /// 설치 후 armDelay(감지 비활성 유예 시간) 동안은 아무것도 감지하지 않다가,
    /// 그 이후 감지가 활성화되면 감지 반경 안에 플레이어가 들어오는 순간 폭발한다.
    /// 실제 폭발 결과 처리(이펙트/지형 채굴/플레이어 피해)는 ExplosiveBase를 상속받아 재사용한다.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class MineExplosive : ExplosiveBase
    {
        [Header("Detection")]
        [SerializeField] private LayerMask playerLayerMask; // 감지(트리거) 대상 - 플레이어만

        [Header("Mine Setting Asset")]
        [SerializeField] private MineData data;

        [Header("Visual Feedback - Armed Blink")]
        [SerializeField] private float armedBlinkInterval = 0.25f; // 감지 활성화 후 경고 깜빡임 간격
        [SerializeField] private Color armedBlinkColor = Color.red;
        [SerializeField] private Color disarmedColor = Color.white; // 유예 시간 동안(안전) 표시 색

        private SpriteRenderer spriteRenderer;
        private Color originalColor;
        private bool isDetectionActive;

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            originalColor = spriteRenderer.color;
        }

        /// <summary>
        /// MineItem.Use()에서 설치 직후 호출. 위치 세팅 및 유예시간 코루틴 시작.
        /// </summary>
        public void Place(Vector3 position)
        {
            transform.position = position;
            isDetectionActive = false;

            spriteRenderer.color = disarmedColor;

            StartCoroutine(ArmRoutine());
        }

        // 설치자가 대피할 수 있도록 armDelay 동안 감지를 비활성 상태로 유지하다가 활성화
        private IEnumerator ArmRoutine()
        {
            yield return new WaitForSeconds(data.ArmDelay);

            isDetectionActive = true;

            StartCoroutine(DetectionRoutine());
            StartCoroutine(ArmedBlinkRoutine());
        }

        // 감지 활성화 이후, 일정 주기로 감지 반경 내 플레이어 존재 여부를 확인
        private IEnumerator DetectionRoutine()
        {
            WaitForSeconds wait = new WaitForSeconds(data.DetectionInterval);

            while (isDetectionActive)
            {
                if (IsPlayerInRange())
                {
                    Explode();
                    yield break;
                }

                yield return wait;
            }
        }

        private bool IsPlayerInRange()
        {
            Collider2D hit = Physics2D.OverlapCircle(
                transform.position,
                data.DetectionRadius,
                playerLayerMask);

            return hit != null;
        }

        // 감지 활성화 상태임을 알리는 경고 깜빡임
        private IEnumerator ArmedBlinkRoutine()
        {
            bool isBlinkOn = false;

            while (isDetectionActive)
            {
                isBlinkOn = !isBlinkOn;
                spriteRenderer.color = isBlinkOn ? armedBlinkColor : originalColor;

                yield return new WaitForSeconds(armedBlinkInterval);
            }
        }

        private void Explode()
        {
            isDetectionActive = false;

            StopAllCoroutines();

            Detonate(data.ExplosionRadius, data.Power);

            Destroy(gameObject);
        }
    }
}