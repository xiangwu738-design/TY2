namespace Tongyuan.Core.Core;

/// <summary>
/// 附魔作用域（规格 §4.4）：可挂到某张具体牌 / 弃牌堆所有牌 / 抽牌堆所有牌等。
/// </summary>
public enum EnchantmentScope
{
    SpecificCard,
    AllInDiscard,
    AllInDraw,
}

/// <summary>
/// 附魔实例（规格 §4.4）。保留：力量/易伤/蓄力；预留扩展：减伤/占位改动/抽牌扰动/条件增伤。
/// </summary>
public sealed class Enchantment
{
    public EnchantmentType Type { get; init; }
    public int Magnitude { get; init; }
    public EnchantmentScope Scope { get; init; }
    public Guid? TargetCardInstanceId { get; set; } // Scope=SpecificCard 时指定
    public int Remaining { get; set; } // 易伤次数等

    public Enchantment Clone() => new()
    {
        Type = Type,
        Magnitude = Magnitude,
        Scope = Scope,
        TargetCardInstanceId = TargetCardInstanceId,
        Remaining = Remaining,
    };
}
