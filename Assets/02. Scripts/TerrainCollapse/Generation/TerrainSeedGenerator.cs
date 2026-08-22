using System;
using System.Collections.Generic;
using UnityEngine;

namespace TerrainCollapse
{
    // 설정과 시드를 기반으로 TerrainMapFile을 생성하는 런타임 지형 생성기

    public static class TerrainSeedGenerator
    {
        /// <summary>설정의 고정 또는 랜덤 시드 정책에 따라 런타임 맵 생성</summary>
        public static TerrainMapFile Generate(
            TerrainMapGenerationSettings settings,
            IReadOnlyList<TerrainMapGenerationRule> rules = null)
        {
            int seed = settings.UseRandomSeed ? Environment.TickCount : settings.Seed;
            return Generate(settings, rules, seed);
        }

        /// <summary>외부에서 확정한 시드로 런타임 맵 생성</summary>
        public static TerrainMapFile Generate(
            TerrainMapGenerationSettings settings,
            IReadOnlyList<TerrainMapGenerationRule> rules,
            int seed)
        {
            TerrainMapGenerationContext context = new(seed);
            TerrainMapFile map = CreateEmptyMap(settings, seed);
            FillBaseTerrain(map, settings);

            if (settings.AddCollapseTestStructure)
                AddCollapseTestStructure(map, settings);

            if (rules != null)
            {
                foreach (TerrainMapGenerationRule rule in rules)
                    rule.Apply(map, context);
            }

            return map;
        }

        /// <summary>설정 크기에 맞춰 Empty 타일로 채운 맵 생성</summary>
        public static TerrainMapFile CreateEmptyMap(
            TerrainMapGenerationSettings settings,
            int seed)
        {
            TerrainMapFile map = new()
            {
                Name = settings.Name,
                Seed = seed,
                Width = settings.Width,
                Height = settings.Height,
                CellSize = settings.CellSize,
                Origin = settings.Origin,
                Tiles = new List<TerrainTileType>(settings.Width * settings.Height)
            };

            for (int i = 0; i < settings.Width * settings.Height; i++)
                map.Tiles.Add(TerrainTileType.Empty);

            return map;
        }

        private static void FillBaseTerrain(
            TerrainMapFile map,
            TerrainMapGenerationSettings settings)
        {
            int depth = Mathf.Clamp(settings.SolidDepth, 0, settings.Height);
            int bedrockDepth = Mathf.Clamp(settings.BedrockDepth, 0, depth);

            for (int y = 0; y < depth; y++)
            for (int x = 0; x < settings.Width; x++)
            {
                TerrainTileType type = y < bedrockDepth
                    ? TerrainTileType.Bedrock
                    : settings.BaseTileType;
                map.SetTile(new Vector2Int(x, y), type);
            }
        }

        private static void AddCollapseTestStructure(
            TerrainMapFile map,
            TerrainMapGenerationSettings settings)
        {
            int centerX = settings.Width / 2;
            int surfaceY = settings.SolidDepth - 1;

            for (int y = 4; y <= 5; y++)
                for (int x = -4; x <= 2; x++)
                    SetTileInsideBounds(map,
                        new Vector2Int(centerX + x, surfaceY + y),
                        settings.BaseTileType);
            
            for (int y = 1; y <= 3; y++)
                for (int x = -1; x <= 0; x++)
                    SetTileInsideBounds(map,
                        new Vector2Int(centerX + x, surfaceY + y),
                        settings.BaseTileType);
        }

        private static void SetTileInsideBounds(
            TerrainMapFile map,
            Vector2Int coord,
            TerrainTileType type)
        {
            bool isInside = coord.x >= 0 && coord.x < map.Width &&
                          coord.y >= 0 && coord.y < map.Height;
            
            if (isInside) map.SetTile(coord, type);
        }
    }
}
