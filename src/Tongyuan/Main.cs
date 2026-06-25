using Godot;
using System.Linq;
using Tongyuan.Core.Core;
using Tongyuan.Core.Data;
using Tongyuan.Core.Layout;
using Tongyuan.Views;

namespace Tongyuan;

/// <summary>
/// 主入口节点。P5：搭一个示例战场（4 角色模板 + 敌人 + 时间轴），绑定 GameView 渲染占位美术。
/// </summary>
public partial class Main : Node
{
    public override void _Ready()
    {
        var state = BuildSampleBattle();
        GD.Print($"[同渊] P5 示例战场就绪 | 角色={state.Characters.Count} 敌人={state.Enemies.Count} 时间轴={state.Timeline.Length} 指针={state.Pointer}");

        var view = new GameView { State = state };
        AddChild(view);

        // 布局自检（无重叠 / 在视口内）——P5 截图矩形检查的确定性等价
        int hand = state.Characters[0].Hand.Count;
        var layout = BattleLayout.Compute(state.AliveCharacters.Count(), state.Enemies.Count, state.Timeline.Length, hand, state.Pointer);
        GD.Print($"[同渊] 布局自检: 无重叠={layout.NoOverlaps()} 在视口内={layout.AllWithinViewport()}");
    }

    private static GameState BuildSampleBattle()
    {
        var tl = new Timeline();
        for (int i = 0; i < 6; i++) tl.Nodes.Add(NodeType.Empty);
        tl.Enemies.Add(new Enemy { Id = 1, Name = "斩击兵", Kind = EnemyKind.Slash, Power = 5, NodeSlot = 2, Hp = 20 });
        tl.Enemies.Add(new Enemy { Id = 2, Name = "突刺兵", Kind = EnemyKind.Thrust, Power = 4, NodeSlot = 4, Hp = 18 });

        var gs = new GameState(seed: 7) { Timeline = tl };
        int pos = 1;
        foreach (var tpl in CharacterTemplates.All())
        {
            var c = CharacterTemplates.Instantiate(tpl, position: pos);
            // 各抽 3 张到手牌用于展示
            c.Draw(3, gs.Rng);
            gs.Characters.Add(c);
            pos++;
        }
        gs.ContractPositions();
        return gs;
    }
}
