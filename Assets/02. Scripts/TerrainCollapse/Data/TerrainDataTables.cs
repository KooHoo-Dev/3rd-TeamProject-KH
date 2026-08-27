using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TerrainCollapse
{
    // 지형 TSV의 로드와 키 해석 및 Def 보관과 조회를 담당하는 테이블 모음

    public sealed class TerrainTileTable
    {
        private const string TableName = "Table/Terrain/terrain_tiles.tsv";
        private readonly Dictionary<TerrainTileType, TerrainTileDef> defs = new();

        public int Count => defs.Count;
        public IReadOnlyCollection<TerrainTileDef> All => defs.Values;

        /// <summary>타일 DTO 로드와 Def 변환 및 키 등록</summary>
        public void Load()
        {
            defs.Clear();
            List<TerrainTileDTO> rows = ReadRows<TerrainTileDTO>(TableName);

            foreach (TerrainTileDTO dto in rows)
            {
                if (dto.TileType == TerrainTileType.Empty)
                    throw new InvalidDataException("Empty 타일은 terrain_tiles.tsv에 등록할 수 없습니다");
                
                if (dto.MaxDurability < 0 || dto.DropItemID < 0)
                    throw new InvalidDataException($"타일 수치는 0 이상이어야 합니다: {dto.TileType}");

                if (dto.IsMineable && dto.MaxDurability <= 0)
                    throw new InvalidDataException($"채굴 가능한 타일의 MaxDurability는 1 이상이어야 합니다: {dto.TileType}");
                
                if (defs.ContainsKey(dto.TileType))
                    throw new InvalidDataException($"중복 TileType입니다: {dto.TileType}");

                defs.Add(dto.TileType, new TerrainTileDef(dto));
            }
        }

        // 타일 종류에 해당하는 Def 조회
        public TerrainTileDef Get(TerrainTileType tileType) => defs[tileType];

        // 타일 종류에 해당하는 Def 조회 시도
        public bool TryGet(TerrainTileType tileType, out TerrainTileDef def)
            => defs.TryGetValue(tileType, out def);

        private static List<T> ReadRows<T>(string tableName) where T : new()
        {
            string path = Path.Combine(Application.streamingAssetsPath, tableName);
            List<T> rows = TerrainTsvReader.ReadTable<T>(path);
            return rows ?? throw new InvalidDataException($"지형 테이블 로드에 실패했습니다: {tableName}");
        }

        internal static List<T> ReadTerrainRows<T>(string tableName) where T : new()
            => ReadRows<T>(tableName);
    }

    public sealed class TerrainSeedProfileTable
    {
        private const string TableName = "Table/Terrain/terrain_seed_profiles.tsv";
        private readonly Dictionary<string, TerrainSeedProfileDef> defs =
            new(StringComparer.Ordinal);

        public int Count => defs.Count;
        public IReadOnlyCollection<TerrainSeedProfileDef> All => defs.Values;

        /// <summary>시드 프로필 DTO 로드와 Def 변환 및 키 등록</summary>
        public void Load()
        {
            defs.Clear();
            List<TerrainSeedProfileDTO> rows =
                TerrainTileTable.ReadTerrainRows<TerrainSeedProfileDTO>(TableName);

            foreach (TerrainSeedProfileDTO dto in rows)
            {
                Validate(dto);

                if (defs.ContainsKey(dto.ProfileID))
                    throw new InvalidDataException($"중복 ProfileID입니다: {dto.ProfileID}");

                defs.Add(dto.ProfileID, new TerrainSeedProfileDef(dto));
            }
        }

        // 프로필 ID에 해당하는 Def 조회
        public TerrainSeedProfileDef Get(string profileID) => defs[profileID];

        // 프로필 ID에 해당하는 Def 조회 시도
        public bool TryGet(string profileID, out TerrainSeedProfileDef def)
            => defs.TryGetValue(profileID, out def);

        private static void Validate(TerrainSeedProfileDTO dto)
        {
            if (string.IsNullOrWhiteSpace(dto.ProfileID))
                throw new InvalidDataException("ProfileID가 비어 있습니다");
            
            if (dto.Width <= 0 || dto.Height <= 0 || dto.CellSize <= 0f)
                throw new InvalidDataException($"맵 크기와 CellSize는 0보다 커야 합니다: {dto.ProfileID}");

            if (dto.TopOpeningWidth < 3 || dto.TopOpeningWidth >= dto.Width)
                throw new InvalidDataException($"TopOpeningWidth 범위가 올바르지 않습니다: {dto.ProfileID}");

            if ((dto.Width - dto.TopOpeningWidth) % 2 != 0)
                throw new InvalidDataException($"TopOpeningWidth는 맵의 X축 중앙에 배치할 수 있어야 합니다: {dto.ProfileID}");

            if (dto.BoundaryThickness <= 0 || dto.BoundaryThickness * 2 >= dto.Width ||
                dto.BoundaryThickness * 2 >= dto.Height)
                throw new InvalidDataException($"BoundaryThickness 범위가 올바르지 않습니다: {dto.ProfileID}");
        }
    }

    public sealed class TerrainDepthSectionTable
    {
        private const string TableName = "Table/Terrain/terrain_depth_sections.tsv";
        private readonly Dictionary<string, TerrainDepthSectionDef> defs =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<TerrainDepthSectionDef>> defsByProfile =
            new(StringComparer.Ordinal);

        public int Count => defs.Count;

        /// <summary>섹션 DTO 로드와 프로필 및 기본 타일 참조 해석</summary>
        public void Load(TerrainSeedProfileTable profileTable, TerrainTileTable tileTable)
        {
            defs.Clear();
            defsByProfile.Clear();
            Dictionary<string, HashSet<int>> ordersByProfile = new(StringComparer.Ordinal);
            List<TerrainDepthSectionDTO> rows =
                TerrainTileTable.ReadTerrainRows<TerrainDepthSectionDTO>(TableName);

            foreach (TerrainDepthSectionDTO dto in rows)
            {
                Validate(dto);
                if (profileTable.TryGet(dto.ProfileID, out TerrainSeedProfileDef profile) == false)
                    throw new InvalidDataException($"등록되지 않은 ProfileID입니다: {dto.ProfileID}");
                
                if (tileTable.TryGet(dto.BaseTileType, out TerrainTileDef baseTile) == false)
                    throw new InvalidDataException($"등록되지 않은 BaseTileType입니다: {dto.BaseTileType}");

                string key = CreateKey(dto.ProfileID, dto.SectionID);
                
                if (defs.ContainsKey(key))
                    throw new InvalidDataException($"중복 섹션입니다: {dto.ProfileID}/{dto.SectionID}");

                if (ordersByProfile.TryGetValue(dto.ProfileID, out HashSet<int> orders) == false)
                {
                    orders = new HashSet<int>();
                    ordersByProfile.Add(dto.ProfileID, orders);
                }
                
                if (orders.Add(dto.SectionOrder) == false)
                    throw new InvalidDataException($"중복 SectionOrder입니다: {dto.ProfileID}/{dto.SectionOrder}");

                TerrainDepthSectionDef def = new(dto, profile, baseTile);
                defs.Add(key, def);
                
                if (defsByProfile.TryGetValue(dto.ProfileID, out List<TerrainDepthSectionDef> profileDefs) == false)
                {
                    profileDefs = new List<TerrainDepthSectionDef>();
                    defsByProfile.Add(dto.ProfileID, profileDefs);
                }
                
                profileDefs.Add(def);
            }

            foreach (List<TerrainDepthSectionDef> profileDefs in defsByProfile.Values)
            {
                profileDefs.Sort((left, right) => left.SectionOrder.CompareTo(right.SectionOrder));
                ValidateSectionRatios(profileDefs);
            }
        }

        // 프로필과 섹션 ID에 해당하는 Def 조회
        public TerrainDepthSectionDef Get(string profileID, string sectionID)
            => defs[CreateKey(profileID, sectionID)];

        // 프로필과 섹션 ID에 해당하는 Def 조회 시도
        public bool TryGet(string profileID, string sectionID, out TerrainDepthSectionDef def)
            => defs.TryGetValue(CreateKey(profileID, sectionID), out def);

        // 프로필의 섹션 목록을 적용 순서대로 조회
        public IReadOnlyList<TerrainDepthSectionDef> GetByProfile(string profileID)
            => defsByProfile.TryGetValue(profileID, out List<TerrainDepthSectionDef> profileDefs)
                ? profileDefs
                : Array.Empty<TerrainDepthSectionDef>();

        internal static string CreateKey(string profileID, string sectionID) => $"{profileID}\u001F{sectionID}";

        private static void Validate(TerrainDepthSectionDTO dto)
        {
            if (string.IsNullOrWhiteSpace(dto.ProfileID) || string.IsNullOrWhiteSpace(dto.SectionID))
                throw new InvalidDataException("ProfileID와 SectionID는 비어 있을 수 없습니다");
            
            if (dto.SectionOrder < 0 || dto.MinDepthRatio < 0f || dto.MaxDepthRatio > 1f ||
                dto.MaxDepthRatio <= dto.MinDepthRatio)
                throw new InvalidDataException($"섹션 순서 또는 깊이 비율이 올바르지 않습니다: {dto.SectionID}");
            
            if (dto.BaseTileType == TerrainTileType.Empty)
                throw new InvalidDataException($"BaseTileType은 Empty일 수 없습니다: {dto.SectionID}");
        }

        private static void ValidateSectionRatios(IReadOnlyList<TerrainDepthSectionDef> profileDefs)
        {
            if (profileDefs.Count == 0) return;
            string profileID = profileDefs[0].Profile.ProfileID;

            if (Mathf.Approximately(profileDefs[0].MinDepthRatio, 0f) == false)
                throw new InvalidDataException($"첫 깊이 섹션은 0에서 시작해야 합니다: {profileID}");

            for (int i = 1; i < profileDefs.Count; i++)
            {
                TerrainDepthSectionDef previous = profileDefs[i - 1];
                TerrainDepthSectionDef current = profileDefs[i];
                if (Mathf.Approximately(previous.MaxDepthRatio, current.MinDepthRatio) == false)
                    throw new InvalidDataException($"깊이 섹션 비율이 이어지지 않습니다: {profileID}/{previous.SectionID}/{current.SectionID}");
            }

            if (Mathf.Approximately(profileDefs[^1].MaxDepthRatio, 1f) == false)
                throw new InvalidDataException($"마지막 깊이 섹션은 1에서 끝나야 합니다: {profileID}");
        }
    }

    public sealed class TerrainSectionMineralTable
    {
        private const string TableName = "Table/Terrain/terrain_section_minerals.tsv";
        private readonly Dictionary<string, List<TerrainSectionMineralDef>> defsBySection =
            new(StringComparer.Ordinal);

        public int Count { get; private set; }

        /// <summary>광물 DTO 로드와 프로필 및 섹션 및 광물 참조 해석</summary>
        public void Load(
            TerrainSeedProfileTable profileTable,
            TerrainDepthSectionTable sectionTable,
            TerrainTileTable tileTable)
        {
            defsBySection.Clear();
            Count = 0;
            Dictionary<string, HashSet<int>> ordersBySection = new(StringComparer.Ordinal);
            HashSet<string> mineralKeys = new(StringComparer.Ordinal);
            List<TerrainSectionMineralDTO> rows =
                TerrainTileTable.ReadTerrainRows<TerrainSectionMineralDTO>(TableName);

            foreach (TerrainSectionMineralDTO dto in rows)
            {
                Validate(dto);
                
                if (profileTable.TryGet(dto.ProfileID, out TerrainSeedProfileDef profile) == false)
                    throw new InvalidDataException($"등록되지 않은 ProfileID입니다: {dto.ProfileID}");
                
                if (sectionTable.TryGet(dto.ProfileID, dto.SectionID, out TerrainDepthSectionDef section) == false)
                    throw new InvalidDataException($"등록되지 않은 섹션입니다: {dto.ProfileID}/{dto.SectionID}");
                
                if (tileTable.TryGet(dto.MineralTileType, out TerrainTileDef mineralTile) == false)
                    throw new InvalidDataException($"등록되지 않은 MineralTileType입니다: {dto.MineralTileType}");

                string sectionKey = TerrainDepthSectionTable.CreateKey(dto.ProfileID, dto.SectionID);
                string mineralKey = $"{sectionKey}\u001F{dto.MineralTileType}";
                
                if (mineralKeys.Add(mineralKey) == false)
                    throw new InvalidDataException($"중복 광물입니다: {dto.ProfileID}/{dto.SectionID}/{dto.MineralTileType}");

                if (ordersBySection.TryGetValue(sectionKey, out HashSet<int> orders) == false)
                {
                    orders = new HashSet<int>();
                    ordersBySection.Add(sectionKey, orders);
                }
                
                if (orders.Add(dto.MineralOrder) == false)
                    throw new InvalidDataException($"중복 MineralOrder입니다: {dto.ProfileID}/{dto.SectionID}/{dto.MineralOrder}");

                if (defsBySection.TryGetValue(sectionKey, out List<TerrainSectionMineralDef> sectionDefs) == false)
                {
                    sectionDefs = new List<TerrainSectionMineralDef>();
                    defsBySection.Add(sectionKey, sectionDefs);
                }

                sectionDefs.Add(new TerrainSectionMineralDef(dto, profile, section, mineralTile));
                Count++;
            }

            foreach (List<TerrainSectionMineralDef> sectionDefs in defsBySection.Values)
                sectionDefs.Sort((left, right) => left.MineralOrder.CompareTo(right.MineralOrder));
        }

        /// <summary>지정 섹션의 광물 목록을 적용 순서대로 조회</summary>
        public IReadOnlyList<TerrainSectionMineralDef> GetBySection(string profileID, string sectionID)
        {
            string key = TerrainDepthSectionTable.CreateKey(profileID, sectionID);
            
            return defsBySection.TryGetValue(key, out List<TerrainSectionMineralDef> sectionDefs)
                ? sectionDefs
                : Array.Empty<TerrainSectionMineralDef>();
        }

        private static void Validate(TerrainSectionMineralDTO dto)
        {
            if (string.IsNullOrWhiteSpace(dto.ProfileID) || string.IsNullOrWhiteSpace(dto.SectionID))
                throw new InvalidDataException("ProfileID와 SectionID는 비어 있을 수 없습니다");
            
            if (dto.MineralOrder < 0 || dto.MineralTileType == TerrainTileType.Empty)
                throw new InvalidDataException($"광물 순서 또는 타일 종류가 올바르지 않습니다: {dto.SectionID}");
            
            if (dto.ClusterDensity < 0f || dto.ClusterDensity > 1f)
                throw new InvalidDataException($"ClusterDensity는 0부터 1 사이여야 합니다: {dto.SectionID}");
            
            if (dto.MinClusterSize <= 0 || dto.MaxClusterSize < dto.MinClusterSize)
                throw new InvalidDataException($"군집 크기 범위가 올바르지 않습니다: {dto.SectionID}");
        }
    }

    public sealed class TerrainVariantClusterTable
    {
        private const string TableName = "Table/Terrain/terrain_variant_clusters.tsv";

        private readonly Dictionary<string, List<TerrainVariantClusterDef>>
            defsByProfile = new(StringComparer.Ordinal);

        public int Count { get; private set; }

        public void Load(
            TerrainSeedProfileTable profileTable,
            TerrainDepthSectionTable sectionTable,
            TerrainTileTable tileTable)
        {
            defsByProfile.Clear();
            Count = 0;
            HashSet<string> ordersBySection = new(StringComparer.Ordinal);

            List<TerrainVariantClusterDTO> rows =
                TerrainTileTable.ReadTerrainRows<TerrainVariantClusterDTO>(
                    TableName);

            foreach (TerrainVariantClusterDTO dto in rows)
            {
                Validate(dto);

                if (profileTable.TryGet(dto.ProfileID, out _) == false)
                    throw new InvalidDataException($"등록되지 않은 ProfileID입니다: {dto.ProfileID}");

                if (sectionTable.TryGet(dto.ProfileID, dto.SectionID, out TerrainDepthSectionDef section) == false)
                    throw new InvalidDataException($"등록되지 않은 섹션입니다: {dto.ProfileID}/{dto.SectionID}");

                if (tileTable.TryGet(dto.VariantTileType, out TerrainTileDef variantTile) == false)
                    throw new InvalidDataException($"등록되지 않은 변칙 타일입니다: {dto.VariantTileType}");

                string orderKey = $"{dto.ProfileID}\u001F{dto.SectionID}\u001F{dto.VariantOrder}";

                if (ordersBySection.Add(orderKey) == false)
                    throw new InvalidDataException($"중복 VariantOrder입니다: {dto.ProfileID}/{dto.SectionID}/{dto.VariantOrder}");

                if (variantTile.TileType == section.BaseTile.TileType)
                    throw new InvalidDataException($"기본 블록과 변칙 블록이 같습니다: {dto.ProfileID}/{dto.SectionID}");

                if (variantTile.TileType == TerrainTileType.Empty ||
                    variantTile.TileType == TerrainTileType.Bedrock)
                    throw new InvalidDataException($"변칙 군집으로 사용할 수 없는 타일입니다: {dto.VariantTileType}");

                TerrainDepthSectionDef variantSection = null;

                foreach (TerrainDepthSectionDef profileSection in sectionTable.GetByProfile(dto.ProfileID))
                {
                    if (profileSection.BaseTile.TileType != variantTile.TileType) continue;

                    variantSection = profileSection;

                    break;
                }

                if (variantSection == null)
                    throw new InvalidDataException($"일반 계층 블록이 아닌 변칙 타일입니다: {dto.VariantTileType}");

                if (variantSection.SectionOrder > section.SectionOrder + 1)
                    throw new InvalidDataException($"두 계층 이상 아래의 블록은 생성할 수 없습니다: {dto.ProfileID}/{dto.SectionID}/{dto.VariantTileType}");

                TerrainVariantClusterDef def = new(dto, section, variantTile);

                if (defsByProfile.TryGetValue(
                        dto.ProfileID,
                        out List<TerrainVariantClusterDef> profileDefs) == false)
                {
                    profileDefs = new List<TerrainVariantClusterDef>();
                    defsByProfile.Add(dto.ProfileID, profileDefs);
                }

                profileDefs.Add(def);
                Count++;
            }

            foreach (List<TerrainVariantClusterDef> defs in defsByProfile.Values)
                defs.Sort((left, right) =>
                {
                    int sectionOrder = left.Section.SectionOrder.CompareTo(right.Section.SectionOrder);

                    return sectionOrder != 0
                        ? sectionOrder
                        : left.VariantOrder.CompareTo(right.VariantOrder);
                });
        }

        public IReadOnlyList<TerrainVariantClusterDef> GetByProfile(
            string profileID)
            => defsByProfile.TryGetValue(
                profileID,
                out List<TerrainVariantClusterDef> defs)
                ? defs
                : Array.Empty<TerrainVariantClusterDef>();

        private static void Validate(TerrainVariantClusterDTO dto)
        {
            if (string.IsNullOrWhiteSpace(dto.ProfileID) ||
                string.IsNullOrWhiteSpace(dto.SectionID))
                throw new InvalidDataException("ProfileID와 SectionID는 비어 있을 수 없습니다");

            if (dto.VariantOrder < 0 || dto.ClusterCount < 0)
                throw new InvalidDataException($"ClusterCount가 올바르지 않습니다: {dto.ProfileID}");

            if (dto.MinWidth < 3 || dto.MaxWidth > 10 ||
                dto.MinWidth > dto.MaxWidth ||
                dto.MinHeight < 3 || dto.MaxHeight > 10 ||
                dto.MinHeight > dto.MaxHeight)
                throw new InvalidDataException($"변칙 군집 크기는 3~10 범위여야 합니다: {dto.ProfileID}");

            if (dto.FillRatio <= 0f || dto.FillRatio > 1f)
                throw new InvalidDataException($"FillRatio 범위가 올바르지 않습니다: {dto.ProfileID}");
        }
    }

    public sealed class TerrainCaveTable
    {
        private const string TableName = "Table/Terrain/terrain_caves.tsv";

        private readonly Dictionary<string, List<TerrainCaveDef>> defsByProfile =
            new(StringComparer.Ordinal);

        public int Count { get; private set; }

        public void Load(TerrainSeedProfileTable profileTable)
        {
            defsByProfile.Clear();
            Count = 0;

            HashSet<string> ordersByProfile = new(StringComparer.Ordinal);
            List<TerrainCaveDTO> rows = TerrainTileTable.ReadTerrainRows<TerrainCaveDTO>(TableName);

            foreach (TerrainCaveDTO dto in rows)
            {
                Validate(dto);

                if (profileTable.TryGet(dto.ProfileID, out TerrainSeedProfileDef profile) == false)
                    throw new InvalidDataException($"등록되지 않은 ProfileID입니다: {dto.ProfileID}");

                string orderKey = $"{dto.ProfileID}\u001F{dto.CaveOrder}";

                if (ordersByProfile.Add(orderKey) == false)
                    throw new InvalidDataException($"중복 CaveOrder입니다: {dto.ProfileID}/{dto.CaveOrder}");

                TerrainCaveDef def = new(dto, profile);

                if (defsByProfile.TryGetValue(dto.ProfileID, out List<TerrainCaveDef> profileDefs) == false)
                {
                    profileDefs = new List<TerrainCaveDef>();
                    defsByProfile.Add(dto.ProfileID, profileDefs);
                }

                profileDefs.Add(def);
                Count++;
            }

            foreach (List<TerrainCaveDef> defs in defsByProfile.Values)
                defs.Sort((left, right)
                    => left.CaveOrder.CompareTo(right.CaveOrder));
        }

        public IReadOnlyList<TerrainCaveDef> GetByProfile(string profileID)
            => defsByProfile.TryGetValue(profileID,
                out List<TerrainCaveDef> defs)
            ? defs
            : Array.Empty<TerrainCaveDef>();

        private static void Validate(TerrainCaveDTO dto)
        {
            if (string.IsNullOrWhiteSpace(dto.ProfileID))
                throw new InvalidDataException("ProfileID는 비어 있을 수 없습니다");

            if (dto.CaveOrder < 0)
                throw new InvalidDataException($"CaveOrder가 올바르지 않습니다: {dto.ProfileID}");

            if (dto.MinDepthRatio < 0f ||
                dto.MaxDepthRatio > 1f ||
                dto.MinDepthRatio >= dto.MaxDepthRatio)
                throw new InvalidDataException($"동굴 깊이 범위가 올바르지 않습니다: {dto.ProfileID}");

            if (dto.CaveCount < 0)
                throw new InvalidDataException($"CaveCount가 올바르지 않습니다: {dto.ProfileID}");

            if (dto.MinLength <= 0 || dto.MaxLength < dto.MinLength)
                throw new InvalidDataException($"동굴 길이 범위가 올바르지 않습니다: {dto.ProfileID}");

            if (dto.MinRadius <= 0 || dto.MaxRadius < dto.MinRadius)
                throw new InvalidDataException($"동굴 반경 범위가 올바르지 않습니다: {dto.ProfileID}");

            if (dto.MaxAttempts <= 0)
                throw new InvalidDataException($"MaxAttempts가 올바르지 않습니다: {dto.ProfileID}");
        }
    }
}
