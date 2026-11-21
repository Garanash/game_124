using Realmforge.Content;
using Realmforge.Rules.Definitions;

namespace Realmforge.Server.Core;

/// <summary>Locates and loads the YAML content every server process shares; refuses to start on content errors.</summary>
public static class ContentSource
{
    public const string EnvironmentVariable = "REALMFORGE_CONTENT";
    private const string ManifestFileName = "manifest.yaml";
    private const string ContentFolderName = "content";

    public static ContentCatalog Load(string? configuredPath = null)
    {
        string directory = Resolve(configuredPath);
        ContentCatalog catalog = ContentLoader.LoadDirectory(directory);
        List<ContentIssue> errors = ContentValidator.Validate(catalog).Where(issue => issue.Severity == IssueSeverity.Error).ToList();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException("Content has errors:\n" + string.Join("\n", errors.Select(error => error.Where + ": " + error.Message)));
        }
        return catalog;
    }

    private static string Resolve(string? configuredPath)
    {
        string? explicitPath = configuredPath ?? Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return explicitPath;
        }
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current != null; current = current.Parent)
        {
            string candidate = Path.Combine(current.FullName, ContentFolderName);
            if (File.Exists(Path.Combine(candidate, ManifestFileName)))
            {
                return candidate;
            }
        }
        throw new DirectoryNotFoundException($"Content folder not found; set {EnvironmentVariable}.");
    }
}
