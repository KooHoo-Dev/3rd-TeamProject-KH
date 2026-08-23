#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace TerrainCollapse
{
    // 에디터용 개발 지형 맵 생성, 페인팅과 JSON 파일 저장
    
    /// <summary>디버깅용 JSON 맵을 생성하며 추후 시드 기반 생성 규칙을 순서대로 합성한다.</summary>
    public sealed class TerrainMapBuilder : MonoBehaviour
    {
        [SerializeField] private TerrainMapGenerationSettings settings = new();
        [SerializeField] private List<TerrainMapGenerationRule> generationRules = new();
        [SerializeField] private string outputPath = "Assets/TerrainCollapseGenerated/DebugTerrainMap.json";
        [Header("Data Table Generation")]
        [SerializeField] private string dataTableProfileID = "Default";
        [SerializeField] private bool useRandomDataTableSeed;
        [SerializeField] private int dataTableSeed = 12345;
        [Header("Map Painter")]
        [SerializeField] private TerrainGridWorld targetWorld;
        [SerializeField] private TerrainManager terrainManager;
        [SerializeField] private Camera painterCamera;
        [SerializeField] private TerrainTileType paintType = TerrainTileType.Ground;
        [SerializeField] private Color gridColor = new(1f, 1f, 1f, 0.2f);
        [SerializeField] private bool showPainterPanel = true;

        private readonly Rect painterPanelRect = new(10f, 10f, 270f, 250f);
        private TerrainMapFile editingMap;
        private GameObject gridOverlay;
        private Mesh gridMesh;
        private Material gridMaterial;
        private Vector2Int lastPaintedCell = new(int.MinValue, int.MinValue);
        private bool painterMode;
        private float previousTimeScale = 1f;
        private bool previousTerrainManagerEnabled;

        public void Configure(
            TerrainGridWorld world,
            TerrainManager manager,
            Camera camera)
        {
            targetWorld = world;
            terrainManager = manager;
            painterCamera = camera;
        }

        private void Update()
        {
            if (painterMode == false || Mouse.current == null) return;
            Vector2 pointer = Mouse.current.position.ReadValue();
            Vector2 guiPointer = new(pointer.x, Screen.height - pointer.y);
            if (showPainterPanel && painterPanelRect.Contains(guiPointer)) return;

            bool painting = Mouse.current.leftButton.isPressed;
            bool erasing = Mouse.current.rightButton.isPressed;
            if (painting == false && erasing == false)
            {
                lastPaintedCell = new Vector2Int(int.MinValue, int.MinValue);
                return;
            }

            Vector3 worldPosition = painterCamera.ScreenToWorldPoint(
                new Vector3(pointer.x, pointer.y,
                    Mathf.Abs(targetWorld.transform.position.z - painterCamera.transform.position.z)));
            Vector2Int cell = targetWorld.WorldToCell(worldPosition);
            if (IsInsideMap(cell) == false || cell == lastPaintedCell) return;

            if (erasing) targetWorld.RemoveCell(cell);
            else targetWorld.SetCell(cell, paintType);
            lastPaintedCell = cell;
        }

        private void OnGUI()
        {
            if (showPainterPanel == false) return;
            GUILayout.BeginArea(painterPanelRect, GUI.skin.box);
            GUILayout.Label("Terrain Map Painter");

            if (painterMode == false)
            {
                if (GUILayout.Button("Begin Painter Mode")) BeginPainterMode();
                GUILayout.EndArea();
                return;
            }

            GUILayout.Label($"Map: {editingMap.Name} ({editingMap.Width} x {editingMap.Height})");
            GUILayout.Label($"Paint Type: {paintType}");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Previous Type")) SelectPreviousPaintType();
            if (GUILayout.Button("Next Type")) SelectNextPaintType();
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Create Empty Grid")) CreateEmptyEditableMap();
            if (GUILayout.Button("Generate From Rules")) GenerateEditableMap();
            if (GUILayout.Button("Generate From Data Table")) GenerateDataTableMap();
            if (GUILayout.Button("Save Current Grid To JSON")) SavePaintedMapJson();
            if (GUILayout.Button("End Painter Mode")) EndPainterMode();
            GUILayout.Label("Left Drag: Paint / Right Drag: Erase");
            GUILayout.EndArea();
        }

        [ContextMenu("Begin Painter Mode")]
        public void BeginPainterMode()
        {
            if (editingMap == null) CreateEmptyEditableMap();
            painterMode = true;
            previousTimeScale = Time.timeScale;
            previousTerrainManagerEnabled = terrainManager.enabled;
            Time.timeScale = 0f;
            terrainManager.enabled = false;
            gridOverlay.SetActive(true);
        }

        [ContextMenu("End Painter Mode")]
        public void EndPainterMode()
        {
            painterMode = false;
            terrainManager.enabled = previousTerrainManagerEnabled;
            Time.timeScale = previousTimeScale;
            gridOverlay.SetActive(false);
        }

        [ContextMenu("Create Empty Editable Map")]
        public void CreateEmptyEditableMap()
        {
            int seed = settings.UseRandomSeed ? Environment.TickCount : settings.Seed;
            editingMap = TerrainSeedGenerator.CreateEmptyMap(settings, seed);
            targetWorld.LoadMap(editingMap);
            RebuildGridOverlay();
        }

        [ContextMenu("Generate Editable Map From Rules")]
        public void GenerateEditableMap()
        {
            editingMap = TerrainSeedGenerator.Generate(settings, generationRules);
            targetWorld.LoadMap(editingMap);
            RebuildGridOverlay();
        }

        [ContextMenu("Generate Editable Map From Data Table")]
        public void GenerateDataTableMap()
        {
            if (Application.isPlaying == false)
            {
                Debug.LogWarning("[Terrain Map Builder] 데이터 테이블 맵 생성은 Play Mode에서 실행해야 합니다", this);
                return;
            }

            int seed = useRandomDataTableSeed ? Environment.TickCount : dataTableSeed;
            editingMap = TerrainSeedGenerator.Generate(dataTableProfileID, seed);
            targetWorld.LoadMap(editingMap);
            RebuildGridOverlay();

            Debug.Log(
                $"[Terrain Map Builder] 데이터 테이블 맵 생성 완료: " +
                $"profile={dataTableProfileID}, seed={seed}",
                this);
        }

        [ContextMenu("Save Painted Map JSON")]
        public void SavePaintedMapJson()
        {
            TerrainMapFile map = CaptureCurrentGrid();
            SaveMapJson(map);
        }

        [ContextMenu("Generate Debug Map JSON")]
        public void GenerateDebugMapJson()
        {
            TerrainMapFile map = TerrainSeedGenerator.Generate(settings, generationRules);
            SaveMapJson(map);
        }

        private void SaveMapJson(TerrainMapFile map)
        {
            string path = ResolveOutputPath(outputPath);
            string directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory) == false) Directory.CreateDirectory(directory);
            File.WriteAllText(path, map.ToJson());

#if UNITY_EDITOR
            AssetDatabase.Refresh();
#endif
            Debug.Log($"[Terrain Map Builder] JSON 생성 완료: {path}, seed={map.Seed}", this);
        }

        private TerrainMapFile CaptureCurrentGrid()
        {
            TerrainMapFile map = CreateEmptyCaptureMap();
            foreach (Vector2Int coord in targetWorld.GetOccupiedCells())
            {
                if (IsInsideMap(coord) == false) continue;
                TerrainTileType type = targetWorld.Data.GetCellOrNull(coord).Type;
                map.SetTile(coord, type);
            }
            editingMap = map;
            return map;
        }

        private TerrainMapFile CreateEmptyCaptureMap()
        {
            TerrainMapFile map = new()
            {
                Name = editingMap.Name,
                Seed = editingMap.Seed,
                Width = editingMap.Width,
                Height = editingMap.Height,
                CellSize = editingMap.CellSize,
                Origin = editingMap.Origin,
                Tiles = new List<TerrainTileType>(editingMap.Width * editingMap.Height)
            };

            for (int i = 0; i < editingMap.Width * editingMap.Height; i++)
                map.Tiles.Add(TerrainTileType.Empty);

            return map;
        }

        private bool IsInsideMap(Vector2Int cell)
            => cell.x >= 0 && cell.x < editingMap.Width && cell.y >= 0 && cell.y < editingMap.Height;

        private void SelectPreviousPaintType()
        {
            TerrainTileType[] values = (TerrainTileType[])Enum.GetValues(typeof(TerrainTileType));
            int index = Array.IndexOf(values, paintType);
            paintType = values[(index - 1 + values.Length) % values.Length];
            if (paintType == TerrainTileType.Empty) SelectPreviousPaintType();
        }

        private void SelectNextPaintType()
        {
            TerrainTileType[] values = (TerrainTileType[])Enum.GetValues(typeof(TerrainTileType));
            int index = Array.IndexOf(values, paintType);
            paintType = values[(index + 1) % values.Length];
            if (paintType == TerrainTileType.Empty) SelectNextPaintType();
        }

        private void RebuildGridOverlay()
        {
            EnsureGridOverlay();
            int lineCount = editingMap.Width + editingMap.Height + 2;
            Vector3[] vertices = new Vector3[lineCount * 2];
            int vertex = 0;
            float width = editingMap.Width * editingMap.CellSize;
            float height = editingMap.Height * editingMap.CellSize;

            for (int x = 0; x <= editingMap.Width; x++)
            {
                float position = x * editingMap.CellSize;
                vertices[vertex++] = new Vector3(position, 0f);
                vertices[vertex++] = new Vector3(position, height);
            }
            for (int y = 0; y <= editingMap.Height; y++)
            {
                float position = y * editingMap.CellSize;
                vertices[vertex++] = new Vector3(0f, position);
                vertices[vertex++] = new Vector3(width, position);
            }

            gridMesh.Clear();
            gridMesh.vertices = vertices;
            int[] indices = new int[vertices.Length];
            for (int i = 0; i < indices.Length; i++) indices[i] = i;
            gridMesh.SetIndices(indices, MeshTopology.Lines, 0);
            gridMesh.RecalculateBounds();
            gridOverlay.transform.position = new Vector3(editingMap.Origin.x, editingMap.Origin.y, -0.05f);
            gridOverlay.SetActive(painterMode);
        }

        private void EnsureGridOverlay()
        {
            if (gridOverlay != null) return;
            gridOverlay = new GameObject("MapPainterGrid", typeof(MeshFilter), typeof(MeshRenderer));
            gridOverlay.transform.SetParent(transform, false);
            gridMesh = new Mesh { name = "MapPainterGridMesh" };
            gridOverlay.GetComponent<MeshFilter>().sharedMesh = gridMesh;
            gridMaterial = new Material(Shader.Find("Sprites/Default")) { color = gridColor };
            MeshRenderer renderer = gridOverlay.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = gridMaterial;
            renderer.sortingOrder = 20;
        }

        private void OnDestroy()
        {
            if (painterMode) Time.timeScale = previousTimeScale;
            if (gridMesh != null) Destroy(gridMesh);
            if (gridMaterial != null) Destroy(gridMaterial);
        }

        private static string ResolveOutputPath(string configuredPath)
        {
#if UNITY_EDITOR
            if (Path.IsPathRooted(configuredPath)) return configuredPath;
            return Path.GetFullPath(configuredPath);
#else
            return Path.Combine(Application.persistentDataPath, Path.GetFileName(configuredPath));
#endif
        }
    }
}
#endif
