namespace Tongyuan.Core.Data;

using Tongyuan.Core.Core;

/// <summary>
/// 示例卡牌目录（展示自定义卡牌系统 CardBuilder 的用法 + 各效果/伤害类型样例）。
/// 数值/名称为占位示例，登记 §7，用户后填正式卡牌。规格 §6/§7。
/// </summary>
public static class SampleCards
{
    public static readonly CardDef[] All =
    {
        // ---- 攻击：四种伤害类型 ----
        new CardBuilder("smp_slash", "旋风斩").Attack(DamageType.Slash, 6, cost: 2)
            .WithDesc("斩击位1，6伤").Build(),

        new CardBuilder("smp_thrust", "贯穿").Attack(DamageType.Thrust, 7, cost: 2)
            .WithDesc("穿刺位2，7伤（位2无人则落空）").Build(),

        new CardBuilder("smp_blunt", "横扫千军").Attack(DamageType.Blunt, 4, cost: 3)
            .WithDesc("打击全体，每敌4伤").Rarity(Rarity.Rare).Build(),

        new CardBuilder("smp_ranged", "狙击").Attack(DamageType.Ranged, 8, cost: 2)
            .WithDesc("远程自选敌，8伤，不位移").Rarity(Rarity.Rare).Build(),

        new CardBuilder("smp_ranged_fast", "速射").Attack(DamageType.Ranged, 3, cost: 1)
            .WithDesc("远程自选敌，3伤，速射").Build(),

        // ---- 护盾：固定吸收 / 次数型 ----
        new CardBuilder("smp_shield_fix", "铁壁").Shield(10, ShieldType.Fixed, cost: 1)
            .WithDesc("护盾吸收10，耗尽").Build(),

        new CardBuilder("smp_shield_cnt", "反应屏障").Shield(4, ShieldType.Count, hits: 3, cost: 2)
            .WithDesc("护盾挡3次×4").Build(),

        // ---- 附魔：力量 / 易伤 / 蓄力，不同作用域 ----
        new CardBuilder("smp_power", "蓄能").Enchant(EnchantmentType.Power, 3, EnchantmentScope.SpecificCard, cost: 0)
            .WithDesc("力量+3，挂到一张手牌（攻击+3）").Build(),

        new CardBuilder("smp_vuln", "标记").Enchant(EnchantmentType.Vulnerable, 3, EnchantmentScope.SpecificCard, cost: 0)
            .WithDesc("易伤+3，挂到敌（受击+3×3次）").Build(),

        new CardBuilder("smp_charge", "破甲").Enchant(EnchantmentType.Charge, 2, EnchantmentScope.SpecificCard, cost: 1)
            .WithDesc("敌蓄力+2（下次攻击+2）").Build(),

        new CardBuilder("smp_power_discard", "战术重整").Enchant(EnchantmentType.Power, 1, EnchantmentScope.AllInDiscard, cost: 1)
            .WithDesc("弃牌堆所有牌力量+1").Build(),

        // ---- 整备/抽牌变体 ----
        new CardBuilder("smp_draw", "急整备").Draw(3, cost: 1)
            .WithDesc("整备：抽3张").Build(),
    };

    /// <summary>把示例卡全部注册到注册表。</summary>
    public static void RegisterInto(CardRegistry registry)
    {
        foreach (var c in All) registry.Register(c);
    }
}
