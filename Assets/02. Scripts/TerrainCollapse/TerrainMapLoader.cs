using UnityEngine;

namespace TerrainCollapse
{
    // 초기 JSON 또는 데이터 테이블 시드 맵 생성과 TerrainGridWorld 로드

    /// <summary>TextAsset JSON을 읽어 TerrainGridWorld의 런타임 데이터와 표현을 생성한다.</summary>
    [RequireComponent(typeof(TerrainGridWorld))]
    public sealed class TerrainMapLoader : MonoBehaviour
    {
        [SerializeField] private TextAsset mapJson;
        [SerializeField] private TerrainGridWorld targetWorld;
        [SerializeField] private bool useDataTableGeneration = true;
        [SerializeField] private string dataTableProfileID = "Default";
        [SerializeField] private bool useRandomSeed = true;
        [SerializeField] private int seed = 12345;
        [SerializeField] private Transform playerSpawnTarget;
        [SerializeField, Min(0)] private int respawnPointIndex;

        public TerrainMapFile LoadedMap { get; private set; }

        public void Configure(TextAsset json, TerrainGridWorld world)
        {
            mapJson = json;
            targetWorld = world;
        }

        private void Awake()
        {
            if (targetWorld == null) targetWorld = GetComponent<TerrainGridWorld>();

            // RoomClient가 있는 씬에서는 서버 Snapshot이 유일한 초기 지형이다.
            // 로컬 맵을 잠깐이라도 생성하면 서버 지형으로 교체될 때 Collider/플레이어 위치가 흔들린다.
            if (FindFirstObjectByType<RoomClient>() != null)
            {
                Debug.Log("[Terrain Map Loader] RoomClient 감지: 로컬 초기 맵 생성을 건너뜁니다.", this);
                return;
            }
            
            if (useDataTableGeneration)
            {
                int generationSeed = useRandomSeed ? System.Environment.TickCount : seed;
                LoadedMap = TerrainSeedGenerator.Generate(dataTableProfileID, generationSeed);
            }
            else
            {
                if (mapJson == null)
                {
                    Debug.LogError("[Terrain Map Loader] Map JSON이 연결되지 않았습니다.");
                    return;
                }

                LoadedMap = TerrainMapFile.FromJson(mapJson.text);
            }
            
            if (LoadedMap == null)
            {
                Debug.LogError("[Terrain Map Loader] JSON 역직렬화에 실패했습니다.");
                return;
            }
            
            string validationError;
            
            if (LoadedMap.IsValid(out validationError) == false)
            {
                Debug.LogError($"[Terrain Map Loader] 맵 로드 실패: {validationError}");
                return;
            }

            targetWorld.LoadMap(LoadedMap);
            PlacePlayerAtRespawnPoint();
            Debug.Log($"[Terrain Map Loader] '{LoadedMap.Name}' 로드 완료 — " +
                      $"{LoadedMap.Width}x{LoadedMap.Height}, cells={LoadedMap.Tiles.Count}");
        }

        private void PlacePlayerAtRespawnPoint()
        {
            if (playerSpawnTarget == null) return;

            if (LoadedMap.TryGetRespawnPoint(respawnPointIndex, out Vector2Int respawnPoint) == false)
            {
                Debug.LogError($"[Terrain Map Loader] 사용할 수 없는 리스폰 인덱스입니다: {respawnPointIndex}");
                return;
            }

            Vector2 worldPosition = LoadedMap.GetCellCenterWorld(respawnPoint);
            playerSpawnTarget.position = new Vector3(
                worldPosition.x,
                worldPosition.y,
                playerSpawnTarget.position.z);
            Physics2D.SyncTransforms();
        }
    }
}
