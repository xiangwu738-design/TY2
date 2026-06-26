using Godot;
using System;
using Tongyuan.Core.Core;

namespace Tongyuan.UI;

/// <summary>
/// 卡牌视图组件（UI 重置规格 §3 阶段1）：卡框/费用球/卡图区/名称/类型/效果/稀有度宝石 + 悬浮抬升。
/// ArtPath 非空→加载贴图；否则按 DamageType/Type 渐变占位。点击/悬浮以回调上抛，由 BattleController 决策。
/// </summary>
public partial class CardView : Panel
{
    public Card? Card { get; private set; }
    public Character? Owner { get; private set; }
    public Action<CardView>? OnClicked;       // 弹窗/奖励等点击场景
    public Action<CardView>? OnHovered;
    public Action<CardView>? OnUnhovered;
    /// <summary>战斗手牌=true 走拖拽；弹窗/奖励=false 走点击。</summary>
    public bool DragPlay { get; set; } = true;
    /// <summary>无目标：向上托出牌。</summary>
    public Action<CardView>? OnPlay;
    /// <summary>敌人目标：拖箭头到敌人出牌。</summary>
    public Action<CardView, int>? OnPlayTarget;
    /// <summary>卡牌目标：向上托→弹窗选牌。</summary>
    public Action<CardView>? OnRequestCardTarget;

    public enum TargetKind { None, Enemy, Card }
    public TargetKind Targeting { get; set; } = TargetKind.None;
    /// <summary>给定全局坐标，返回其上的敌人 id（无则 null）。</summary>
    public Func<Vector2, int?>? TargetPicker { get; set; }

    private Label _cost = null!;
    private Label _enchant = null!;
    private ColorRect _art = null!;
    private TextureRect _artTex = null!;
    private Label _name = null!;
    private Label _type = null!;
    private Label _desc = null!;
    private ColorRect _rarity = null!;
    private bool _built;
    private bool _hovering;
    private bool _dragging;
    private Vector2 _dragStart;
    private Vector2 _dragPos;
    private Vector2 _origin;

    public static readonly Vector2 CardSize = new(184, 268);

    public override void _Ready()
    {
        CustomMinimumSize = CardSize;
        if (!_built) BuildInternals();
        MouseFilter = MouseFilterEnum.Stop;
        MouseEntered += () => { _hovering = true; OnHovered?.Invoke(this); AnimateHover(true); };
        MouseExited += () => { _hovering = false; OnUnhovered?.Invoke(this); AnimateHover(false); };
        SetProcess(true);
    }

