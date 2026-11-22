using Realmforge.Rules.Characters;
using Realmforge.Rules.Definitions;
using Xunit;

namespace Realmforge.Content.Tests;

public class ShippedContentTests
{
    private const int ConceptRaceCount = 47;

    [Fact]
    public void Shipped_content_has_no_validation_errors()
    {
        IReadOnlyList<ContentIssue> issues = ContentValidator.Validate(ContentRepository.Catalog);

        Assert.DoesNotContain(issues, issue => issue.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void Every_concept_race_starts_in_its_own_start_zone()
    {
        ContentCatalog catalog = ContentRepository.Catalog;

        Assert.Equal(ConceptRaceCount, catalog.Races.Count);
        Assert.All(catalog.Races.Values, race => Assert.Equal(ZoneKind.StartZone, catalog.Zones[race.StartZoneId].Kind));
        Assert.True(catalog.Continents.Count >= 4);
    }

    [Fact]
    public void Content_provides_pvp_zones_and_all_instance_sizes()
    {
        ContentCatalog catalog = ContentRepository.Catalog;

        Assert.True(catalog.Zones.Values.Count(zone => zone.Kind == ZoneKind.PvpZone) >= 2);
        int[] sizes = catalog.Instances.Values.SelectMany(instance => instance.PartySizes).Distinct().OrderBy(size => size).ToArray();
        Assert.Equal(new[] { 1, 3, 5, 25 }, sizes);
    }

    [Fact]
    public void Paladin_requires_good_alignment()
    {
        ClassDefinition paladin = ContentRepository.Catalog.Classes["paladin"];

        Assert.True(paladin.Alignment.IsSatisfiedBy(Alignment.FromCell(AlignmentCell.LawfulGood)));
        Assert.False(paladin.Alignment.IsSatisfiedBy(Alignment.FromCell(AlignmentCell.ChaoticEvil)));
    }

    [Fact]
    public void Content_loads_from_in_memory_files_like_unity_does()
    {
        var files = new Dictionary<string, string>
        {
            ["manifest.yaml"] = "version: 9.9.9",
            ["races/test.yaml"] = "- { id: test_race, name: Тест, continent_id: c, start_zone_id: z, ability_bonuses: { Strength: 2 } }",
        };

        ContentCatalog catalog = ContentLoader.LoadFromFiles(files);

        Assert.Equal("9.9.9", catalog.Version);
        Assert.Equal(2, catalog.Races["test_race"].AbilityBonuses[Ability.Strength]);
    }

    [Fact]
    public void Duplicate_ids_are_rejected()
    {
        var files = new Dictionary<string, string>
        {
            ["items/a.yaml"] = "- { id: dup, name: A }",
            ["items/b.yaml"] = "- { id: dup, name: B }",
        };

        Assert.Throws<ContentLoadException>(() => ContentLoader.LoadFromFiles(files));
    }

    [Theory]
    [InlineData("start_silverleaf", "StartSilverleaf")]
    [InlineData("thri_kreen", "ThriKreen")]
    [InlineData("1001", "_1001")]
    public void Generated_identifiers_are_valid_csharp(string id, string expected)
    {
        Assert.Equal(expected, ContentIdGenerator.ToIdentifier(id));
    }
}
