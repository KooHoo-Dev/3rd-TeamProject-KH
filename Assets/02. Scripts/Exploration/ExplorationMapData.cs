using System;
using UnityEngine;

namespace TerrainCollapse
{
    // 맵 좌표별 탐험 완료 상태 관리

    public sealed class ExplorationMapData
    {
        private readonly bool[] exploredCells;

        public int Width { get; }
        public int Height { get; }

        public ExplorationMapData(int width, int height)
        {
            Width = Mathf.Max(0, width);
            Height = Mathf.Max(0, height);
            exploredCells = new bool[Width * Height];
        }

        public bool IsInside(Vector2Int cell)
        {
            return cell.x >= 0 && cell.x < Width && cell.y >= 0 && cell.y < Height;
        }

        public bool IsExplored(Vector2Int cell)
        {
            return IsInside(cell) && exploredCells[GetIndex(cell)];
        }

        public bool TryExplore(Vector2Int cell)
        {
            if (IsInside(cell) == false) return false;

            int index = GetIndex(cell);

            if (exploredCells[index]) return false;

            exploredCells[index] = true;
            return true;
        }

        private int GetIndex(Vector2Int cell)
        {
            return cell.y * Width + cell.x;
        }

        public ExplorationSnapshot CreateSnapshot()
        {
            ExplorationSnapshot snapshot = new()
            {
                MapWidth = Width,
                MapHeight = Height
            };

            for (int index = 0; index < exploredCells.Length; index++)
            {
                if (exploredCells[index] == false) continue;

                snapshot.ExploredCellIndices.Add(index);
            }

            return snapshot;
        }

        public bool TryApplySnapshot(ExplorationSnapshot snapshot)
        {
            if (snapshot == null) return false;
            if (snapshot.MapWidth != Width || snapshot.MapHeight != Height) return false;

            Array.Clear(exploredCells, 0, exploredCells.Length);

            foreach (int index in snapshot.ExploredCellIndices)
            {
                if (index < 0 || index >= exploredCells.Length) continue;

                exploredCells[index] = true;
            }

            return true;
        }
    }
}
