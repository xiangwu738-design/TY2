using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Tongyuan.Core.Core;
using Tongyuan.Core.Data;
using Tongyuan.Net;
using Tongyuan.UI;

namespace Tongyuan.Views;

/// <summary>
/// 可交互战场表现层（规格 §3/§4.8）。每进程一个。
/// 战场（角色左/敌人右）/时间轴/手牌——占位色块 + 真实按钮/标签，接 Core 出牌/整备/空过/预演。
/// 单人模式：点角色头像切换"当前操控角色"，底部出其手牌。
/// 立绘状态机（§4.8）暂以色块占位，留 PortraitController 扩展位。
/// </summary>
public partial class GameView : Control
{
    [Signal] public delegate void BattleOverEventHandler(bool win);
    public GameState? State { get; set; }
    private int _activeId;
    private PlayerAction? _hoverAction;
    private List<GameEvent>? _hoverEvents;

    // 目标选取（攻击选敌 / 力量附魔选牌）
    private enum TargetMode { None, Enemy, HandCard }
    private TargetMode _targetMode = TargetMode.None;
    private Card? _targetingCard;
    private int _targetingCharId;

    // 立绘状态机：按 id 查找，Play 后喂事件驱动动画
    private readonly Dictionary<int, PortraitController> _charPortraits = new();
    private readonly Dictionary<int, PortraitController> _enemyPortraits = new();

    // 局域网联机
    private NetController? _net;
    private bool _isClient;
    private LineEdit? _ipEdit;
    private Label? _netLabel;

