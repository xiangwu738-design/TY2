namespace Tongyuan.Core.Core;

/// <summary>
/// 卡牌独立逻辑接口（自定义卡牌扩展位）。卡牌可挂自定义代码而非纯数据效果。
/// 数据卡用 <see cref="CardDef.Effect"/> 枚举；代码卡实现本接口并挂到 <see cref="CardDef.CustomEffect"/>。
/// 约束：实现必须确定性——随机只许用 <c>gs.Rng</c>，不得用系统 RNG/时间，否则联机重放不一致。
/// 代码卡随程序集分发，各端同逻辑，动作同步即可。
/// </summary>
public interface ICardEffect
{
    /// <summary>在占位终点结算：把后果写入 gs（状态变更 + gs.Emit 事件）。</summary>
    void Apply(GameState gs, Character caster, Card card, PlayerAction? action);
}
