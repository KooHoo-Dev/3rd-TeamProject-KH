using UnityEngine;

namespace TerrainCollapse
{
    // Grid 셀이 보관하는 아이템 및 골드 수량 정보

    [System.Serializable]
    public struct TerrainCellLootEntry
    {
        public int ItemID;
        public int Quantity;

        public TerrainCellLootEntry(int itemID, int quantity)
        {
            ItemID = itemID;
            Quantity = Mathf.Max(0, quantity);
        }
    }
}
