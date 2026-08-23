using System;
using System.Collections.Generic;
using UnityEngine;

namespace TerrainCollapse
{
    // 데이터 테이블의 프로필과 깊이 섹션을 조합하여 기본 지형을 생성하는 내부 생성기

    internal static class TerrainTableSeedGenerator
    {
        public static TerrainMapFile Generate(string profileID, int seed)
        {
            TerrainDataManager dataManager = TerrainDataManager.Instance;
            dataManager.EnsureLoaded();

            TerrainSeedProfileDef profile = dataManager.SeedProfiles.Get(profileID);
            IReadOnlyList<TerrainDepthSectionDef> sections =
                dataManager.DepthSections.GetByProfile(profileID);

            return Generate(profile, sections, dataManager.SectionMinerals, seed);
        }

        public static TerrainMapFile Generate(
            TerrainSeedProfileDef profile,
            IReadOnlyList<TerrainDepthSectionDef> sections,
            TerrainSectionMineralTable mineralTable,
            int seed)
        {
            Validate(profile, sections, mineralTable);

            TerrainMapGenerationContext context = new(seed);
            TerrainMapFile map = CreateEmptyMap(profile, seed);
            int[] surfaceByColumn = FillSectionTerrain(map, profile, sections, context);
            TerrainMineralClusterGenerator.Apply(
                map,
                sections,
                mineralTable,
                surfaceByColumn,
                context);

            return map;
        }

        public static TerrainMapFile CreateEmptyMap(
            TerrainSeedProfileDef profile,
            int seed)
        {
            TerrainMapFile map = new()
            {
                Name = profile.Name,
                Seed = seed,
                Width = profile.Width,
                Height = profile.Height,
                CellSize = profile.CellSize,
                Origin = new Vector2(profile.OriginX, profile.OriginY),
                Tiles = new List<TerrainTileType>(profile.Width * profile.Height)
            };

            for (int i = 0; i < profile.Width * profile.Height; i++)
                map.Tiles.Add(TerrainTileType.Empty);

            return map;
        }

        private static int[] FillSectionTerrain(
            TerrainMapFile map,
            TerrainSeedProfileDef profile,
            IReadOnlyList<TerrainDepthSectionDef> sections,
            TerrainMapGenerationContext context)
        {
            int[] surfaceByColumn = new int[profile.Width];

            for (int x = 0; x < profile.Width; x++)
            {
                int variation = profile.SurfaceAmplitude == 0
                    ? 0
                    : context.Random.Next(-profile.SurfaceAmplitude, profile.SurfaceAmplitude + 1);
                int surfaceY = Mathf.Clamp(
                    profile.SurfaceHeight - 1 + variation,
                    profile.BedrockDepth,
                    profile.Height - 1);
                surfaceByColumn[x] = surfaceY;

                FillColumn(map, x, surfaceY, profile.BedrockDepth, sections);
            }

            return surfaceByColumn;
        }

        private static void FillColumn(
            TerrainMapFile map,
            int x,
            int surfaceY,
            int bedrockDepth,
            IReadOnlyList<TerrainDepthSectionDef> sections)
        {
            for (int y = 0; y <= surfaceY; y++)
            {
                Vector2Int coord = new(x, y);
                
                if (y < bedrockDepth)
                {
                    map.SetTile(coord, TerrainTileType.Bedrock);
                    continue;
                }

                int availableDepth = surfaceY - bedrockDepth;
                float depthRatio = availableDepth > 0
                    ? (float)(surfaceY - y) / availableDepth
                    : 0f;
                TerrainDepthSectionDef section = FindSection(sections, depthRatio);
                
                if (section != null) map.SetTile(coord, section.BaseTile.TileType);
            }
        }

        private static TerrainDepthSectionDef FindSection(
            IReadOnlyList<TerrainDepthSectionDef> sections,
            float depthRatio)
        {
            foreach (TerrainDepthSectionDef section in sections)
                if (section.ContainsDepthRatio(depthRatio)) return section;

            return null;
        }

        private static void Validate(
            TerrainSeedProfileDef profile,
            IReadOnlyList<TerrainDepthSectionDef> sections,
            TerrainSectionMineralTable mineralTable)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (sections == null) throw new ArgumentNullException(nameof(sections));
            if (mineralTable == null) throw new ArgumentNullException(nameof(mineralTable));
            if (sections.Count == 0) throw new InvalidOperationException($"지형 섹션이 없는 프로필입니다: {profile.ProfileID}");
        }
    }
}
