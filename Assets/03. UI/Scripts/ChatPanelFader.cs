using UnityEngine;
using UnityEngine.InputSystem;

namespace TerrainCollapse
{
    /// <summary>채팅 활동이 없을 때 패널 전체를 서서히 숨기고 활동 시 즉시 복원한다.</summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class ChatPanelFader : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float visibleDuration = 3f;
        [SerializeField, Min(0.01f)] private float fadeDuration = 1f;

        private CanvasGroup canvasGroup;
        private float lastActivityTime;
        private bool isInputActive;

        private void Awake()
        {
            canvasGroup = GetComponent<CanvasGroup>();
            ShowImmediately();
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null &&
                (keyboard.enterKey.wasPressedThisFrame ||
                 keyboard.numpadEnterKey.wasPressedThisFrame ||
                 keyboard.tKey.wasPressedThisFrame))
            {
                ShowImmediately();
            }

            if (isInputActive)
            {
                canvasGroup.alpha = 1f;
                return;
            }

            float idleTime = Time.unscaledTime - lastActivityTime;
            if (idleTime <= visibleDuration) return;

            float fadeProgress = (idleTime - visibleDuration) / fadeDuration;
            canvasGroup.alpha = 1f - Mathf.Clamp01(fadeProgress);
        }

        /// <summary>새 채팅 또는 시스템 메시지를 표시할 때 호출한다.</summary>
        public void NotifyMessageReceived() => ShowImmediately();

        /// <summary>입력 중에는 시간이 지나도 패널을 숨기지 않는다.</summary>
        public void SetInputActive(bool isActive)
        {
            isInputActive = isActive;
            if (isActive) ShowImmediately();
            else lastActivityTime = Time.unscaledTime;
        }

        public void ShowImmediately()
        {
            lastActivityTime = Time.unscaledTime;
            if (canvasGroup != null) canvasGroup.alpha = 1f;
        }
    }
}
