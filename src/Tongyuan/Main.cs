using Godot;
using Tongyuan.Core.Core;

namespace Tongyuan;

/// <summary>主入口节点（P0 骨架）。P1 起接入 GameView/Core。</summary>
public partial class Main : Node
{
    public override void _Ready()
    {
        var gs = new GameState();
        GD.Print($"[同渊] P0 主场景就绪 | seed={gs.Seed} | timeline={gs.Timeline.Length} | core=ok");
    }
}
