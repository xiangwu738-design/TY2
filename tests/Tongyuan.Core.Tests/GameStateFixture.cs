using Tongyuan.Core.Core;

namespace Tongyuan.Core.Tests;

/// <summary>测试夹具：快速构造 GameState / CardDef。</summary>
internal static class GameStateFixture
{
    public static CardDef Prep(int cost = 1, int draw = 2) => new()
    {
        Id = "prep", Name = "整备", Type = CardType.Prep, Cost = cost,
        Effect = EffectKind.DrawCards, Magnitude = draw,
    };

    public static CardDef Attack(int cost, int damage) => new()
    {
        Id = "atk", Name = "攻击", Type = CardType.Attack, Cost = cost,
        Effect = EffectKind.AttackDamage, Magnitude = damage,
    };

    public static CardDef Shield(int cost, int amount, ShieldType type = ShieldType.Fixed, int hits = 1) => new()
    {
        Id = "shd", Name = "护盾", Type = CardType.Defense, Cost = cost,
        Effect = EffectKind.ApplyShield, Magnitude = amount,
        ShieldType = type, ShieldHits = hits,
    };

    public static CardDef Enchant(int cost, EnchantmentType t, int mag, EnchantmentScope scope = EnchantmentScope.SpecificCard) => new()
    {
        Id = "enc", Name = "附魔", Type = CardType.Skill, Cost = cost,
        Effect = EffectKind.ApplyEnchantment, Magnitude = mag,
        EnchantType = t, EnchantScope = scope,
    };

    public static Card Card(CardDef def) => new() { Def = def };

    public static Character Char(int id, int hp, int pos, CardDef? prep = null, int maxHp = 0) => new()
    {
        Id = id, Name = $"c{id}", Hp = hp, MaxHp = maxHp > 0 ? maxHp : hp,
        Position = pos, PrepCard = prep is null ? null! : Card(prep),
    };

    public static Enemy Enemy(int id, int slot, EnemyKind kind, int power, int hp = 1000) => new()
    {
        Id = id, Name = $"e{id}", Kind = kind, Power = power, NodeSlot = slot, Hp = hp,
    };

    /// <summary>长度 n 的空时间轴。</summary>
    public static Timeline TimelineOf(int n)
    {
        var t = new Timeline();
        for (int i = 0; i < n; i++) t.Nodes.Add(NodeType.Empty);
        return t;
    }

    public static GameState State(int seed, Timeline timeline, params Character[] chars)
    {
        var gs = new GameState(seed) { Timeline = timeline };
        foreach (var c in chars) gs.Characters.Add(c);
        return gs;
    }
}
