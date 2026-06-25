using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Tongyuan.Core.Core;

namespace Tongyuan.Views;

/// <summary>
/// 可交互战场表现层（规格 §3/§4.8）。每进程一个。
/// 战场（角色左/敌人右）/时间轴/手牌——占位色块 + 真实按钮/标签，接 Core 出牌/整备/空过/预演。
/// 单人模式：点角色头像切换"当前操控角色"，底部出其手牌。
/// 立绘状态机（§4.8）暂以色块占位，留 PortraitController 扩展位。
/// </summary>
public partial class GameView : Control
{
    public GameState? State { get; set; }
    private int _activeId;
    private PlayerAction? _hoverAction;
    private List<GameEvent>? _hoverEvents;

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

        // 预览
        var pp = new Panel { CustomMinimumSize = new Vector2(0, 56) };
        pp.AddThemeStyleboxOverride("panel", PreviewStyle());
        vb.AddChild(pp);
        _previewLabel = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _previewLabel.AddThemeFontSizeOverride("font_size", 13);
        _previewLabel.SetAnchorsPreset(LayoutPreset.FullRect);
        _previewLabel.AddThemeConstantOverride("offset_left", 8);
        _previewLabel.AddThemeConstantOverride("offset_right", -8);
        _previewLabel.AddThemeConstantOverride("offset_top", 5);
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
    }

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
        var alive = State!.AliveCharacters.OrderByDescending(c => c.Position).ToList();
        foreach (var c in alive)
            _battleField.AddChild(MakePortrait(c, c.Id == _activeId));
        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _battleField.AddChild(spacer);
        var arrow = new Label { Text = "⚔" };
        arrow.AddThemeFontSizeOverride("font_size", 20);
        _battleField.AddChild(arrow);
        foreach (var e in State.Enemies)
            _battleField.AddChild(MakeEnemyBlock(e));
    }

    private Control MakePortrait(Character c, bool isActive)
    {
        var p = new Button
        {
            CustomMinimumSize = new Vector2(108, 92),
            Text = $"{(isActive ? "▶ " : "")}{c.Name}\n位{c.Position}\nHP {c.Hp}/{c.MaxHp}",
            Disabled = !c.IsAlive,
        };
        p.AddThemeFontSizeOverride("font_size", 12);
        var sb = new StyleBoxFlat
        {
            BgColor = ColorOf(c.Color).Darkened(isActive ? 0.45f : 0.72f),
            BorderColor = isActive ? Colors.White : ColorOf(c.Color),
        };
        sb.SetBorderWidthAll(3);
        sb.ContentMarginLeft = 5; sb.ContentMarginTop = 3; sb.ContentMarginRight = 5; sb.ContentMarginBottom = 3;
        sb.SetCornerRadiusAll(4);
        p.AddThemeStyleboxOverride("normal", sb);
        p.AddThemeStyleboxOverride("hover", sb);
        p.AddThemeStyleboxOverride("pressed", sb);
        p.AddThemeStyleboxOverride("disabled", sb);
        int id = c.Id;
        p.Pressed += () => { _activeId = id; Render(); };
        return p;
    }

    private Control MakeEnemyBlock(Enemy e)
    {
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
        var kind = new Label { Text = $"{KindText(e.Kind)} · {e.EffectivePower}伤" };
        kind.AddThemeFontSizeOverride("font_size", 11);
        kind.AddThemeColorOverride("font_color", Colors.DimGray);
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
        return p;
    }

    private void RenderTimeline()
    {
        ClearChildren(_timelineRow);
        if (State is null) return;
        int start = Mathf.Max(0, State.Pointer);
        var trav = TraversedSet();
        var trig = TriggeredPreviewSet();
        var pcolor = PreviewColor();
        for (int i = 0; i < 12; i++)
        {
            int cell = start + i;
            if (cell >= State.Timeline.Length) break;
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

    private Button MakeCardButton(Character c, Card card)
    {
        var btn = new Button
        {
            Text = $"{card.Def.Name}\n占{card.Def.Cost}",
            CustomMinimumSize = new Vector2(80, 40),
        };
        btn.AddThemeFontSizeOverride("font_size", 12);
        var action = ActionForCard(c, card);
        btn.MouseEntered += () => Hover(action);
        btn.MouseExited += ClearHover;
        btn.Pressed += () => Play(action);
        return btn;
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
        var ev = State.Apply(action);
        LogEvents(ev);
        _hoverAction = null;
        _hoverEvents = null;
        Render();
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
        string head = a.Type switch
        {
            ActionType.PlayCard => $"{(c?.Hand.Find(card => card.InstanceId == a.CardInstanceId)?.Def.Name ?? "牌")} · 占{occ}→格{State!.Pointer + occ}",
            ActionType.UsePrep => $"{c?.PrepCard?.Def.Name} · 占{occ}→格{State!.Pointer + occ}（回手·抽{c?.PrepCard?.Def.Magnitude}）",
            ActionType.Skip => $"空过 · →格{State!.Pointer + occ}",
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
        for (int i = 1; i <= occ; i++) d.Add(State.Pointer + i);
        return d;
    }

    private HashSet<int> TriggeredPreviewSet()
    {
        var d = new HashSet<int>();
        if (_hoverEvents is null) return d;
        if (_hoverAction is not PlayerAction a || State is null) return d;
        int occ = ActionCost(a);
        for (int i = 1; i <= occ; i++)
        {
            int slot = State.Pointer + i;
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
            GameEvent.EnchantmentApplied ea => $"    [color=#ffd070]✦ 附魔 {ea.Enchantment.Type}[/color]",
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
