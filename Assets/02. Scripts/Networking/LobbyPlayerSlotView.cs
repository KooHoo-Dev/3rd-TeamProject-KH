using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace TerrainCollapse
{
    /// <summary>로비 슬롯 순서와 인게임 캐릭터가 공유하는 플레이어 색상표입니다.</summary>
    public static class PlayerColorPalette
    {
        private static readonly int NewColorId =
            Shader.PropertyToID("_ColorChangeNewCol");
        private static readonly int AlphaId =
            Shader.PropertyToID("_Alpha");

        private static readonly Color[] Colors =
        {
            new(0.96f, 0.80f, 0.16f, 1f),
            new(0.93f, 0.23f, 0.23f, 1f),
            new(0.24f, 0.52f, 0.95f, 1f),
            new(0.25f, 0.78f, 0.35f, 1f),
        };

        public static Color GetColor(int colorIndex)
            => Colors[Mathf.Clamp(colorIndex, 0, Colors.Length - 1)];

        public static void ApplyToSpriteRenderers(GameObject playerRoot, int colorIndex, float alpha = 1f)
        {
            if (playerRoot == null) return;

            Color color = GetColor(colorIndex);
            color.a *= Mathf.Clamp01(alpha);
            MaterialPropertyBlock properties = new();
            foreach (SpriteRenderer spriteRenderer in
                     playerRoot.GetComponentsInChildren<SpriteRenderer>(true))
            {
                spriteRenderer.GetPropertyBlock(properties);
                properties.SetColor(NewColorId, color);
                // Player.mat의 AllIn1 Shader는 최종 alpha에 이 값을 사용한다.
                properties.SetFloat(AlphaId, Mathf.Clamp01(alpha));
                spriteRenderer.SetPropertyBlock(properties);
            }
        }
    }

    /// <summary>대기방의 고정 4인 슬롯 하나를 표시한다.</summary>
    public sealed class LobbyPlayerSlotView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        private const float PreviewScale = 135f;
        private const int PreviewSortingOrder = 1;
        private static readonly Vector3 PreviewLocalPosition = Vector3.zero;
        private const int HoverOverlaySortingOrder = 900;
        private static Canvas hoverOverlayCanvas;

        [SerializeField] private Image avatarImage;
        [SerializeField] private TMP_Text nickNameText;
        [SerializeField] private GameObject localPlayerPrefab;
        [SerializeField] private GameObject remotePlayerPrefab;
        [SerializeField] private GameObject kickOverlay;

        private GameObject previewInstance;
        private GameObject currentPrefab;
        private System.Action kickAction;
        private RectTransform slotRect;
        private RectTransform kickOverlayRect;
        private Camera sourceCanvasCamera;

        private void Awake()
        {
            slotRect = transform as RectTransform;
            if (avatarImage != null) avatarImage.enabled = false;
            MoveKickOverlayToForegroundCanvas();
            if (kickOverlay != null) kickOverlay.SetActive(false);
        }

        private void LateUpdate()
        {
            if (kickOverlay != null && kickOverlay.activeSelf)
                RefreshKickOverlayRect();
        }

        public void SetPlayer(string nickName, bool isLocal, int colorIndex, bool isConnected,
            bool canKick, System.Action onKick)
        {
            Color playerColor = PlayerColorPalette.GetColor(colorIndex);
            ShowPrefab(isLocal ? localPlayerPrefab : remotePlayerPrefab, colorIndex, isConnected);
            if (nickNameText != null)
            {
                nickNameText.text = nickName;
                nickNameText.color = isConnected ? playerColor : new Color(playerColor.r, playerColor.g, playerColor.b, 0.35f);
            }
            kickAction = canKick ? onKick : null;
            if (kickOverlay != null) kickOverlay.SetActive(false);
        }

        public void SetEmpty()
        {
            if (previewInstance != null) previewInstance.SetActive(false);
            if (nickNameText != null)
            {
                nickNameText.text = "Waiting...";
                nickNameText.color = new Color(1f, 1f, 1f, 0.4f);
            }
            kickAction = null;
            if (kickOverlay != null) kickOverlay.SetActive(false);
        }

        public void OnPointerEnter(PointerEventData _)
        {
            if (kickAction == null || kickOverlay == null) return;
            RefreshKickOverlayRect();
            kickOverlay.SetActive(true);
        }

        public void OnPointerExit(PointerEventData _)
        {
            if (kickOverlay != null) kickOverlay.SetActive(false);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || kickAction == null) return;
            kickAction.Invoke();
        }

        private void MoveKickOverlayToForegroundCanvas()
        {
            if (kickOverlay == null) return;

            Canvas sourceCanvas = kickOverlay.GetComponentInParent<Canvas>();
            if (sourceCanvas == null || slotRect == null) return;

            sourceCanvasCamera = sourceCanvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : sourceCanvas.worldCamera;
            Canvas canvas = GetOrCreateHoverOverlayCanvas(sourceCanvas);
            if (canvas == null) return;

            kickOverlay.transform.SetParent(canvas.transform, false);
            kickOverlayRect = kickOverlay.transform as RectTransform;
            kickOverlayRect.anchorMin = kickOverlayRect.anchorMax = new Vector2(0.5f, 0.5f);
            kickOverlayRect.pivot = new Vector2(0.5f, 0.5f);
            foreach (Graphic graphic in kickOverlay.GetComponentsInChildren<Graphic>(true))
                graphic.raycastTarget = false;
        }

        private static Canvas GetOrCreateHoverOverlayCanvas(Canvas sourceCanvas)
        {
            if (hoverOverlayCanvas != null) return hoverOverlayCanvas;

            GameObject root = new("Lobby Hover Overlay", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            hoverOverlayCanvas = root.GetComponent<Canvas>();
            hoverOverlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            hoverOverlayCanvas.overrideSorting = true;
            hoverOverlayCanvas.sortingOrder = HoverOverlaySortingOrder;

            CanvasScaler sourceScaler = sourceCanvas.GetComponent<CanvasScaler>();
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            if (sourceScaler != null)
            {
                scaler.uiScaleMode = sourceScaler.uiScaleMode;
                scaler.referenceResolution = sourceScaler.referenceResolution;
                scaler.screenMatchMode = sourceScaler.screenMatchMode;
                scaler.matchWidthOrHeight = sourceScaler.matchWidthOrHeight;
                scaler.referencePixelsPerUnit = sourceScaler.referencePixelsPerUnit;
            }
            return hoverOverlayCanvas;
        }

        private void RefreshKickOverlayRect()
        {
            if (slotRect == null || kickOverlayRect == null || hoverOverlayCanvas == null) return;

            Vector3[] corners = new Vector3[4];
            slotRect.GetWorldCorners(corners);
            RectTransform canvasRect = hoverOverlayCanvas.transform as RectTransform;
            Vector2 min = new(float.MaxValue, float.MaxValue);
            Vector2 max = new(float.MinValue, float.MinValue);
            for (int i = 0; i < corners.Length; i++)
            {
                Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(sourceCanvasCamera, corners[i]);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect, screenPoint, null, out Vector2 localPoint);
                min = Vector2.Min(min, localPoint);
                max = Vector2.Max(max, localPoint);
            }

            kickOverlayRect.anchoredPosition = (min + max) * 0.5f;
            kickOverlayRect.sizeDelta = max - min;
        }

        private void ShowPrefab(GameObject prefab, int colorIndex, bool isConnected)
        {
            if (avatarImage == null || prefab == null)
            {
                if (prefab == null)
                    Debug.LogWarning("[LobbyPlayerSlotView] Player prefab is not assigned.", this);
                return;
            }

            if (currentPrefab != prefab || previewInstance == null)
            {
                if (previewInstance != null) Destroy(previewInstance);

                // 비활성 부모 아래에서 생성해 Awake/OnEnable 기반 게임플레이 진입을 막는다.
                GameObject avatarRoot = avatarImage.gameObject;
                bool wasActive = avatarRoot.activeSelf;
                avatarRoot.SetActive(false);
                previewInstance = Instantiate(prefab, avatarImage.transform, false);
                previewInstance.name = $"{prefab.name} Preview";
                DisableGameplayComponents(previewInstance);
                avatarRoot.SetActive(wasActive);
                currentPrefab = prefab;
            }

            previewInstance.SetActive(true);
            ConfigureIdleAnimation(previewInstance);
            PlayerColorPalette.ApplyToSpriteRenderers(previewInstance, colorIndex, isConnected ? 1f : 0.35f);
            SetPreviewSortingOrder();
            ApplyPreviewTransform();
        }

        private void ApplyPreviewTransform()
        {
            if (previewInstance == null) return;

            previewInstance.transform.localPosition = PreviewLocalPosition;
            previewInstance.transform.localScale = Vector3.one * PreviewScale;
        }

        private void SetPreviewSortingOrder()
        {
            if (previewInstance == null) return;

            foreach (SpriteRenderer spriteRenderer in
                     previewInstance.GetComponentsInChildren<SpriteRenderer>(true))
                spriteRenderer.sortingOrder = PreviewSortingOrder;
        }

        private static void DisableGameplayComponents(GameObject root)
        {
            foreach (MonoBehaviour behaviour in
                     root.GetComponentsInChildren<MonoBehaviour>(true))
                behaviour.enabled = false;

            foreach (Rigidbody2D body in root.GetComponentsInChildren<Rigidbody2D>(true))
                body.simulated = false;
            foreach (Collider2D collider in root.GetComponentsInChildren<Collider2D>(true))
                collider.enabled = false;
            foreach (AudioSource source in root.GetComponentsInChildren<AudioSource>(true))
                source.enabled = false;
        }

        private void ConfigureIdleAnimation(GameObject root)
        {
            Animator animator = root.GetComponentInChildren<Animator>(true);
            if (animator == null) return;

            animator.enabled = true;
            animator.Rebind();
            animator.SetFloat("Speed", 0f);
            animator.SetFloat("YVelocity", 0f);
            animator.SetBool("Grounded", true);
            animator.SetBool("Climbing", false);
            animator.SetBool("Dead", false);
            animator.Play("Idle", 0, 0f);
            animator.Update(0f);
        }

    }
}
