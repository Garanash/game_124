using System;
using System.Collections.Generic;
using System.Linq;
using Realmforge.Rules.Definitions;
using Realmforge.Rules.Inventory;
using Realmforge.Rules.Stats;

namespace Realmforge.Rules.Housing
{
    public sealed class PlacedModule
    {
        public Guid InstanceId { get; set; }
        public string ModuleId { get; set; } = string.Empty;
        public int X { get; set; }
        public int Z { get; set; }
        public bool Rotated { get; set; }
    }

    /// <summary>A buildable plot in an instanced housing district; modules snap to a coarse grid.</summary>
    public sealed class HousePlot
    {
        public const int DefaultSize = 12;

        public string DistrictZoneId { get; set; } = string.Empty;
        public int PlotNumber { get; set; }
        public int Width { get; set; } = DefaultSize;
        public int Depth { get; set; } = DefaultSize;
        public List<PlacedModule> Modules { get; set; } = new List<PlacedModule>();
        public StackInventory Chest { get; set; } = new StackInventory();

        public RuleResult CanBuild(string moduleId, int x, int z, bool rotated, ContentCatalog catalog, StackInventory materials)
        {
            if (!catalog.HouseModules.TryGetValue(moduleId, out HouseModuleDefinition? module))
            {
                return RuleResult.Fail("housing.unknown_module", "Unknown module " + moduleId);
            }
            if (!module.IsFoundation && !Modules.Any(placed => catalog.HouseModules[placed.ModuleId].IsFoundation))
            {
                return RuleResult.Fail("housing.no_foundation", "Lay a foundation first.");
            }
            Footprint(module, rotated, out int width, out int depth);
            if (x < 0 || z < 0 || x + width > Width || z + depth > Depth)
            {
                return RuleResult.Fail("housing.out_of_plot", "Module does not fit on the plot.");
            }
            bool overlaps = Modules
                .Where(placed => catalog.HouseModules[placed.ModuleId].IsFoundation == module.IsFoundation)
                .Any(placed => Overlaps(placed, catalog, x, z, width, depth));
            if (overlaps)
            {
                return RuleResult.Fail("housing.overlap", "Space is already taken.");
            }
            return materials.HasAll(module.Cost) ? RuleResult.Ok() : RuleResult.Fail("housing.materials", "Not enough materials.");
        }

        public RuleResult Build(Guid instanceId, string moduleId, int x, int z, bool rotated, ContentCatalog catalog, StackInventory materials)
        {
            RuleResult check = CanBuild(moduleId, x, z, rotated, catalog, materials);
            if (!check.Success)
            {
                return check;
            }
            materials.TryRemoveAll(catalog.HouseModules[moduleId].Cost);
            Modules.Add(new PlacedModule { InstanceId = instanceId, ModuleId = moduleId, X = x, Z = z, Rotated = rotated });
            return RuleResult.Ok();
        }

        public bool Demolish(Guid instanceId, ContentCatalog catalog, StackInventory refundTo)
        {
            PlacedModule? module = Modules.FirstOrDefault(placed => placed.InstanceId == instanceId);
            if (module == null)
            {
                return false;
            }
            Modules.Remove(module);
            foreach (ItemQuantity cost in catalog.HouseModules[module.ModuleId].Cost)
            {
                refundTo.Add(cost.ItemId, cost.Quantity / 2);
            }
            return true;
        }

        public StatBlock Bonuses(ContentCatalog catalog)
        {
            var block = new StatBlock();
            foreach (PlacedModule module in Modules)
            {
                block.AddRange(catalog.HouseModules[module.ModuleId].Bonuses);
            }
            return block;
        }

        private static void Footprint(HouseModuleDefinition module, bool rotated, out int width, out int depth)
        {
            width = rotated ? module.Depth : module.Width;
            depth = rotated ? module.Width : module.Depth;
        }

        private static bool Overlaps(PlacedModule placed, ContentCatalog catalog, int x, int z, int width, int depth)
        {
            Footprint(catalog.HouseModules[placed.ModuleId], placed.Rotated, out int placedWidth, out int placedDepth);
            return x < placed.X + placedWidth && placed.X < x + width && z < placed.Z + placedDepth && placed.Z < z + depth;
        }
    }
}
