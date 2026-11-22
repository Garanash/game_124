using Realmforge.Rules.Definitions;
using Xunit;

namespace Realmforge.Content.Tests;

public class ZoneScaffoldTests
{
    private const string NewZoneId = "wilds_frost_test";
    private const string ContinentId = "eldarwen";
    private const string LinkedZoneId = "wilds_eldarwen";

    [Fact]
    public void Scaffolded_zone_loads_with_shipped_content_and_validates()
    {
        string contentDirectory = ContentRepository.FindContentDirectory();
        string yaml = ZoneScaffold.CreateYaml(NewRequest(), ContentRepository.Catalog);

        List<KeyValuePair<string, string>> files = ShippedFiles(contentDirectory);
        files.Add(new KeyValuePair<string, string>(ZoneScaffold.RelativePath(NewZoneId), yaml));
        ContentCatalog catalog = ContentLoader.LoadFromFiles(files);

        ZoneDefinition zone = catalog.Zones[NewZoneId];
        Assert.Equal(ZoneScaffold.SceneAddress(ContinentId, NewZoneId), zone.SceneAddress);
        Assert.Equal(LinkedZoneId, Assert.Single(zone.Portals).ToZoneId);
        Assert.DoesNotContain(ContentValidator.Validate(catalog), issue => issue.Severity == IssueSeverity.Error);
    }

    [Theory]
    [InlineData("Bad-Id", ContinentId)]
    [InlineData(LinkedZoneId, ContinentId)]
    [InlineData(NewZoneId, "atlantis")]
    public void Invalid_requests_are_rejected(string zoneId, string continentId)
    {
        ZoneScaffoldRequest request = NewRequest();
        request.ZoneId = zoneId;
        request.ContinentId = continentId;

        Assert.Throws<ArgumentException>(() => ZoneScaffold.CreateYaml(request, ContentRepository.Catalog));
    }

    private static ZoneScaffoldRequest NewRequest()
    {
        return new ZoneScaffoldRequest
        {
            ZoneId = NewZoneId,
            Name = "Морозные пустоши",
            ContinentId = ContinentId,
            Kind = ZoneKind.Wilderness,
            LinkedZoneId = LinkedZoneId,
        };
    }

    private static List<KeyValuePair<string, string>> ShippedFiles(string contentDirectory)
    {
        return Directory.EnumerateFiles(contentDirectory, "*.yaml", SearchOption.AllDirectories)
            .Select(path => new KeyValuePair<string, string>(Path.GetRelativePath(contentDirectory, path).Replace('\\', '/'), File.ReadAllText(path)))
            .ToList();
    }
}
