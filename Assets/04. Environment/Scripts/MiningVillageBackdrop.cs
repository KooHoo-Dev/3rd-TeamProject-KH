using TerrainCollapse;
using UnityEngine;

namespace WorldEnvironment
{
    /// <summary>확정된 스폰 구역에 배경과 플랫폼 덱 표현만 맞춘다. 충돌은 기존 지형이 소유한다.</summary>
    public sealed class MiningVillageBackdrop : MonoBehaviour
    {
        [SerializeField] private RoomClient roomClient;
        [SerializeField] private SpriteRenderer[] panoramaPanels;
        [SerializeField] private SpriteRenderer platformDeck;
        [SerializeField, Min(1f)] private float panoramaWidth = 28f;

        public void Configure(RoomClient source, SpriteRenderer[] panels,
            SpriteRenderer deck)
        {
            Unsubscribe();
            roomClient = source;
            panoramaPanels = panels;
            platformDeck = deck;
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
                if (platformDeck != null) platformDeck.enabled = false;
                return;
            }

            float cell = snapshot.CellSize;
            float left = snapshot.Origin.x + area.xMin * cell;
            float bottom = snapshot.Origin.y + area.yMin * cell;
            float width = area.width * cell;
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
                        bottom - cell + panelHeight * 0.5f, 0f);
                    panel.transform.localScale = new Vector3(panelWidth / panel.sprite.bounds.size.x,
                        panelHeight / panel.sprite.bounds.size.y, 1f);
                }
            }

            PlacePlatform(snapshot);
        }

        private void PlacePlatform(TerrainSnapshot snapshot)
        {
            if (platformDeck == null) return;
            int minX = int.MaxValue;
            int maxX = int.MinValue;
            foreach (TerrainCellChange cell in snapshot.Cells)
            {
                if (cell.Coord.y != snapshot.SpawnArea.yMin - 1 || cell.Type != TerrainTileType.SpawnPlatform) continue;
                minX = Mathf.Min(minX, cell.Coord.x);
                maxX = Mathf.Max(maxX, cell.Coord.x);
            }
            platformDeck.enabled = maxX >= minX;
            if (platformDeck.enabled == false) return;
            float size = snapshot.CellSize;
            PlaceFence(platformDeck, snapshot.Origin + new Vector2((minX + maxX + 1) * 0.5f * size,
                (snapshot.SpawnArea.yMin - 0.5f) * size), new Vector2(size, (maxX - minX + 1) * size), true);
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

    }
}
