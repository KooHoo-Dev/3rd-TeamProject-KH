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

        private void Awake()
        {
            vignetteImage ??= GetComponent<Image>();
            playerHp ??= FindFirstObjectByType<PlayerHp>();

            if (vignetteImage != null)
            {
                vignetteImage.raycastTarget = false;
                SetAlpha(0f);
            }
        }

        private void OnEnable()
        {
            if (playerHp == null) return;

            lastHp = playerHp.CurrentHp;
            playerHp.HpChanged += HandleHpChanged;
        }

        private void OnDisable()
        {
            if (playerHp != null)
                playerHp.HpChanged -= HandleHpChanged;

            if (vignetteRoutine != null)
                StopCoroutine(vignetteRoutine);

            vignetteRoutine = null;
            SetAlpha(0f);
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
            if (vignetteImage == null) return;

            Color color = damageColor;
            color.a = alpha;
            vignetteImage.color = color;
        }
    }
}
