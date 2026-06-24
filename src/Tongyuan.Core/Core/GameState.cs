using System.Collections.Generic;

namespace Tongyuan.Core.Core;

/// <summary>
/// 游戏内核状态（规格 §3）。持有角色/敌人/时间轴/指针/事件流。
/// 分层铁律：Core 纯 C#，不 using Godot，可单测、可 Clone() 预演、确定性。
/// 确定性：除开局带种子洗牌外，结算零 RNG（规格 §2）。
/// P0 骨架；逐格结算/出牌/预览逻辑见 P1。
/// </summary>
public sealed class GameState
{
    public Timeline Timeline { get; init; } = new();
    public List<Character> Characters { get; init; } = new();
    public List<Enemy> Enemies => Timeline.Enemies;

    /// <summary>洗牌种子（输入端随机；联机需同步种子）。结算本身不使用 RNG。</summary>
    public int Seed { get; init; }

    /// <summary>当前回合事件流（PlayCard/UsePrep/Skip 产出，供 Views/Net）。</summary>
    public List<GameEvent> Events { get; } = new();

    /// <summary>在克隆上预演动作，给出“松手前”完整后果（不改变本体）。</summary>
    public GameState Clone()
    {
        // P0：浅克隆骨架。P1 实现深克隆（含牌堆/附魔/护盾），保证预演确定性。
        return new GameState
        {
            Timeline = Timeline, // P1 替换为深克隆
            Characters = Characters,
            Seed = Seed,
        };
    }

    /// <summary>预演一个动作，返回预测事件流（不改本体）。P1 实现。</summary>
    public List<GameEvent> Preview(PlayerAction action) => Clone().Apply(action);

    /// <summary>执行动作，产出事件流写入 <see cref="Events"/>。P1 实现逐格结算。</summary>
    public List<GameEvent> Apply(PlayerAction action)
    {
        Events.Clear();
        // P1：按 ActionType 分发到 PlayCard/UsePrep/Skip，逐格结算。
        return Events;
    }
}
