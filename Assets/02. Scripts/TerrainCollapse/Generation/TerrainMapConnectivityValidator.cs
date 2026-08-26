using System.Collections.Generic;
using UnityEngine;

namespace TerrainCollapse
{
    // 생성된 지형 맵의 고립 그룹과 바닥 지지 여부 검사

    internal static class TerrainMapConnectivityValidator
    {
        private static readonly Vector2Int[] DIRECTIONS =
        {
            Vector2Int.up,
            Vector2Int.down,
            Vector2Int.left,
            Vector2Int.right
        };

        public static bool HasFloatingGroup(TerrainMapFile map)
        {
            HashSet<Vector2Int> visited = new();

            for (int y = 0; y < map.Height; y++)
                for (int x = 0; x < map.Width; x++)
                {
                    Vector2Int start = new(x, y);

                    TerrainTileType type = map.GetTile(start);

                    if (type == TerrainTileType.Empty ||
                        type == TerrainTileType.Bedrock ||
                        visited.Contains(start))
                        continue;

                    if (IsGroupSupported(map, start, visited) == false)
                        return true;
                }

            return false;
        }

        private static bool IsGroupSupported(
            TerrainMapFile map,
            Vector2Int start,
            HashSet<Vector2Int> visited)
        {
            Queue<Vector2Int> queue = new();

            bool isSupported = false;

            visited.Add(start);
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                Vector2Int current = queue.Dequeue();
                Vector2Int below = current + Vector2Int.down;

                if (IsInsideMap(map, below) && map.GetTile(below) == TerrainTileType.Bedrock)
                    isSupported = true;

                foreach (Vector2Int direction in DIRECTIONS)
                {
                    Vector2Int next = current + direction;

                    if (IsInsideMap(map, next) == false) continue;

                    TerrainTileType type = map.GetTile(next);

                    if (type == TerrainTileType.Empty || type == TerrainTileType.Bedrock)
                        continue;

                    if (visited.Add(next) == false) continue;

                    queue.Enqueue(next);
                }
            }

            return isSupported;
        }

        private static bool IsInsideMap(TerrainMapFile map, Vector2Int coord)
            => coord.x >= 0 && coord.x < map.Width &&
               coord.y >= 0 && coord.y < map.Height;
    }
}
