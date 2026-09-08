using TerrainCollapse;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace WorldEnvironment
{
    /// <summary>확정된 스폰 구역에 배경과 울타리 표현만 맞춘다. 충돌은 기존 지형이 소유한다.</summary>
    public sealed class MiningVillageBackdrop : MonoBehaviour
    {
        [SerializeField] private RoomClient roomClient;
        [SerializeField] private SpriteRenderer[] panoramaPanels;
        [SerializeField] private SpriteRenderer leftFence;
        [SerializeField] private SpriteRenderer rightFence;
        [SerializeField] private SpriteRenderer upperFence;
        [SerializeField] private SpriteRenderer platformDeck;
        [SerializeField] private Light2D surfaceLight;
        [SerializeField, Min(1f)] private float panoramaWidth = 28f;

        public void Configure(RoomClient source, SpriteRenderer[] panels,
            SpriteRenderer left, SpriteRenderer right, SpriteRenderer upper,
            SpriteRenderer deck, Light2D daylight)
        {
            Unsubscribe();
            roomClient = source;
            panoramaPanels = panels;
            leftFence = left;
            rightFence = right;
            upperFence = upper;
            platformDeck = deck;
            surfaceLight = daylight;
            if (Application.isPlaying && isActiveAndEnabled) Subscribe();
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (roomClient == null) return;
            roomClient.TerrainSnapshotLoaded -= ApplySnapshot;
            roomClient.TerrainSnapshotLoaded += ApplySnapshot;
            if (roomClient.CurrentTerrainSnapshot != null)
                ApplySnapshot(roomClient.CurrentTerrainSnapshot);
        }

        private void Unsubscribe()
        {
            if (roomClient != null) roomClient.TerrainSnapshotLoaded -= ApplySnapshot;
        }

        /// <summary>기존 스냅샷의 안전 구역과 같은 셀 위치에 표시한다.</summary>
        public void ApplySnapshot(TerrainSnapshot snapshot)
        {
            if (snapshot == null || snapshot.CellSize <= 0f) return;
            RectInt area = snapshot.SpawnArea;
            if (area.width < 3 || area.height < 2)
            {
                SetFencesVisible(false);
                return;
            }

            float cell = snapshot.CellSize;
            float left = snapshot.Origin.x + area.xMin * cell;
            float bottom = snapshot.Origin.y + area.yMin * cell;
            float width = area.width * cell;
            float height = area.height * cell;
            float center = left + width * 0.5f;

            if (panoramaPanels != null)
            {
                for (int i = 0; i < panoramaPanels.Length; i++)
                {
                    SpriteRenderer panel = panoramaPanels[i];
                    if (panel == null || panel.sprite == null) continue;
                    panel.flipX = i != 1;
                    panel.color = new Color(0.78f, 0.78f, 0.78f, 1f);
                    float panelWidth = panoramaWidth * cell;
                    float panelHeight = panelWidth * panel.sprite.bounds.size.y / panel.sprite.bounds.size.x;
                    panel.transform.position = new Vector3(center + (i - 1) * panelWidth,
                        bottom + panelHeight * 0.5f, 0f);
                    panel.transform.localScale = new Vector3(panelWidth / panel.sprite.bounds.size.x,
                        panelHeight / panel.sprite.bounds.size.y, 1f);
                }
            }

            PlaceFence(leftFence, new Vector2(left + cell * 0.5f, bottom + (height - cell) * 0.5f),
                new Vector2(cell, height - cell), false);
            PlaceFence(rightFence, new Vector2(left + width - cell * 0.5f, bottom + (height - cell) * 0.5f),
                new Vector2(cell, height - cell), false);
            PlaceFence(upperFence, new Vector2(center, bottom + height - cell * 0.5f),
                new Vector2(cell, width), true);
            SetFencesVisible(true);
            PlacePlatform(snapshot);
            if (surfaceLight != null)
            {
                surfaceLight.transform.position = new Vector3(center, bottom, 0f);
                surfaceLight.SetShapePath(new[]
                {
                    new Vector3(-width * 0.5f, 0f), new Vector3(width * 0.5f, 0f),
                    new Vector3(width * 0.5f, height), new Vector3(-width * 0.5f, height)
                });
            }
        }

        private void PlacePlatform(TerrainSnapshot snapshot)
        {
            if (platformDeck == null) return;
            int minX = int.MaxValue;
            int maxX = int.MinValue;
            foreach (TerrainCellChange cell in snapshot.Cells)
            {
                if (cell.Coord.y != snapshot.SpawnArea.yMin || cell.Type != TerrainTileType.SpawnPlatform) continue;
                minX = Mathf.Min(minX, cell.Coord.x);
                maxX = Mathf.Max(maxX, cell.Coord.x);
            }
            platformDeck.enabled = maxX >= minX;
            if (platformDeck.enabled == false) return;
            float size = snapshot.CellSize;
            PlaceFence(platformDeck, snapshot.Origin + new Vector2((minX + maxX + 1) * 0.5f * size,
                (snapshot.SpawnArea.yMin + 0.5f) * size), new Vector2(size, (maxX - minX + 1) * size), true);
        }

        private static void PlaceFence(SpriteRenderer renderer, Vector2 center, Vector2 size, bool horizontal)
        {
            if (renderer == null) return;
            renderer.transform.position = new Vector3(center.x, center.y, 0f);
            renderer.transform.rotation = Quaternion.Euler(0f, 0f, horizontal ? 90f : 0f);
            renderer.transform.localScale = Vector3.one;
            renderer.drawMode = SpriteDrawMode.Tiled;
            renderer.tileMode = SpriteTileMode.Continuous;
            renderer.size = size;
        }

        private void SetFencesVisible(bool visible)
        {
            if (leftFence != null) leftFence.enabled = visible;
            if (rightFence != null) rightFence.enabled = visible;
            if (upperFence != null) upperFence.enabled = visible;
            if (visible == false && platformDeck != null) platformDeck.enabled = false;
            if (surfaceLight != null) surfaceLight.enabled = visible;
        }
    }
}
