using System;
using System.Collections.Generic;

namespace Realmforge.Rules.Combat
{
    public readonly struct GridPosition : IEquatable<GridPosition>
    {
        public GridPosition(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; }
        public int Y { get; }

        /// <summary>5e simple diagonal rule: every step, including diagonal, costs one cell.</summary>
        public int DistanceTo(GridPosition other)
        {
            return Math.Max(Math.Abs(X - other.X), Math.Abs(Y - other.Y));
        }

        public bool Equals(GridPosition other)
        {
            return X == other.X && Y == other.Y;
        }

        public override bool Equals(object? obj)
        {
            return obj is GridPosition other && Equals(other);
        }

        public override int GetHashCode()
        {
            return X * 7919 + Y;
        }

        public override string ToString()
        {
            return "(" + X + "," + Y + ")";
        }

        public static bool operator ==(GridPosition left, GridPosition right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(GridPosition left, GridPosition right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>
    /// Square tactical grid laid over the open world when an encounter starts. One cell = 5 ft (1.5 m).
    /// Grid coordinate (0,0) maps to <see cref="OriginX"/>/<see cref="OriginZ"/> in world meters.
    /// </summary>
    public sealed class CombatGrid
    {
        public const float CellSizeMeters = 1.5f;

        private static readonly GridPosition[] Neighbours =
        {
            new GridPosition(-1, -1), new GridPosition(0, -1), new GridPosition(1, -1),
            new GridPosition(-1, 0), new GridPosition(1, 0),
            new GridPosition(-1, 1), new GridPosition(0, 1), new GridPosition(1, 1),
        };

        private readonly HashSet<GridPosition> _blocked = new HashSet<GridPosition>();

        public CombatGrid(int width, int height, float originX, float originZ)
        {
            Width = width;
            Height = height;
            OriginX = originX;
            OriginZ = originZ;
        }

        public int Width { get; }
        public int Height { get; }
        public float OriginX { get; }
        public float OriginZ { get; }

        public IEnumerable<GridPosition> BlockedCells => _blocked;

        /// <summary>Builds a square arena of (2 * radius + 1) cells centred on a world point.</summary>
        public static CombatGrid AroundWorldPoint(float centerX, float centerZ, int radiusCells)
        {
            int size = radiusCells * 2 + 1;
            float originX = centerX - radiusCells * CellSizeMeters;
            float originZ = centerZ - radiusCells * CellSizeMeters;
            return new CombatGrid(size, size, originX, originZ);
        }

        public bool InBounds(GridPosition position)
        {
            return position.X >= 0 && position.Y >= 0 && position.X < Width && position.Y < Height;
        }

        public void Block(GridPosition position)
        {
            _blocked.Add(position);
        }

        public bool IsBlocked(GridPosition position)
        {
            return !InBounds(position) || _blocked.Contains(position);
        }

        public GridPosition WorldToCell(float worldX, float worldZ)
        {
            int x = (int)Math.Round((worldX - OriginX) / CellSizeMeters);
            int y = (int)Math.Round((worldZ - OriginZ) / CellSizeMeters);
            return new GridPosition(Math.Max(0, Math.Min(Width - 1, x)), Math.Max(0, Math.Min(Height - 1, y)));
        }

        public void CellToWorld(GridPosition cell, out float worldX, out float worldZ)
        {
            worldX = OriginX + cell.X * CellSizeMeters;
            worldZ = OriginZ + cell.Y * CellSizeMeters;
        }

        /// <summary>Breadth-first step costs from <paramref name="start"/> up to <paramref name="maxSteps"/>.</summary>
        public Dictionary<GridPosition, int> ReachableCells(GridPosition start, int maxSteps, Func<GridPosition, bool> isOccupied)
        {
            var costs = new Dictionary<GridPosition, int> { { start, 0 } };
            var frontier = new Queue<GridPosition>();
            frontier.Enqueue(start);
            while (frontier.Count > 0)
            {
                GridPosition current = frontier.Dequeue();
                int cost = costs[current];
                if (cost >= maxSteps)
                {
                    continue;
                }
                foreach (GridPosition offset in Neighbours)
                {
                    var next = new GridPosition(current.X + offset.X, current.Y + offset.Y);
                    if (costs.ContainsKey(next) || IsBlocked(next) || isOccupied(next))
                    {
                        continue;
                    }
                    costs[next] = cost + 1;
                    frontier.Enqueue(next);
                }
            }
            return costs;
        }

        public GridPosition? NearestFreeCell(GridPosition around, Func<GridPosition, bool> isOccupied)
        {
            Dictionary<GridPosition, int> reachable = ReachableCells(around, Math.Max(Width, Height), _ => false);
            GridPosition? best = null;
            int bestCost = int.MaxValue;
            foreach (KeyValuePair<GridPosition, int> pair in reachable)
            {
                if (pair.Value < bestCost && !isOccupied(pair.Key) && !IsBlocked(pair.Key))
                {
                    best = pair.Key;
                    bestCost = pair.Value;
                }
            }
            return best;
        }
    }
}
