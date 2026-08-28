using System.Collections.Generic;
using UnityEngine;

namespace TerrainCollapse
{
    // 섹션별 광물 규칙에 따라 연결된 광물 군집을 결정적으로 배치하는 내부 생성기

    internal static class TerrainMineralClusterGenerator
    {
        private static readonly Vector2Int[] ClusterDirections =
        {
            Vector2Int.left,
            Vector2Int.right,
            Vector2Int.down,
            Vector2Int.up
        };

        public static void Apply(
            TerrainMapFile map,
            IReadOnlyList<TerrainDepthSectionDef> sections,
            TerrainSectionMineralTable mineralTable,
            TerrainMapGenerationContext context)
        {
            foreach (TerrainDepthSectionDef section in sections)
            {
                IReadOnlyList<TerrainSectionMineralDef> minerals =
                    mineralTable.GetBySection(section.Profile.ProfileID, section.SectionID);

                foreach (TerrainSectionMineralDef mineral in minerals)
                    ApplyMineral(map, section, mineral, context);
            }
        }

        private static void ApplyMineral(
            TerrainMapFile map,
            TerrainDepthSectionDef section,
            TerrainSectionMineralDef mineral,
            TerrainMapGenerationContext context)
        {
            for (int x = 0; x < map.Width; x++)
            for (int y = 0; y < map.Height; y++)
            {
                Vector2Int coord = new(x, y);
                
                if (IsBaseCellInSection(map, coord, section) == false)
                    continue;
                
                if (context.Random.NextDouble() >= mineral.ClusterDensity)
                    continue;

                int clusterSize = context.Random.Next(
                    mineral.MinClusterSize,
                    mineral.MaxClusterSize + 1);
                FillCluster(
                    map,
                    coord,
                    clusterSize,
                    section,
                    mineral,
                    context);
            }
        }

        private static void FillCluster(
            TerrainMapFile map,
            Vector2Int start,
            int clusterSize,
            TerrainDepthSectionDef section,
            TerrainSectionMineralDef mineral,
            TerrainMapGenerationContext context)
        {
            List<Vector2Int> members = new() { start };
            map.SetResource(start, mineral.Resource.ResourceID);

            while (members.Count < clusterSize)
            {
                List<Vector2Int> candidates = CollectCandidates(
                    map,
                    members,
                    section);
                
                if (candidates.Count == 0) break;

                Vector2Int selected = candidates[context.Random.Next(candidates.Count)];
                map.SetResource(selected, mineral.Resource.ResourceID);
                members.Add(selected);
            }
        }

        private static List<Vector2Int> CollectCandidates(
            TerrainMapFile map,
            IReadOnlyList<Vector2Int> members,
            TerrainDepthSectionDef section)
        {
            List<Vector2Int> candidates = new();
            HashSet<Vector2Int> uniqueCandidates = new();

            foreach (Vector2Int member in members)
                foreach (Vector2Int direction in ClusterDirections)
                {
                    Vector2Int candidate = member + direction;
                    
                    if (uniqueCandidates.Add(candidate) == false) continue;
                    
                    if (IsBaseCellInSection(map, candidate, section))
                        candidates.Add(candidate);
                }

            return candidates;
        }

        private static bool IsBaseCellInSection(
            TerrainMapFile map,
            Vector2Int coord,
            TerrainDepthSectionDef section)
        {
            if (coord.x < 0 || coord.x >= map.Width || coord.y < 0 || coord.y >= map.Height)
                return false;

            float depthRatio = Mathf.Clamp01(
                TerrainTableSeedGenerator.GetDepthRatio(map, coord.y) +
                TerrainTableSeedGenerator.GetSectionBoundaryOffset(map, coord.x));

            if (section.ContainsDepthRatio(depthRatio) == false) return false;
            if (map.GetResource(coord) > 0) return false;

            return map.GetTile(coord) == section.BaseTile.TileType;
        }
    }
}
