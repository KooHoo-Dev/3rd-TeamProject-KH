using UnityEngine;

namespace TerrainCollapse
{
    // ResourceID에 대응하는 자원 표시 프리팹 생성

    internal static class TerrainResourceViewFactory
    {
        public static TerrainResourceView Create(
            int resourceID,
            Transform parent,
            Vector3 localPosition)
        {
            if (resourceID <= 0) return null;

            TerrainDataManager dataManager = TerrainDataManager.Instance;
            dataManager.EnsureLoaded();

            if (dataManager.Resources.TryGet(resourceID, out TerrainResourceDef resourceDef) == false)
            {
                Debug.LogWarning($"[Terrain Resource] Resource definition missing: {resourceID}");
                return null;
            }

            TerrainResourceView prefab =
                UnityEngine.Resources.Load<TerrainResourceView>(resourceDef.PrefabKey);

            if (prefab == null)
            {
                Debug.LogWarning(
                    $"[Terrain Resource] Resource prefab missing: {resourceDef.PrefabKey}");
                return null;
            }

            TerrainResourceView view = Object.Instantiate(prefab, parent, false);
            view.transform.localPosition = localPosition;
            view.Initialize(resourceID);

            return view;
        }
    }
}
