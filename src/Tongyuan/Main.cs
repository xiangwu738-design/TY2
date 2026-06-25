using Godot;
using System.Linq;
using Tongyuan.Core.Core;
using Tongyuan.Core.Data;
using Tongyuan.Core.Layout;
using Tongyuan.Core.Roguelike;
using Tongyuan.UI;
using Tongyuan.Views;

namespace Tongyuan;

/// <summary>
/// UI 重置（规格 §3 路由）：屏幕路由器根节点。持 RunController，按当前节点类型切换子屏。
/// 阶段0：战斗屏复用 GameView；Map/Shop/Rest/Event 占位（阶段4实现）。F1 呼出开发者面板。
/// 子节点交换优于 ChangeScene，便于保留 LAN NetController 与跨屏状态。
/// </summary>
public partial class Main : Control
{
    private Control? _screen;
    private RunController? _run;
    private Control? _devPanel;
    private bool _devVisible;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        Theme = UiPalette.BuildTheme(); // 全局 CJK SystemFont + 配色
        BuildDevPanel();
        ShowMenu();
        GetViewport().Connect("size_changed", Callable.From(ResizeView));
        ResizeView();
    }

    private enum LanIntent { None, Host, Join }
    private LanIntent _pendingLan = LanIntent.None;

    private void ShowMenu()
    {
        if (_screen is not null) { _screen.QueueFree(); _screen = null; }
        _run = null;
        _screen = new MainMenuScreen
        {
            OnStart = () => { _pendingLan = LanIntent.None; StartRun(); },
            OnStartHost = () => { _pendingLan = LanIntent.Host; StartRun(); },
            OnStartJoin = () => { _pendingLan = LanIntent.Join; StartRun(); },
        };
        AddChild(_screen);
        ResizeView();
    }

    private void StartRun()
    {
        var map = MapGenerator.Generate(RunState.MapLayers, seed: 7);
        _run = new RunController(new RunState { Map = map, Gold = 50 });
        _run.Start();
        Route();
    }

    /// <summary>按 RunController.CurrentType 切屏。</summary>
    private void Route()
    {
        if (_screen is not null) { _screen.QueueFree(); _screen = null; }
        if (_run is null) { StartRun(); return; }
        if (_run.State.RunOver)
        {
            _screen = MakePlaceholder(_run.State.Victory ? "🏆 通关！" : "💀 失败", StartRun);
            AddChild(_screen); ResizeView(); return;
        }
        var type = _run.CurrentType;
        switch (type)
        {
            case MapNodeType.Combat:
            case MapNodeType.Elite:
            case MapNodeType.Boss:
                var gs = BuildSampleBattle();
                var gv = new GameView { State = gs };
                gv.BattleOver += OnBattleOver;
                _screen = gv;
                // 主菜单联机意图：进战斗后建主/加入
                if (_pendingLan == LanIntent.Host) gv.StartLanHost();
                else if (_pendingLan == LanIntent.Join) gv.StartLanClient();
                _pendingLan = LanIntent.None;
                GD.Print($"[同渊] 路由→战斗({type}) | 角色={gs.Characters.Count} 敌人={gs.Enemies.Count}");
                break;
            case MapNodeType.Shop:
                _screen = new ShopScreen { Run = _run, OnLeave = () => { _run.Advance(); Route(); } };
                GD.Print("[同渊] 路由→商店");
                break;
            case MapNodeType.Rest:
                _screen = new RestScreen { Run = _run, OnDone = () => { _run.Advance(); Route(); } };
                GD.Print("[同渊] 路由→休息");
                break;
            case MapNodeType.Event:
                _screen = new EventScreen { Run = _run, OnDone = () => { _run.Advance(); Route(); } };
                GD.Print("[同渊] 路由→事件");
                break;
            default:
                _screen = MakePlaceholder($"[{type}]", () => { _run.Advance(); Route(); });
                break;
        }
        AddChild(_screen);
        ResizeView();
    }

    /// <summary>战斗结束：胜→奖励(三选一)或通关；败→失败。延迟切换避免在信号里释放发射者。</summary>
    private void OnBattleOver(bool win) => CallDeferred(MethodName.OnBattleOverDeferred, win);

    private void OnBattleOverDeferred(Variant win)
    {
        bool w = (bool)win;
        if (_run is null) return;
        if (_screen is GameView gv) { gv.QueueFree(); _screen = null; }
        if (w)
        {
            if (_run.CurrentType == MapNodeType.Boss) { _run.Advance(); Route(); return; }
            _screen = new RewardScreen { Run = _run, OnPicked = () => { _run.Advance(); Route(); } };
            GD.Print("[同渊] 路由→奖励(三选一)");
        }
        else
        {
            _screen = MakePlaceholder("💀 战败 — 新局", StartRun);
        }
        AddChild(_screen);
        ResizeView();
    }

    private static Control MakePlaceholder(string text, System.Action onAdvance)
    {
        var c = new Control();
        c.SetAnchorsPreset(LayoutPreset.FullRect);
        var vb = new VBoxContainer();
        vb.OffsetLeft = 240; vb.OffsetTop = 260; vb.OffsetRight = 900; vb.OffsetBottom = 520;
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", 22);
        vb.AddChild(l);
        var b = new Button { Text = "推进到下一节点", CustomMinimumSize = new Vector2(200, 44) };
        b.Pressed += onAdvance;
        vb.AddChild(b);
        c.AddChild(vb);
        return c;
    }

    public override void _Input(InputEvent e)
    {
        if (e is InputEventKey k && k.Pressed && !k.Echo && k.Keycode == Key.F1)
        {
            _devVisible = !_devVisible;
            if (_devPanel is not null) _devPanel.Visible = _devVisible;
        }
    }

    // ---- F1 开发者面板：LAN/加自定义卡 收纳于此，不进主 chrome ----
    private void BuildDevPanel()
    {
        _devPanel = new Control { Visible = false };
        _devPanel.SetAnchorsPreset(LayoutPreset.TopRight);
        var p = new PanelContainer { CustomMinimumSize = new Vector2(260, 0) };
        p.OffsetRight = 260; p.OffsetBottom = 240;
        var vb = new VBoxContainer();
        vb.AddThemeConstantOverride("separation", 6);
        vb.AddChild(new Label { Text = "F1 开发者面板" });
        var b1 = new Button { Text = "新局" }; b1.Pressed += StartRun; vb.AddChild(b1);
        var b2 = new Button { Text = "LAN 建主" }; b2.Pressed += () => (_screen as GameView)?.StartLanHost(); vb.AddChild(b2);
        var b3 = new Button { Text = "LAN 加入" }; b3.Pressed += () => (_screen as GameView)?.StartLanClient(); vb.AddChild(b3);
        var b4 = new Button { Text = "加自定义卡" }; b4.Pressed += () => (_screen as GameView)?.AddCustomCard(); vb.AddChild(b4);
        var b5 = new Button { Text = "推进节点(调试)" }; b5.Pressed += () => { _run?.Advance(); Route(); }; vb.AddChild(b5);
        p.AddChild(vb);
        _devPanel.AddChild(p);
        AddChild(_devPanel);
    }

    private void ResizeView()
    {
        var size = GetViewport().GetVisibleRect().Size;
        if (_screen is GameView gv) gv.ResizeTo(size);
        else if (_screen is Control c) { c.Size = size; c.Position = Vector2.Zero; }
    }

    public static GameState BuildSampleBattle()
    {
        var tl = new Timeline();
        for (int i = 0; i < 6; i++) tl.Nodes.Add(NodeType.Empty);
        var slash = new Enemy { Id = 1, Name = "斩击兵", Kind = EnemyKind.Slash, Power = 5, NodeSlot = 2, Hp = 20 };
        slash.ActionChain.AddRange(new EnemyAction[] { new EnemyAction.Attack(5, 1) });
        var thrust = new Enemy { Id = 2, Name = "突刺兵", Kind = EnemyKind.Thrust, Power = 4, NodeSlot = 4, Hp = 18 };
        thrust.ActionChain.AddRange(new EnemyAction[] { new EnemyAction.Charge(2), new EnemyAction.Attack(4, 2), new EnemyAction.Idle() });
        var striker = new Enemy { Id = 3, Name = "重锤兵", Kind = EnemyKind.Strike, Power = 3, NodeSlot = 5, Hp = 26 };
        striker.ActionChain.AddRange(new EnemyAction[] { new EnemyAction.Charge(3), new EnemyAction.Attack(3, -1) });
        tl.Enemies.Add(slash); tl.Enemies.Add(thrust); tl.Enemies.Add(striker);
        slash.Position = 1; thrust.Position = 2; striker.Position = 3;

        var gs = new GameState(seed: 7) { Timeline = tl };
        int pos = 1;
        foreach (var tpl in CharacterTemplates.All())
        {
            var c = CharacterTemplates.Instantiate(tpl, position: pos);
            c.Draw(3, gs.Rng);
            gs.Characters.Add(c);
            pos++;
        }
        gs.ContractPositions();

        // 布局自检（规格验证清单 #2）
        int hand = gs.Characters[0].Hand.Count;
        var layout = BattleLayout.Compute(gs.AliveCharacters.Count(), gs.Enemies.Count, gs.Timeline.Length, hand, gs.Pointer);
        GD.Print($"[同渊] 布局自检: 无重叠={layout.NoOverlaps()} 在视口内={layout.AllWithinViewport()}");
        return gs;
    }
}
