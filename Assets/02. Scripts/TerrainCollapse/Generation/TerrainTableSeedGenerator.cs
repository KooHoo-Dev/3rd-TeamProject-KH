using System;
using System.Collections.Generic;
using UnityEngine;

namespace TerrainCollapse
{
    // 데이터 테이블의 프로필과 깊이 섹션으로 상하단이 잘린 타원형 지형을 생성하는 내부 생성기

    internal static class TerrainTableSeedGenerator
    {
        private readonly struct EllipseShape
        {
            private readonly TerrainSeedProfileDef profile;
            private readonly float radiusX;
            private readonly float radiusY;
            private readonly float centerY;

            public EllipseShape(TerrainSeedProfileDef profile)
            {
                this.profile = profile;
                radiusX = profile.Width * 0.5f;

                float topRatio = profile.TopOpeningWidth / (float)profile.Width;
                float bottomRatio = profile.BottomFlatWidth / (float)profile.Width;
                float topFactor = Mathf.Sqrt(1f - topRatio * topRatio);
                float bottomFactor = Mathf.Sqrt(1f - bottomRatio * bottomRatio);

                radiusY = profile.Height / (topFactor + bottomFactor);
                centerY = -topFactor * radiusY;
            }

            public bool Contains(Vector2Int coord)
            {
                if (coord.x < 0 || coord.x >= profile.Width ||
                    coord.y < 0 || coord.y >= profile.Height)
                    return false;

                if (coord.y < profile.BoundaryThickness &&
                    IsInsideCenteredWidth(coord.x, profile.BottomFlatWidth) == false)
                    return false;

                const float BOTTOM_WIDTH_MULTIPLIER = 0.7f;

                float centeredX = coord.x + 0.5f - radiusX;
                float worldY = coord.y + 0.5f - profile.Height;
                float heightRatio = coord.y / (float)(profile.Height - 1);
                float widthMultiplier = Mathf.Lerp(
                    BOTTOM_WIDTH_MULTIPLIER, 1f, heightRatio);
                float normalizedX = centeredX / (radiusX * widthMultiplier);
                float normalizedY = (worldY - centerY) / radiusY;

                return normalizedX * normalizedX + normalizedY * normalizedY <= 1f;
            }

            public bool IsTopOpening(Vector2Int coord)
                => coord.y >= profile.Height - profile.BoundaryThickness &&
                   IsInsideCenteredWidth(coord.x, profile.TopOpeningWidth);

            public bool IsTopOpeningColumn(int x)
                => IsInsideCenteredWidth(x, profile.TopOpeningWidth);

            private bool IsInsideCenteredWidth(int x, int width)
            {
                float centeredX = x + 0.5f - radiusX;

                return Mathf.Abs(centeredX) < width * 0.5f;
            }
        }

        public static TerrainMapFile Generate(string profileID, int seed)
        {
            TerrainDataManager dataManager = TerrainDataManager.Instance;
            dataManager.EnsureLoaded();

            TerrainSeedProfileDef profile = dataManager.SeedProfiles.Get(profileID);
            IReadOnlyList<TerrainDepthSectionDef> sections =
                dataManager.DepthSections.GetByProfile(profileID);
            IReadOnlyList<TerrainVariantClusterDef> variants =
                dataManager.VariantClusters.GetByProfile(profileID);
            IReadOnlyList<TerrainCaveDef> caves =
                dataManager.Caves.GetByProfile(profileID);

            return Generate(profile, sections, variants, caves, dataManager.SectionMinerals, seed);
        }

        public static TerrainMapFile Generate(
            TerrainSeedProfileDef profile,
            IReadOnlyList<TerrainDepthSectionDef> sections,
            TerrainSectionMineralTable mineralTable,
            int seed)
            => Generate(profile,
                sections,
                Array.Empty<TerrainVariantClusterDef>(),
                Array.Empty<TerrainCaveDef>(),
                mineralTable,
                seed);

