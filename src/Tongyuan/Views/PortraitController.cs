using Godot;
using Tongyuan.Core.Core;

namespace Tongyuan.Views;

/// <summary>
/// 立绘状态机（规格 §4.8）。每角色/敌人一个。
/// 状态：Idle（循环帧）/ Skill（出招帧，播完回 Idle）/ 预留 Hit/Down。
/// 由 Core 事件驱动；动画异步、不阻塞结算（结算即时完成）。P0 占位色块/帧。
/// </summary>
public partial class PortraitController : Node2D
{
    public PortraitState State { get; private set; } = PortraitState.Idle;

    public override void _Ready()
    {
        // P0：占位色块（P5 换立绘）
        QueueRedraw();
    }

    public override void _Draw()
    {
        // 占位：按状态画不同色块
        var color = State switch
        {
            PortraitState.Idle => new Color(0.3f, 0.6f, 0.9f),
            PortraitState.Skill => new Color(0.9f, 0.7f, 0.2f),
            _ => new Color(0.5f, 0.5f, 0.5f),
        };
        DrawRect(new Rect2(-32, -64, 64, 128), color);
    }

    public void PlaySkill() { State = PortraitState.Skill; QueueRedraw(); }
    public void ToIdle() { State = PortraitState.Idle; QueueRedraw(); }
}
