using UnityEngine;
using UnityEngine.UI;

namespace GameUI
{
    /// <summary>미니맵 텍스처와 로컬 플레이어 마커를 표시한다.</summary>
    public sealed class MinimapView : MonoBehaviour
    {
        [SerializeField] private RawImage mapImage;
        [SerializeField] private RectTransform playerMarker;

        private Texture2D mapTexture;
        private int mapWidth;
        private int mapHeight;

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
            if (playerMarker == null) return;

            bool isInside = cell.x >= 0 && cell.x < mapWidth &&
                            cell.y >= 0 && cell.y < mapHeight;

            if (isInside == false)
            {
                SetPlayerVisible(false);
                return;
            }

            float normalizedX = (cell.x + 0.5f) / mapWidth;
            float normalizedY = (cell.y + 0.5f) / mapHeight;
            Vector2 markerAnchor = new Vector2(normalizedX, normalizedY);

            playerMarker.anchorMin = markerAnchor;
            playerMarker.anchorMax = markerAnchor;
            playerMarker.anchoredPosition = Vector2.zero;
            SetPlayerVisible(true);
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
