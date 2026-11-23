using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Realmforge.Rules.Definitions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Realmforge.Content
{
    public sealed class ContentManifest
    {
        public string Version { get; set; } = "0.0.0";
        public string MinClientVersion { get; set; } = "0.0.0";
    }

    public sealed class ContentLoadException : Exception
    {
        public ContentLoadException(string message, Exception? inner = null)
            : base(message, inner)
        {
        }
    }

    /// <summary>
    /// Loads content from YAML. Every top-level folder maps to one definition type and every file holds a list of
    /// definitions, so a new zone or continent ships as new files without engine changes.
    /// Unity passes TextAsset contents through <see cref="LoadFromFiles"/>; the server and tools read a directory.
    /// </summary>
    public static class ContentLoader
    {
        public const string ManifestFileName = "manifest.yaml";

        private static readonly IDeserializer Deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();

        private static readonly Dictionary<string, Action<ContentCatalog, string, string>> FolderLoaders =
            new Dictionary<string, Action<ContentCatalog, string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                { "races", (catalog, yaml, path) => AddAll(catalog.Races, Parse<RaceDefinition>(yaml, path), item => item.Id, path) },
                { "classes", (catalog, yaml, path) => AddAll(catalog.Classes, Parse<ClassDefinition>(yaml, path), item => item.Id, path) },
                { "abilities", (catalog, yaml, path) => AddAll(catalog.Abilities, Parse<AbilityDefinition>(yaml, path), item => item.Id, path) },
                { "perks", (catalog, yaml, path) => AddAll(catalog.Perks, Parse<PerkDefinition>(yaml, path), item => item.Id, path) },
                { "items", (catalog, yaml, path) => AddAll(catalog.Items, Parse<ItemDefinition>(yaml, path), item => item.Id, path) },
                { "loot", (catalog, yaml, path) => AddAll(catalog.LootTables, Parse<LootTable>(yaml, path), item => item.Id, path) },
                { "continents", (catalog, yaml, path) => AddAll(catalog.Continents, Parse<ContinentDefinition>(yaml, path), item => item.Id, path) },
                { "zones", (catalog, yaml, path) => AddAll(catalog.Zones, Parse<ZoneDefinition>(yaml, path), item => item.Id, path) },
                { "mobs", (catalog, yaml, path) => AddAll(catalog.Mobs, Parse<MobDefinition>(yaml, path), item => item.Id, path) },
                { "instances", (catalog, yaml, path) => AddAll(catalog.Instances, Parse<InstanceDefinition>(yaml, path), item => item.Id, path) },
                { "npcs", (catalog, yaml, path) => AddAll(catalog.Npcs, Parse<NpcDefinition>(yaml, path), item => item.Id, path) },
                { "factions", (catalog, yaml, path) => AddAll(catalog.Factions, Parse<FactionDefinition>(yaml, path), item => item.Id, path) },
                { "quests", (catalog, yaml, path) => AddAll(catalog.Quests, Parse<QuestDefinition>(yaml, path), item => item.Id, path) },
                { "professions", (catalog, yaml, path) => AddAll(catalog.Professions, Parse<ProfessionDefinition>(yaml, path), item => item.Id, path) },
                { "nodes", (catalog, yaml, path) => AddAll(catalog.Nodes, Parse<GatheringNodeDefinition>(yaml, path), item => item.Id, path) },
                { "recipes", (catalog, yaml, path) => AddAll(catalog.Recipes, Parse<CraftingRecipe>(yaml, path), item => item.Id, path) },
                { "offline_jobs", (catalog, yaml, path) => AddAll(catalog.OfflineJobs, Parse<OfflineJobDefinition>(yaml, path), item => item.Id, path) },
                { "housing", (catalog, yaml, path) => AddAll(catalog.HouseModules, Parse<HouseModuleDefinition>(yaml, path), item => item.Id, path) },
                { "cosmetics", (catalog, yaml, path) => AddAll(catalog.Cosmetics, Parse<CosmeticDefinition>(yaml, path), item => item.Id, path) },
                { "store", (catalog, yaml, path) => AddAll(catalog.StoreSkus, Parse<StoreSkuDefinition>(yaml, path), item => item.Id, path) },
            };

        public static IReadOnlyCollection<string> KnownFolders => FolderLoaders.Keys;

        public static ContentCatalog LoadDirectory(string rootDirectory)
        {
            if (!Directory.Exists(rootDirectory))
            {
                throw new ContentLoadException("Content directory not found: " + rootDirectory);
            }
            IEnumerable<KeyValuePair<string, string>> files = Directory
                .EnumerateFiles(rootDirectory, "*.yaml", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => new KeyValuePair<string, string>(
                    Path.GetRelativePath(rootDirectory, path).Replace('\\', '/'),
                    File.ReadAllText(path)));
            return LoadFromFiles(files);
        }

        /// <param name="files">Pairs of relative path (e.g. "races/core.yaml") and YAML text.</param>
        public static ContentCatalog LoadFromFiles(IEnumerable<KeyValuePair<string, string>> files)
        {
            var catalog = new ContentCatalog();
            foreach (KeyValuePair<string, string> file in files)
            {
                string relativePath = file.Key.Replace('\\', '/');
                if (relativePath.Equals(ManifestFileName, StringComparison.OrdinalIgnoreCase))
                {
                    catalog.Version = ParseSingle<ContentManifest>(file.Value, relativePath).Version;
                    continue;
                }
                string folder = relativePath.Split('/')[0];
                if (!FolderLoaders.TryGetValue(folder, out Action<ContentCatalog, string, string>? loader))
                {
                    throw new ContentLoadException("Unknown content folder '" + folder + "' in " + relativePath);
                }
                loader(catalog, file.Value, relativePath);
            }
            return catalog;
        }

        private static List<T> Parse<T>(string yaml, string path)
        {
            try
            {
                return Deserializer.Deserialize<List<T>>(yaml) ?? new List<T>();
            }
            catch (Exception exception)
            {
                throw new ContentLoadException("Failed to parse " + path + ": " + exception.Message, exception);
            }
        }

        private static T ParseSingle<T>(string yaml, string path)
            where T : new()
        {
            try
            {
                return Deserializer.Deserialize<T>(yaml) ?? new T();
            }
            catch (Exception exception)
            {
                throw new ContentLoadException("Failed to parse " + path + ": " + exception.Message, exception);
            }
        }

        private static void AddAll<T>(Dictionary<string, T> target, List<T> items, Func<T, string> idOf, string path)
        {
            foreach (T item in items)
            {
                string id = idOf(item);
                if (string.IsNullOrWhiteSpace(id))
                {
                    throw new ContentLoadException("Definition without id in " + path);
                }
                if (target.ContainsKey(id))
                {
                    throw new ContentLoadException("Duplicate id '" + id + "' in " + path);
                }
                target[id] = item;
            }
        }
    }
}
