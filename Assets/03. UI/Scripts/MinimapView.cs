using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace TerrainCollapse
{
    /// <summary>미니맵 텍스처와 플레이어 마커를 표시한다.</summary>
    public sealed class MinimapView : MonoBehaviour
    {
        [SerializeField] private RawImage mapImage;
        [SerializeField] private RectTransform playerMarker;
        [Tooltip("비워 두면 Local Player Marker를 복제해 원격 마커로 사용합니다.")]
        [SerializeField] private RectTransform remotePlayerMarkerPrefab;
        [Tooltip("비워 두면 Local Player Marker와 같은 부모에 생성합니다.")]
        [SerializeField] private RectTransform remotePlayerMarkerRoot;
        [SerializeField, Min(1)] private int visibleRadius = 6;
        [SerializeField] private RectTransform mapFrame;
        [SerializeField] private GameObject fullMapDimmer;
        [SerializeField] private Vector2 fullMapSize = new(900f, 900f);
        [SerializeField, Min(0.1f)] private float fullMapZoomStep = 0.25f;
        [SerializeField, Min(1f)] private float minFullMapZoom = 1f;
        [SerializeField, Min(1f)] private float maxFullMapZoom = 5f;
        [SerializeField, Min(0.1f)] private float fullMapPanMultiplier = 2.5f;

        private const float SCROLL_THRESHOLD = 3.5f; // 지도 확대/축소 한 단계로 판정할 휠 누적 델타 기준값

        private readonly Dictionary<string, RectTransform> remotePlayerMarkers = new();
        private Texture2D mapTexture;
        private int mapWidth;
        private int mapHeight;
        private Rect currentViewUvRect;
        private Vector2 normalMapFrameAnchoredPosition;
        private Vector2 normalMapFrameSize;
        private Vector2 normalMapFrameAnchorMin;
        private Vector2 normalMapFrameAnchorMax;
        private Vector2 normalMapFramePivot;
        private RectTransform mapRect;
        private Vector2 normalMapAnchorMin;
        private Vector2 normalMapAnchorMax;
        private Vector2 normalMapPivot;
        private Vector2 normalMapAnchoredPosition;
        private Vector2 normalMapSize;
        private Vector2 playerNormalizedPosition;
        private Vector2 fullMapPan;
        private float fullMapZoom = 1f;
        private float fullMapZoomAccumulator;
        private bool hasPlayerNormalizedPosition;
        private bool isFullMapOpen;

        public bool IsFullMapOpen => isFullMapOpen;

        private void Awake()
        {
            mapFrame ??= transform.parent as RectTransform;
            mapRect = mapImage != null ? mapImage.rectTransform : null;

            if (mapFrame != null)
            {
                normalMapFrameAnchoredPosition = mapFrame.anchoredPosition;
                normalMapFrameSize = mapFrame.sizeDelta;
                normalMapFrameAnchorMin = mapFrame.anchorMin;
                normalMapFrameAnchorMax = mapFrame.anchorMax;
                normalMapFramePivot = mapFrame.pivot;
                if (mapFrame.GetComponent<RectMask2D>() == null)
                    mapFrame.gameObject.AddComponent<RectMask2D>();
            }

            if (mapRect != null)
            {
                normalMapAnchorMin = mapRect.anchorMin;
                normalMapAnchorMax = mapRect.anchorMax;
                normalMapPivot = mapRect.pivot;
                normalMapAnchoredPosition = mapRect.anchoredPosition;
                normalMapSize = mapRect.sizeDelta;
            }

            fullMapDimmer?.SetActive(false);
        }

        private void Update()
        {
            if (Keyboard.current?.mKey.wasPressedThisFrame == true)
                ToggleFullMap();

            if (Keyboard.current?.escapeKey.wasPressedThisFrame == true && isFullMapOpen)
                ToggleFullMap();

            if (isFullMapOpen)
                HandleFullMapInput();
        }

        public bool Initialize(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                Debug.LogError($"[UI] 미니맵 크기가 잘못되었습니다: {width}x{height}", this);
                return false;
            }

            if (mapImage == null)
            {
                Debug.LogError("[UI] MinimapView의 Map Image가 연결되지 않았습니다.", this);
                return false;
            }

            ReleaseTexture();

            mapWidth = width;
            mapHeight = height;
            mapTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "Runtime Terrain Minimap",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            mapImage.texture = mapTexture;
            currentViewUvRect = new Rect(0f, 0f, 1f, 1f);
            mapImage.uvRect = currentViewUvRect;
            SetPlayerVisible(false);
            ClearRemotePlayerMarkers();
            return true;
        }

        public void ApplyPixels(Color32[] pixels)
        {
            if (mapTexture == null) return;
            if (pixels == null) return;

            int expectedLength = mapWidth * mapHeight;
            if ((pixels.Length == expectedLength) == false)
            {
                Debug.LogError(
                    $"[UI] 미니맵 픽셀 수가 맞지 않습니다: expected={expectedLength}, actual={pixels.Length}",
                    this);
                return;
            }

            mapTexture.SetPixels32(pixels);
            mapTexture.Apply(false, false);
        }

        public void SetPlayerCell(Vector2Int cell)
        {
            if (mapWidth <= 0 || mapHeight <= 0)
            {
                SetPlayerVisible(false);
                return;
            }

            Vector2 normalizedPosition = new((cell.x + 0.5f) / mapWidth, (cell.y + 0.5f) / mapHeight);

            SetPlayerNormalizedPosition(normalizedPosition);
        }

        public void SetPlayerNormalizedPosition(Vector2 normalizedPosition)
        {
            bool isInside = normalizedPosition.x >= 0f && normalizedPosition.x <= 1f &&
                            normalizedPosition.y >= 0f && normalizedPosition.y <= 1f;

            if (isInside == false)
            {
                SetPlayerVisible(false);
                return;
            }

            playerNormalizedPosition = normalizedPosition;
            hasPlayerNormalizedPosition = true;
            UpdateViewUvRect();

            float markerX = (normalizedPosition.x - currentViewUvRect.x) / currentViewUvRect.width;
            float markerY = (normalizedPosition.y - currentViewUvRect.y) / currentViewUvRect.height;
            Vector2 markerAnchor = new(Mathf.Clamp01(markerX), Mathf.Clamp01(markerY));

            playerMarker.anchorMin = markerAnchor;
            playerMarker.anchorMax = markerAnchor;
            playerMarker.anchoredPosition = Vector2.zero;

            SetPlayerVisible(true);
        }

        public void SetPlayerMarkerColor(Color color)
        {
            if (playerMarker != null && playerMarker.TryGetComponent(out Image image))
                image.color = color;
        }

        /// <summary>
        /// 원격 플레이어를 현재 미니맵 뷰에 맞춰 표시한다.
        /// 전체 맵에서는 전체 UV가 사용되므로 모든 맵 내부 원격 플레이어가 보인다.
        /// </summary>
        public void SetRemotePlayerNormalizedPosition(
            string playerId,
            Vector2 normalizedPosition,
            Color color)
        {
            if (string.IsNullOrWhiteSpace(playerId)) return;

            bool isInsideMap = normalizedPosition.x >= 0f && normalizedPosition.x <= 1f &&
                               normalizedPosition.y >= 0f && normalizedPosition.y <= 1f;
            RectTransform marker = GetOrCreateRemotePlayerMarker(playerId);
            if (marker == null) return;
            if (marker.TryGetComponent(out Image image)) image.color = color;

            bool isInsideView = isInsideMap && currentViewUvRect.Contains(normalizedPosition);
            marker.gameObject.SetActive(isInsideView);
            if (isInsideView == false) return;

            float markerX = (normalizedPosition.x - currentViewUvRect.x) / currentViewUvRect.width;
            float markerY = (normalizedPosition.y - currentViewUvRect.y) / currentViewUvRect.height;
            Vector2 markerAnchor = new(markerX, markerY);
            marker.anchorMin = markerAnchor;
            marker.anchorMax = markerAnchor;
            marker.anchoredPosition = Vector2.zero;
        }

        public void RemoveRemotePlayerMarker(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId) ||
                remotePlayerMarkers.Remove(playerId, out RectTransform marker) == false)
                return;

            if (marker != null) Destroy(marker.gameObject);
        }

        public void ToggleFullMap()
        {
            SetFullMapOpen(isFullMapOpen == false);
        }

        private void SetFullMapOpen(bool isOpen)
        {
            if (mapFrame == null) return;

            isFullMapOpen = isOpen;
            fullMapZoomAccumulator = 0f;
            fullMapDimmer?.SetActive(isFullMapOpen);

            if (isFullMapOpen)
            {
                fullMapZoom = minFullMapZoom;
                fullMapPan = Vector2.zero;
                mapFrame.anchorMin = new Vector2(0.5f, 0.5f);
                mapFrame.anchorMax = new Vector2(0.5f, 0.5f);
                mapFrame.pivot = new Vector2(0.5f, 0.5f);
                mapFrame.anchoredPosition = Vector2.zero;
                mapFrame.sizeDelta = fullMapSize;

                ApplyFullMapTransform();
                mapFrame.SetAsLastSibling();
            }
            else
            {
                mapFrame.anchorMin = normalMapFrameAnchorMin;
                mapFrame.anchorMax = normalMapFrameAnchorMax;
                mapFrame.pivot = normalMapFramePivot;
                mapFrame.anchoredPosition = normalMapFrameAnchoredPosition;
                mapFrame.sizeDelta = normalMapFrameSize;

                RestoreMinimapTransform();
            }

            if (hasPlayerNormalizedPosition) SetPlayerNormalizedPosition(playerNormalizedPosition);
            else UpdateViewUvRect();
        }

        private void HandleFullMapInput()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || mapRect == null) return;

            fullMapZoomAccumulator += Mathf.Clamp(mouse.scroll.ReadValue().y, -1f, 1f);
            bool hasZoomChanged = false;

            while (Mathf.Abs(fullMapZoomAccumulator) >= SCROLL_THRESHOLD)
            {
                int scrollDirection = fullMapZoomAccumulator > 0f ? 1 : -1;
                fullMapZoomAccumulator -= scrollDirection * SCROLL_THRESHOLD;

                if (ZoomFullMapAtCursor(
                        scrollDirection * fullMapZoomStep,
                        mouse.position.ReadValue()))
                    hasZoomChanged = true;
            }

            if (hasZoomChanged)
            {
                ApplyFullMapTransform();
            }

            if (mouse.middleButton.isPressed == false) return;

            Canvas canvas = GetComponentInParent<Canvas>();
            float scaleFactor = canvas != null ? canvas.scaleFactor : 1f;
            fullMapPan += mouse.delta.ReadValue() * (fullMapZoom * fullMapPanMultiplier) / scaleFactor;
            ClampFullMapPan();
            ApplyFullMapTransform();
        }

        private bool ZoomFullMapAtCursor(float zoomDelta, Vector2 screenPosition)
        {
            float previousZoom = fullMapZoom;
            fullMapZoom = Mathf.Clamp(
                fullMapZoom + zoomDelta,
                minFullMapZoom,
                Mathf.Max(minFullMapZoom, maxFullMapZoom));

            if (Mathf.Approximately(previousZoom, fullMapZoom))
                return false;

            if (TryGetCursorLocalPosition(screenPosition, out Vector2 cursorLocal))
            {
                float zoomRatio = fullMapZoom / previousZoom;
                fullMapPan = cursorLocal - (cursorLocal - fullMapPan) * zoomRatio;
                ClampFullMapPan();
            }

            return true;
        }

        private bool TryGetCursorLocalPosition(
            Vector2 screenPosition,
            out Vector2 cursorLocal)
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            Camera eventCamera = canvas != null &&
                                 canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;

            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                mapFrame,
                screenPosition,
                eventCamera,
                out cursorLocal);
        }

        private void ApplyFullMapTransform()
        {
            if (mapRect == null) return;

            mapRect.anchorMin = new Vector2(0.5f, 0.5f);
            mapRect.anchorMax = new Vector2(0.5f, 0.5f);
            mapRect.pivot = new Vector2(0.5f, 0.5f);
            mapRect.sizeDelta = GetFullMapContentSize() * fullMapZoom;
            mapRect.anchoredPosition = fullMapPan;
        }

        private void RestoreMinimapTransform()
        {
            if (mapRect == null) return;

            mapRect.anchorMin = normalMapAnchorMin;
            mapRect.anchorMax = normalMapAnchorMax;
            mapRect.pivot = normalMapPivot;
            mapRect.anchoredPosition = normalMapAnchoredPosition;
            mapRect.sizeDelta = normalMapSize;
        }

        private void ClampFullMapPan()
        {
            Vector2 contentSize = GetFullMapContentSize();
            Vector2 mapSize = contentSize * fullMapZoom;
            Vector2 maxPan = (mapSize - contentSize) * 0.5f;

            fullMapPan.x = Mathf.Clamp(fullMapPan.x, -maxPan.x, maxPan.x);
            fullMapPan.y = Mathf.Clamp(fullMapPan.y, -maxPan.y, maxPan.y);
        }

        private Vector2 GetFullMapContentSize()
        {
            return fullMapSize + normalMapSize;
        }

        private void UpdateViewUvRect()
        {
            if (mapImage == null) return;
            if (mapWidth <= 0 || mapHeight <= 0) return;

            if (isFullMapOpen)
            {
                currentViewUvRect = new Rect(0f, 0f, 1f, 1f);
                mapImage.uvRect = currentViewUvRect;
                return;
            }

            float visibleCellCount = visibleRadius * 2 + 1;

            float viewWidth = Mathf.Min(1f, visibleCellCount / mapWidth);
            float viewHeight = Mathf.Min(1f, visibleCellCount / mapHeight);

            float viewX = Mathf.Clamp(playerNormalizedPosition.x - viewWidth * 0.5f, 0f, 1f - viewWidth);
            float viewY = Mathf.Clamp(playerNormalizedPosition.y - viewHeight * 0.5f, 0f, 1f - viewHeight);

            currentViewUvRect = new Rect(viewX, viewY, viewWidth, viewHeight);
            mapImage.uvRect = currentViewUvRect;
        }

        private RectTransform GetOrCreateRemotePlayerMarker(string playerId)
        {
            if (remotePlayerMarkers.TryGetValue(playerId, out RectTransform existing) && existing != null)
                return existing;

            RectTransform prefab = remotePlayerMarkerPrefab != null
                ? remotePlayerMarkerPrefab
                : playerMarker;
            RectTransform root = remotePlayerMarkerRoot != null
                ? remotePlayerMarkerRoot
                : playerMarker != null ? playerMarker.parent as RectTransform : null;
            if (prefab == null || root == null) return null;

            RectTransform marker = Instantiate(prefab, root);
            marker.name = $"RemotePlayerMarker_{playerId}";
            marker.gameObject.SetActive(true);
            remotePlayerMarkers[playerId] = marker;
            return marker;
        }

        private void ClearRemotePlayerMarkers()
        {
            foreach (RectTransform marker in remotePlayerMarkers.Values)
                if (marker != null) Destroy(marker.gameObject);

            remotePlayerMarkers.Clear();
        }

        public void SetPlayerVisible(bool visible)
        {
            if (playerMarker == null) return;

            playerMarker.gameObject.SetActive(visible);
        }

        private void OnDestroy()
        {
            ClearRemotePlayerMarkers();
            ReleaseTexture();
        }

        private void ReleaseTexture()
        {
            if (mapTexture == null) return;

            Destroy(mapTexture);
            mapTexture = null;
        }
    }

}
