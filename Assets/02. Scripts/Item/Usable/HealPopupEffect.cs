using UnityEngine;

namespace TerrainCollapse
{
    // Potion 사용 시 캐릭터 주변에 생성되는 녹십자 힐 이펙트
    // 플레이어를 부모로 삼아 따라다니며, 팝인 -> (부모 기준) 위로 떠오름 + 회전 -> 페이드아웃 후 스스로 파괴됨
    public class HealPopupEffect : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer sprite;

        [Header("스케일 배율 (프리팹 원본 크기 기준)")]
        [SerializeField] private float popOvershootMultiplier = 1.2f;
        [SerializeField] private float settledMultiplier = 1f;

        [Header("페이드아웃 설정")]
        [SerializeField, Min(0.05f)] private float fadeOutDuration = 0.35f;

        [Header("회전 설정")]
        [SerializeField] private float spinSpeedDegrees = 180f;

        private Vector3 baseScale;
        private Vector3 startLocalPos; // 부모(플레이어) 기준 상대 위치 - 부모가 움직이면 자동으로 같이 이동
        private float currentAngle;

        private float startDelay;
        private float duration;
        private float floatSpeed;
        private float elapsed;
        private bool isPlaying;

        private const float PopRatio = 0.25f;

        public void Play(float playDuration, float riseSpeed, float delay = 0f)
        {
            baseScale = transform.localScale;

            duration = Mathf.Max(0.01f, playDuration);
            floatSpeed = riseSpeed;
            startDelay = Mathf.Max(0f, delay);
            elapsed = 0f;
            currentAngle = 0f;
            isPlaying = true;

            // SetParent(true)로 붙은 직후 시점의 로컬 위치를 기준점으로 저장
            startLocalPos = transform.localPosition;

            transform.localScale = Vector3.zero;
        }

        private void Update()
        {
            if (isPlaying == false) return;

            if (startDelay > 0f)
            {
                startDelay -= Time.deltaTime;
                return;
            }

            elapsed += Time.deltaTime;
            float popDuration = duration * PopRatio;

            if (elapsed <= popDuration)
            {
                float popT = elapsed / popDuration;
                float multiplier = popT < 0.6f
                    ? Mathf.Lerp(0f, popOvershootMultiplier, popT / 0.6f)
                    : Mathf.Lerp(popOvershootMultiplier, settledMultiplier, (popT - 0.6f) / 0.4f);
                transform.localScale = baseScale * multiplier;
            }
            else
            {
                transform.localScale = baseScale * settledMultiplier;

                // 부모(플레이어) 기준 로컬 좌표로 상승 -> 플레이어가 이동해도 그 위에서 계속 떠오름
                float riseAmount = floatSpeed * (elapsed - popDuration);
                transform.localPosition = startLocalPos + Vector3.up * riseAmount;

                float fadeStart = Mathf.Max(popDuration, duration - fadeOutDuration);
                float fadeT = Mathf.Clamp01((elapsed - fadeStart) / (duration - fadeStart));

                if (sprite != null)
                {
                    Color c = sprite.color;
                    c.a = Mathf.Lerp(1f, 0f, fadeT);
                    sprite.color = c;
                }
            }

            if (elapsed >= duration)
            {
                isPlaying = false;
                Destroy(gameObject);
            }
        }

        private void LateUpdate()
        {
            if (isPlaying == false) return;
            if (startDelay > 0f) return;

            currentAngle += spinSpeedDegrees * Time.deltaTime;
            transform.rotation = Quaternion.Euler(0f, currentAngle, 0f); // 부모 회전과 무관하게 항상 화면 기준으로 회전
        }
    }
}