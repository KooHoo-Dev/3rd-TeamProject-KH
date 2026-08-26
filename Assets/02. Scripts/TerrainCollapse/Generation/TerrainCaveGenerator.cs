using System.Collections.Generic;
using UnityEngine;

namespace TerrainCollapse
{
    // 지형 연결성을 유지하는 연속된 지하 동굴 생성

    internal static class TerrainCaveGenerator
    {
        public static void Apply(
            TerrainMapFile map,
            IReadOnlyList<TerrainCaveDef> rules,
            TerrainMapGenerationContext context)
        {
            foreach (TerrainCaveDef rule in rules)
                for (int i = 0; i < rule.CaveCount; i++)
                    TryCreateCave(map, rule, context);
        }

        private static void TryCreateCave(
            TerrainMapFile map,
            TerrainCaveDef rule,
            TerrainMapGenerationContext context)
        {
            for (int i = 0; i < rule.MaxAttempts; i++)
            {
                if (TryFindStart(map, rule, context, out Vector2Int start) == false)
                    return;

                HashSet<Vector2Int> candidateCells =
                    BuildCandidate(map, start, rule, context);

                if (candidateCells.Count == 0) continue;
                if (TryApplyCandidate(map, candidateCells)) return;
            }
        }

        private static bool TryFindStart(
            TerrainMapFile map,
            TerrainCaveDef rule,
            TerrainMapGenerationContext context,
            out Vector2Int start)
        {
            List<Vector2Int> candidates = new();

            for (int y = 0; y < map.Height; y++)
                for (int x = 0; x < map.Width; x++)
                {
                    Vector2Int coord = new(x, y);

                    TerrainTileType type = map.GetTile(coord);

                    if (type == TerrainTileType.Empty || type == TerrainTileType.Bedrock)
                        continue;

                    float depthRatio = TerrainTableSeedGenerator.GetDepthRatio(map, y);

                    if (rule.ContainsDepthRatio(depthRatio) == false) continue;
                    if (IsNearBoundary(map, coord, rule.MaxRadius + 1)) continue;

                    candidates.Add(coord);
                }

            if (candidates.Count == 0)
            {
                start = default;

                return false;
            }

            start = candidates[context.Random.Next(candidates.Count)];

            return true;
        }

        private static HashSet<Vector2Int> BuildCandidate(
            TerrainMapFile map,
            Vector2Int start,
            TerrainCaveDef rule,
            TerrainMapGenerationContext context)
        {
            HashSet<Vector2Int> candidateCells = new();
            Vector2 position = start;
            float horizontalDirection = context.Random.Next(2) == 0 ? -1f : 1f;
            Vector2 direction = new(horizontalDirection, 0f);
            int length = context.Random.Next(rule.MinLength, rule.MaxLength + 1);

            for (int i = 0; i < length; i++)
            {
                int radius = context.Random.Next(rule.MinRadius, rule.MaxRadius + 1);

                CollectCircleCells(map, position, radius, candidateCells);

                float verticalChange = (float)(context.Random.NextDouble() * 0.5 - 0.25);

                direction = new Vector2(
                        horizontalDirection,
                        Mathf.Clamp(direction.y + verticalChange, -0.5f, 0.5f))
                    .normalized;

                position += direction;
            }

            return candidateCells;
        }

        private static void CollectCircleCells(
            TerrainMapFile map,
            Vector2 position,
            int radius,
            HashSet<Vector2Int> candidateCells)
        {
            Vector2Int center = new(Mathf.RoundToInt(position.x),  Mathf.RoundToInt(position.y));

            for (int y = -radius; y <= radius; y++)
                for (int x = -radius; x <= radius; x++)
                {
                    if (x * x + y * y > radius * radius) continue;

                    Vector2Int coord = new(center.x + x, center.y + y);

                    if (IsInsideMap(map, coord) == false) continue;

                    TerrainTileType type = map.GetTile(coord);

                    if (type == TerrainTileType.Empty || type == TerrainTileType.Bedrock) continue;
                    if (IsNearBoundary(map, coord, 1)) continue;

                    candidateCells.Add(coord);
                }
        }

        private static bool IsNearBoundary(
            TerrainMapFile map,
            Vector2Int center,
            int margin)
        {
            for (int y = -margin; y <= margin; y++)
                for (int x = -margin; x <= margin; x++)
                {
                    Vector2Int coord = new(center.x + x, center.y + y);

                    if (IsInsideMap(map, coord) == false) return true;
                    if (map.GetTile(coord) == TerrainTileType.Bedrock) return true;
                }

            return false;
        }

        private static bool IsInsideMap(TerrainMapFile map, Vector2Int coord)
            => coord.x >= 0 && coord.x < map.Width &&
               coord.y >= 0 && coord.y < map.Height;

        private readonly struct CaveTileBackup
        {
            public Vector2Int Coord { get; }
            public TerrainTileType Type { get; }

            public CaveTileBackup(Vector2Int coord, TerrainTileType type)
            {
                Coord = coord;
                Type = type;
            }
        }

        private static bool TryApplyCandidate(
            TerrainMapFile map,
            HashSet<Vector2Int> candidateCells)
        {
            List<CaveTileBackup> backups = new(candidateCells.Count);

            foreach (Vector2Int coord in candidateCells)
            {
                TerrainTileType previousType = map.GetTile(coord);

                backups.Add(new CaveTileBackup(coord, previousType));
                map.SetTile(coord, TerrainTileType.Empty);
            }

            if (TerrainMapConnectivityValidator.HasFloatingGroup(map) == false) return true;

            RestoreTiles(map, backups);

            return false;
        }

        private static void RestoreTiles(
            TerrainMapFile map,
            IReadOnlyList<CaveTileBackup> backups)
        {
            foreach (CaveTileBackup backup in backups)
                map.SetTile(backup.Coord, backup.Type);
        }
    }
}
