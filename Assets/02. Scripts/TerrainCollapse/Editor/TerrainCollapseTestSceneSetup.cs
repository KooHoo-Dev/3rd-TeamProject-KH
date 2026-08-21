// TerrainCollapse 테스트 씬의 오브젝트와 컴포넌트 참조를 자동 구성하는 에디터 도구
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TerrainCollapse.Editor
{
    public static class TerrainCollapseTestSceneSetup
    {
        private const string ScenePath = "Assets/01. Scenes/TerrainCollpseTestScene.unity";
        private const string MapPath = "Assets/02. Scripts/TerrainCollapse/Development/TerrainCollapseTestMap.json";
        private const string SpritePath =
            "Assets/02. Scripts/TerrainCollapse/Development/Sprites/OutlinedWhiteSquare.png";

        [MenuItem("Tools/Terrain Collapse/Configure Test Scene")]
        public static void ConfigureTestScene()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Camera camera = Object.FindFirstObjectByType<Camera>();
            camera.transform.position = new Vector3(0f, 2f, -10f);
            camera.orthographic = true;
            camera.orthographicSize = 8f;

            GameObject terrainSystem = FindOrCreateRoot("TerrainSystem");
            TerrainGridWorld world = GetOrAdd<TerrainGridWorld>(terrainSystem);
            TerrainMapLoader loader = GetOrAdd<TerrainMapLoader>(terrainSystem);
            TerrainManager manager = GetOrAdd<TerrainManager>(terrainSystem);
            TerrainSampler sampler = GetOrAdd<TerrainSampler>(terrainSystem);
            CollapseManager collapse = GetOrAdd<CollapseManager>(terrainSystem);
            TerrainCollapseCoordinator coordinator = GetOrAdd<TerrainCollapseCoordinator>(terrainSystem);
            TerrainMapBuilder builder = GetOrAdd<TerrainMapBuilder>(terrainSystem);
            TestTerrainAuthority authority = GetOrAdd<TestTerrainAuthority>(terrainSystem);

            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
            SetObjectReference(world, "terrainSprite", sprite);
            SetObjectReference(loader, "mapJson", AssetDatabase.LoadAssetAtPath<TextAsset>(MapPath));
            SetObjectReference(loader, "targetWorld", world);
            SetObjectReference(manager, "terrainWorld", world);
            SetObjectReference(manager, "inputCamera", camera);
            SetObjectReference(sampler, "targetWorld", world);
            SetObjectReference(collapse, "terrainGridWorld", world);
            SetObjectReference(collapse, "terrainSampler", sampler);
            SetObjectReference(coordinator, "terrainManager", manager);
            SetObjectReference(coordinator, "collapseManager", collapse);
            SetObjectReference(builder, "targetWorld", world);
            SetObjectReference(builder, "terrainManager", manager);
            SetObjectReference(builder, "painterCamera", camera);
            SetObjectReference(authority, "collapseManager", collapse);
            SetObjectReference(authority, "terrainManager", manager);

            GameObject player = FindOrCreateRoot("TestPlayer");
            player.transform.position = new Vector3(5f, 1.5f, 0f);
            Rigidbody2D playerBody = GetOrAdd<Rigidbody2D>(player);
            playerBody.bodyType = RigidbodyType2D.Kinematic;
            BoxCollider2D playerCollider = GetOrAdd<BoxCollider2D>(player);
            playerCollider.size = Vector2.one * world.CellSize;
            SpriteRenderer playerRenderer = GetOrAdd<SpriteRenderer>(player);
            playerRenderer.sprite = sprite;
            playerRenderer.color = Color.white;
            player.transform.localScale = Vector3.one;
            TestPlayerController playerController = GetOrAdd<TestPlayerController>(player);
            SetObjectReference(playerController, "terrainManager", manager);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[Terrain Collapse] 테스트 씬 참조 구성 완료: {ScenePath}");
        }

        private static GameObject FindOrCreateRoot(string name)
        {
            GameObject found = GameObject.Find(name);
            return found != null ? found : new GameObject(name);
        }

        private static T GetOrAdd<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }

        private static void SetObjectReference(Object target, string propertyName, Object value)
        {
            var serializedObject = new SerializedObject(target);
            serializedObject.FindProperty(propertyName).objectReferenceValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif
