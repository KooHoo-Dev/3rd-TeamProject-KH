using System.Collections.Generic;
using UnityEngine;

namespace TerrainCollapse
{
    // Static Grid 지형에 포함된 자원 표시 오브젝트 관리

    public sealed class TerrainResourceViewManager : MonoBehaviour
    {
        private readonly Dictionary<Vector2Int, TerrainResourceView> views = new();

        private Transform resourceViewRoot;

        public void RebuildAll(TerrainGridWorld world)
        {
            Clear();

            foreach (Vector2Int coord in world.GetOccupiedCells())
                RefreshCell(world, coord);
        }

        public void RefreshCell(TerrainGridWorld world, Vector2Int coord)
        {
            TerrainCell cell = world.Data.GetCellOrNull(coord);

            if (cell == null || cell.ResourceID <= 0)
            {
                RemoveView(coord);
                return;
            }

            if (views.TryGetValue(coord, out TerrainResourceView currentView))
            {
                if (currentView != null && currentView.ResourceID == cell.ResourceID)
                    return;

                RemoveView(coord);
            }

            EnsureRoot();

            Vector3 localPosition = new(
                (coord.x + 0.5f) * world.CellSize,
                (coord.y + 0.5f) * world.CellSize,
                -0.1f);

            TerrainResourceView view = TerrainResourceViewFactory.Create(
                cell.ResourceID,
                resourceViewRoot,
                localPosition);

            if (view != null)
                views.Add(coord, view);
        }

        public void Clear()
        {
            foreach (TerrainResourceView view in views.Values)
                Destroy(view?.gameObject);

            views.Clear();
        }

        private void EnsureRoot()
        {
            if (resourceViewRoot != null) return;

            GameObject root = new("TerrainResourceViews");
            root.transform.SetParent(transform, false);
            resourceViewRoot = root.transform;
        }

        private void RemoveView(Vector2Int coord)
        {
            if (views.Remove(coord, out TerrainResourceView view) == false)
                return;

            Destroy(view?.gameObject);
        }
    }
}
