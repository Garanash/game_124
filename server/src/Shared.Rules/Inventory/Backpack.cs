using System;
using System.Collections.Generic;
using System.Linq;
using Realmforge.Rules.Definitions;

namespace Realmforge.Rules.Inventory
{
    public sealed class PlacedItem
    {
        public Guid InstanceId { get; set; }
        public string ItemId { get; set; } = string.Empty;
        public int X { get; set; }
        public int Y { get; set; }
        /// <summary>Quarter turns clockwise, 0..3.</summary>
        public int Rotation { get; set; }
    }

    /// <summary>
    /// Diablo-style grid backpack: shaped items are stored here; only what is worn on the doll (<see cref="Equipment"/>) counts.
    /// </summary>
    public sealed class Backpack
    {
        public const int DefaultWidth = 14;
        public const int DefaultHeight = 6;
        private const int RotationCount = 4;

        public int Width { get; set; } = DefaultWidth;
        public int Height { get; set; } = DefaultHeight;
        public List<PlacedItem> Items { get; set; } = new List<PlacedItem>();

        public static IReadOnlyList<(int X, int Y)> ShapeCells(ItemDefinition definition, int rotation)
        {
            List<(int X, int Y)> cells = definition.Shape.Count == 0
                ? new List<(int X, int Y)> { (0, 0) }
                : definition.Shape.Select(offset => (offset.X, offset.Y)).ToList();
            for (int turn = 0; turn < ((rotation % RotationCount) + RotationCount) % RotationCount; turn++)
            {
                cells = cells.Select(cell => (-cell.Y, cell.X)).ToList();
            }
            int minX = cells.Min(cell => cell.X);
            int minY = cells.Min(cell => cell.Y);
            return cells.Select(cell => (cell.X - minX, cell.Y - minY)).ToList();
        }

        public IEnumerable<(int X, int Y)> OccupiedCells(PlacedItem item, ContentCatalog catalog)
        {
            ItemDefinition definition = catalog.Items[item.ItemId];
            return ShapeCells(definition, item.Rotation).Select(cell => (item.X + cell.X, item.Y + cell.Y));
        }

        public RuleResult CanPlace(string itemId, int x, int y, int rotation, ContentCatalog catalog, Guid? ignoreInstance = null)
        {
            if (!catalog.Items.TryGetValue(itemId, out ItemDefinition? definition))
            {
                return RuleResult.Fail("backpack.unknown_item", "Unknown item " + itemId);
            }
            var taken = new HashSet<(int X, int Y)>(Items
                .Where(item => item.InstanceId != ignoreInstance)
                .SelectMany(item => OccupiedCells(item, catalog)));
            foreach ((int X, int Y) cell in ShapeCells(definition, rotation))
            {
                int cellX = x + cell.X;
                int cellY = y + cell.Y;
                if (cellX < 0 || cellY < 0 || cellX >= Width || cellY >= Height)
                {
                    return RuleResult.Fail("backpack.out_of_bounds", "Item does not fit inside the backpack.");
                }
                if (taken.Contains((cellX, cellY)))
                {
                    return RuleResult.Fail("backpack.overlap", "Cells are already occupied.");
                }
            }
            return RuleResult.Ok();
        }

        public PlacedItem Place(Guid instanceId, string itemId, int x, int y, int rotation, ContentCatalog catalog)
        {
            RuleResult check = CanPlace(itemId, x, y, rotation, catalog);
            if (!check.Success)
            {
                throw new InvalidOperationException(check.ToString());
            }
            var placed = new PlacedItem { InstanceId = instanceId, ItemId = itemId, X = x, Y = y, Rotation = rotation };
            Items.Add(placed);
            return placed;
        }

        public bool TryAutoPlace(Guid instanceId, string itemId, ContentCatalog catalog, out PlacedItem? placed)
        {
            for (int rotation = 0; rotation < RotationCount; rotation++)
            {
                for (int y = 0; y < Height; y++)
                {
                    for (int x = 0; x < Width; x++)
                    {
                        if (CanPlace(itemId, x, y, rotation, catalog).Success)
                        {
                            placed = Place(instanceId, itemId, x, y, rotation, catalog);
                            return true;
                        }
                    }
                }
            }
            placed = null;
            return false;
        }

        public RuleResult Move(Guid instanceId, int x, int y, int rotation, ContentCatalog catalog)
        {
            PlacedItem? item = Find(instanceId);
            if (item == null)
            {
                return RuleResult.Fail("backpack.not_found", "Item not in backpack.");
            }
            RuleResult check = CanPlace(item.ItemId, x, y, rotation, catalog, instanceId);
            if (!check.Success)
            {
                return check;
            }
            item.X = x;
            item.Y = y;
            item.Rotation = rotation;
            return RuleResult.Ok();
        }

        public PlacedItem? Find(Guid instanceId)
        {
            return Items.FirstOrDefault(item => item.InstanceId == instanceId);
        }

        public bool Remove(Guid instanceId)
        {
            return Items.RemoveAll(item => item.InstanceId == instanceId) > 0;
        }

        /// <summary>Saves from the time of the small 6×4 backpack keep their items and get the full grid.</summary>
        public bool GrowToDefault()
        {
            bool grown = Width < DefaultWidth || Height < DefaultHeight;
            Width = Math.Max(Width, DefaultWidth);
            Height = Math.Max(Height, DefaultHeight);
            return grown;
        }
    }
}
