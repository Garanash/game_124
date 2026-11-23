using Realmforge.Rules.Characters;
using Realmforge.Rules.Combat;
using Realmforge.Rules.Inventory;
using Realmforge.Server.Core.Characters;
using Xunit;

namespace Realmforge.Server.Tests;

public class StarterKitTests
{
    [Fact]
    public void New_heroes_wear_a_weapon_and_armor_and_carry_spares()
    {
        var operations = new CharacterOperations(SharedContent.Catalog);
        var request = new CreateCharacterRequest { Name = "Новичок", RaceId = "elf", Alignment = AlignmentCell.TrueNeutral, Abilities = new[] { 15, 14, 13, 12, 10, 8 } };

        CharacterState state = operations.Create(Guid.NewGuid(), Guid.NewGuid(), request, DateTime.UtcNow).Value!;

        Assert.Equal("short_sword", state.Equipment.Slots[EquipmentSlot.MainHand].ItemId);
        Assert.Equal("leather_vest", state.Equipment.Slots[EquipmentSlot.Chest].ItemId);
        Assert.Equal(2, state.Materials.Count("dagger"));
        Assert.Equal("short_sword", CharacterStats.Build(state.Sheet, state.Equipment, SharedContent.Catalog).WeaponId);
    }
}
