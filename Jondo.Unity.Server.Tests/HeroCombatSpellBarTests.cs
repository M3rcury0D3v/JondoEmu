using System;
using System.Collections.Generic;
using System.Linq;
using Jondo.Unity.Protocol;
using Jondo.Unity.Server.Handlers;
using Jondo.Unity.Server.Managers;
using Jondo.Unity.Server.Network;
using Jondo.Unity.World.Fights;
using Xunit;

namespace Jondo.Unity.Server.Tests;

public sealed class HeroCombatSpellBarTests : IDisposable
{
    public HeroCombatSpellBarTests()
    {
        FightHandler.ClearHeroBarsForTests();
    }

    public void Dispose()
    {
        FightHandler.ClearHeroBarsForTests();
    }

    [Fact]
    public void HeroBar_preserves_custom_spell_organization_and_empty_slots()
    {
        // Setup: A hero fighter (e.g. Steamer) controlled by master (id: 100)
        long masterId = 100;
        long heroId = 200;
        var heroFighter = new Fighter
        {
            Id = heroId,
            Name = "SteamerHero",
            Breed = 18, // Steamer
            IsMonster = false,
            IsPlayedAlly = true,
            DrivenBy = masterId
        };

        // User setup: Slot 0 = Spell 3, Slot 1 = empty, Slot 2 = empty, Slot 3 = Spell 1, Slot 4 = Spell 2
        var layout = new FightSpellLayout.Layout { WithWeapon = true };
        layout.Spells.Add((1, 1));
        layout.Spells.Add((2, 1));
        layout.Spells.Add((3, 1));
        layout.Spells.Add((4, 1)); // Known but not placed on bar
        layout.Spells.Add((5, 1)); // Known but not placed on bar

        layout.Bar.Add((0, 3));
        layout.Bar.Add((3, 1));
        layout.Bar.Add((4, 2));

        FightHandler.RegisterHeroBarForTests(heroId, layout);

        byte[] rawBytes = FightHandler.PlayedAllyBarForTests(heroFighter, masterId);
        Assert.NotNull(rawBytes);

        var message = ProtoMessage.Parse(rawBytes);

        // Field 3: fighterId
        var fighterField = Assert.Single(message.Fields, f => f.FieldNumber == 3 && f.WireType == 0);
        Assert.Equal(heroId, fighterField.VarIntValue);

        // Field 4: controllerId (master)
        var controllerField = Assert.Single(message.Fields, f => f.FieldNumber == 4 && f.WireType == 0);
        Assert.Equal(masterId, controllerField.VarIntValue);

        // Field 7 entries: the slots on the bar
        var slotFields = message.Fields.Where(f => f.FieldNumber == 7 && f.WireType == 2).ToList();
        Assert.Equal(3, slotFields.Count);

        var slotsMap = new Dictionary<int, int>();
        foreach (var slotField in slotFields)
        {
            var slotMsg = ProtoMessage.Parse(slotField.BytesValue);
            int slotId = 0;
            int spellId = 0;

            foreach (var f in slotMsg.Fields)
            {
                if (f.FieldNumber == 2 && f.WireType == 0) slotId = (int)f.VarIntValue;
                else if (f.FieldNumber == 6 && f.WireType == 2)
                {
                    var inner = ProtoMessage.Parse(f.BytesValue);
                    foreach (var inf in inner.Fields)
                    {
                        if (inf.FieldNumber == 2 && inf.WireType == 0) spellId = (int)inf.VarIntValue;
                    }
                }
            }

            slotsMap[slotId] = spellId;
        }

        // Custom organization must be strictly preserved:
        Assert.True(slotsMap.ContainsKey(0), "Slot 0 must exist");
        Assert.Equal(3, slotsMap[0]);

        Assert.False(slotsMap.ContainsKey(1), "Slot 1 must remain empty");
        Assert.False(slotsMap.ContainsKey(2), "Slot 2 must remain empty");

        Assert.True(slotsMap.ContainsKey(3), "Slot 3 must exist");
        Assert.Equal(1, slotsMap[3]);

        Assert.True(slotsMap.ContainsKey(4), "Slot 4 must exist");
        Assert.Equal(2, slotsMap[4]);

        // Unused class spells 4 and 5 must NOT be appended to the hero's bar
        Assert.DoesNotContain(slotsMap.Values, id => id == 4);
        Assert.DoesNotContain(slotsMap.Values, id => id == 5);
    }

    [Fact]
    public void HeroBar_preserves_weapon_at_arbitrary_slot_without_overwriting_slot_zero()
    {
        long masterId = 100;
        long heroId = 201;
        var heroFighter = new Fighter
        {
            Id = heroId,
            Name = "IopHero",
            Breed = 8,
            IsMonster = false,
            IsPlayedAlly = true,
            DrivenBy = masterId
        };

        // User placed Spell 1001 at Slot 0 and Weapon (spell 0) at Slot 5
        var layout = new FightSpellLayout.Layout { WithWeapon = true };
        layout.Spells.Add((1001, 1));
        layout.Bar.Add((0, 1001));
        layout.Bar.Add((5, FightProtocol.Weapon));

        FightHandler.RegisterHeroBarForTests(heroId, layout);

        byte[] rawBytes = FightHandler.PlayedAllyBarForTests(heroFighter, masterId);
        Assert.NotNull(rawBytes);

        var message = ProtoMessage.Parse(rawBytes);
        var slotFields = message.Fields.Where(f => f.FieldNumber == 7 && f.WireType == 2).ToList();
        Assert.Equal(2, slotFields.Count);

        var slots = new Dictionary<int, (bool isWeapon, int spellId)>();
        foreach (var sf in slotFields)
        {
            var slotMsg = ProtoMessage.Parse(sf.BytesValue);
            int slotId = 0;
            bool isWeapon = false;
            int spellId = 0;

            foreach (var f in slotMsg.Fields)
            {
                if (f.FieldNumber == 2 && f.WireType == 0) slotId = (int)f.VarIntValue;
                else if (f.FieldNumber == 6 && f.WireType == 2)
                {
                    if (f.BytesValue.Length == 0)
                    {
                        isWeapon = true;
                    }
                    else
                    {
                        var inner = ProtoMessage.Parse(f.BytesValue);
                        foreach (var inf in inner.Fields)
                            if (inf.FieldNumber == 2 && inf.WireType == 0) spellId = (int)inf.VarIntValue;
                    }
                }
            }
            slots[slotId] = (isWeapon, spellId);
        }

        // Slot 0 MUST remain spell 1001
        Assert.True(slots.ContainsKey(0));
        Assert.False(slots[0].isWeapon);
        Assert.Equal(1001, slots[0].spellId);

        // Slot 5 MUST be the weapon
        Assert.True(slots.ContainsKey(5));
        Assert.True(slots[5].isWeapon);
    }
}
