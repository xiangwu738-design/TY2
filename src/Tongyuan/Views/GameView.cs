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

    // 头像组件：按 id 查找，Play 后喂事件驱动立绘动画
    private readonly Dictionary<int, PortraitView> _charPortraits = new();
    private readonly Dictionary<int, PortraitView> _enemyPortraits = new();

    // 局域网联机
    private NetController? _net;
    private bool _isClient;
    private LineEdit? _ipEdit;
    private Label? _netLabel;

    private Label _topLabel = null!;
    private HBoxContainer _battleField = null!;
    private HBoxContainer _timelineRow = null!;
    private Label _activeLabel = null!;
    private Control _handRow = null!;
    private HBoxContainer _actionRow = null!;
    private RichTextLabel _log = null!;
    private MarginContainer? _margin;
    private VBoxContainer _sidebar = null!;
    private Button _sidebarToggle = null!;
    private bool _sidebarOpen;

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
        _viewSize = size;
        Size = size;
        Position = Vector2.Zero;
        if (_margin is not null)
        {
            _margin.Position = Vector2.Zero;
            _margin.Size = size;
        }
    }
    private Vector2 _viewSize = new(1920, 1080);

    // ------------------------------------------------------------------ UI 构建
    private void BuildUi()
    {
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_right", 10);
        margin.AddThemeConstantOverride("margin_top", 24);   // 整体下移
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

        // 主区(左) + 日志侧边栏(右)
        var row = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 12);
        scroll.AddChild(row);

        var vb = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        vb.AddThemeConstantOverride("separation", 14);
        row.AddChild(vb);

        // 日志侧边栏（默认折叠，点按钮展开）
        _sidebar = new VBoxContainer { CustomMinimumSize = new Vector2(76, 0), SizeFlagsVertical = SizeFlags.ExpandFill };
        _sidebar.AddThemeConstantOverride("separation", 4);
        row.AddChild(_sidebar);
        _sidebarToggle = new Button { Text = "◀ 日志" };
        _sidebarToggle.AddThemeFontSizeOverride("font_size", 11);
        _sidebarToggle.Pressed += ToggleSidebar;
        _sidebar.AddChild(_sidebarToggle);
        _log = new RichTextLabel
        {
            BbcodeEnabled = true,
            ScrollFollowing = true,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(300, 0),
            Visible = false,
        };
        _log.AddThemeFontSizeOverride("font_size", 12);
        _sidebar.AddChild(_log);

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

        // 战场（居中：左右各加弹性间距，角色组和敌人组聚拢在视口中央）
        vb.AddChild(SectionLabel("战场（左=后排 ··· 前线⚔敌人 | 点头像切换当前角色）"));
        var battleWrapper = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var bLeft = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        battleWrapper.AddChild(bLeft);
        _battleField = new HBoxContainer();
        _battleField.AddThemeConstantOverride("separation", 10);
        battleWrapper.AddChild(_battleField);
        var bRight = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        battleWrapper.AddChild(bRight);
        vb.AddChild(battleWrapper);

        // 时间轴
        vb.AddChild(SectionLabel("时间轴 / 行动条（▶ 当前指针；彩=将推进·各角色色，红⚠=将触发）"));
        var tlClip = new Control
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 108),
            ClipContents = true,
        };
        vb.AddChild(tlClip);
        _timelineRow = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        _timelineRow.AddThemeConstantOverride("separation", 4);
        tlClip.AddChild(_timelineRow);

        // 行动（牌）
        _activeLabel = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _activeLabel.AddThemeFontSizeOverride("font_size", 14);
        vb.AddChild(_activeLabel);
        _handRow = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 300) };
        vb.AddChild(_handRow);
        _actionRow = new HBoxContainer();
        _actionRow.AddThemeConstantOverride("separation", 6);
        vb.AddChild(_actionRow);

        // （预计框已删除——预计后果改为悬浮在牌上方，见 CardView.ShowPreview）

        // （日志已移到右侧侧边栏）

        // FX 层：伤害飘字等悬浮特效（置顶、不挡鼠标、跨 Render 持久）
        _fxLayer = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _fxLayer.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_fxLayer);

        // 指针光点（文档 §七：指针逐格平滑滑动）——持久节点，出牌后 Tween 到当前格
        _pointerGlow = new ColorRect { CustomMinimumSize = new Vector2(10, 10), Color = UiPalette.PointerGold, ZIndex = 90, MouseFilter = MouseFilterEnum.Ignore };
        _fxLayer.AddChild(_pointerGlow);
    }

    private Control _fxLayer = null!;
    private ColorRect _pointerGlow = null!;
    private Control? _modalBg;
    private CardView? _hoveredCard;
    private Control? _detailPopup;
    private float _pendingTimelineShift;

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
    }

    private void RenderHover()
    {
        RenderTimeline(); // 遍历/触发高亮（预计框已移到牌上方悬浮）
    }

    private void RenderTop()
    {
        bool over = IsBattleOver();
        string st = over ? ("结束—" + (IsWin() ? "胜" : "败")) : "进行中";
        string q = _playQueue.Count > 0 ? $" · 队列{_playQueue.Count}/{QueueCap}" : (_playing ? " · 演出中" : "");
        _topLabel.Text = $"《同渊》正式版 · 指针格={State!.Pointer} · 历史={State.ActionHistory.Count} · {st}{q}";
    }

    private void RenderBattleField()
    {
        ClearChildren(_battleField);
        _charPortraits.Clear();
        _enemyPortraits.Clear();
        var alive = State!.AliveCharacters.OrderByDescending(c => c.Position).ToList();
        foreach (var c in alive)
            _battleField.AddChild(MakePortrait(c, c.Id == _activeId));
        var gap1 = new Control { CustomMinimumSize = new Vector2(24, 0) };
        _battleField.AddChild(gap1);
        var arrow = new Label { Text = "⚔", VerticalAlignment = VerticalAlignment.Center };
        arrow.AddThemeFontSizeOverride("font_size", 22);
        _battleField.AddChild(arrow);
        var gap2 = new Control { CustomMinimumSize = new Vector2(24, 0) };
        _battleField.AddChild(gap2);
        foreach (var e in State.Enemies.Where(e => e.IsAlive).OrderBy(e => e.Position))
            _battleField.AddChild(MakeEnemyBlock(e, _targetMode == TargetMode.Enemy));
    }

    private Control MakePortrait(Character c, bool isActive)
    {
        var pv = new PortraitView();
        pv.SetupCharacter(c, isActive, IsAimed(c));
        int id = c.Id;
        pv.Clicked += () => { _activeId = id; Render(); };
        _charPortraits[c.Id] = pv;
        return pv;
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
        var pv = new PortraitView();
        pv.SetupEnemy(e, clickable && e.IsAlive, aimed: false);
        if (clickable && e.IsAlive)
        {
            int eid = e.Id;
            pv.Clicked += () => PlayCardAtEnemy(eid);
        }
        _enemyPortraits[e.Id] = pv;
        return pv;
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
        int ptr = State.Pointer;
        var trav = TraversedSet();
        var trig = TriggeredPreviewSet();
        var pcolor = PreviewColor();
        const int histCount = 4;  // 历史格（指针左侧，灰显）
        const int futCount = 8;   // 未来格（指针右侧）
        for (int i = histCount; i >= 1; i--)
            _timelineRow.AddChild(MakeCell((ptr - i + len) % len, trav, trig, pcolor, isPast: true));
        _timelineRow.AddChild(MakeCell(ptr, trav, trig, pcolor, isPast: false));
        for (int i = 1; i <= futCount && i < len; i++)
            _timelineRow.AddChild(MakeCell((ptr + i) % len, trav, trig, pcolor, isPast: false));
    }

    private Control MakeCell(int cell, HashSet<int> trav, HashSet<int> trig, Color previewColor, bool isPast = false)
    {
        bool isPtr = cell == State!.Pointer;
        var enemies = State.Timeline.EnemiesAt(cell).FindAll(e => e.IsAlive);
        bool hasEnemy = enemies.Count > 0;

        var p = new Panel { CustomMinimumSize = new Vector2(isPast ? 50 : 68, 96) };
        if (isPtr) p.Name = "__ptr_cell__";
        var sb = new StyleBoxFlat();
        sb.SetCornerRadiusAll(4);
        Color bg = isPast ? new Color(0.08f, 0.08f, 0.10f)
                  : trav.Contains(cell) ? previewColor
                  : hasEnemy ? new Color(0.28f, 0.12f, 0.10f)
                  : isPtr ? new Color(0.22f, 0.22f, 0.35f)
                  : new Color(0.12f, 0.12f, 0.15f);
        sb.BgColor = bg;
        if (isPtr)
        { sb.BorderColor = new Color(1f, 0.85f, 0.2f); sb.SetBorderWidthAll(3); }   // 黄框：当前格
        else if (hasEnemy && !isPast)
        { sb.BorderColor = enemies.Count > 1 ? new Color(1f, 0.55f, 0.2f) : new Color(0.9f, 0.28f, 0.28f); sb.SetBorderWidthAll(2); }
        p.AddThemeStyleboxOverride("panel", sb);

        var vb = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        vb.AddThemeConstantOverride("separation", 2);
        p.AddChild(vb);
        var l1 = new Label { Text = isPtr ? $"▶{cell}" : $"{cell}" };
        l1.AddThemeFontSizeOverride("font_size", isPast ? 9 : 11);
        if (isPast) l1.AddThemeColorOverride("font_color", new Color(0.42f, 0.42f, 0.46f));
        else if (isPtr) l1.AddThemeColorOverride("font_color", new Color(1f, 0.92f, 0.4f));
        vb.AddChild(l1);

        if (!isPast)
        {
            foreach (var enemy in enemies)
            {
                var nl = new Label { Text = $"{KindText(enemy.Kind)}{enemy.EffectivePower}" };
                nl.AddThemeFontSizeOverride("font_size", 12);
                nl.AddThemeColorOverride("font_color", EnemyColor(enemy.Kind));
                vb.AddChild(nl);
            }
            if (trig.Contains(cell))
            {
                var t = new Label { Text = "⚠" };
                t.AddThemeColorOverride("font_color", new Color(1f, 0.35f, 0.35f));
                t.AddThemeFontSizeOverride("font_size", 13);
                vb.AddChild(t);
            }
            if (hasEnemy)
            {
                p.MouseFilter = MouseFilterEnum.Stop;
                var capturedEnemies = enemies;
                p.GuiInput += ev =>
                {
                    if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Right)
                        ShowEnemyDetail(capturedEnemies, p.GlobalPosition + new Vector2(0, p.Size.Y));
                };
            }
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

        // 扇形手牌：普通手牌 + 整备牌（整备牌排在最右，作为普通卡显示，打出后自动回手）
        var hand = c.Hand;
        // 合并：普通牌 + 整备牌（整备牌作为常驻卡排末尾）
        bool hasPrep = c.PrepCard is not null;
        int n = hand.Count + (hasPrep ? 1 : 0);
        float cardW = CardView.CardSize.X;
        float cardGap = 10f;
        float centerX = _viewSize.X / 2f;
        float baseY = 24f;
        for (int i = 0; i < n; i++)
        {
            bool isPrep = hasPrep && i == n - 1;
            CardView cv = isPrep
                ? MakePrepCardView(c, c.PrepCard!)
                : (CardView)MakeCardButton(c, hand[i]);
            float off = n > 1 ? (i - (n - 1f) / 2f) : 0f;
            float spacing = Math.Min(cardW + cardGap, (_viewSize.X * 0.7f) / Math.Max(1, n));
            float x = centerX + off * spacing - cardW / 2f;
            float rot = off * 0.05f;
            float y = baseY + off * off * 4f;
            cv.Position = new Vector2(x, y);
            cv.Rotation = rot;
            int baseZ = n > 1 ? n - Math.Abs(2 * i - (n - 1)) : 1;
            cv.ZIndex = baseZ;
            cv.SetBaseZIndex(baseZ);
            _handRow.AddChild(cv);
        }

        // 行动区：只保留空过
        var skip = new Button { Text = "空过", CustomMinimumSize = new Vector2(64, 40) };
        skip.AddThemeFontSizeOverride("font_size", 12);
        int sid = c.Id;
        skip.MouseEntered += () => Hover(new PlayerAction(sid, ActionType.Skip));
        skip.MouseExited += ClearHover;
        skip.Pressed += () => Play(new PlayerAction(sid, ActionType.Skip));
        _actionRow.AddChild(skip);
    }

    private CardView MakePrepCardView(Character c, Card card)
    {
        var view = new CardView();
        view.Setup(card, c);
        int id = c.Id;
        view.OnHovered += v => { _hoveredCard = v; Hover(new PlayerAction(id, ActionType.UsePrep)); };
        view.OnUnhovered += v => { v.HidePreview(); if (_hoveredCard == v) _hoveredCard = null; ClearHover(); };
        view.Targeting = CardView.TargetKind.None;
        view.OnPlay += _ => Play(new PlayerAction(id, ActionType.UsePrep));
        var capturedCard = card;
        var capturedOwner = c;
        view.OnRightClicked += (_, pos) => ShowCardDetail(capturedCard, capturedOwner, pos);
        return view;
    }

    private void ToggleSidebar()
    {
        _sidebarOpen = !_sidebarOpen;
        _log.Visible = _sidebarOpen;
        _sidebarToggle.Text = _sidebarOpen ? "▶ 关闭日志" : "◀ 日志";
        _sidebar.CustomMinimumSize = _sidebarOpen ? new Vector2(300, 0) : new Vector2(76, 0);
    }

    private Control MakeCardButton(Character c, Card card)
    {
        var def = card.Def;
        var view = new CardView();
        view.Setup(card, c);
        var action = ActionForCard(c, card);
        view.OnHovered += v => { _hoveredCard = v; Hover(action); };
        view.OnUnhovered += v => { v.HidePreview(); if (_hoveredCard == v) _hoveredCard = null; ClearHover(); };
        view.TargetPicker = PickEnemyAt;
        view.OnEnemyHover += HighlightEnemyAt;
        view.OnNoTarget += () => Toast("无可选目标敌人，牌已返回手牌");
        int id = c.Id;
        // 目标类型：远程/代码卡→拖箭头选敌；力量附魔具体牌→向上托弹窗选牌；其余→向上托出牌
        if (def.Effect == EffectKind.AttackDamage && (def.DamageType == DamageType.Ranged || def.NeedsTargetEnemy))
            view.Targeting = CardView.TargetKind.Enemy;
        else if (def.Effect == EffectKind.ApplyEnchantment && def.EnchantType == EnchantmentType.Power && def.EnchantScope == EnchantmentScope.SpecificCard)
            view.Targeting = CardView.TargetKind.Card;
        else
            view.Targeting = CardView.TargetKind.None;

        view.OnPlay += _ => Play(action);
        view.OnPlayTarget += (_, eid) => Play(new PlayerAction(id, ActionType.PlayCard, card.InstanceId, TargetEnemyId: eid));
        view.OnRequestCardTarget += _ => OpenCardTargetModal(c, card);
        var capturedCard = card;
        var capturedOwner = c;
        view.OnRightClicked += (_, pos) => ShowCardDetail(capturedCard, capturedOwner, pos);
        return view;
    }

    /// <summary>全局坐标是否落在某敌人头像上，返回其 id（杀戮尖塔箭头落点）。</summary>
    private int? PickEnemyAt(Vector2 globalPos)
    {
        foreach (var (eid, pv) in _enemyPortraits)
        {
            if (pv.GetGlobalRect().HasPoint(globalPos)) return eid;
        }
        return null;
    }

    /// <summary>拖箭头时按光标高亮其上的敌人（杀戮尖塔落点高亮）；null 清除。</summary>
    private void HighlightEnemyAt(Vector2? pos)
    {
        foreach (var pv in _enemyPortraits.Values) pv.Highlight(false);
        if (pos is Vector2 p)
        {
            var eid = PickEnemyAt(p);
            if (eid is int id && _enemyPortraits.TryGetValue(id, out var pv)) pv.Highlight(true);
        }
    }

    /// <summary>短暂提示（无目标等）。</summary>
    private void Toast(string msg)
    {
        var label = new Label { Text = msg, ZIndex = 80, Position = new Vector2(_viewSize.X / 2f - 180, 120), Size = new Vector2(360, 30), HorizontalAlignment = HorizontalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", 15);
        label.AddThemeColorOverride("font_color", UiPalette.WarnOrange);
        var bg = new StyleBoxFlat { BgColor = UiPalette.PanelBg with { A = 0.9f } };
        bg.SetCornerRadiusAll(4); bg.ContentMarginLeft = 8; bg.ContentMarginRight = 8;
        label.AddThemeStyleboxOverride("normal", bg);
        _fxLayer.AddChild(label);
        var tw = CreateTween();
        tw.TweenInterval(1.2);
        tw.TweenProperty(label, "modulate:a", 0f, 0.5);
        tw.TweenCallback(Callable.From(() => { if (IsInstanceValid(label)) label.QueueFree(); }));
    }

    /// <summary>卡牌目标弹窗（文档：屏幕中心显示可选卡牌，背景透明灰）。</summary>
    private void OpenCardTargetModal(Character c, Card enchCard)
    {
        if (_modalBg is not null) { _modalBg.QueueFree(); _modalBg = null; }
        _modalBg = new Control { MouseFilter = MouseFilterEnum.Stop };
        _modalBg.SetAnchorsPreset(LayoutPreset.FullRect);
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.55f), MouseFilter = MouseFilterEnum.Stop };
        dim.SetAnchorsPreset(LayoutPreset.FullRect);
        _modalBg.AddChild(dim);
        AddChild(_modalBg);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        var vb = new VBoxContainer();
        vb.AddThemeConstantOverride("separation", 16);
        vb.AddChild(new Label { Text = "选择一张手牌挂力量附魔", HorizontalAlignment = HorizontalAlignment.Center });
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 20);
        foreach (var hc in c.Hand)
        {
            if (hc.InstanceId == enchCard.InstanceId) continue;
            var cv = new CardView();
            cv.Setup(hc, c);
            cv.DragPlay = false; // 弹窗：点击选牌
            var target = hc;
            cv.OnClicked += _ =>
            {
                Play(new PlayerAction(c.Id, ActionType.PlayCard, enchCard.InstanceId, TargetCardInstanceId: target.InstanceId));
                CloseCardTargetModal();
            };
            row.AddChild(cv);
        }
        vb.AddChild(row);
        center.AddChild(vb);
        _modalBg.AddChild(center);

        // 点暗背景取消
        dim.GuiInput += e => { if (e is InputEventMouseButton mb && mb.Pressed) CloseCardTargetModal(); };
    }

    private void CloseCardTargetModal()
    {
        if (_modalBg is not null) { _modalBg.QueueFree(); _modalBg = null; }
    }

    // ------------------------------------------------------------------ 详情弹窗
    private void ShowCardDetail(Card card, Character? owner, Vector2 screenPos)
    {
        CloseDetailPopup();
        var panel = new PanelContainer { ZIndex = 80 };
        var sb = new StyleBoxFlat { BgColor = UiPalette.PanelBg with { A = 0.97f }, BorderColor = UiPalette.GoldBorder };
        sb.SetBorderWidthAll(2); sb.SetCornerRadiusAll(6);
        sb.ContentMarginLeft = 14; sb.ContentMarginTop = 12; sb.ContentMarginRight = 14; sb.ContentMarginBottom = 12;
        panel.AddThemeStyleboxOverride("panel", sb);
        var vb = new VBoxContainer();
        vb.AddThemeConstantOverride("separation", 5);
        var nameL = new Label { Text = card.Def.Name };
        nameL.AddThemeFontSizeOverride("font_size", 18);
        vb.AddChild(nameL);
        string typeTag = card.Def.Type == CardType.Attack ? CardDef.DamageText(card.Def.DamageType) : card.Def.Type.ToString();
        var costTypeL = new Label { Text = $"费用 {card.Def.Cost}  ·  {typeTag}" };
        costTypeL.AddThemeFontSizeOverride("font_size", 12);
        costTypeL.AddThemeColorOverride("font_color", UiPalette.TextDim);
        vb.AddChild(costTypeL);
        vb.AddChild(new HSeparator());
        var descL = new Label { Text = card.Def.EffectDescription(), AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(240, 0) };
        descL.AddThemeFontSizeOverride("font_size", 13);
        vb.AddChild(descL);
        if (card.Def.Type == CardType.Prep)
        {
            var prepL = new Label { Text = "★ 打出后自动回手，不进弃牌堆" };
            prepL.AddThemeFontSizeOverride("font_size", 11);
            prepL.AddThemeColorOverride("font_color", UiPalette.ShieldTeal);
            vb.AddChild(prepL);
        }
        if (card.Enchantments.Count > 0)
        {
            vb.AddChild(new HSeparator());
            var enchL = new Label { Text = "附魔：" + DetailEnchantText(card) };
            enchL.AddThemeFontSizeOverride("font_size", 11);
            enchL.AddThemeColorOverride("font_color", UiPalette.VulnGold);
            vb.AddChild(enchL);
        }
        var hintL = new Label { Text = "[右键或点击其他处关闭]" };
        hintL.AddThemeFontSizeOverride("font_size", 10);
        hintL.AddThemeColorOverride("font_color", UiPalette.TextDim);
        vb.AddChild(hintL);
        panel.AddChild(vb);
        _fxLayer.AddChild(panel);
        _detailPopup = panel;
        float px = Math.Min(screenPos.X + 16, _viewSize.X - 290);
        float py = Math.Clamp(screenPos.Y - 80, 8, _viewSize.Y - 260);
        panel.Position = new Vector2(px, py);
    }

    private void ShowEnemyDetail(List<Enemy> enemies, Vector2 screenPos)
    {
        CloseDetailPopup();
        var panel = new PanelContainer { ZIndex = 80 };
        var sb = new StyleBoxFlat { BgColor = new Color(0.14f, 0.07f, 0.07f, 0.97f), BorderColor = new Color(0.85f, 0.28f, 0.28f) };
        sb.SetBorderWidthAll(2); sb.SetCornerRadiusAll(6);
        sb.ContentMarginLeft = 14; sb.ContentMarginTop = 12; sb.ContentMarginRight = 14; sb.ContentMarginBottom = 12;
        panel.AddThemeStyleboxOverride("panel", sb);
        var vb = new VBoxContainer();
        vb.AddThemeConstantOverride("separation", 5);
        for (int ei = 0; ei < enemies.Count; ei++)
        {
            var enemy = enemies[ei];
            if (ei > 0) vb.AddChild(new HSeparator());
            var nameL = new Label { Text = enemy.IsAlive ? enemy.Name : $"{enemy.Name}（已倒下）" };
            nameL.AddThemeFontSizeOverride("font_size", 16);
            nameL.AddThemeColorOverride("font_color", EnemyColor(enemy.Kind));
            vb.AddChild(nameL);
            var infoL = new Label { Text = $"HP {enemy.Hp}  ·  位置{enemy.Position}  ·  触发格{enemy.NodeSlot}" };
            infoL.AddThemeFontSizeOverride("font_size", 11);
            infoL.AddThemeColorOverride("font_color", UiPalette.TextDim);
            vb.AddChild(infoL);
            var chainL = new Label { Text = "行动链：" + BuildChainText(enemy), AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(220, 0) };
            chainL.AddThemeFontSizeOverride("font_size", 12);
            vb.AddChild(chainL);
            var nextL = new Label { Text = "▶ 下步：" + IntentText(enemy) };
            nextL.AddThemeFontSizeOverride("font_size", 12);
            nextL.AddThemeColorOverride("font_color", new Color(1f, 0.72f, 0.3f));
            vb.AddChild(nextL);
            if (enemy.Charge > 0 || enemy.Statuses.Count > 0)
            {
                var parts = new List<string>();
                if (enemy.Charge > 0) parts.Add($"蓄力+{enemy.Charge}");
                foreach (var s in enemy.Statuses)
                    parts.Add(s.Type == EnchantmentType.Vulnerable ? $"易伤{s.Magnitude}×{s.Remaining}" : $"{s.Type}{s.Magnitude}");
                var statL = new Label { Text = "状态：" + string.Join("  ", parts) };
                statL.AddThemeFontSizeOverride("font_size", 11);
                statL.AddThemeColorOverride("font_color", UiPalette.VulnGold);
                vb.AddChild(statL);
            }
        }
        var hintL = new Label { Text = "[右键或点击其他处关闭]" };
        hintL.AddThemeFontSizeOverride("font_size", 10);
        hintL.AddThemeColorOverride("font_color", UiPalette.TextDim);
        vb.AddChild(hintL);
        panel.AddChild(vb);
        _fxLayer.AddChild(panel);
        _detailPopup = panel;
        float px = Math.Min(screenPos.X + 8, _viewSize.X - 290);
        float py = Math.Clamp(screenPos.Y, 8, _viewSize.Y - 320);
        panel.Position = new Vector2(px, py);
    }

    private void CloseDetailPopup()
    {
        if (_detailPopup is not null && IsInstanceValid(_detailPopup)) _detailPopup.QueueFree();
        _detailPopup = null;
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_detailPopup is null) return;
        bool dismiss = (e is InputEventMouseButton mb && mb.Pressed)
                    || (e is InputEventKey k && k.Pressed && k.Keycode == Key.Escape);
        if (dismiss) CloseDetailPopup();
    }

    private static string BuildChainText(Enemy enemy)
    {
        if (enemy.ActionChain.Count == 0) return $"默认攻击{enemy.EffectivePower}";
        int cur = enemy.ChainIndex % enemy.ActionChain.Count;
        var parts = new List<string>();
        for (int i = 0; i < enemy.ActionChain.Count; i++)
            parts.Add((i == cur ? "▶" : $"{i + 1}.") + ActionStepText(enemy.ActionChain[i]));
        return string.Join(" → ", parts);
    }

    private static string ActionStepText(EnemyAction action) => action switch
    {
        EnemyAction.Attack a => a.TargetPos == -1 ? $"打全体{a.Amount}" : a.TargetPos == 1 ? $"斩位1·{a.Amount}" : a.TargetPos == 2 ? $"突位2·{a.Amount}" : $"攻{a.Amount}",
        EnemyAction.Charge c => $"蓄力+{c.Amount}",
        EnemyAction.Idle => "待机",
        _ => "?",
    };

    private static string DetailEnchantText(Card card)
    {
        int power = 0, vuln = 0, vulnT = 0, charge = 0;
        foreach (var e in card.Enchantments)
        {
            if (e.Type == EnchantmentType.Power) power += e.Magnitude;
            else if (e.Type == EnchantmentType.Vulnerable) { vuln += e.Magnitude; vulnT += e.Remaining; }
            else if (e.Type == EnchantmentType.Charge) charge += e.Magnitude;
        }
        var parts = new List<string>();
        if (power > 0) parts.Add($"力量+{power}（攻击+{power}）");
        if (vuln > 0) parts.Add($"易伤+{vuln}×{vulnT}次");
        if (charge > 0) parts.Add($"蓄力+{charge}");
        return string.Join("  ", parts);
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

    // ---- 出牌队列（文档 §四：即时出牌→进队列→逐张结算；硬上限防失控；同一时刻只一份演出）----
    private readonly Queue<PlayerAction> _playQueue = new();
    private bool _playing;
    private const int QueueCap = 2; // 文档：同时未结算牌 ≤ 2

    private void Play(PlayerAction action)
    {
        if (State is null || IsBattleOver()) return;
        // 客户端：发主机，不本地结算
        if (_net is not null && _isClient) { _net.SubmitAction(action); return; }
        // 正在演出当前牌 → 进队列（硬上限），不打断当前演出
        if (_playing) { if (_playQueue.Count < QueueCap) { _playQueue.Enqueue(action); RenderTop(); } return; }
        ExecutePlay(action);
    }

    private void ExecutePlay(PlayerAction action)
    {
        _playing = true;
        int prevPtr = State!.Pointer;
        var ev = State.Apply(action);
        int ptrDelta = (State.Pointer - prevPtr + State.Timeline.Length) % State.Timeline.Length;
        _pendingTimelineShift = ptrDelta * 72f; // 每格宽68 + 间距4 = 72px
        LogEvents(ev);
        _hoverAction = null;
        _hoverEvents = null;
        _targetMode = TargetMode.None;
        _targetingCard = null;
        Render();
        AnimatePortraits(ev);      // 立绘状态机协同（当前牌演出）
        if (_net is not null && !_isClient) _net.Broadcast(action);
        RenderTop(); // 更新队列计数
        CallDeferred(MethodName.TweenPointerToCurrentCell); // 指针平滑滑到当前格
        // 节奏：当前牌演出一小段后，解锁并处理队列下一张（或结算战斗结束）
        GetTree().CreateTimer(0.4f).Timeout += OnPlayPaced;
    }

    /// <summary>指针光点归位到当前格，并触发时间轴滑动动画（指针格始终固定在第5列）。</summary>
    private void TweenPointerToCurrentCell()
    {
        var cell = _timelineRow.GetChildren().OfType<Control>().FirstOrDefault(c => c.Name == "__ptr_cell__");
        if (cell is null) return;
        // 当前格固定在同屏位置——光点直接归位，无需 tween
        Vector2 target = cell.GlobalPosition + new Vector2(28, 2);
        _pointerGlow.Position = target;
        _pointerGlow.Visible = true;
        // 时间轴滑动：从右偏 shift 处向 0 滑（传递"时间前进"的感觉）
        if (_pendingTimelineShift > 0)
        {
            float shift = _pendingTimelineShift;
            _pendingTimelineShift = 0;
            _timelineRow.Position = new Vector2(shift, 0);
            var tw = CreateTween();
            tw.TweenProperty(_timelineRow, "position:x", 0f, 0.28f)
              .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        }
    }

    private void OnPlayPaced()
    {
        _playing = false;
        if (IsBattleOver()) { EmitSignal(SignalName.BattleOver, IsWin()); _playQueue.Clear(); RenderTop(); return; }
        if (_playQueue.Count > 0) ExecutePlay(_playQueue.Dequeue());
        else RenderTop();
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
                var pv = dd.TargetIsEnemy
                    ? (_enemyPortraits.TryGetValue(dd.TargetId, out var ep) ? ep : null)
                    : (_charPortraits.TryGetValue(dd.TargetId, out var cp) ? cp : null);
                if (pv is not null) _pendingDmg.Add((pv, dd.Amount, dd.TargetIsEnemy));
                if (!dd.TargetIsEnemy && dd.Amount > 0) Shake(); // 角色受击屏震（文档 §七）
            }
        }
        if (_pendingDmg.Count > 0) CallDeferred(MethodName.SpawnPendingDamageNumbers);
    }

    /// <summary>屏震（文档 §七：受击→屏震）：_margin 短促抖动后归零。</summary>
    private void Shake()
    {
        if (_margin is null) return;
        var tw = CreateTween();
        tw.TweenProperty(_margin, "position", new Vector2(-5, 2), 0.04f);
        tw.TweenProperty(_margin, "position", new Vector2(5, -2), 0.04f);
        tw.TweenProperty(_margin, "position", new Vector2(-3, 1), 0.04f);
        tw.TweenProperty(_margin, "position", Vector2.Zero, 0.05f);
    }

    private readonly List<(PortraitView View, int Amount, bool Enemy)> _pendingDmg = new();

    private void SpawnPendingDamageNumbers()
    {
        foreach (var (view, amount, enemy) in _pendingDmg)
        {
            if (IsInstanceValid(view) && view.Portrait is not null)
                SpawnDamageNumber(view.Portrait.GlobalPosition, amount, enemy);
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
        _hoveredCard?.ShowPreview(BuildPreviewText()); // 预计后果显示在牌上方
        HighlightAffected(action); // 近战牌悬停：高亮将受影响的敌人
    }

    private void ClearHover()
    {
        _hoverAction = null;
        _hoverEvents = null;
        RenderHover();
        _hoveredCard?.HidePreview();
        foreach (var pv in _enemyPortraits.Values) pv.Highlight(false);
    }

    /// <summary>近战攻击牌悬停时，按伤害类型高亮将挨打的敌人（斩位1/突位2/打全体）。</summary>
    private void HighlightAffected(PlayerAction action)
    {
        foreach (var pv in _enemyPortraits.Values) pv.Highlight(false);
        if (State is null || action.Type != ActionType.PlayCard || action.CardInstanceId is null) return;
        var c = State.Characters.Find(x => x.Id == action.CharacterId);
        var card = c?.Hand.Find(k => k.InstanceId == action.CardInstanceId);
        if (card is null || card.Def.Effect != EffectKind.AttackDamage) return;
        // 仅近战（非远程/非代码卡自选）做预读高亮；远程走拖箭头，不高亮
        if (card.Def.DamageType == DamageType.Ranged || card.Def.NeedsTargetEnemy) return;
        foreach (var id in AffectedEnemyIds(card.Def))
            if (_enemyPortraits.TryGetValue(id, out var pv)) pv.Highlight(true);
    }

    private List<int> AffectedEnemyIds(CardDef def)
    {
        var ids = new List<int>();
        if (State is null) return ids;
        switch (def.DamageType)
        {
            case DamageType.Slash: var e1 = State.Enemies.FirstOrDefault(e => e.IsAlive && e.Position == 1); if (e1 is not null) ids.Add(e1.Id); break;
            case DamageType.Thrust: var e2 = State.Enemies.FirstOrDefault(e => e.IsAlive && e.Position == 2); if (e2 is not null) ids.Add(e2.Id); break;
            case DamageType.Blunt: ids.AddRange(State.Enemies.Where(e => e.IsAlive).Select(e => e.Id)); break;
        }
        return ids;
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
    /// <summary>构建预计后果文本（悬浮在牌上方）。</summary>
    private string BuildPreviewText()
    {
        if (_hoverAction is not PlayerAction a || _hoverEvents is null) return "";
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
        if (_hoverEvents.Any(e => e is GameEvent.EnchantmentApplied)
            || _hoverEvents.OfType<GameEvent.EnemyTriggered>().Any(et => State?.Enemies.Find(x => x.Id == et.EnemyId)?.Charge > 0))
            parts.Add("（已含修正）");
        if (_hoverEvents.Any(e => e is GameEvent.CharacterDied)) parts.Add("【角色阵亡】");
        if (_hoverEvents.Any(e => e is GameEvent.EnemyDied)) parts.Add("【敌死】");
        return string.Join("\n", parts);
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
            GameEvent.CardPlayed cp => $"▸ {CharName(cp.CharacterId)} 出牌〔{CardName(cp.CardInstanceId, cp.CharacterId)}〕",
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
    private string CardName(Guid instanceId, int charId)
    {
        var c = State?.Characters.Find(x => x.Id == charId);
        return c?.Hand.Find(k => k.InstanceId == instanceId)?.Def.Name
            ?? c?.DiscardPile.Find(k => k.InstanceId == instanceId)?.Def.Name
            ?? c?.DrawPile.Find(k => k.InstanceId == instanceId)?.Def.Name
            ?? "?";
    }

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
