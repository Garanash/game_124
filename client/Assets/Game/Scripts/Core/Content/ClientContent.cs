using System;
using System.Collections.Generic;
using System.Linq;
using Realmforge.Content;
using Realmforge.Rules.Definitions;

namespace Realmforge.Client.Core.Content
{
    /// <summary>
    /// Loads the same YAML content the server uses from files exported by <c>ContentTool export-unity</c>.
    /// The index lists paths like "races/elves.yaml"; the reader resolves one path to its text.
    /// </summary>
    public static class ClientContent
    {
        public const string ResourcesFolder = "Content";
        public const string IndexName = "index";

        public static ContentCatalog Load(string indexText, Func<string, string?> readFile)
        {
            IEnumerable<string> paths = indexText
                .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0);
            var files = new List<KeyValuePair<string, string>>();
            foreach (string path in paths)
            {
                string? text = readFile(path);
                if (text == null)
                {
                    throw new ContentLoadException($"Content file {path} is listed in the index but missing from the build.");
                }
                files.Add(new KeyValuePair<string, string>(path, text));
            }
            return ContentLoader.LoadFromFiles(files);
        }

        /// <summary>Resources.Load wants the path without extension, relative to a Resources folder.</summary>
        public static string ResourcePath(string contentPath)
        {
            int extension = contentPath.LastIndexOf('.');
            string withoutExtension = extension > 0 ? contentPath.Substring(0, extension) : contentPath;
            return ResourcesFolder + "/" + withoutExtension;
        }
    }
}
