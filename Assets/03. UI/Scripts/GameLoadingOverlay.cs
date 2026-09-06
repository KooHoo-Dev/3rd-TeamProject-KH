using System.Collections;
using TerrainCollapse.Networking;
using UnityEngine;

namespace TerrainCollapse
{
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class GameLoadingOverlay : MonoBehaviour
    {
        [SerializeField] private RoomClient roomClient;
        [SerializeField, Min(0.01f)] private float fadeDuration = 0.25f;

        private CanvasGroup canvasGroup;
        private Coroutine fadeRoutine;

        private void Awake()
        {
            canvasGroup = GetComponent<CanvasGroup>();
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = true;

            if (roomClient == null)
                roomClient = FindFirstObjectByType<RoomClient>();
        }

        private void OnEnable()
        {
            if (roomClient == null)
                roomClient = FindFirstObjectByType<RoomClient>();

            if (roomClient != null)
                roomClient.GameStarted += HandleGameStarted;
        }

        private void Start()
        {
            if (roomClient != null && roomClient.HasGameStartSchedule)
                BeginFadeOut();
        }

        private void OnDisable()
        {
            if (roomClient != null)
                roomClient.GameStarted -= HandleGameStarted;
        }

        private void HandleGameStarted(GameStartedMessage _)
        {
            BeginFadeOut();
        }

        private void BeginFadeOut()
        {
            if (fadeRoutine != null) return;

            canvasGroup.blocksRaycasts = false;
            fadeRoutine = StartCoroutine(FadeOut());
        }

        private IEnumerator FadeOut()
        {
            float startAlpha = canvasGroup.alpha;
            float elapsed = 0f;

            while (elapsed < fadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, elapsed / fadeDuration);
                yield return null;
            }

            canvasGroup.alpha = 0f;
            gameObject.SetActive(false);
        }
    }
}
