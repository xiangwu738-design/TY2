using Tongyuan.Core.Core;
using Tongyuan.Core.Data;
using Xunit;

namespace Tongyuan.Core.Tests;

public class SampleCardsTests
{
    [Fact]
    public void SampleCards_AllHaveIdNameAndDescription()
    {
        Assert.True(SampleCards.All.Length >= 8);
        foreach (var c in SampleCards.All)
        {
            Assert.False(string.IsNullOrEmpty(c.Id));
            Assert.False(string.IsNullOrEmpty(c.Name));
            Assert.False(string.IsNullOrEmpty(c.EffectDescription()));
        }
    }

    [Fact]
    public void SampleCards_CoverAllDamageTypes()
    {
        var types = SampleCards.All.Where(c => c.Effect == EffectKind.AttackDamage).Select(c => c.DamageType).ToHashSet();
        Assert.Contains(DamageType.Blunt, types);
        Assert.Contains(DamageType.Slash, types);
        Assert.Contains(DamageType.Thrust, types);
        Assert.Contains(DamageType.Ranged, types);
    }

    [Fact]
    public void SampleCards_RegisterIntoRegistry()
    {
        var reg = CardRegistry.LoadDefaults();
        int before = reg.Count;
        SampleCards.RegisterInto(reg);
        Assert.Equal(before + SampleCards.All.Length, reg.Count);
        foreach (var c in SampleCards.All)
            Assert.NotNull(reg.Get(c.Id));
    }

    [Fact]
    public void SampleCards_IdsUnique()
    {
        var ids = SampleCards.All.Select(c => c.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }
}
