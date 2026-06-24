using Godot;
using Tongyuan.Core.Core;

namespace Tongyuan.Views;

/// <summary>
/// 战场表现层（规格 §3/§4.8）。每进程一个，订阅 Core 事件流渲染
/// 战场/时间轴/手牌/预览/日志。P0 骨架；P1 接事件。
/// </summary>
public partial class GameView : Control
{
    public override void _Ready()
    {
        GD.Print("[GameView] P0 占位就绪");
    }

    /// <summary>订阅 Core 事件流，播放动画/更新 UI。P1 实现。</summary>
    public void Subscribe(GameState state)
    {
        // P1：遍历 state.Events 分发到子控件
    }
}
