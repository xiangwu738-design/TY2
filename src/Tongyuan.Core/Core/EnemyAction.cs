namespace Tongyuan.Core.Core;

/// <summary>
/// 敌人行动链的一步（规格 §4.3/§4.9 衍生）。敌人触发时按链执行，链尽则循环（循环而非无限增长）。
/// </summary>
public abstract record EnemyAction
{
    /// <summary>攻击：对某位置造成伤害。TargetPos=null 用敌人默认 TargetPosition；-1=全体。</summary>
    public sealed record Attack(int Amount, int? TargetPos = null) : EnemyAction;

    /// <summary>蓄力：增加蓄力层数，下一次攻击附带（攻击后清零）。 Telegraphed wind-up。</summary>
    public sealed record Charge(int Amount) : EnemyAction;

    /// <summary>待机/示警：本步不行动（占位，便于编排节奏）。</summary>
    public sealed record Idle() : EnemyAction;
}
