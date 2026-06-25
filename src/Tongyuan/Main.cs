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
public partial class Main : Control
{
    private GameView? _view;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        var state = BuildSampleBattle();
        GD.Print($"[同渊] P5 示例战场就绪 | 角色={state.Characters.Count} 敌人={state.Enemies.Count} 时间轴={state.Timeline.Length} 指针={state.Pointer}");

        _view = new GameView { State = state };
        AddChild(_view);
        GetViewport().Connect("size_changed", Callable.From(ResizeView));
        ResizeView();

        // 布局自检（无重叠 / 在视口内）——P5 截图矩形检查的确定性等价
        int hand = state.Characters[0].Hand.Count;
        var layout = BattleLayout.Compute(state.AliveCharacters.Count(), state.Enemies.Count, state.Timeline.Length, hand, state.Pointer);
        GD.Print($"[同渊] 布局自检: 无重叠={layout.NoOverlaps()} 在视口内={layout.AllWithinViewport()}");
    }

    private void ResizeView()
    {
        if (_view is null || !IsInstanceValid(_view)) return;
        _view.ResizeTo(GetViewport().GetVisibleRect().Size);
    }

    public static GameState BuildSampleBattle()
    {
        var tl = new Timeline();
        for (int i = 0; i < 6; i++) tl.Nodes.Add(NodeType.Empty);
        // 敌人各带差异化行动链（链尽循环，不无限增长）
        var slash = new Enemy { Id = 1, Name = "斩击兵", Kind = EnemyKind.Slash, Power = 5, NodeSlot = 2, Hp = 20 };
        slash.ActionChain.AddRange(new EnemyAction[] {
            new EnemyAction.Attack(5, 1),                 // 斩位1
        });
        var thrust = new Enemy { Id = 2, Name = "突刺兵", Kind = EnemyKind.Thrust, Power = 4, NodeSlot = 4, Hp = 18 };
        thrust.ActionChain.AddRange(new EnemyAction[] {
            new EnemyAction.Charge(2),                   // 蓄力示警
            new EnemyAction.Attack(4, 2),                 // 突位2（含蓄力）
            new EnemyAction.Idle(),                       // 喘息
        });
        var striker = new Enemy { Id = 3, Name = "重锤兵", Kind = EnemyKind.Strike, Power = 3, NodeSlot = 5, Hp = 26 };
        striker.ActionChain.AddRange(new EnemyAction[] {
            new EnemyAction.Charge(3),
            new EnemyAction.Attack(3, -1),                // 打全体（含蓄力）
        });
        tl.Enemies.Add(slash);
        tl.Enemies.Add(thrust);
        tl.Enemies.Add(striker);

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