    private Label _topLabel = null!;
    private HBoxContainer _battleField = null!;
    private HBoxContainer _timelineRow = null!;
    private Label _activeLabel = null!;
    private HBoxContainer _handRow = null!;
    private HBoxContainer _actionRow = null!;
    private Label _previewLabel = null!;
    private RichTextLabel _log = null!;
    private MarginContainer? _margin;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        if (State is null) return;
        _activeId = State.Characters[0].Id;
        BuildUi();
        AppendLog($"[b]==== 战斗开始 · 角色={State.Characters.Count} 敌人={State.Enemies.Count} ====[/b]");
        Render();
    }

    /// <summary>由 Main 按视口尺寸驱动：显式设置本节点与内部 MarginContainer 尺寸
    /// （运行时 AddChild 的 Control 不会由普通 Control 父节点自动布局，须显式驱动）。</summary>
    public void ResizeTo(Vector2 size)
    {
        Size = size;
        Position = Vector2.Zero;
        if (_margin is not null)
        {
            _margin.Position = Vector2.Zero;
            _margin.Size = size;
        }
    }

    // ------------------------------------------------------------------ UI 构建
    private void BuildUi()
    {
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_right", 10);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        _margin = margin;
        AddChild(margin);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        margin.AddChild(scroll);

        var vb = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        vb.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(vb);

        // 顶栏
        var topbar = new HBoxContainer();
        topbar.AddThemeConstantOverride("separation", 10);
        _topLabel = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _topLabel.AddThemeFontSizeOverride("font_size", 16);
        topbar.AddChild(_topLabel);
        var nb = new Button { Text = "新局" };
        nb.Pressed += Restart;
        topbar.AddChild(nb);

        // 局域网联机
        topbar.AddChild(new Label { Text = "IP:" });
        _ipEdit = new LineEdit { Text = "127.0.0.1", CustomMinimumSize = new Vector2(120, 0) };
        topbar.AddChild(_ipEdit);
        var hostBtn = new Button { Text = "建主(LAN)" };
        hostBtn.Pressed += StartLanHost;
        topbar.AddChild(hostBtn);
        var joinBtn = new Button { Text = "加入(LAN)" };
        joinBtn.Pressed += StartLanClient;
        topbar.AddChild(joinBtn);
        _netLabel = new Label { Text = "[离线]" };
        _netLabel.AddThemeColorOverride("font_color", Colors.DimGray);
        topbar.AddChild(_netLabel);

        // 自定义卡牌（注册一张示例自定义卡并加入当前手牌）
        var customBtn = new Button { Text = "加自定义卡" };
        customBtn.Pressed += AddCustomCard;
        topbar.AddChild(customBtn);

        vb.AddChild(topbar);

        // 战场
        vb.AddChild(SectionLabel("战场（左=位N后方 ··· 右=位1前线 ··· 敌人 | 点头像切换当前角色）"));
        _battleField = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _battleField.AddThemeConstantOverride("separation", 8);
        vb.AddChild(_battleField);

        // 时间轴
        vb.AddChild(SectionLabel("时间轴 / 行动条（▶ 当前指针；彩=将推进·各角色色，红⚠=将触发）"));
        var tlScroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
            VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
            CustomMinimumSize = new Vector2(0, 108),
        };
        vb.AddChild(tlScroll);
        _timelineRow = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        _timelineRow.AddThemeConstantOverride("separation", 4);
        tlScroll.AddChild(_timelineRow);

        // 行动（牌）
        _activeLabel = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _activeLabel.AddThemeFontSizeOverride("font_size", 14);
        vb.AddChild(_activeLabel);
        _handRow = new HBoxContainer();
        _handRow.AddThemeConstantOverride("separation", 6);
        vb.AddChild(_handRow);
        _actionRow = new HBoxContainer();
        _actionRow.AddThemeConstantOverride("separation", 6);
        vb.AddChild(_actionRow);

        // 预览（文档 §五：数值预览替玩家算——放大加粗，暖金边框，醒目）
        var pp = new Panel { CustomMinimumSize = new Vector2(0, 72) };
        var pstyle = new StyleBoxFlat { BgColor = UiPalette.PanelBg, BorderColor = UiPalette.GoldBorder };
        pstyle.SetBorderWidthAll(2); pstyle.SetCornerRadiusAll(6);
        pstyle.BorderWidthTop = 3;
        pstyle.ContentMarginLeft = 10; pstyle.ContentMarginTop = 6; pstyle.ContentMarginRight = 10; pstyle.ContentMarginBottom = 6;
        pp.AddThemeStyleboxOverride("panel", pstyle);
        vb.AddChild(pp);
        _previewLabel = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _previewLabel.AddThemeFontSizeOverride("font_size", 17);
        _previewLabel.AddThemeColorOverride("font_color", UiPalette.TextMain);
        _previewLabel.SetAnchorsPreset(LayoutPreset.FullRect);
        _previewLabel.AddThemeConstantOverride("offset_left", 10);
        _previewLabel.AddThemeConstantOverride("offset_right", -10);
        _previewLabel.AddThemeConstantOverride("offset_top", 6);
        _previewLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        pp.AddChild(_previewLabel);

        // 日志
        vb.AddChild(SectionLabel("事件日志"));
        _log = new RichTextLabel
        {
            CustomMinimumSize = new Vector2(0, 130),
            BbcodeEnabled = true,
            ScrollFollowing = true,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        _log.AddThemeFontSizeOverride("font_size", 12);
        vb.AddChild(_log);

        // FX 层：伤害飘字等悬浮特效（置顶、不挡鼠标、跨 Render 持久）
        _fxLayer = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _fxLayer.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_fxLayer);
    }

    private Control _fxLayer = null!;

    private static Label SectionLabel(string text)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", 13);
        l.AddThemeColorOverride("font_color", new Color(0.75f, 0.78f, 0.85f));
        return l;
    }

    private static StyleBoxFlat PreviewStyle()
    {
        var sb = new StyleBoxFlat
        {
            BgColor = new Color(0.14f, 0.16f, 0.20f),
            BorderColor = new Color(0.45f, 0.5f, 0.6f),
        };
        sb.SetBorderWidthAll(1);
        sb.ContentMarginLeft = 8; sb.ContentMarginTop = 6; sb.ContentMarginRight = 8; sb.ContentMarginBottom = 6;
        sb.SetCornerRadiusAll(4);
        sb.BorderWidthLeft = 3;
        return sb;
    }

    // ------------------------------------------------------------------ 渲染
    private void Render()
    {
        if (State is null) return;
        EnsureActiveAlive();
        RenderTop();
        RenderBattleField();
        RenderTimeline();
        RenderHand();
        RenderPreview();
    }

    private void RenderHover()
    {
        RenderTimeline();
        RenderPreview();
    }

    private void RenderTop()
    {
        bool over = IsBattleOver();
        string st = over ? ("结束—" + (IsWin() ? "胜" : "败")) : "进行中";
        _topLabel.Text = $"《同渊》正式版 · 指针格={State!.Pointer} · 历史={State.ActionHistory.Count} · {st}";
    }

    private void RenderBattleField()
    {
        ClearChildren(_battleField);
        _charPortraits.Clear();
        _enemyPortraits.Clear();
        var alive = State!.AliveCharacters.OrderByDescending(c => c.Position).ToList();
        foreach (var c in alive)
            _battleField.AddChild(MakePortrait(c, c.Id == _activeId));
        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _battleField.AddChild(spacer);
        var arrow = new Label { Text = "⚔" };
        arrow.AddThemeFontSizeOverride("font_size", 20);
        _battleField.AddChild(arrow);
        // 敌方占位规则与我方对称：只渲染存活敌人、按 Position 1..N 排开；
        // 死亡者由 Core.ContractEnemyPositions 自动收缩，UI 即"死一个少一个"，不留空位。
        foreach (var e in State.Enemies.Where(e => e.IsAlive).OrderBy(e => e.Position))
            _battleField.AddChild(MakeEnemyBlock(e, _targetMode == TargetMode.Enemy));
    }

    private Control MakePortrait(Character c, bool isActive)
    {
        var p = new Button
        {
            CustomMinimumSize = new Vector2(108, 110),
            Text = $"{(isActive ? "▶ " : "")}{c.Name}\n位{c.Position}\nHP {c.Hp}/{c.MaxHp}",
            Disabled = !c.IsAlive,
        };
        p.AddThemeFontSizeOverride("font_size", 12);
        bool aimed = IsAimed(c); // 文档 §二：1位/被瞄准位警示高亮
        var sb = new StyleBoxFlat
        {
            BgColor = ColorOf(c.Color).Darkened(isActive ? 0.45f : 0.72f),
            BorderColor = aimed ? UiPalette.WarnOrange : (isActive ? Colors.White : ColorOf(c.Color)),
        };
        sb.SetBorderWidthAll(3);
        sb.ContentMarginLeft = 5; sb.ContentMarginTop = 3; sb.ContentMarginRight = 5; sb.ContentMarginBottom = 3;
        sb.SetCornerRadiusAll(4);
        p.AddThemeStyleboxOverride("normal", sb);
        p.AddThemeStyleboxOverride("hover", sb);
        p.AddThemeStyleboxOverride("pressed", sb);
        p.AddThemeStyleboxOverride("disabled", sb);

        // 立绘状态机（规格 §4.8）：挂为子节点，按角色 id 绑定；事件驱动动画
        var portrait = new PortraitController
        {
            BoundCharacterId = c.Id,
            DrawW = 44, DrawH = 50,
            Position = new Vector2(54, 28),
            IdleBreath = c.IsAlive,
        };
        if (!c.IsAlive) portrait.ToDown();
        p.AddChild(portrait);
        _charPortraits[c.Id] = portrait;

        int id = c.Id;
        p.Pressed += () => { _activeId = id; Render(); };
        return p;
    }

    /// <summary>该角色是否被任一存活敌人的下一步攻击瞄准（含全体）。</summary>
    private bool IsAimed(Character c)
    {
        if (State is null || !c.IsAlive) return false;
        foreach (var e in State.Enemies)
        {
            if (!e.IsAlive) continue;
            if (e.NextAction is EnemyAction.Attack a)
            {
                int tp = a.TargetPos ?? e.TargetPosition;
                if (tp == -1 || tp == c.Position) return true;
            }
        }
        return false;
    }

    private Control MakeEnemyBlock(Enemy e, bool clickable)
    {
        if (clickable && e.IsAlive)
        {
            var btn = new Button
            {
                Text = $"➜ {e.Name}\n{IntentText(e)}\nHP {e.Hp}",
                CustomMinimumSize = new Vector2(120, 92),
            };
            btn.AddThemeFontSizeOverride("font_size", 11);
            var csb = new StyleBoxFlat
            {
                BgColor = new Color(0.30f, 0.16f, 0.10f),
                BorderColor = new Color(1f, 0.85f, 0.3f),
            };
            csb.SetBorderWidthAll(3); csb.SetCornerRadiusAll(4);
            btn.AddThemeStyleboxOverride("normal", csb);
            btn.AddThemeStyleboxOverride("hover", csb);
            AttachEnemyPortrait(e, btn);
            int eid = e.Id;
            btn.Pressed += () => PlayCardAtEnemy(eid);
            return btn;
        }

        var p = new PanelContainer { CustomMinimumSize = new Vector2(120, 92) };
        var sb = new StyleBoxFlat
        {
            BgColor = new Color(0.20f, 0.10f, 0.10f),
            BorderColor = new Color(0.85f, 0.35f, 0.35f),
        };
        sb.SetBorderWidthAll(3);
        sb.ContentMarginLeft = 5; sb.ContentMarginTop = 3; sb.ContentMarginRight = 5; sb.ContentMarginBottom = 3;
        sb.SetCornerRadiusAll(4);
        p.AddThemeStyleboxOverride("panel", sb);
        var vb = new VBoxContainer();
        vb.AddThemeConstantOverride("separation", 1);
        p.AddChild(vb);
        var nl = new Label { Text = e.IsAlive ? e.Name : $"{e.Name}×" };
        nl.AddThemeColorOverride("font_color", new Color(1f, 0.6f, 0.6f));
        nl.AddThemeFontSizeOverride("font_size", 13);
        vb.AddChild(nl);
        var kind = new Label { Text = $"{KindText(e.Kind)} · 下一步:{IntentText(e)}" };
        kind.AddThemeFontSizeOverride("font_size", 11);
        kind.AddThemeColorOverride("font_color", new Color(0.85f, 0.7f, 0.4f));
        vb.AddChild(kind);
        var bar = new ProgressBar { MinValue = 0, MaxValue = e.Hp > 0 ? e.Hp : 1, Value = e.Hp, CustomMinimumSize = new Vector2(96, 0) };
        vb.AddChild(bar);
        var hl = new Label { Text = $"HP {e.Hp}" };
        hl.AddThemeFontSizeOverride("font_size", 10);
        vb.AddChild(hl);
        if (e.Charge > 0)
        {
            var cl = new Label { Text = $"蓄+{e.Charge}" };
            cl.AddThemeColorOverride("font_color", new Color(1f, 0.8f, 0.3f));
            cl.AddThemeFontSizeOverride("font_size", 10);
            vb.AddChild(cl);
        }
        // 易伤标记（附魔只读显示器，文档 §五）
        int vuln = e.Statuses.Where(s => s.Type == EnchantmentType.Vulnerable).Sum(s => s.Magnitude);
        int vulnTimes = e.Statuses.Where(s => s.Type == EnchantmentType.Vulnerable).Sum(s => s.Remaining);
        if (vuln > 0)
        {
            var vl = new Label { Text = $"易伤+{vuln}×{vulnTimes}" };
            vl.AddThemeColorOverride("font_color", UiPalette.VulnGold);
            vl.AddThemeFontSizeOverride("font_size", 10);
            vb.AddChild(vl);
        }
        AttachEnemyPortrait(e, p);
        return p;
    }

    /// <summary>给敌人块挂立绘状态机（顶部小色块，按 id 绑定）。</summary>
    private void AttachEnemyPortrait(Enemy e, Control parent)
    {
        var portrait = new PortraitController
        {
            BoundEnemyId = e.Id,
            DrawW = 30, DrawH = 30,
            Position = new Vector2(20, 18),
            IdleBreath = e.IsAlive,
        };
        if (!e.IsAlive) portrait.ToDown();
        parent.AddChild(portrait);
        _enemyPortraits[e.Id] = portrait;
    }

    /// <summary>敌人下一步行动的意图文本（受击模式差异化展示）。</summary>
    private static string IntentText(Enemy e)
    {
        if (!e.IsAlive) return "—";
        return e.NextAction switch
        {
            EnemyAction.Attack a => a.TargetPos == -1 ? $"打全体 {a.Amount + e.Charge}伤"
                                   : a.TargetPos == 1 ? $"斩位1 {a.Amount + e.Charge}伤"
                                   : a.TargetPos == 2 ? $"突位2 {a.Amount + e.Charge}伤"
                                   : $"{a.Amount + e.Charge}伤",
            EnemyAction.Charge c => $"蓄力+{c.Amount}",
            EnemyAction.Idle => "待机",
            _ => "?",
        };
    }

    private void PlayCardAtEnemy(int enemyId)
    {
        if (_targetingCard is not Card card) return;
        var action = new PlayerAction(_targetingCharId, ActionType.PlayCard, card.InstanceId, TargetEnemyId: enemyId);
        _targetMode = TargetMode.None;
        _targetingCard = null;
        Play(action);
    }

    private void PlayCardAtHandCard(Guid targetCardId)
    {
        if (_targetingCard is not Card card) return;
        var action = new PlayerAction(_targetingCharId, ActionType.PlayCard, card.InstanceId, TargetCardInstanceId: targetCardId);
        _targetMode = TargetMode.None;
        _targetingCard = null;
        Play(action);
    }

    private void RenderTimeline()
    {
        ClearChildren(_timelineRow);
        if (State is null) return;
        int len = State.Timeline.Length;
        int start = State.Pointer;
        var trav = TraversedSet();
        var trig = TriggeredPreviewSet();
        var pcolor = PreviewColor();
        int show = Math.Min(12, len);
        for (int i = 0; i < show; i++)
        {
            int cell = (start + i) % len; // 时间轴循环
            _timelineRow.AddChild(MakeCell(cell, trav, trig, pcolor));
        }
    }

    private Control MakeCell(int cell, HashSet<int> trav, HashSet<int> trig, Color previewColor)
    {
        var p = new Panel { CustomMinimumSize = new Vector2(66, 96) };
        var sb = new StyleBoxFlat();
        sb.SetCornerRadiusAll(4);
        var bg = new Color(0.12f, 0.12f, 0.15f);
        if (cell == State!.Pointer) bg = new Color(0.30f, 0.30f, 0.42f);
        if (trav.Contains(cell)) bg = previewColor;
        sb.BgColor = bg;
        p.AddThemeStyleboxOverride("panel", sb);
        var vb = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        vb.AddThemeConstantOverride("separation", 2);
        p.AddChild(vb);
        var l1 = new Label { Text = cell == State.Pointer ? $"▶格{cell}" : $"格{cell}" };
        l1.AddThemeFontSizeOverride("font_size", 11);
        vb.AddChild(l1);
        var enemy = State.Timeline.EnemyAt(cell);
        if (enemy is not null && enemy.IsAlive)
        {
            var nl = new Label { Text = $"{KindText(enemy.Kind)}{enemy.EffectivePower}" };
            nl.AddThemeFontSizeOverride("font_size", 12);
            nl.AddThemeColorOverride("font_color", EnemyColor(enemy.Kind));
            vb.AddChild(nl);
        }
        if (trig.Contains(cell))
        {
            var t = new Label { Text = "⚠触发" };
            t.AddThemeColorOverride("font_color", new Color(1f, 0.35f, 0.35f));
            t.AddThemeFontSizeOverride("font_size", 10);
            vb.AddChild(t);
        }
        return p;
    }

    private void RenderHand()
    {
        var c = ActiveCharacter;
        _activeLabel.Text = c is null || !c.IsAlive
            ? "（无可用角色）"
            : $"{c.Name}（位{c.Position} · HP {c.Hp}/{c.MaxHp} · 抽{c.DrawPile.Count} 弃{c.DiscardPile.Count} 手{c.Hand.Count}）";
        _activeLabel.AddThemeColorOverride("font_color", c is null ? Colors.DimGray : ColorOf(c.Color));

        ClearChildren(_handRow);
        ClearChildren(_actionRow);
        if (c is null || !c.IsAlive || IsBattleOver()) return;

        // 目标选取模式
        if (_targetMode != TargetMode.None)
        {
            string hint = _targetMode == TargetMode.Enemy
                ? "🎯 选择目标敌人（远程不位移 / 近战移位1）"
                : "✦ 选择一张手牌挂力量附魔";
            _handRow.AddChild(new Label { Text = hint });
            var cancel = new Button { Text = "取消" };
            cancel.Pressed += CancelTargeting;
            _actionRow.AddChild(cancel);
            if (_targetMode == TargetMode.HandCard && _targetingCard is Card ench)
            {
                foreach (var card in c.Hand)
                {
                    if (card.InstanceId == ench.InstanceId) continue; // 不附魔自己
                    var btn = new Button
                    {
                        Text = $"➜ {card.Def.Name}·{CardDef.DamageText(card.Def.DamageType)}",
                        CustomMinimumSize = new Vector2(120, 40),
                    };
                    btn.AddThemeFontSizeOverride("font_size", 11);
                    Guid tid = card.InstanceId;
                    btn.Pressed += () => PlayCardAtHandCard(tid);
                    _actionRow.AddChild(btn);
                }
            }
            return;
        }

        foreach (var card in c.Hand)
            _handRow.AddChild(MakeCardButton(c, card));

        // 整备牌（回手，常驻）+ 空过
        if (c.PrepCard is not null)
        {
            var prep = c.PrepCard;
            var btn = new Button
            {
                Text = $"{prep.Def.Name} 占{prep.Def.Cost}\n（回手·抽{prep.Def.Magnitude}）",
                CustomMinimumSize = new Vector2(110, 40),
            };
            btn.AddThemeFontSizeOverride("font_size", 11);
            int id = c.Id;
            btn.MouseEntered += () => Hover(new PlayerAction(id, ActionType.UsePrep));
            btn.MouseExited += ClearHover;
            btn.Pressed += () => Play(new PlayerAction(id, ActionType.UsePrep));
            _actionRow.AddChild(btn);
        }
        var skip = new Button { Text = "空过", CustomMinimumSize = new Vector2(64, 40) };
        skip.AddThemeFontSizeOverride("font_size", 12);
        int sid = c.Id;
        skip.MouseEntered += () => Hover(new PlayerAction(sid, ActionType.Skip));
        skip.MouseExited += ClearHover;
        skip.Pressed += () => Play(new PlayerAction(sid, ActionType.Skip));
        _actionRow.AddChild(skip);
    }

    private Control MakeCardButton(Character c, Card card)
    {
        var def = card.Def;
        var view = new CardView();
        view.Setup(card, c);
        var action = ActionForCard(c, card);
        view.OnHovered += _ => Hover(action);
        view.OnUnhovered += _ => ClearHover();
        int id = c.Id;
        view.OnClicked += _ =>
        {
            if (NeedsTarget(def)) BeginTargeting(id, card);
            else Play(action);
        };
        return view;
    }

    private static Color CardBgColor(CardDef def) => def.Type switch
    {
        CardType.Attack => def.DamageType switch
        {
            DamageType.Ranged => new Color(0.18f, 0.22f, 0.30f),
            DamageType.Blunt => new Color(0.26f, 0.16f, 0.14f),
            DamageType.Thrust => new Color(0.20f, 0.18f, 0.28f),
            _ => new Color(0.24f, 0.14f, 0.14f),
        },
        CardType.Defense => new Color(0.14f, 0.20f, 0.26f),
        CardType.Skill => new Color(0.22f, 0.16f, 0.26f),
        _ => new Color(0.16f, 0.22f, 0.18f),
    };
    private static Color CardAccentColor(CardDef def) => def.Type == CardType.Attack
        ? (def.DamageType == DamageType.Ranged ? new Color(0.5f, 0.8f, 1f) : new Color(0.9f, 0.5f, 0.4f))
        : new Color(0.6f, 0.7f, 0.85f);

    /// <summary>需要玩家点选目标的牌：远程攻击、代码卡(NeedsTargetEnemy)、力量附魔·具体牌。
    /// 近战（打击/斩击/突刺）固定自动锁定（规格：仅远程自选）。</summary>
    private static bool NeedsTarget(CardDef def) =>
        (def.Effect == EffectKind.AttackDamage && def.DamageType == DamageType.Ranged)
        || def.NeedsTargetEnemy
        || (def.Effect == EffectKind.ApplyEnchantment && def.EnchantType == EnchantmentType.Power && def.EnchantScope == EnchantmentScope.SpecificCard);

    private void BeginTargeting(int charId, Card card)
    {
        var def = card.Def;
        _targetingCharId = charId;
        _targetingCard = card;
        _targetMode = def.Effect == EffectKind.AttackDamage ? TargetMode.Enemy : TargetMode.HandCard;
        _hoverAction = null;
        _hoverEvents = null;
        Render();
    }

    private void CancelTargeting()
    {
        _targetMode = TargetMode.None;
        _targetingCard = null;
        Render();
    }

    // ------------------------------------------------------------------ 动作
    private PlayerAction ActionForCard(Character c, Card card)
    {
        var def = card.Def;
        int? targetEnemy = null;
        int? targetChar = null;
        Guid? targetCard = null;
        switch (def.Effect)
        {
            case EffectKind.AttackDamage:
                targetEnemy = FirstAliveEnemyId();
                break;
            case EffectKind.ApplyShield:
                targetChar = c.Id;
                break;
            case EffectKind.ApplyEnchantment:
                if (def.EnchantType == EnchantmentType.Vulnerable || def.EnchantType == EnchantmentType.Charge)
                    targetEnemy = FirstAliveEnemyId();
                else if (def.EnchantType == EnchantmentType.Power && def.EnchantScope == EnchantmentScope.SpecificCard)
                    targetCard = c.Hand.FirstOrDefault()?.InstanceId ?? c.PrepCard?.InstanceId;
                break;
        }
        return new PlayerAction(c.Id, ActionType.PlayCard, card.InstanceId, targetChar, targetEnemy, targetCard);
    }

    private int? FirstAliveEnemyId() => State?.Enemies.FirstOrDefault(e => e.IsAlive)?.Id;

    private void Play(PlayerAction action)
    {
        if (State is null || IsBattleOver()) return;

        // 客户端：发主机，不本地结算（主机权威）；等广播回来自动重渲染
        if (_net is not null && _isClient) { _net.SubmitAction(action); return; }

        // 主机 / 离线：本地权威结算
        var ev = State.Apply(action);
        LogEvents(ev);
        _hoverAction = null;
        _hoverEvents = null;
        _targetMode = TargetMode.None;
        _targetingCard = null;
        Render();
        AnimatePortraits(ev);   // 立绘状态机协同
        PlayCardAnimation(action); // 出牌动画
        if (_net is not null && !_isClient) _net.Broadcast(action); // 主机广播
        if (IsBattleOver()) EmitSignal(SignalName.BattleOver, IsWin()); // 通知路由器
    }

    /// <summary>客户端/主机收到远端动作后：用最新状态重渲染 + 日志 + 立绘动画。</summary>
    private void OnNetApplied()
    {
        if (_net?.State is not null) State = _net.State; // 客户端快照重放后切换到权威状态
        if (State is null) return;
        LogEvents(State.Events);
        AnimatePortraits(State.Events);
        _netLabel!.Text = _isClient ? "[已加入·同步]" : "[建主·端口" + NetController.DefaultPort + "]";
        Render();
    }

    // ---- 局域网联机 ----
    public void StartLanHost()
    {
        if (State is null) return;
        _net?.QueueFree();
        _net = new NetController();
        _net.ActionApplied += OnNetApplied;
        AddChild(_net);
        _net.StartHost(NetController.DefaultPort, State);
        _isClient = false;
        _netLabel!.Text = "[建主·端口" + NetController.DefaultPort + "]";
        AppendLog("[b]==== 局域网建主，等客户端加入 ====[/b]");
    }

    public void StartLanClient()
    {
        if (State is null) return;
        _net?.QueueFree();
        _net = new NetController();
        _net.ActionApplied += OnNetApplied;
        AddChild(_net);
        _net.StartClient(_ipEdit?.Text ?? "127.0.0.1", NetController.DefaultPort, State.Seed);
        _isClient = true;
        _netLabel!.Text = "[加入中…]";
        AppendLog("[b]==== 加入局域网 ====[/b]");
    }

    // ---- 自定义/示例卡牌（规格 §6/§7 可扩展；数据卡 + 代码卡）----
    private static readonly CardDef[] DealtCatalog = SampleCards.All.Concat(CodeCards.All).ToArray();
    private int _sampleIdx;
    public void AddCustomCard()
    {
        var c = ActiveCharacter;
        if (c is null || State is null) return;
        var def = DealtCatalog[_sampleIdx % DealtCatalog.Length];
        _sampleIdx++;
        c.Hand.Add(new Card { Def = def });
        string tag = def.CustomEffect is not null ? "代码卡" : "数据卡";
        AppendLog($"[color=#80ff80]✦ 发示例卡[{tag}]：{def.Name}（{def.EffectDescription()}）[/color]");
        Render();
    }

    // ---- 出牌动画：中央卡牌代理缩放+上浮+淡出（Tween，不阻塞 Core）----
    private void PlayCardAnimation(PlayerAction action)
    {
        var size = GetViewport().GetVisibleRect().Size;
        var proxy = new Panel { CustomMinimumSize = new Vector2(90, 120) };
        var sb = new StyleBoxFlat { BgColor = new Color(0.95f, 0.78f, 0.2f, 0.92f), BorderColor = Colors.White };
        sb.SetCornerRadiusAll(6); sb.SetBorderWidthAll(2);
        proxy.AddThemeStyleboxOverride("panel", sb);
        proxy.PivotOffset = new Vector2(45, 60);
        proxy.Position = new Vector2(size.X / 2f - 45, size.Y / 2f - 60);
        proxy.Scale = new Vector2(0.3f, 0.3f);
        AddChild(proxy);
        var tw = CreateTween();
        tw.TweenProperty(proxy, "scale", Vector2.One, 0.12).SetTrans(Tween.TransitionType.Back);
        tw.Parallel().TweenProperty(proxy, "position:y", size.Y / 2f - 90, 0.20);
        tw.TweenInterval(0.10);
        tw.TweenProperty(proxy, "modulate:a", 0f, 0.18);
        tw.TweenCallback(Callable.From(() => { if (IsInstanceValid(proxy)) proxy.QueueFree(); }));
    }

    /// <summary>把事件流喂给各立绘（每 PortraitController.OnEvent 按 id 过滤，只响应自身）。</summary>
    private void AnimatePortraits(List<GameEvent> ev)
    {
        // 收集伤害事件，延迟到下一帧布局 settled 后按 GlobalPosition 生成飘字
        foreach (var e in ev)
        {
            foreach (var p in _charPortraits.Values) p.OnEvent(e);
            foreach (var p in _enemyPortraits.Values) p.OnEvent(e);
            if (e is GameEvent.DamageDealt dd)
            {
                var portrait = dd.TargetIsEnemy
                    ? (_enemyPortraits.TryGetValue(dd.TargetId, out var ep) ? ep : null)
                    : (_charPortraits.TryGetValue(dd.TargetId, out var cp) ? cp : null);
                if (portrait is not null) _pendingDmg.Add((portrait, dd.Amount, dd.TargetIsEnemy));
            }
        }
        if (_pendingDmg.Count > 0) CallDeferred(MethodName.SpawnPendingDamageNumbers);
    }

    private readonly List<(PortraitController Portrait, int Amount, bool Enemy)> _pendingDmg = new();

    private void SpawnPendingDamageNumbers()
    {
        foreach (var (portrait, amount, enemy) in _pendingDmg)
        {
            if (IsInstanceValid(portrait))
                SpawnDamageNumber(portrait.GlobalPosition, amount, enemy);
        }
        _pendingDmg.Clear();
    }

    /// <summary>在 pos 处生成上飘+淡出的伤害数字（敌受击=暖金，角色受击=警示红，治疗负值=青白）。</summary>
    private void SpawnDamageNumber(Vector2 pos, int amount, bool targetIsEnemy)
    {
        bool heal = amount < 0;
        var label = new Label
        {
            Text = heal ? $"+{-amount}" : amount.ToString(),
            ZIndex = 100,
            Position = pos + new Vector2(-14, -40),
            Size = new Vector2(60, 30),
        };
        label.AddThemeFontSizeOverride("font_size", heal ? 20 : (amount >= 8 ? 28 : 22));
        label.AddThemeColorOverride("font_color", heal ? UiPalette.ShieldTeal : (targetIsEnemy ? UiPalette.VulnGold : UiPalette.WarnOrange));
        _fxLayer.AddChild(label);
        var tw = CreateTween();
        tw.TweenProperty(label, "position:y", label.Position.Y - 44, 0.55f).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        tw.Parallel().TweenProperty(label, "modulate:a", 0f, 0.7f).SetDelay(0.2f);
        tw.TweenCallback(Callable.From(() => { if (IsInstanceValid(label)) label.QueueFree(); }));
    }

    private void Hover(PlayerAction action)
    {
        if (State is null || IsBattleOver()) return;
        _hoverAction = action;
        _hoverEvents = State.Preview(action);
        RenderHover();
    }

    private void ClearHover()
    {
        _hoverAction = null;
        _hoverEvents = null;
        RenderHover();
    }

    private void Restart()
    {
        State = Main.BuildSampleBattle();
        _activeId = State.Characters[0].Id;
        _hoverAction = null;
        _hoverEvents = null;
        _targetMode = TargetMode.None;
        _targetingCard = null;
        _log.Clear();
        AppendLog("[b]==== 新局开始 ====[/b]");
        Render();
    }

    // ------------------------------------------------------------------ 预览/辅助
    private void RenderPreview()
    {
        if (_hoverAction is not PlayerAction a || _hoverEvents is null)
        {
            _previewLabel.Text = "（悬停手牌 / 整备 / 空过 查看预计后果）";
            _previewLabel.AddThemeColorOverride("font_color", Colors.DimGray);
            return;
        }
        _previewLabel.RemoveThemeColorOverride("font_color");
        var c = State?.Characters.Find(x => x.Id == a.CharacterId);
        int occ = ActionCost(a);
        int len = State?.Timeline.Length ?? 1;
        int dest = len > 0 ? (State!.Pointer + occ) % len : State!.Pointer + occ;
        string head = a.Type switch
        {
            ActionType.PlayCard => $"{(c?.Hand.Find(card => card.InstanceId == a.CardInstanceId)?.Def.Name ?? "牌")} · 占{occ}→格{dest}",
            ActionType.UsePrep => $"{c?.PrepCard?.Def.Name} · 占{occ}→格{dest}（回手·抽{c?.PrepCard?.Def.Magnitude}）",
            ActionType.Skip => $"空过 · →格{dest}",
            _ => "",
        };
        var parts = new List<string> { head };
        var triggers = new List<string>();
        int enemyDmg = 0;
        var charDmg = new List<string>();
        foreach (var e in _hoverEvents)
        {
            if (e is GameEvent.EnemyTriggered et)
            {
                var en = State!.Enemies.Find(x => x.Id == et.EnemyId);
                triggers.Add($"{(en?.Name ?? "?")}{et.Damage}");
            }
            else if (e is GameEvent.DamageDealt dd)
            {
                if (dd.TargetIsEnemy) enemyDmg += dd.Amount;
                else
                {
                    var t = State!.Characters.Find(x => x.Id == dd.TargetId);
                    charDmg.Add($"{t?.Name ?? "?"}-{dd.Amount}");
                }
            }
        }
        if (triggers.Count > 0) parts.Add("触发: " + string.Join(", ", triggers));
        if (charDmg.Count > 0) parts.Add("受伤: " + string.Join(", ", charDmg));
        if (enemyDmg > 0) parts.Add($"敌-{enemyDmg}");
        // 文档 §五：数值预览实时含修正（附魔/易伤/蓄力），旁注"已含修正"
        if (_hoverEvents.Any(e => e is GameEvent.EnchantmentApplied)
            || _hoverEvents.OfType<GameEvent.EnemyTriggered>().Any(et => State?.Enemies.Find(x => x.Id == et.EnemyId)?.Charge > 0))
            parts.Add("（已含修正）");
        if (_hoverEvents.Any(e => e is GameEvent.CharacterDied)) parts.Add("【角色阵亡】");
        if (_hoverEvents.Any(e => e is GameEvent.EnemyDied)) parts.Add("【敌死】");
        _previewLabel.Text = string.Join("   ", parts);
    }

    private int ActionCost(PlayerAction a)
    {
        var c = State?.Characters.Find(x => x.Id == a.CharacterId);
        return a.Type switch
        {
            ActionType.PlayCard => c?.Hand.Find(card => card.InstanceId == a.CardInstanceId)?.Def.Cost ?? 0,
            ActionType.UsePrep => c?.PrepCard?.Def.Cost ?? 0,
            ActionType.Skip => 1,
            _ => 0,
        };
    }

    private HashSet<int> TraversedSet()
    {
        var d = new HashSet<int>();
        if (_hoverAction is not PlayerAction a || State is null) return d;
        int occ = ActionCost(a);
        int len = State.Timeline.Length;
        if (len <= 0) return d;
        for (int i = 1; i <= occ; i++) d.Add((State.Pointer + i) % len); // 循环
        return d;
    }

    private HashSet<int> TriggeredPreviewSet()
    {
        var d = new HashSet<int>();
        if (_hoverEvents is null) return d;
        if (_hoverAction is not PlayerAction a || State is null) return d;
        int occ = ActionCost(a);
        int len = State.Timeline.Length;
        if (len <= 0) return d;
        for (int i = 1; i <= occ; i++)
        {
            int slot = (State.Pointer + i) % len; // 循环
            var en = State.Timeline.EnemyAt(slot);
            if (en is not null && en.IsAlive) d.Add(slot);
        }
        return d;
    }

    private Color PreviewColor()
    {
        if (_hoverAction is not PlayerAction a || State is null) return new Color(0.52f, 0.46f, 0.16f);
        var c = State.Characters.Find(x => x.Id == a.CharacterId);
        return c is null ? new Color(0.52f, 0.46f, 0.16f) : ColorOf(c.Color).Darkened(0.5f);
    }

    private void LogEvents(List<GameEvent> ev)
    {
        foreach (var e in ev) AppendLog(FormatEvent(e));
    }

    private void AppendLog(string s) => _log?.AppendText(s + "\n");

    private string FormatEvent(GameEvent e)
    {
        return e switch
        {
            GameEvent.PointerMoved pm => $"    → 指针 {pm.From}→{pm.To}",
            GameEvent.EnemyTriggered et => $"    [color=#ff8080]敌 {EnemyName(et.EnemyId)} 触发 → 位{et.TargetPosition} {et.Damage}伤[/color]",
            GameEvent.EnemyCharged ec => $"    [color=#ffd070]敌 {EnemyName(ec.EnemyId)} 蓄力 +{ec.Amount}（下次攻击附带）[/color]",
            GameEvent.EnemyIdle ei => $"    敌 {EnemyName(ei.EnemyId)} 待机",
            GameEvent.DamageDealt dd => dd.TargetIsEnemy
                ? $"    敌 {dd.TargetId} 受 {dd.Amount} 伤"
                : $"       {CharName(dd.TargetId)} 受 {dd.Amount} 伤",
            GameEvent.CardPlayed cp => $"▸ {CharName(cp.CharacterId)} 出牌",
            GameEvent.PrepUsed pu => $"▸ {CharName(pu.CharacterId)} 整备（抽{pu.Drawn}）",
            GameEvent.CardsDrawn cd => $"    {CharName(cd.CharacterId)} 抽 {cd.Count}",
            GameEvent.PositionChanged pc => $"    {CharName(pc.CharacterId)} 位 {pc.From}→{pc.To}",
            GameEvent.CharacterDied cd => $"    [color=#ffaa55]{CharName(cd.CharacterId)} 阵亡[/color]",
            GameEvent.EnemyDied ed => $"    [color=#80ff80]敌 {EnemyName(ed.EnemyId)} 被击败[/color]",
            GameEvent.ShieldPlaced sp => $"    {CharName(sp.GuardianId)} 铺盾→{CharName(sp.ProtectedId)}",
            GameEvent.ShieldAbsorbed sa => $"    护盾吸收 {sa.Amount}{(sa.Exhausted ? "（耗尽）" : "")}",
            GameEvent.EnchantmentApplied ea => $"    [color=#ffd070]✦ 附魔 {EnchantName(ea.Enchantment.Type)} +{ea.Enchantment.Magnitude}{(ea.TargetEnemyId is not null ? " →敌" : ea.TargetCharacterId is not null ? " →角色" : " →牌")}[/color]",
            GameEvent.TurnEnded => "",
            _ => "",
        };
    }

    private string CharName(int id) => State?.Characters.Find(c => c.Id == id)?.Name ?? id.ToString();
    private string EnemyName(int id) => State?.Enemies.Find(e => e.Id == id)?.Name ?? id.ToString();

    private static string KindText(EnemyKind k) => k switch
    {
        EnemyKind.Slash => "斩",
        EnemyKind.Thrust => "突",
        EnemyKind.Strike => "打",
        _ => "?",
    };

    private static string EnchantName(EnchantmentType t) => t switch
    {
        EnchantmentType.Power => "力量",
        EnchantmentType.Vulnerable => "易伤",
        EnchantmentType.Charge => "蓄力",
        _ => t.ToString(),
    };

    private static Color EnemyColor(EnemyKind k) => k switch
    {
        EnemyKind.Slash => new Color(1f, 0.35f, 0.35f),
        EnemyKind.Thrust => new Color(0.95f, 0.65f, 0.25f),
        EnemyKind.Strike => new Color(0.70f, 0.45f, 0.95f),
        _ => Colors.White,
    };

    private static Color ColorOf(int argb)
    {
        float a = ((argb >> 24) & 0xFF) / 255f;
        float r = ((argb >> 16) & 0xFF) / 255f;
        float g = ((argb >> 8) & 0xFF) / 255f;
        float b = (argb & 0xFF) / 255f;
        return new Color(r, g, b, a);
    }

    private Character? ActiveCharacter => State?.Characters.Find(c => c.Id == _activeId);

    private void EnsureActiveAlive()
    {
        var c = ActiveCharacter;
        if (c is null || !c.IsAlive)
        {
            var first = State?.AliveCharacters.FirstOrDefault();
            if (first is not null) _activeId = first.Id;
        }
    }

    private bool IsBattleOver() => IsWin() || IsLose();
    private bool IsWin() => State is not null && State.Enemies.All(e => !e.IsAlive);
    private bool IsLose() => State is not null && !State.AliveCharacters.Any();

    private static void ClearChildren(Node node)
    {
        foreach (var n in node.GetChildren()) n.QueueFree();
    }
}
