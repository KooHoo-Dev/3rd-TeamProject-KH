using System.Collections.Generic;
using UnityEngine;

namespace TerrainCollapse
{
    // 깊이 계층의 기본 타일을 다른 일반 타일 군집으로 치환

    internal static class TerrainVariantClusterGenerator
    {
        public static void Apply(
            TerrainMapFile map,
            IReadOnlyList<TerrainVariantClusterDef> rules,
            TerrainMapGenerationContext context)
        {
            foreach (TerrainVariantClusterDef rule in rules)
                for (int i = 0; i < rule.ClusterCount; i++)
                    TryCreateCluster(map, rule, context);
        }

        private static void TryCreateCluster(
            TerrainMapFile map,
            TerrainVariantClusterDef rule,
            TerrainMapGenerationContext context)
        {
            const int MAX_PLACEMENT_ATTEMPTS = 20;

            for (int i = 0; i < MAX_PLACEMENT_ATTEMPTS; i++)
            {
                int width = context.Random.Next(rule.MinWidth, rule.MaxWidth + 1);
                int height = context.Random.Next(rule.MinHeight, rule.MaxHeight + 1);

                if (TryFindCenter(map, rule, width, height, context, out Vector2Int center) == false)
                    continue;

                FillCluster(map, center, width, height, rule, context);

                return;
            }
        }

        private static bool TryFindCenter(
            TerrainMapFile map,
            TerrainVariantClusterDef rule,
            int width,
            int height,
            TerrainMapGenerationContext context,
            out Vector2Int center)
        {
            List<Vector2Int> candidates = new();

            for (int y = 0; y < map.Height; y++)
                for (int x = 0; x < map.Width; x++)
                {
                    Vector2Int coord = new (x, y);

                    if (map.GetTile(coord) != rule.Section.BaseTile.TileType) continue;

                    float depthRatio = Mathf.Clamp01(
                        TerrainTableSeedGenerator.GetDepthRatio(map, y) +
                        TerrainTableSeedGenerator.GetSectionBoundaryOffset(map, x));

                    if (rule.Section.ContainsDepthRatio(depthRatio) == false) continue;
                    if (HasPlacementMargin(map, coord, width, height) == false) continue;

                    candidates.Add(coord);
                }

            if (candidates.Count == 0)
            {
                center = default;

                return false;
            }

            center = candidates[context.Random.Next(candidates.Count)];

            return true;
        }

        private static void FillCluster(
            TerrainMapFile map,
            Vector2Int center,
            int width,
            int height,
            TerrainVariantClusterDef rule,
            TerrainMapGenerationContext context)
        {
            float radiusX = width * 0.5f;
            float radiusY = height * 0.5f;

            int minX = center.x - width / 2;
            int minY = center.y - height / 2;

            for (int localY = 0; localY < height; localY++)
            for (int localX = 0; localX < width; localX++)
            {
                Vector2Int coord = new(
                    minX + localX,
                    minY + localY);

                if (IsInsideMap(map, coord) == false)
                    continue;

                if (map.GetTile(coord) !=
                    rule.Section.BaseTile.TileType)
                    continue;

                float depthRatio = Mathf.Clamp01(
                    TerrainTableSeedGenerator.GetDepthRatio(map, coord.y) +
                    TerrainTableSeedGenerator.GetSectionBoundaryOffset(map, coord.x));

                if (rule.Section.ContainsDepthRatio(depthRatio) == false)
                    continue;

                float normalizedX =
                    (localX + 0.5f - radiusX) / radiusX;

                float normalizedY =
                    (localY + 0.5f - radiusY) / radiusY;

                float distance =
                    normalizedX * normalizedX +
                    normalizedY * normalizedY;

                if (distance > 1f)
                    continue;

                if (context.Random.NextDouble() >
                    rule.FillRatio)
                    continue;

                map.SetTile(
                    coord,
                    rule.VariantTile.TileType);
            }
        }

        private static bool HasPlacementMargin(
            TerrainMapFile map,
            Vector2Int center,
            int width,
            int height)
        {
            int minX = center.x - width / 2;
            int minY = center.y - height / 2;

            return minX >= 0 && minX + width <= map.Width &&
                   minY >= 0 && minY + height <= map.Height;
        }

        private static bool IsInsideMap(TerrainMapFile map, Vector2Int coord)
            => coord.x >= 0 && coord.x < map.Width &&
               coord.y >= 0 && coord.y < map.Height;
    }
}
