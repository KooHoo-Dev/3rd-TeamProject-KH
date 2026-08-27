using System;
using System.Collections.Generic;
using UnityEngine;

namespace TerrainCollapse
{
    // 초기 맵 JSON 형식과 에디터용 테스트 맵 생성 설정 정의

    /// <summary>JSON에 저장되는 데이터 기반 지형 맵 형식.</summary>
    [Serializable]
    public sealed class TerrainMapFile
    {
        public int Version = 1;
        public string Name = "TerrainMap";
        public int Seed;
        public int Width;
        public int Height;
        public float CellSize = 1f;
        public Vector2 Origin;
        public List<TerrainTileType> Tiles = new();
        public Vector2Int RespawnAreaOrigin;
        public Vector2Int RespawnAreaSize;
        public List<Vector2Int> RespawnPoints = new();

        public TerrainTileType GetTile(Vector2Int coord)
            => Tiles[coord.y * Width + coord.x];

        public void SetTile(Vector2Int coord, TerrainTileType type)
            => Tiles[coord.y * Width + coord.x] = type;

        public Vector2 GetCellCenterWorld(Vector2Int coord)
            => Origin + new Vector2(
                (coord.x + 0.5f) * CellSize,
                (coord.y + 0.5f) * CellSize);

        public bool TryGetRespawnPoint(int index, out Vector2Int coord)
        {
            coord = default;

            if (RespawnPoints == null || index < 0 || index >= RespawnPoints.Count)
                return false;

            coord = RespawnPoints[index];

            return true;
        }

        public bool IsValid(out string error)
        {
            if (Version <= 0) { error = "Version은 1 이상이어야 합니다."; return false; }
            if (Width <= 0 || Height <= 0) { error = $"맵 크기가 잘못되었습니다: {Width}x{Height}"; return false; }
            if (CellSize <= 0f) { error = $"CellSize가 잘못되었습니다: {CellSize}"; return false; }
            if (Tiles == null || Tiles.Count != Width * Height)
            {
                error = $"셀 수가 맞지 않습니다: 기대 {Width * Height}, 실제 {Tiles?.Count ?? 0}";
                return false;
            }

            if (RespawnPoints != null)
            {
                HashSet<Vector2Int> uniqueRespawnPoints = new();

                foreach (Vector2Int respawnPoint in RespawnPoints)
                {
                    if (respawnPoint.x < 0 || respawnPoint.x >= Width ||
                        respawnPoint.y < 0 || respawnPoint.y >= Height)
                    {
                        error = $"리스폰 좌표가 맵 범위를 벗어났습니다: {respawnPoint}";
                        return false;
                    }

                    if (uniqueRespawnPoints.Add(respawnPoint) == false)
                    {
                        error = $"중복 리스폰 좌표입니다: {respawnPoint}";
                        return false;
                    }

                    if (GetTile(respawnPoint) != TerrainTileType.Empty)
                    {
                        error = $"리스폰 좌표에 지형이 존재합니다: {respawnPoint}";
                        return false;
                    }
                }
            }
            
            error = string.Empty;
            
            return true;
        }

#if UNITY_EDITOR
        public string ToJson(bool prettyPrint = true) => JsonUtility.ToJson(this, prettyPrint);
#endif
        public static TerrainMapFile FromJson(string json) => JsonUtility.FromJson<TerrainMapFile>(json);
    }

#if UNITY_EDITOR
    /// <summary>에디터용 성능/테스트 맵 생성 규칙.</summary>
    public static class TerrainMapFactory
    {
        public const int PerformanceMapWidth = 100;
        public const int PerformanceMapDepth = 100;
        public const int CollapseStructureHeight = 5;

        public static TerrainMapFile CreatePerformanceMap()
        {
            TerrainMapGenerationSettings settings = new()
            {
                Name = "TerrainCollapsePerformanceMap",
                Width = PerformanceMapWidth,
                Height = PerformanceMapDepth + CollapseStructureHeight,
                SolidDepth = PerformanceMapDepth,
                CellSize = 1f,
                Origin = new Vector2(-PerformanceMapWidth / 2f, -(PerformanceMapDepth - 1)),
                BedrockDepth = 1,
                BaseTileType = TerrainTileType.Ground,
                AddCollapseTestStructure = true
            };
            return TerrainSeedGenerator.Generate(settings);
        }
    }
#endif
}