        public static TerrainMapFile Generate(
            TerrainSeedProfileDef profile,
            IReadOnlyList<TerrainDepthSectionDef> sections,
            IReadOnlyList<TerrainVariantClusterDef> variants,
            TerrainSectionMineralTable mineralTable,
            int seed)
            => Generate(profile,
                sections,
                variants,
                Array.Empty<TerrainCaveDef>(),
                mineralTable,
                seed);

        public static TerrainMapFile Generate(
            TerrainSeedProfileDef profile,
            IReadOnlyList<TerrainDepthSectionDef> sections,
            IReadOnlyList<TerrainVariantClusterDef> variants,
            IReadOnlyList<TerrainCaveDef> caves,
            TerrainSectionMineralTable mineralTable,
            int seed)
        {
            Validate(profile, sections, mineralTable);

            if (variants == null) throw new ArgumentNullException(nameof(variants));
            if (caves == null) throw new ArgumentNullException(nameof(caves));

            TerrainMapGenerationContext context = new(seed);
            TerrainMapFile map = CreateEmptyMap(profile, seed);
            EllipseShape shape = new(profile);
            FillSectionTerrain(map, sections, shape);
            ApplyBoundaries(map, profile, shape);
            TerrainCaveGenerator.Apply(map, caves, context);
            TerrainVariantClusterGenerator.Apply(map, variants, context);
            TerrainMineralClusterGenerator.Apply(map, sections, mineralTable, context);

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

        internal static float GetDepthRatio(TerrainMapFile map, int y)
            => map.Height > 1
                ? (float)(map.Height - 1 - y) / (map.Height - 1)
                : 0f;

        private static void FillSectionTerrain(
            TerrainMapFile map,
            IReadOnlyList<TerrainDepthSectionDef> sections,
            EllipseShape shape)
        {
            for (int y = 0; y < map.Height; y++)
                for (int x = 0; x < map.Width; x++)
                {
                    Vector2Int coord = new(x, y);

                    if (shape.Contains(coord) == false) continue;

                    float depthRatio = GetDepthRatio(map, y);
                    float boundaryOffset = GetSectionBoundaryOffset(map, x);

                    TerrainDepthSectionDef section = FindSection(
                        sections, Mathf.Clamp01(depthRatio + boundaryOffset));

                    if (section != null) map.SetTile(coord, section.BaseTile.TileType);
                }
        }

        internal static float GetSectionBoundaryOffset(TerrainMapFile map, int x)
        {
            const float NOISE_SCALE = 0.2f;
            const float MAX_OFFSET_IN_CELLS = 4f;

            float noise = Mathf.PerlinNoise(x * NOISE_SCALE, map.Seed * 0.01f);
            float centeredNoise = noise * Mathf.Lerp(-1f, 1f, noise);

            return centeredNoise * MAX_OFFSET_IN_CELLS / Mathf.Max(1, map.Height - 1);
        }

        private static void ApplyBoundaries(
            TerrainMapFile map,
            TerrainSeedProfileDef profile,
            EllipseShape shape)
        {
            for (int y = 0; y < map.Height; y++)
                for (int x = 0; x < map.Width; x++)
                {
                    Vector2Int coord = new(x, y);

                    if (shape.Contains(coord) == false) continue;
                    if (shape.IsTopOpening(coord)) continue;

                    if (IsBoundary(coord, profile, shape))
                        map.SetTile(coord, TerrainTileType.Bedrock);
                }
        }

        private static bool IsBoundary(
            Vector2Int coord,
            TerrainSeedProfileDef profile,
            EllipseShape shape)
        {
            for (int distance = 1; distance <= profile.BoundaryThickness; distance++)
            {
                if (shape.Contains(coord + Vector2Int.left * distance) == false ||
                    shape.Contains(coord + Vector2Int.right * distance) == false ||
                    shape.Contains(coord + Vector2Int.down * distance) == false)
                    return true;

                Vector2Int above = coord + Vector2Int.up * distance;

                if (shape.Contains(above) == false &&
                    (above.y < profile.Height || shape.IsTopOpeningColumn(coord.x) == false))
                    return true;
            }

            return false;
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
