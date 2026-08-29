using UnityEngine;

namespace TerrainCollapse
{
    // 지형과 필드 드롭의 물리 레이어 및 충돌 관계 관리

    internal static class TerrainPhysicsLayers
    {
        private const string TerrainLayerName = "Terrain";
        private const string ItemDropLayerName = "ItemDrop";

        private static bool isDropCollisionConfigured;

        public static int Terrain => LayerMask.NameToLayer(TerrainLayerName);
        public static int ItemDrop => LayerMask.NameToLayer(ItemDropLayerName);

        public static void ConfigureDropCollision()
        {
            if (isDropCollisionConfigured) return;

            int terrainLayer = Terrain;
            int itemDropLayer = ItemDrop;

            for (int layer = 0; layer < 32; layer++)
                Physics2D.IgnoreLayerCollision(itemDropLayer, layer, layer != terrainLayer);

            isDropCollisionConfigured = true;
        }
    }
}
