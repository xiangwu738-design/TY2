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
    public CardType Type { get; set; }
    public int Cost { get; set; } // 占位=推进格数=时间成本

    public EffectKind Effect { get; set; } = EffectKind.None;
    public int Magnitude { get; set; } // 攻击伤害 / 抽牌数 / 附魔量

    /// <summary>攻击伤害类型（仅攻击牌）。远程=自选敌人且不位移。</summary>
    public DamageType DamageType { get; set; } = DamageType.Slash;

    // 护盾参数
    public ShieldType ShieldType { get; set; } = ShieldType.Fixed;
    public int ShieldHits { get; set; } = 1; // 次数型：挡几次

    // 附魔参数
    public EnchantmentType EnchantType { get; set; } = EnchantmentType.Power;
    public EnchantmentScope EnchantScope { get; set; } = EnchantmentScope.SpecificCard;

    /// <summary>卡牌独立逻辑（代码卡）。非 null 时结算优先派发给它，覆盖纯数据 Effect。</summary>
    public ICardEffect? CustomEffect { get; set; }

    /// <summary>代码卡是否需要玩家点选敌人目标（远程数据卡自动为 true）。</summary>
    public bool NeedsTargetEnemy { get; set; }

    /// <summary>展示用描述（空则自动生成）。避免“描述模糊”。</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>美术资源占位口（卡面图路径，null=用占位色块）。规格 §6：美术资源后填。</summary>
    public string? ArtPath { get; set; }

    /// <summary>自动生成效果描述（UI 直接用）。</summary>
    public string EffectDescription()
    {
        if (!string.IsNullOrEmpty(Description)) return Description;
        return Effect switch
        {
            EffectKind.AttackDamage => $"{DamageText(DamageType)} {Magnitude} 伤{(DamageType == DamageType.Ranged ? "·自选敌·不位移" : "·近战·出牌移位1")}",
            EffectKind.ApplyShield => ShieldType == ShieldType.Fixed
                ? $"护盾 吸收 {Magnitude}（耗尽）"
                : $"护盾 挡 {ShieldHits} 次×{Magnitude}",
            EffectKind.ApplyEnchantment => EnchantType switch
            {
                EnchantmentType.Power => $"力量 +{Magnitude}（挂到一张牌：攻击+{Magnitude}）",
                EnchantmentType.Vulnerable => $"易伤 +{Magnitude}（受击+{Magnitude}×{Magnitude}次）",
                EnchantmentType.Charge => $"敌蓄力 +{Magnitude}（下次攻击+{Magnitude}）",
                _ => $"附魔 {EnchantType} +{Magnitude}"
            },
            EffectKind.DrawCards => $"抽 {Magnitude} 张",
            _ => "",
        };
    }

    public static string DamageText(DamageType t) => t switch
    {
        DamageType.Blunt => "打击",
        DamageType.Slash => "斩击",
        DamageType.Thrust => "突刺",
        DamageType.Ranged => "远程",
        _ => "攻击",
    };
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
