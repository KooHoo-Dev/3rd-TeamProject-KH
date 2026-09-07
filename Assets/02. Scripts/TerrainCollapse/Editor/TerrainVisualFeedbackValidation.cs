using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DG.Tweening;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TerrainCollapse.Editor
{
    /// <summary>게임 상태와 분리된 임시 World에서 실행하는 시각 피드백 회귀 검사.</summary>
    public static class TerrainVisualFeedbackValidation
    {
        private const string BatchKey = "TerrainVisualFeedbackValidation.Batch";
        private static readonly List<string> results = new();

        [MenuItem("Tools/Terrain Collapse/Validate Visual Feedback (Play Mode)")]
        public static void Run()
        {
            if (Application.isPlaying == false)
                throw new InvalidOperationException("Run this validation in Play Mode.");
            results.Clear();
            CheckStages();
            GameObject root = new("Visual Feedback Validation");
            root.SetActive(false);
            TerrainGridWorld world = root.AddComponent<TerrainGridWorld>();
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/02. Scripts/TerrainCollapse/Development/Sprites/OutlinedWhiteSquare.png");
            world.ConfigureVisual(sprite, Color.white);
            Vector2Int a = Vector2Int.zero;
            Vector2Int b = Vector2Int.right;
            TerrainSnapshot snapshot = new()
            {
                CellSize = 1f, Origin = new Vector2(10, 20), MapWidth = 3, MapHeight = 1,
                Cells = new List<TerrainCellChange>
                {
                    new(a, TerrainTileType.Stone, 6),
                    new(b, TerrainTileType.Stone, 3, 1),
                    new(new Vector2Int(2, 0), TerrainTileType.Stone, 8)
                }
            };
            try
            {
                world.LoadSnapshot(snapshot);
                root.SetActive(true);
                Check(Cracks(world).Length == 2, "Snapshot restores only damaged cells");
                SpriteRenderer crackA = CrackAt(world, a);
                SpriteRenderer crackB = CrackAt(world, b);
                Check(crackA.sprite.name == "crackOverlay20_0", "Snapshot crack20");
                Check(world.TryGetMaxDurability(b, out int maximum) && maximum == 4,
                    "Resource maximum overrides Stone maximum 8");
                Check(crackB.sprite.name == "crackOverlay20_0", "Resource crack uses maximum 4");
                Check(Vector2.Distance(crackA.bounds.size, Vector2.one) < 0.0001f, $"Crack fits one cell: renderer={crackA.bounds.size}, sprite={crackA.sprite.bounds.size}, scale={crackA.transform.lossyScale}");
                TerrainResourceView resource = world.GetComponentInChildren<TerrainResourceView>();
                Check(resource != null, "Resource prefab component is imported and instantiated");
                Vector3 resourceBase = resource.transform.position;
                Vector3 crackBase = crackB.transform.position;
                MeshFilter mesh = world.GetComponentInChildren<MeshFilter>();
                Vector3[] baseline = mesh.sharedMesh.vertices;
                string state = JsonUtility.ToJson(world.CreateSnapshot());
                EdgeCollider2D[] colliders = world.GetComponentsInChildren<EdgeCollider2D>();
                Vector2[][] points = colliders.Select(c => c.points).ToArray();
                Vector3 offset = new(0.03f, -0.02f, 0f);
                world.TrySetCellVisualOffset(b, offset);
                Flush(world);
                Vector3[] moved = mesh.sharedMesh.vertices;
                for (int i = 0; i < moved.Length; i++)
                    Check(Near(moved[i], baseline[i] + (i >= 4 && i < 8 ? offset : Vector3.zero)),
                        "Only target quad vertex " + i);
                Check(Near(resource.transform.position, resourceBase + offset), "Resource shares offset");
                Check(Near(crackB.transform.position, crackBase + offset), "Crack shares offset");
                world.TrySetCellVisualOffset(b, offset);
                Flush(world);
                Check(Near(mesh.sharedMesh.vertices[4], moved[4]), "Repeated offset does not accumulate");
                Check(JsonUtility.ToJson(world.CreateSnapshot()) == state, "Offset leaves Snapshot and Revision unchanged");
                Check(world.GetComponentsInChildren<EdgeCollider2D>().SequenceEqual(colliders), "Offset keeps Collider objects");
                for (int i = 0; i < colliders.Length; i++)
                    Check(colliders[i].points.SequenceEqual(points[i]), "Offset keeps Collider points " + i);

                Vector2Int c = new(2, 0);
                world.TrySetCellVisualOffset(c, offset);
                world.TryDamageCell(c, 2, out _, out _);
                Check(CrackAt(world, c).sprite.name == "crackOverlay20_0", "New crack starts at current visual offset");
                world.RemoveCell(c);

                TerrainChangeBatch batch = new(0, world.Revision, world.Revision + 1,
                    new[] { new TerrainCellChange(b, TerrainTileType.Stone, 2, 1) });
                Check(world.ApplyAuthoritativeBatch(batch, out string error), "Server batch: " + error);
                Check(CrackAt(world, b) == crackB && crackB.sprite.name == "crackOverlay40_0", "Batch changes existing crack Sprite");
                Flush(world);
                Check(Near(mesh.sharedMesh.vertices[4], baseline[4] + offset), "Chunk rebuild preserves offset");
                Check(Near(resource.transform.position, resourceBase + offset), "Resource stays aligned after batch");
                Check(Near(crackB.transform.position, crackBase + offset), "Crack stays aligned after batch");
                Check(world.ApplyAuthoritativeBatch(batch, out _) == false && crackB.sprite.name == "crackOverlay40_0",
                    "Rejected batch leaves crack unchanged");

                world.TrySetCellVisualOffset(b, Vector3.zero);
                world.PlayCellHit(a);
                TerrainTileHitEffect effect = world.GetComponent<TerrainTileHitEffect>();
                Tween first = DOTween.TweensByTarget(effect)[0];
                first.Goto(0.03f);
                Check(world.GetCellVisualOffset(a).sqrMagnitude > 0f, "DOTween generates offset");
                world.PlayCellHit(b);
                Check(DOTween.TweensByTarget(effect).Count == 2, "Two cells shake independently");
                world.PlayCellHit(a);
                Check(first.IsActive() == false && world.GetCellVisualOffset(a) == Vector3.zero,
                    "Same cell kills old Tween and resets offset");
                Check(DOTween.TweensByTarget(effect).Count == 2, "Same cell replacement keeps other shake");
                Check(CrackAt(world, b).sprite.name == "crackOverlay40_0", "Shake alone does not predict crack damage");
                foreach (Tween tween in DOTween.TweensByTarget(effect).ToArray()) tween.Complete();
                Flush(world);
                Check(world.GetCellVisualOffset(a) == Vector3.zero && world.GetCellVisualOffset(b) == Vector3.zero,
                    "Completion resets both offsets");
                Check(DOTween.IsTweening(effect) == false, "Completion kills Tweens");

                world.TryDamageCell(a, 2, out _, out _);
                Check(CrackAt(world, a) == crackA && crackA.sprite.name == "crackOverlay40_0", "Local damage refreshes existing view");
                world.PlayCellHit(b);
                world.TryDamageCell(b, 2, out _, out bool destroyed);
                Check(destroyed && world.IsOccupied(b) == false && Cracks(world).Length == 1, "Destruction removes crack");
                Check(world.GetCellVisualOffset(b) == Vector3.zero && DOTween.IsTweening(effect) == false,
                    "Destruction clears offset and Tween");
                world.SetCell(b, TerrainTileType.Stone, 1, 1);
                Check(CrackAt(world, b) == crackB && crackB.sprite.name == "crackOverlay60_0", "Placement reuses pooled crack from durability");
                world.PlayCellHit(a);
                world.LoadSnapshot(snapshot);
                Check(DOTween.IsTweening(effect) == false && Cracks(world).Length == 2, "Snapshot reload clears Tweens and rebuilds cracks");
                world.PlayCellHit(a);
                DOTween.TweensByTarget(effect)[0].Goto(0.03f);
                root.SetActive(false);
                Check(world.GetCellVisualOffset(a) == Vector3.zero && DOTween.IsTweening(effect) == false,
                    "Disable clears Tween and offset");
                root.SetActive(true);
                world.PlayCellHit(a);
                DOTween.TweensByTarget(effect)[0].Goto(0.03f);
                effect.enabled = false;
                Flush(world);
                Check(world.GetCellVisualOffset(a) == Vector3.zero && DOTween.IsTweening(effect) == false,
                    "Disabling effect component clears Tween and offset");
                effect.enabled = true;
                world.RemoveCell(a);
                Check(Cracks(world).Length == 1, "RemoveCell clears crack");
                world.LoadMap(new TerrainMapFile
                {
                    Width = 1, Height = 1, CellSize = 1, Tiles = new List<TerrainTileType> { TerrainTileType.Stone }
                });
                Check(Cracks(world).Length == 0, "LoadMap resets damage feedback");
                world.PlayCellHit(a);
                UnityEngine.Object.DestroyImmediate(effect);
                Check(world.GetCellVisualOffset(a) == Vector3.zero, "Component destruction restores offset");
                results.Add("PASS: all checks completed");
                Debug.Log(string.Join("\n", results));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                Directory.CreateDirectory("Logs");
                File.WriteAllLines("Logs/TerrainVisualFeedbackValidation.txt", results);
            }
        }

        private static SpriteRenderer[] Cracks(TerrainGridWorld world)
            => world.GetComponentsInChildren<SpriteRenderer>().Where(r => r.name == "Terrain Crack").ToArray();
        private static SpriteRenderer CrackAt(TerrainGridWorld world, Vector2Int coord)
            => Cracks(world).Single(r => Near(r.bounds.center,
                world.CellToWorldCenter(coord) + world.GetCellVisualOffset(coord)));
        private static bool Near(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 0.000001f;
        private static void Flush(TerrainGridWorld world) => world.SendMessage("LateUpdate");
        private static void Check(bool condition, string description)
        {
            if (condition == false) throw new InvalidOperationException("FAIL: " + description);
            results.Add("PASS: " + description);
        }
        private static void CheckStages()
        {
            int[] five = { 0, 80, 60, 40, 20, 0 };
            for (int current = 0; current <= 5; current++)
                Check(TerrainCrackOverlayManager.GetDamageStage(current, 5) == five[current], "Stage " + current + "/5");
            int[] three = { 0, 60, 20, 0 };
            for (int current = 0; current <= 3; current++)
                Check(TerrainCrackOverlayManager.GetDamageStage(current, 3) == three[current], "Stage " + current + "/3");
            Check(TerrainCrackOverlayManager.GetDamageStage(1, int.MaxValue) == 80, "Stage arithmetic does not overflow");
            Check(TerrainCrackOverlayManager.GetDamageStage(0, 0) == 0, "Zero maximum has no crack");
        }

        // 별도 임시 프로젝트에서 -executeMethod로 호출. 현재 작업 씬은 변경하지 않는다.
        public static void ExecuteBatch()
        {
            if (Application.isBatchMode == false) throw new InvalidOperationException("Batch mode only");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetBool(BatchKey, true);
            EditorApplication.EnterPlaymode();
        }
        [InitializeOnLoadMethod]
        private static void ResumeBatch()
        {
            if (SessionState.GetBool(BatchKey, false)) EditorApplication.update += RunBatch;
        }
        private static void RunBatch()
        {
            if (EditorApplication.isPlaying == false || EditorApplication.isCompiling ||
                EditorApplication.isUpdating || Time.frameCount < 2) return;
            EditorApplication.update -= RunBatch;
            SessionState.SetBool(BatchKey, false);
            try { Run(); EditorApplication.Exit(0); }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                File.AppendAllText("Logs/TerrainVisualFeedbackValidation.txt", "\n" + exception);
                EditorApplication.Exit(1);
            }
        }
    }
}
