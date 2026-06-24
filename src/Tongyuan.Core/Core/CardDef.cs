namespace Tongyuan.Core.Core;

/// <summary>卡牌效果类型（P1 实现核心四种）。</summary>
public enum EffectKind
{
    None,
    AttackDamage,    // 攻击：造成伤害，打出者移到位1
    ApplyShield,     // 防御：铺护盾（固定吸收/次数型）
    ApplyEnchantment,// 技能：给牌挂附魔
    DrawCards,       // 整备：抽牌（整备牌专用）
}

/// <summary>
/// 卡牌定义（静态模板）。具体数值/卡牌内容用模板占位，登记 §7，由用户后填（规格 §6）。
/// </summary>
public sealed class CardDef
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public CardType Type { get; init; }
    public int Cost { get; init; } // 占位=推进格数=时间成本

    public EffectKind Effect { get; init; } = EffectKind.None;
    public int Magnitude { get; init; } // 攻击伤害 / 抽牌数 / 附魔量

    // 护盾参数
    public ShieldType ShieldType { get; init; } = ShieldType.Fixed;
    public int ShieldHits { get; init; } = 1; // 次数型：挡几次

    // 附魔参数
    public EnchantmentType EnchantType { get; init; } = EnchantmentType.Power;
    public EnchantmentScope EnchantScope { get; init; } = EnchantmentScope.SpecificCard;
}

/// <summary>
/// 整备牌模板参数（规格 §4.2 / §7，默认：占2·抽2·不弃·回手）。
/// </summary>
public sealed class PrepCardTemplate
{
    public int SlotCost { get; init; } = 2;   // 占位格数
    public int DrawCount { get; init; } = 2;  // 抽牌数
    public bool DiscardAfter { get; init; } = false; // 不弃
    public bool ReturnToHand { get; init; } = true;  // 回手
}
