using System;
using UnityEngine;

namespace TerrainCollapse
{
    // 지형 데이터 테이블을 보유하고 의존 순서에 맞춰 로드를 조율하는 단일 진입점

    public sealed class TerrainDataManager : MonoBehaviour
    {
        private static TerrainDataManager instance;

        public static TerrainDataManager Instance
        {
            get
            {
                if (instance != null) return instance;

                instance = FindAnyObjectByType<TerrainDataManager>();
                
                if (instance != null) return instance;

                GameObject managerObject = new(nameof(TerrainDataManager));
                instance = managerObject.AddComponent<TerrainDataManager>();
                DontDestroyOnLoad(managerObject);
                
                return instance;
            }
        }

        public TerrainTileTable Tiles { get; } = new();
        public TerrainSeedProfileTable SeedProfiles { get; } = new();
        public TerrainDepthSectionTable DepthSections { get; } = new();
        public TerrainSectionMineralTable SectionMinerals { get; } = new();
        public TerrainVariantClusterTable VariantClusters { get; } = new();
        public TerrainCaveTable Caves { get; } = new();

        public bool IsReady { get; private set; }
        public event Action OnReady;

        private void Awake()
        {
            if (instance == null)
            {
                instance = this;
                DontDestroyOnLoad(gameObject);
                
                return;
            }

            if (instance != this) Destroy(gameObject);
        }

        private void Start()
        {
            EnsureLoaded();
        }

        /// <summary>미로드 상태에서만 전체 지형 테이블 로드</summary>
        public void EnsureLoaded()
        {
            if (IsReady == false) Load();
        }

        /// <summary>참조 의존 순서에 따른 전체 지형 테이블 로드</summary>
        public void Load()
        {
            IsReady = false;

            Tiles.Load();
            SeedProfiles.Load();
            DepthSections.Load(SeedProfiles, Tiles);
            SectionMinerals.Load(SeedProfiles, DepthSections, Tiles);
            VariantClusters.Load(SeedProfiles, DepthSections, Tiles);
            Caves.Load(SeedProfiles);

            IsReady = true;
            OnReady?.Invoke();

            Debug.Log(
                $"[TerrainDataManager] Loaded " +
                $"{Tiles.Count} tiles, {SeedProfiles.Count} profiles, " +
                $"{DepthSections.Count} sections, {SectionMinerals.Count} minerals, " +
                $"{VariantClusters.Count} variants, {Caves.Count} caves");
        }

        // 타일 종류에 해당하는 Def 조회
        public TerrainTileDef GetTileDef(TerrainTileType tileType) => Tiles.Get(tileType);

        public bool TryGetTileDef(TerrainTileType tileType, out TerrainTileDef tileDef)
            => Tiles.TryGet(tileType, out tileDef);

        // 프로필 ID에 해당하는 Def 조회
        public TerrainSeedProfileDef GetSeedProfileDef(string profileID)
            => SeedProfiles.Get(profileID);
    }
}
