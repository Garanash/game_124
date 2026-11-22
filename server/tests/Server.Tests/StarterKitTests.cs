using Realmforge.Rules.Characters;
using Realmforge.Rules.Combat;
using Realmforge.Server.Core.Characters;
using Xunit;

namespace Realmforge.Server.Tests;

public class StarterKitTests
{
    [Fact]
    public void New_heroes_get_a_weapon_armor_and_a_fusable_pair_of_daggers()
    {
        var operations = new CharacterOperations(SharedContent.Catalog);
        var request = new CreateCharacterRequest { Name = "Новичок", RaceId = "elf", Alignment = AlignmentCell.TrueNeutral, Abilities = new[] { 15, 14, 13, 12, 10, 8 } };

        CharacterState state = operations.Create(Guid.NewGuid(), Guid.NewGuid(), request, DateTime.UtcNow).Value!;

        Assert.Equal(new[] { "short_sword", "leather_vest", "whetstone" }, state.Backpack.Items.Select(item => item.ItemId));
        Assert.Equal(2, state.Materials.Count("dagger"));
        Assert.Equal("short_sword", CharacterStats.Build(state.Sheet, state.Backpack, SharedContent.Catalog).WeaponId);
    }
}
