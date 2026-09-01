using System;

namespace TerrainCollapse.Networking
{
    // 서버와 클라이언트가 공유하는 Unity 비의존 Grid 좌표 값

    [Serializable]
    public struct GridCoord : IEquatable<GridCoord>, IComparable<GridCoord>
    {
        public int X { get; set; }
        public int Y { get; set; }

        public GridCoord(int x, int y)
        {
            X = x;
            Y = y;
        }

        public bool Equals(GridCoord other) => X == other.X && Y == other.Y;

        public override bool Equals(object obj)
            => obj is GridCoord other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X, Y);

        public int CompareTo(GridCoord other)
        {
            int yComparison = Y.CompareTo(other.Y);
            return yComparison != 0 ? yComparison : X.CompareTo(other.X);
        }

        public override string ToString() => $"({X}, {Y})";

        public static bool operator ==(GridCoord left, GridCoord right) => left.Equals(right);
        public static bool operator !=(GridCoord left, GridCoord right) => left.Equals(right);
    }
}
