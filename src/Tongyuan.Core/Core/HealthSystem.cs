namespace Tongyuan.Core.Core;

/// <summary>
/// 血量上限系统（规格 §4.6）。
/// 个人血，非共享；每场开局回满当前血。战斗结算按受伤程度下调血量上限（跨场长期损伤）；
/// 上限只能靠休息恢复。被击倒→本场临时 debuff（弱而不废），本场结束清除；长期代价由上限下调独担。
/// 上限一路被压→重伤→天堂线（叙事死亡阶梯，占位）。
/// 速率参数（默认按受伤20%下调上限 / 休息回25%上限）登记 §7。
/// </summary>
public static class HealthSystem
{
    /// <summary>战斗结算：按本场总受伤量下调血量上限（默认 20%）。</summary>
    public const float MaxHpReductionRate = 0.20f;

    /// <summary>休息恢复血量上限（默认 25% 基础上限）。</summary>
    public const float RestRestoreFraction = 0.25f;

    /// <summary>被击倒阈值：单次受击后 Hp 占 MaxHp 比例低于此值则击倒（占位）。</summary>
    public const float DownThresholdFraction = 0.0f; // Hp<=0 才算击倒（与死亡一致，占位待调）

    /// <summary>记录角色本场累计受伤，供结算下调上限。</summary>
    public static int TotalDamageTakenThisFight(Character c) => c.DamageTakenThisFight;

    /// <summary>战斗结束结算：下调血量上限，回满当前血到新上限，清除击倒 debuff。</summary>
    public static void SettleEndOfFight(Character c)
    {
        int reduction = (int)Math.Round(c.DamageTakenThisFight * MaxHpReductionRate);
        c.MaxHp = Math.Max(1, c.MaxHp - reduction);
        c.Hp = c.MaxHp;                  // 每场开局回满当前血
        c.IsDown = false;                // 本场 debuff 清除
        c.DamageTakenThisFight = 0;
    }

    /// <summary>休息恢复血量上限（量用模板参数，默认回 25% 基础上限）。</summary>
    public static void Rest(Character c, int baseMaxHp)
    {
        int restore = (int)Math.Round(baseMaxHp * RestRestoreFraction);
        c.MaxHp = Math.Min(baseMaxHp, c.MaxHp + restore);
        c.Hp = c.MaxHp;
    }

    /// <summary>重伤阶梯（占位）：上限被压到阈值触发叙事死亡阶梯“天堂线”。</summary>
    public static bool IsCriticalWound(Character c, int baseMaxHp) =>
        c.MaxHp <= baseMaxHp * 0.25;
}
