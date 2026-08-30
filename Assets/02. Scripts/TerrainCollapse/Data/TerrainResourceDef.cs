using System;

namespace TerrainCollapse
{
    // 일반 지형 셀에 부착되는 자원과 드롭 아이템 관계 정의

    [Serializable]
    public sealed class TerrainResourceDTO
    {
        public int ResourceID;
        public string Name;
        public int MaxDurability;
        public string PrefabKey;
        public int DropItemID;
        public int DropCount;
    }

    public sealed class TerrainResourceDef
    {
        public int ResourceID { get; }
        public string Name { get; }
        public int MaxDurability { get; }
        public string PrefabKey { get; }
        public int DropItemID { get; }
        public int DropCount { get; }

        public TerrainResourceDef(TerrainResourceDTO dto)
        {
            ResourceID = dto.ResourceID;
            Name = dto.Name;
            MaxDurability = dto.MaxDurability;
            PrefabKey = dto.PrefabKey;
            DropItemID = dto.DropItemID;
            DropCount = dto.DropCount;
        }
    }
}
