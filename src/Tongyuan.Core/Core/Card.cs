namespace Tongyuan.Core.Core;

/// <summary>
/// 卡牌实例（规格 §4.4 引入实例层）。
/// 手牌/弃牌堆/抽牌堆里的每张牌是独立实例，可挂 <see cref="Enchantment"/>。
/// </summary>
public sealed class Card
{
    public CardDef Def { get; init; } = null!;
    public Guid InstanceId { get; set; } = Guid.NewGuid(); // 可继承：克隆/确定性重放需保持一致
    public List<Enchantment> Enchantments { get; } = new();

    public bool IsPrep => Def.Type == CardType.Prep;

    /// <summary>本牌当前攻击伤害（含力量附魔加成）。</summary>
    public int EffectiveAttack => Def.Magnitude + EnchantMagOfType(EnchantmentType.Power);

    private int EnchantMagOfType(EnchantmentType t)
    {
        int sum = 0;
        foreach (var e in Enchantments) if (e.Type == t) sum += e.Magnitude;
        return sum;
    }

    public Card Clone()
    {
        var c = new Card { Def = Def, InstanceId = InstanceId };
        foreach (var e in Enchantments) c.Enchantments.Add(e.Clone());
        return c;
    }
}
