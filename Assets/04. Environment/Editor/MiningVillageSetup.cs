using System;
using System.IO;
using System.Text;
using TerrainCollapse;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace WorldEnvironment.Editor
{
    /// <summary>Unity API로 배치하고, 별도 오프라인 씬에서 실제 지형 충돌을 검사한다.</summary>
    [InitializeOnLoad]
    public static class MiningVillageSetup
    {
        private const string ScenePath = "Assets/01. Scenes/InGame.unity";
        private const string PreviewPath = "Assets/04. Environment/Scenes/MiningVillagePreview.unity";
        private const string PanoramaPath = "Assets/04. Environment/Art/MiningVillagePanorama.png";
        private const string FencePath = "Assets/04. Environment/Art/MiningVillageFence.png";
        private const string MaterialPath = "Assets/04. Environment/Art/MiningVillageUnlit.mat";
        private const string RootName = "Mining Village Environment";
        private static bool previewPending;
        private static double previewDeadline;

        static MiningVillageSetup()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        [MenuItem("Tools/Mining Village/1. Install in InGame")]
        public static void Install()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath) throw new InvalidOperationException("Open InGame before installing.");
            if (GameObject.Find(RootName) != null)
            {
                Debug.Log("[Mining Village] Already installed; existing environment was preserved.");
                return;
            }

            Sprite panorama = ImportSprite(PanoramaPath, 48f);
            Texture2D fenceTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(FencePath);
            if (fenceTexture == null) throw new InvalidOperationException("Fence texture has not been imported.");
            Sprite fence = ImportSprite(FencePath, fenceTexture.width);
            GameObject root = new(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Install mining village environment");
            SpriteRenderer[] panels = new SpriteRenderer[3];
            for (int i = 0; i < panels.Length; i++)
                panels[i] = CreateRenderer(root.transform, $"Distant Village {i + 1}", panorama, -30);
            MiningVillageBackdrop backdrop = root.AddComponent<MiningVillageBackdrop>();
            ConfigureSurface(backdrop, panels, fence);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = root;
            SceneView.lastActiveSceneView?.LookAt(new Vector3(0f, 5f, 0f), Quaternion.identity, 15f);
            Debug.Log("[Mining Village] InGame saved. Existing terrain collisions and shop references preserved.");
        }

        [MenuItem("Tools/Mining Village/2. Create Offline Preview")]
        public static void CreatePreview()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
            Scene source = SceneManager.GetActiveScene();
            if (source.path != ScenePath || GameObject.Find(RootName) == null)
                throw new InvalidOperationException("Install in InGame first.");
            if (File.Exists(PreviewPath))
                throw new InvalidOperationException("Preview already exists; open it without replacing it.");
            Directory.CreateDirectory(Path.GetDirectoryName(PreviewPath));
            EditorSceneManager.SaveScene(source, PreviewPath, true);
            Scene preview = EditorSceneManager.OpenScene(PreviewPath);
            RoomClient room = Object.FindFirstObjectByType<RoomClient>();
            if (room != null) room.gameObject.SetActive(false);
            foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                canvas.gameObject.SetActive(false);
            TerrainMapLoader loader = Object.FindFirstObjectByType<TerrainMapLoader>();
            if (loader == null) throw new InvalidOperationException("TerrainMapLoader is missing.");
            SerializedObject settings = new(loader);
            settings.FindProperty("useDataTableGeneration").boolValue = true;
            settings.FindProperty("dataTableProfileID").stringValue = "Default";
            settings.FindProperty("useRandomSeed").boolValue = false;
            settings.FindProperty("seed").intValue = 12345;
            settings.ApplyModifiedPropertiesWithoutUndo();
            loader.enabled = true;
            TerrainGridWorld world = Object.FindFirstObjectByType<TerrainGridWorld>();
            SerializedObject worldSettings = new(world);
            worldSettings.FindProperty("isDurabilityTextEnabled").boolValue = false;
            worldSettings.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(preview);
            EditorSceneManager.SaveScene(preview);
            AssetDatabase.Refresh();
            Selection.activeGameObject = GameObject.Find(RootName);
            SceneView.lastActiveSceneView?.LookAt(new Vector3(0f, 5f, 0f), Quaternion.identity, 15f);
            Debug.Log("[Mining Village] Offline preview saved. Press Play to verify boundaries and movement.");
        }

        [MenuItem("Tools/Mining Village/3. Open InGame %#&3")]
        public static void OpenInGame()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() == false) return;
            EditorSceneManager.OpenScene(ScenePath);
            Selection.activeGameObject = GameObject.Find(RootName);
            SceneView.lastActiveSceneView?.LookAt(new Vector3(0f, 5f, 0f), Quaternion.identity, 15f);
        }

        [MenuItem("Tools/Mining Village/4. Refresh Environment Rendering %#&4")]
        public static void RefreshRendering()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode first.");
            MiningVillageBackdrop backdrop = Object.FindFirstObjectByType<MiningVillageBackdrop>();
            if (backdrop == null) throw new InvalidOperationException("No mining village in this scene.");
            SpriteRenderer[] panels = new SpriteRenderer[3];
            for (int i = 0; i < panels.Length; i++)
                panels[i] = backdrop.transform.Find($"Distant Village {i + 1}").GetComponent<SpriteRenderer>();
            Texture2D fenceTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(FencePath);
            if (fenceTexture == null) throw new InvalidOperationException("Fence texture has not been imported.");
            ConfigureSurface(backdrop, panels, ImportSprite(FencePath, fenceTexture.width));
            foreach (SpriteRenderer renderer in backdrop.GetComponentsInChildren<SpriteRenderer>())
            {
                Undo.RecordObject(renderer, "Set outdoor environment material");
                renderer.sharedMaterial = GetEnvironmentMaterial();
            }
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        }

        private static void ConfigureSurface(MiningVillageBackdrop backdrop, SpriteRenderer[] panels,
            Sprite deckSprite)
        {
            SerializedObject backdropSettings = new(backdrop);
            backdropSettings.FindProperty("panoramaWidth").floatValue = 28f;
            backdropSettings.ApplyModifiedPropertiesWithoutUndo();
            Transform deckTransform = backdrop.transform.Find("Spawn Platform Deck");
            SpriteRenderer deck = deckTransform != null ? deckTransform.GetComponent<SpriteRenderer>() :
                CreateRenderer(backdrop.transform, "Spawn Platform Deck", deckSprite, 1);
            RoomClient room = Object.FindFirstObjectByType<RoomClient>(FindObjectsInactive.Include);
            backdrop.Configure(room, panels, deck);
            TerrainSnapshot layout = CreateLayoutSnapshot();
            backdrop.ApplySnapshot(layout);
            GameObject shop = GameObject.Find("Shop Sprite");
            SpriteRenderer shopRenderer = shop != null ? shop.GetComponent<SpriteRenderer>() : null;
            if (shopRenderer != null)
            {
                Undo.RecordObject(shop.transform, "Align shop with surface platform");
                float floor = layout.Origin.y + (layout.SpawnArea.yMin + 1) * layout.CellSize;
                shop.transform.position += Vector3.up * (floor - shopRenderer.bounds.min.y);
            }
        }

        [MenuItem("Tools/Mining Village/5. Capture Play Mode %#&5")]
        public static void CapturePlayMode()
        {
            if (EditorApplication.isPlaying == false || SceneManager.GetActiveScene().path != PreviewPath)
                throw new InvalidOperationException("Run the offline preview first.");
            Directory.CreateDirectory("Library/MiningVillageVerification");
            ScreenCapture.CaptureScreenshot("Library/MiningVillageVerification/play-mode.png");
            Debug.Log("[Mining Village] Capturing Game view to Library/MiningVillageVerification/play-mode.png");
        }

        private static Material GetEnvironmentMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null) return material;
            Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader == null) throw new InvalidOperationException("URP 2D unlit shader is missing.");
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, MaterialPath);
            return material;
        }

        private static Sprite ImportSprite(string path, float pixelsPerUnit)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException($"Missing texture: {path}");
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            TextureImporterSettings spriteSettings = new();
            importer.ReadTextureSettings(spriteSettings);
            spriteSettings.spriteMeshType = SpriteMeshType.FullRect;
            spriteSettings.spriteGenerateFallbackPhysicsShape = false;
            importer.SetTextureSettings(spriteSettings);
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static SpriteRenderer CreateRenderer(Transform parent, string name, Sprite sprite, int order)
        {
            GameObject child = new(name, typeof(SpriteRenderer));
            child.transform.SetParent(parent, false);
            SpriteRenderer renderer = child.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = order;
            renderer.sharedMaterial = GetEnvironmentMaterial();
            return renderer;
        }

        private static TerrainSnapshot CreateLayoutSnapshot()
        {
            TerrainSeedProfileTable profiles = new();
            profiles.Load();
            TerrainSeedProfileDef profile = profiles.Get("Default");
            TerrainSnapshot snapshot = new()
            {
                CellSize = profile.CellSize,
                Origin = new Vector2(profile.OriginX, profile.OriginY),
                SpawnArea = new RectInt((profile.Width - profile.TopOpeningWidth) / 2,
                    profile.Height, profile.TopOpeningWidth, profile.RespawnAreaHeight)
            };
            int start = snapshot.SpawnArea.xMin + profile.BoundaryThickness;
            int end = snapshot.SpawnArea.xMax - profile.BoundaryThickness;
            for (int x = start; x < end; x++)
                snapshot.Cells.Add(new TerrainCellChange(new Vector2Int(x, snapshot.SpawnArea.yMin),
                    TerrainTileType.SpawnPlatform));
            return snapshot;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            EditorApplication.update -= VerifyWhenReady;
            previewPending = state == PlayModeStateChange.EnteredPlayMode &&
                SceneManager.GetActiveScene().path == PreviewPath;
            if (previewPending == false) return;
            previewDeadline = EditorApplication.timeSinceStartup + 30d;
            EditorApplication.update += VerifyWhenReady;
        }

        private static void VerifyWhenReady()
        {
            if (previewPending == false || EditorApplication.isPlaying == false)
            {
                EditorApplication.update -= VerifyWhenReady;
                return;
            }
            TerrainMapLoader loader = Object.FindFirstObjectByType<TerrainMapLoader>();
            TerrainManager manager = Object.FindFirstObjectByType<TerrainManager>();
            if (loader?.LoadedMap == null || manager == null || manager.IsReady == false)
            {
                if (EditorApplication.timeSinceStartup < previewDeadline) return;
                EditorApplication.update -= VerifyWhenReady;
                Debug.LogError("[Mining Village] Offline terrain did not initialize within 30 seconds.");
                return;
            }
            previewPending = false;
            EditorApplication.update -= VerifyWhenReady;
            try
            {
                MiningVillageBackdrop backdrop = Object.FindFirstObjectByType<MiningVillageBackdrop>();
                TerrainSnapshot snapshot = TerrainNetworkAdapter.FromGeneratedMap(loader.LoadedMap);
                backdrop.ApplySnapshot(snapshot);
                Physics2D.SyncTransforms();
                VerifyCollisions(snapshot, backdrop);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static void VerifyCollisions(TerrainSnapshot snapshot, MiningVillageBackdrop backdrop)
        {
            PlayerController player = Object.FindFirstObjectByType<PlayerController>();
            Rigidbody2D body = player.GetComponent<Rigidbody2D>();
            BoxCollider2D box = player.GetComponent<BoxCollider2D>();
            PlayerCollisionMove movement = new(body, box, player.CollisionMask,
                player.CollisionSkin, 0.0001f, 0.01f);
            RectInt area = snapshot.SpawnArea;
            float cell = snapshot.CellSize;
            float left = snapshot.Origin.x + (area.xMin + 1) * cell;
            float right = snapshot.Origin.x + (area.xMax - 1) * cell;
            float bottom = snapshot.Origin.y + area.yMin * cell;
            float ceiling = snapshot.Origin.y + (area.yMax - 1) * cell;
            float center = (left + right) * 0.5f;
            StringBuilder report = new("Mining village — Unity Play Mode verification\n");
            Vector3 colliderOffset = box.transform.TransformVector(box.offset);
            report.AppendLine($"Spawn interior: x=[{left}, {right}], bottom={bottom}, ceiling={ceiling}");
            CollisionMoveResult leftMove = movement.Move(new Vector2(left + 1f, bottom + 3f), Vector2.left * 20f, 1f);
            CollisionMoveResult rightMove = movement.Move(new Vector2(right - 1f, bottom + 3f), Vector2.right * 20f, 1f);
            CollisionMoveResult upMove = movement.Move(new Vector2(center, ceiling - 1f), Vector2.up * 20f, 1f);
            Check(leftMove.Position.x + colliderOffset.x - box.bounds.extents.x >= left - 0.01f && leftMove.Velocity.x == 0f,
                $"Left fence blocks actual PlayerCollisionMove at x={leftMove.Position.x:F3}", report);
            Check(rightMove.Position.x + colliderOffset.x + box.bounds.extents.x <= right + 0.01f && rightMove.Velocity.x == 0f,
                $"Right fence blocks actual PlayerCollisionMove at x={rightMove.Position.x:F3}", report);
            Check(upMove.Position.y + colliderOffset.y + box.bounds.extents.y <= ceiling + 0.01f && upMove.Velocity.y == 0f,
                $"Upper timber blocks actual PlayerCollisionMove at y={upMove.Position.y:F3}", report);
            Check(Physics2D.Raycast(new Vector2(left + 1.5f, bottom + 1.5f), Vector2.down,
                    1.25f * cell, player.CollisionMask).collider == null,
                "Left mine descent remains open", report);
            Check(Physics2D.Raycast(new Vector2(right - 1.5f, bottom + 1.5f), Vector2.down,
                    1.25f * cell, player.CollisionMask).collider == null,
                "Right mine descent remains open", report);
            Check(backdrop.GetComponentsInChildren<Collider2D>().Length == 0,
                "Environment creates no duplicate or client-only colliders", report);
            foreach (SpriteRenderer renderer in backdrop.GetComponentsInChildren<SpriteRenderer>())
                Check(renderer.sprite != null, $"Sprite assigned: {renderer.name}", report);
            Directory.CreateDirectory("Library/MiningVillageVerification");
            File.WriteAllText("Library/MiningVillageVerification/results.txt", report.ToString());
            Debug.Log(report.ToString());
        }

        private static void Check(bool result, string label, StringBuilder report)
        {
            report.AppendLine($"{(result ? "PASS" : "FAIL")}: {label}");
            if (result == false) Debug.LogError($"[Mining Village] {label}");
        }
    }
}
