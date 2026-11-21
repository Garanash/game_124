using Realmforge.Content;
using Realmforge.Rules.Definitions;

const int ExitOk = 0;
const int ExitUsage = 1;
const int ExitInvalidContent = 2;
const string UnityExtension = ".txt";

if (args.Length < 2)
{
    PrintUsage();
    return ExitUsage;
}

string command = args[0];
string contentDirectory = args[1];

try
{
    ContentCatalog catalog = ContentLoader.LoadDirectory(contentDirectory);
    switch (command)
    {
        case "validate":
            return Validate(catalog);
        case "codegen" when args.Length >= 3:
            File.WriteAllText(args[2], ContentIdGenerator.Generate(catalog));
            Console.WriteLine($"Generated {args[2]}");
            return ExitOk;
        case "export-unity" when args.Length >= 3:
            return ExportForUnity(contentDirectory, args[2]);
        default:
            PrintUsage();
            return ExitUsage;
    }
}
catch (ContentLoadException exception)
{
    Console.Error.WriteLine("ERROR " + exception.Message);
    return ExitInvalidContent;
}

static int Validate(ContentCatalog catalog)
{
    IReadOnlyList<ContentIssue> issues = ContentValidator.Validate(catalog);
    foreach (ContentIssue issue in issues)
    {
        Console.WriteLine(issue);
    }
    int errors = issues.Count(issue => issue.Severity == IssueSeverity.Error);
    int warnings = issues.Count - errors;
    Console.WriteLine($"Content {catalog.Version}: {catalog.Races.Count} races, {catalog.Classes.Count} classes, " +
                      $"{catalog.Zones.Count} zones, {catalog.Quests.Count} quests, {catalog.Items.Count} items. " +
                      $"{errors} errors, {warnings} warnings.");
    return errors == 0 ? ExitOk : ExitInvalidContent;
}

// Unity only imports known TextAsset extensions, so YAML is mirrored as .txt under Resources.
static int ExportForUnity(string contentDirectory, string outputDirectory)
{
    if (Directory.Exists(outputDirectory))
    {
        foreach (string stale in Directory.EnumerateFiles(outputDirectory, "*" + UnityExtension, SearchOption.AllDirectories))
        {
            File.Delete(stale);
        }
    }
    var exported = new List<string>();
    foreach (string source in Directory.EnumerateFiles(contentDirectory, "*.yaml", SearchOption.AllDirectories))
    {
        string relative = Path.GetRelativePath(contentDirectory, source);
        string target = Path.Combine(outputDirectory, Path.ChangeExtension(relative, UnityExtension));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, overwrite: true);
        exported.Add(relative.Replace('\\', '/'));
    }
    File.WriteAllLines(Path.Combine(outputDirectory, "index" + UnityExtension), exported.OrderBy(path => path, StringComparer.Ordinal));
    Console.WriteLine($"Exported {exported.Count} content files to {outputDirectory}");
    return ExitOk;
}

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  ContentTool validate <contentDir>");
    Console.WriteLine("  ContentTool codegen <contentDir> <output.cs>");
    Console.WriteLine("  ContentTool export-unity <contentDir> <unityResourcesDir>");
}
