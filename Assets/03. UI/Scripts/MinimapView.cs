using UnityEngine;
using UnityEngine.UI;

namespace TerrainCollapse
{
    /// <summary>미니맵 텍스처와 로컬 플레이어 마커를 표시한다.</summary>
    public sealed class MinimapView : MonoBehaviour
    {
        [SerializeField] private RawImage mapImage;
        [SerializeField] private RectTransform playerMarker;
        [SerializeField, Min(1)] private int visibleRadius = 6;

        private Texture2D mapTexture;
        private int mapWidth;
        private int mapHeight;
        private Rect currentViewUvRect;

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

            UpdateViewUvRect(normalizedPosition);

            float markerX = (normalizedPosition.x - currentViewUvRect.x) / currentViewUvRect.width;
            float markerY = (normalizedPosition.y - currentViewUvRect.y) / currentViewUvRect.height;
            Vector2 markerAnchor = new(Mathf.Clamp01(markerX), Mathf.Clamp01(markerY));

            playerMarker.anchorMin = markerAnchor;
            playerMarker.anchorMax = markerAnchor;
            playerMarker.anchoredPosition = Vector2.zero;

            SetPlayerVisible(true);
        }

        private void UpdateViewUvRect(Vector2 normalizedPosition)
        {
            float visibleCellCount = visibleRadius * 2 + 1;

            float viewWidth = Mathf.Min(1f, visibleCellCount / mapWidth);
            float viewHeight = Mathf.Min(1f, visibleCellCount / mapHeight);

            float viewX = Mathf.Clamp(normalizedPosition.x - viewWidth * 0.5f, 0f, 1f - viewWidth);
            float viewY = Mathf.Clamp(normalizedPosition.y - viewHeight * 0.5f, 0f, 1f - viewHeight);

            currentViewUvRect = new Rect(
                viewX,
                viewY,
                viewWidth,
                viewHeight);

            mapImage.uvRect = currentViewUvRect;
        }

        public void SetPlayerVisible(bool visible)
        {
            if (playerMarker == null) return;

            playerMarker.gameObject.SetActive(visible);
        }

        private void OnDestroy()
        {
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
