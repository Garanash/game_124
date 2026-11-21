using Realmforge.Rules.Definitions;

namespace Realmforge.Content.Tests;

internal static class ContentRepository
{
    private static readonly Lazy<ContentCatalog> Shipped = new(() => ContentLoader.LoadDirectory(FindContentDirectory()));

    public static ContentCatalog Catalog => Shipped.Value;

    public static string FindContentDirectory()
    {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Realmforge.sln")))
        {
            directory = directory.Parent;
        }
        if (directory == null)
        {
            throw new DirectoryNotFoundException("Repository root with Realmforge.sln not found.");
        }
        return Path.Combine(directory.FullName, "content");
    }
}
