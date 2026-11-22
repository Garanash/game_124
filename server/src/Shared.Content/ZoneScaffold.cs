using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Realmforge.Rules.Definitions;

namespace Realmforge.Content
{
    public sealed class ZoneScaffoldRequest
    {
        public string ZoneId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ContinentId { get; set; } = string.Empty;
        public ZoneKind Kind { get; set; } = ZoneKind.Wilderness;
        /// <summary>Existing zone the new one is reachable from; gets a portal back on the new side.</summary>
        public string? LinkedZoneId { get; set; }
    }

    /// <summary>
    /// First step of shipping a new location: emits a valid zone file so designers start from
    /// something that already loads, validates and maps to an Addressables scene key.
    /// </summary>
    public static class ZoneScaffold
    {
        public const float DefaultSizeMeters = 512f;
        private const float EdgeOffsetMeters = 40f;
        private static readonly Regex ContentIdPattern = new Regex("^[a-z][a-z0-9_]*$");

        public static string SceneAddress(string continentId, string zoneId) => $"scenes/{continentId}/{zoneId}";

        public static string RelativePath(string zoneId) => $"zones/{zoneId}.yaml";

        public static string CreateYaml(ZoneScaffoldRequest request, ContentCatalog catalog)
        {
            Validate(request, catalog);
            float center = DefaultSizeMeters / 2;
            var yaml = new StringBuilder();
            yaml.AppendLine("# Scaffolded by ContentTool new-zone; add mobs, nodes and NPCs, then run validate.");
            yaml.AppendLine($"- id: {request.ZoneId}");
            yaml.AppendLine($"  name: \"{request.Name.Replace("\"", "'")}\"");
            yaml.AppendLine($"  continent_id: {request.ContinentId}");
            yaml.AppendLine($"  kind: {request.Kind}");
            yaml.AppendLine($"  scene_address: {SceneAddress(request.ContinentId, request.ZoneId)}");
            yaml.AppendLine($"  size_meters: {Format(DefaultSizeMeters)}");
            yaml.AppendLine($"  spawn: {Point(center, center)}");
            if (request.LinkedZoneId == null)
            {
                yaml.AppendLine("  portals: []");
            }
            else
            {
                ZoneDefinition linked = catalog.Zones[request.LinkedZoneId];
                yaml.AppendLine("  portals:");
                yaml.AppendLine($"    - {{ to_zone_id: {linked.Id}, position: {Point(center, EdgeOffsetMeters)}, " +
                                $"arrival: {Point(linked.Spawn.X, linked.Spawn.Z)} }}");
            }
            yaml.AppendLine("  mobs: []");
            yaml.AppendLine("  nodes: []");
            return yaml.ToString();
        }

        private static void Validate(ZoneScaffoldRequest request, ContentCatalog catalog)
        {
            if (!ContentIdPattern.IsMatch(request.ZoneId))
            {
                throw new ArgumentException($"Zone id '{request.ZoneId}' must be lower_snake_case.");
            }
            if (catalog.Zones.ContainsKey(request.ZoneId))
            {
                throw new ArgumentException($"Zone '{request.ZoneId}' already exists.");
            }
            if (!catalog.Continents.ContainsKey(request.ContinentId))
            {
                throw new ArgumentException($"Unknown continent '{request.ContinentId}'.");
            }
            if (request.LinkedZoneId != null && !catalog.Zones.ContainsKey(request.LinkedZoneId))
            {
                throw new ArgumentException($"Unknown linked zone '{request.LinkedZoneId}'.");
            }
        }

        private static string Point(float x, float z) => $"{{ x: {Format(x)}, z: {Format(z)} }}";

        private static string Format(float value) => value.ToString("0.0", CultureInfo.InvariantCulture);
    }
}