    private void BuildInternals()
    {
        _built = true;
        PivotOffset = CardSize / 2f;
        var vb = new VBoxContainer();
        vb.SetAnchorsPreset(LayoutPreset.FullRect);
        vb.OffsetLeft = 8; vb.OffsetTop = 8; vb.OffsetRight = -8; vb.OffsetBottom = -8;
        vb.AddThemeConstantOverride("separation", 4);
        AddChild(vb);

        // 顶部：费用球
        _cost = new Label { Text = "0", HorizontalAlignment = HorizontalAlignment.Center };
        _cost.AddThemeFontSizeOverride("font_size", 18);
        _cost.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.3f));
        vb.AddChild(_cost);

        // 附魔徽标（右上角，文档 §五：附魔要看得见）
        _enchant = new Label { Text = "", ZIndex = 5 };
        _enchant.SetAnchorsPreset(LayoutPreset.TopRight);
        _enchant.OffsetLeft = -60; _enchant.OffsetTop = 6; _enchant.OffsetRight = -6;
        _enchant.AddThemeFontSizeOverride("font_size", 14);
        _enchant.AddThemeColorOverride("font_color", UiPalette.VulnGold);
        AddChild(_enchant);

        // 卡图区（技能图片：CardDef.ArtPath；无图则占位色块）
        _art = new ColorRect { CustomMinimumSize = new Vector2(0, 96) };
        vb.AddChild(_art);
        _artTex = new TextureRect { CustomMinimumSize = new Vector2(0, 96), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, Visible = false };
        vb.AddChild(_artTex);

        _name = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _name.AddThemeFontSizeOverride("font_size", 15);
        vb.AddChild(_name);

        _type = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _type.AddThemeFontSizeOverride("font_size", 11);
        _type.AddThemeColorOverride("font_color", new Color(0.7f, 0.75f, 0.85f));
        vb.AddChild(_type);

        _desc = new Label { HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsVertical = SizeFlags.ExpandFill };
        _desc.AddThemeFontSizeOverride("font_size", 11);
        _desc.AddThemeColorOverride("font_color", new Color(0.82f, 0.85f, 0.9f));
        vb.AddChild(_desc);

        // 底部稀有度宝石
        _rarity = new ColorRect { CustomMinimumSize = new Vector2(0, 8), Color = Colors.Gray };
        vb.AddChild(_rarity);
    }

    /// <summary>填充卡牌数据。</summary>
    public void Setup(Card card, Character owner)
    {
        Card = card; Owner = owner;
        var def = card.Def;
        if (!_built) BuildInternals();
        AddThemeStyleboxOverride("panel", UiPalette.CardStyle(def));

        _cost.Text = def.Cost.ToString();
        _name.Text = def.Name;
        string typeTag = def.Type == CardType.Attack ? CardDef.DamageText(def.DamageType) : def.Type.ToString();
        _type.Text = $"〔{typeTag}〕";
        _desc.Text = def.EffectDescription();
        _rarity.Color = UiPalette.RarityBorder(def.Rarity);
        _art.Color = UiPalette.CardBg(def).Lightened(0.18f);

        // 附魔徽标：汇总本牌附魔（力量+易伤等），让附魔在卡面看得见
        _enchant.Text = EnchantBadge(card);

        // 技能图片（CardDef.ArtPath）：有则显示真图（纯粹图片区分），无则占位色块
        if (!string.IsNullOrEmpty(def.ArtPath) && ResourceLoader.Exists(def.ArtPath) && ResourceLoader.Load(def.ArtPath) is Texture2D tex)
        {
            _artTex.Texture = tex;
            _artTex.Visible = true;
        }
        else
        {
            _artTex.Visible = false;
        }
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            if (!DragPlay) { OnClicked?.Invoke(this); return; } // 弹窗/奖励：直接点击
            // 战斗手牌：开始拖拽
            _dragging = true;
            _origin = Position;
            _dragStart = GetGlobalMousePosition();
            _dragPos = _dragStart;
            ZIndex = 20;
        }
    }

    public override void _Process(double delta)
    {
        if (!_dragging) return;
        _dragPos = GetGlobalMousePosition();
        var parentGlobal = (GetParent() as Control)?.GlobalPosition ?? Vector2.Zero;
        var localMouse = _dragPos - parentGlobal;
        if (Targeting == TargetKind.None)
            Position = localMouse - Size / 2f;                 // 无目标：卡牌跟手
        else
            Position = _origin + new Vector2(0, -170);          // 有目标：升到手牌区中央，箭头从这出发
        QueueRedraw();
        if (!Godot.Input.IsMouseButtonPressed(MouseButton.Left))
            ResolveDrag();
    }

    private void ResolveDrag()
    {
        _dragging = false;
        ZIndex = 0;
        Position = _origin; // 回到手牌原位
        QueueRedraw();
        float dy = _dragPos.Y - _dragStart.Y;
        switch (Targeting)
        {
            case TargetKind.Enemy:
                var eid = TargetPicker?.Invoke(_dragPos);
                if (eid is int id) OnPlayTarget?.Invoke(this, id);
                break;
            case TargetKind.Card:
                if (dy < -30) OnRequestCardTarget?.Invoke(this); // 向上托→弹窗选牌
                break;
            default:
                if (dy < -30) OnPlay?.Invoke(this); // 无目标：向上托出牌
                break;
        }
    }

    public override void _Draw()
    {
        if (!_dragging || Targeting != TargetKind.Enemy) return;
        // 曲线箭头（二次贝塞尔，杀戮尖塔式弯曲）：从卡牌中心到光标（局部坐标）
        var from = Size / 2f;
        var to = GetGlobalMousePosition() - GlobalPosition;
        var mid = (from + to) / 2f;
        float len = (to - from).Length();
        var ctrl = mid + new Vector2(0, -Mathf.Min(90f, len * 0.22f)); // 控制点上提 → 向上弯曲
        var col = UiPalette.VulnGold with { A = 0.92f };
        Vector2 prev = Quad(from, ctrl, to, 0f);
        const int N = 18;
        for (int i = 1; i <= N; i++)
        {
            float t = i / (float)N;
            var p = Quad(from, ctrl, to, t);
            DrawLine(prev, p, col, 3.5f);
            prev = p;
        }
        // 箭头头（沿末端切线方向）
        var tan = (Quad(from, ctrl, to, 1f) - Quad(from, ctrl, to, 0.92f)).Normalized();
        var perp = new Vector2(-tan.Y, tan.X);
        DrawLine(to, to - tan * 16 + perp * 7, col, 3.5f);
        DrawLine(to, to - tan * 16 - perp * 7, col, 3.5f);
    }

    private static Vector2 Quad(Vector2 p0, Vector2 p1, Vector2 p2, float t)
    {
        float u = 1f - t;
        return u * u * p0 + 2 * u * t * p1 + t * t * p2;
    }

    /// <summary>汇总卡牌附魔为徽标文本（力+N / 易+N×K / 蓄+N）。</summary>
    private static string EnchantBadge(Card card)
    {
        if (card.Enchantments.Count == 0) return "";
        var parts = new System.Collections.Generic.List<string>();
        int power = 0, vuln = 0, vulnTimes = 0, charge = 0;
        foreach (var e in card.Enchantments)
        {
            if (e.Type == EnchantmentType.Power) power += e.Magnitude;
            else if (e.Type == EnchantmentType.Vulnerable) { vuln += e.Magnitude; vulnTimes += e.Remaining; }
            else if (e.Type == EnchantmentType.Charge) charge += e.Magnitude;
        }
        if (power > 0) parts.Add($"力+{power}");
        if (vuln > 0) parts.Add($"易+{vuln}×{vulnTimes}");
        if (charge > 0) parts.Add($"蓄+{charge}");
        return string.Join(" ", parts);
    }

    private void AnimateHover(bool hover)
    {
        ZIndex = hover ? 10 : 0;
        var tw = CreateTween();
        tw.TweenProperty(this, "scale", hover ? new Vector2(1.08f, 1.08f) : Vector2.One, 0.1f)
          .SetTrans(Tween.TransitionType.Sine);
    }
}
