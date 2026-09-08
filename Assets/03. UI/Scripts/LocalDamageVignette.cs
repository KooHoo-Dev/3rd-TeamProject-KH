using System.Collections;
using Character;
using UnityEngine;
using UnityEngine.UI;

namespace TerrainCollapse
{
    public sealed class LocalDamageVignette : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerHp playerHp;
        [SerializeField] private Image vignetteImage;

        [Header("Damage Vignette")]
        [SerializeField] private Color damageColor = new(1f, 0.15f, 0.1f, 1f);
        [SerializeField, Range(0f, 1f)] private float maxAlpha = 0.45f;
        [SerializeField, Min(0f)] private float fadeInDuration = 0.03f;
        [SerializeField, Min(0f)] private float fadeOutDuration = 0.2f;

        private Coroutine vignetteRoutine;
        private int lastHp;
        private bool isBound;

        private void Awake()
        {
            vignetteImage ??= GetComponent<Image>();

            if (vignetteImage != null)
            {
                vignetteImage.raycastTarget = false;
                SetAlpha(0f);
            }
        }

        private void OnEnable()
        {
            TryBindPlayerHp();
        }

        private void Update()
        {
            if (isBound == false)
                TryBindPlayerHp();
        }

        private void OnDisable()
        {
            if (isBound)
                playerHp.HpChanged -= HandleHpChanged;

            isBound = false;

            if (vignetteRoutine != null)
                StopCoroutine(vignetteRoutine);

            vignetteRoutine = null;
            SetAlpha(0f);
        }

        private void TryBindPlayerHp()
        {
            if (isBound) return;

            playerHp ??= FindFirstObjectByType<PlayerHp>();
            if (playerHp == null) return;

            lastHp = playerHp.CurrentHp;
            playerHp.HpChanged += HandleHpChanged;
            isBound = true;
        }

        private void HandleHpChanged(int currentHp, int _)
        {
            bool tookDamage = currentHp < lastHp;
            lastHp = currentHp;

            if (tookDamage)
                PlayDamageVignette();
        }

        private void PlayDamageVignette()
        {
            if (vignetteImage == null) return;

            if (vignetteRoutine != null)
                StopCoroutine(vignetteRoutine);

            vignetteRoutine = StartCoroutine(PlayDamageVignetteRoutine());
        }

        private IEnumerator PlayDamageVignetteRoutine()
        {
            yield return Fade(0f, maxAlpha, fadeInDuration);
            yield return Fade(maxAlpha, 0f, fadeOutDuration);
            vignetteRoutine = null;
        }

        private IEnumerator Fade(float from, float to, float duration)
        {
            if (duration <= 0f)
            {
                SetAlpha(to);
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                SetAlpha(Mathf.Lerp(from, to, elapsed / duration));
                yield return null;
            }

            SetAlpha(to);
        }

        private void SetAlpha(float alpha)
        {
            alpha = Mathf.Clamp01(alpha);

            if (vignetteImage == null) return;

            // 0 알파 상태는 렌더러도 끈다. 프리팹에 저장된 기본 Image 색상이
            // 남아 있어 비네트가 상시 표시되는 경우를 막는다.
            vignetteImage.enabled = alpha > 0.001f;

            Color color = damageColor;
            color.a = alpha;
            vignetteImage.color = color;
        }
    }
}
