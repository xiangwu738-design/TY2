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
    public new Character? Owner { get; private set; }
    public Action<CardView>? OnClicked;       // 弹窗/奖励等点击场景
    public Action<CardView>? OnHovered;
    public Action<CardView>? OnUnhovered;
    public Action<CardView, Vector2>? OnRightClicked;
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
    /// <summary>拖拽中：按光标全局坐标高亮其上的敌人（箭头落点预览）。</summary>
    public Action<Vector2?>? OnEnemyHover;
    /// <summary>无可选目标提示。</summary>
    public Action? OnNoTarget;

    private Label _cost = null!;
    private Label _enchant = null!;
    private TextureRect _frameTex = null!;
    private ColorRect _art = null!;
    private TextureRect _artTex = null!;
    private Label _name = null!;
    private Label _type = null!;
    private Label _desc = null!;
    private ColorRect _rarity = null!;
    private Label _tooltip = null!;
    private bool _built;
    private bool _hovering;
    private bool _dragging;
    private Vector2 _dragStart;
    private Vector2 _dragPos;
    private Vector2 _origin;
    private int _baseZIndex;

    public static readonly Vector2 CardSize = new(216, 346);

    public override void _Ready()
    {
        CustomMinimumSize = CardSize;
        TextureFilter = TextureFilterEnum.LinearWithMipmapsAnisotropic;
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
        // 牌上方悬浮预计框（文档：预计后果显示在牌上面）
        _tooltip = new Label { Visible = false, AutowrapMode = TextServer.AutowrapMode.WordSmart, HorizontalAlignment = HorizontalAlignment.Center, ZIndex = 40, MouseFilter = Control.MouseFilterEnum.Ignore, Position = new Vector2(-8, -78), Size = new Vector2(CardSize.X + 16, 72) };
        _tooltip.AddThemeFontSizeOverride("font_size", 13);
        _tooltip.AddThemeColorOverride("font_color", UiPalette.TextMain);
        var tbg = new StyleBoxFlat { BgColor = UiPalette.PanelBg with { A = 0.92f }, BorderColor = UiPalette.GoldBorder with { A = 0.7f } };
        tbg.SetBorderWidthAll(1); tbg.SetCornerRadiusAll(4); tbg.ContentMarginLeft = 6; tbg.ContentMarginRight = 6; tbg.ContentMarginTop = 3; tbg.ContentMarginBottom = 3;
        _tooltip.AddThemeStyleboxOverride("normal", tbg);
        AddChild(_tooltip);

        _art = new ColorRect
        {
            Position = new Vector2(47f, 46f),
            Size = new Vector2(169f, 221f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_art);

        _artTex = new TextureRect
        {
            Position = _art.Position,
            Size = _art.Size,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            TextureFilter = TextureFilterEnum.LinearWithMipmapsAnisotropic,
            Visible = false,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_artTex);

        _frameTex = new TextureRect
        {
            Position = Vector2.Zero,
            Size = CardSize,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            TextureFilter = TextureFilterEnum.LinearWithMipmapsAnisotropic,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_frameTex);

        // 顶部：费用
        _cost = new Label
        {
            Text = "0",
            Position = new Vector2(27f, 4f),
            Size = new Vector2(56f, 42f),
            Rotation = -0.37f,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _cost.AddThemeFontSizeOverride("font_size", 34);
        _cost.AddThemeFontOverride("font", UiPalette.CardFont());
        _cost.AddThemeColorOverride("font_color", new Color(0.08f, 0.08f, 0.09f));
        AddChild(_cost);

        // 附魔徽标（右上角，文档 §五：附魔要看得见）
        _enchant = new Label { Text = "", ZIndex = 5, MouseFilter = MouseFilterEnum.Ignore };
        _enchant.SetAnchorsPreset(LayoutPreset.TopRight);
        _enchant.OffsetLeft = -86; _enchant.OffsetTop = 10; _enchant.OffsetRight = -10;
        _enchant.AddThemeFontSizeOverride("font_size", 17);
        _enchant.AddThemeFontOverride("font", UiPalette.CardFont());
        _enchant.AddThemeColorOverride("font_color", UiPalette.VulnGold);
        AddChild(_enchant);

        _name = new Label
        {
            Position = new Vector2(58f, 37f),
            Size = new Vector2(154f, 43f),
            Rotation = -0.34f,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _name.AddThemeFontSizeOverride("font_size", 26);
        _name.AddThemeFontOverride("font", UiPalette.CardFont());
        _name.AddThemeColorOverride("font_color", Colors.White);
        AddChild(_name);

        _type = new Label
        {
            Position = new Vector2(5f, 95f),
            Size = new Vector2(92f, 24f),
            Rotation = -Mathf.Pi / 2f,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _type.AddThemeFontSizeOverride("font_size", 14);
        _type.AddThemeFontOverride("font", UiPalette.CardFont());
        _type.AddThemeColorOverride("font_color", Colors.White);
        AddChild(_type);

        _desc = new Label
        {
            Position = new Vector2(58f, 262f),
            Size = new Vector2(152f, 62f),
            Rotation = -0.32f,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _desc.AddThemeFontSizeOverride("font_size", 16);
        _desc.AddThemeFontOverride("font", UiPalette.CardFont());
        _desc.AddThemeColorOverride("font_color", new Color(0.04f, 0.04f, 0.05f));
        AddChild(_desc);

        _rarity = new ColorRect
        {
            Position = new Vector2(7f, CardSize.Y - 8f),
            Size = new Vector2(CardSize.X - 14f, 4f),
            Color = Colors.Gray,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_rarity);
    }

    /// <summary>填充卡牌数据。</summary>
    public void Setup(Card card, Character? owner)
    {
        Card = card; Owner = owner;
        var def = card.Def;
        if (!_built) BuildInternals();
        AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        SetFrameTexture(FramePathFor(def));
        var ink = InkColorFor(def);

        _cost.Visible = true;
        _cost.Text = def.Cost.ToString();
        _cost.AddThemeColorOverride("font_color", ink);
        _name.Text = def.Name;
        string typeTag = def.Type == CardType.Attack ? CardDef.DamageText(def.DamageType) : def.Type.ToString();
        _type.Text = $"〔{typeTag}〕";
        _type.Visible = false;
        // 攻击牌：伤害直接显示实际值（基础+力量附魔），有力量加成时整段绿色高亮
        if (def.Type == CardType.Attack)
        {
            int eff = card.EffectiveAttack;
            bool buffed = eff > def.Magnitude;
            _desc.Text = $"{CardDef.DamageText(def.DamageType)} {eff} 伤{(def.DamageType == DamageType.Ranged ? "·自选敌·不位移" : "·近战·出牌移位1")}";
            _desc.AddThemeColorOverride("font_color", buffed ? new Color(0.02f, 0.40f, 0.14f) : ink);
        }
        else
        {
            _desc.Text = def.EffectDescription();
            _desc.AddThemeColorOverride("font_color", ink);
        }
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

    public void SetupEnemyAction(Enemy enemy, EnemyAction action)
    {
        Card = null;
        Owner = null;
        if (!_built) BuildInternals();

        AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        SetFrameTexture("res://art/cards/frame_ability.png");
        DragPlay = false;
        Targeting = TargetKind.None;
        _cost.Visible = false;
        _type.Visible = false;
        _enchant.Text = "";
        _artTex.Visible = false;

        var (name, type, desc, bg, border, rarity) = EnemyActionCardData(enemy, action);
        _name.Text = name;
        _type.Text = type;
        _desc.Text = desc;
        _desc.AddThemeColorOverride("font_color", new Color(0.11f, 0.12f, 0.53f));
        _art.Color = bg.Lightened(0.18f);
        _rarity.Color = rarity;

        _cost.Visible = false;
    }

    private void SetFrameTexture(string path)
    {
        if (ResourceLoader.Exists(path) && ResourceLoader.Load(path) is Texture2D tex)
            _frameTex.Texture = tex;
    }

    private static string FramePathFor(CardDef def)
    {
        if (def.Type == CardType.Attack)
        {
            return def.DamageType switch
            {
                DamageType.Blunt => "res://art/cards/frame_blunt.png",
                DamageType.Slash => "res://art/cards/frame_slash.png",
                DamageType.Thrust => "res://art/cards/frame_thrust.png",
                DamageType.Ranged => "res://art/cards/frame_ranged.png",
                _ => "res://art/cards/frame_slash.png",
            };
        }

        return def.Type switch
        {
            CardType.Skill => "res://art/cards/frame_skill.png",
            CardType.Prep => "res://art/cards/frame_ability.png",
            CardType.Defense => "res://art/cards/frame_ability.png",
            _ => "res://art/cards/frame_ability.png",
        };
    }

    private static Color InkColorFor(CardDef def)
    {
        return def.Type == CardType.Skill || def.Type == CardType.Prep || def.Type == CardType.Defense
            ? new Color(0.11f, 0.12f, 0.53f)
            : new Color(0.04f, 0.04f, 0.05f);
    }

    private static (string Name, string Type, string Desc, Color Bg, Color Border, Color Rarity) EnemyActionCardData(Enemy enemy, EnemyAction action)
    {
        return action switch
        {
            EnemyAction.Attack a => (
                "敌方攻击",
                a.TargetPos == -1 ? "【全体】" : a.TargetPos == 1 ? "【前排】" : a.TargetPos == 2 ? "【二位】" : "【攻击】",
                a.TargetPos == -1
                    ? $"对我方全体造成 {a.Amount + enemy.Charge} 点伤害。"
                    : $"对我方位置 {(a.TargetPos ?? enemy.TargetPosition)} 造成 {a.Amount + enemy.Charge} 点伤害。",
                new Color(0.30f, 0.10f, 0.10f),
                new Color(0.92f, 0.30f, 0.24f),
                new Color(0.92f, 0.30f, 0.24f)),
            EnemyAction.Charge c => (
                "蓄力",
                "【强化】",
                $"获得 {c.Amount} 点蓄力。下次攻击会附加已储存的蓄力。",
                new Color(0.28f, 0.20f, 0.08f),
                new Color(0.95f, 0.68f, 0.20f),
                new Color(0.95f, 0.68f, 0.20f)),
            EnemyAction.Idle => (
                "待机",
                "【空过】",
                "这一步不行动。",
                new Color(0.16f, 0.17f, 0.20f),
                new Color(0.55f, 0.58f, 0.64f),
                new Color(0.55f, 0.58f, 0.64f)),
            _ => (
                "未知行动",
                "【？】",
                "未知的敌方行动。",
                new Color(0.16f, 0.17f, 0.20f),
                Colors.Gray,
                Colors.Gray),
        };
    }

    public void SetBaseZIndex(int z)
    {
        _baseZIndex = z;
        if (!_hovering && !_dragging) ZIndex = z;
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb && mb.Pressed)
        {
            if (mb.ButtonIndex == MouseButton.Left)
            {
                if (!DragPlay) { OnClicked?.Invoke(this); return; }
                _dragging = true;
                _origin = Position;
                _dragStart = GetGlobalMousePosition();
                _dragPos = _dragStart;
                ZIndex = 20;
            }
            else if (mb.ButtonIndex == MouseButton.Right)
            {
                OnRightClicked?.Invoke(this, GetGlobalMousePosition());
            }
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
        if (Targeting == TargetKind.Enemy) OnEnemyHover?.Invoke(_dragPos); // 箭头落点高亮敌人
        QueueRedraw();
        if (!Godot.Input.IsMouseButtonPressed(MouseButton.Left))
            ResolveDrag();
    }

    private void ResolveDrag()
    {
        _dragging = false;
        ZIndex = _baseZIndex;
        Position = _origin; // 回到手牌原位
        QueueRedraw();
        OnEnemyHover?.Invoke(null); // 清除高亮
        float dy = _dragPos.Y - _dragStart.Y;
        switch (Targeting)
        {
            case TargetKind.Enemy:
                var eid = TargetPicker?.Invoke(_dragPos);
                if (eid is int id) OnPlayTarget?.Invoke(this, id);
                else OnNoTarget?.Invoke(); // 无可选目标提示（牌已回手牌）
                break;
            case TargetKind.Card:
                if (dy < -30) OnRequestCardTarget?.Invoke(this); // 向上托→弹窗选牌
                break;
            default:
                if (dy < -30) OnPlay?.Invoke(this); // 无目标：向上托出牌
                break;
        }
    }

    public void ShowPreview(string text) { } // 暂时禁用
    public void HidePreview() { }

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
        ZIndex = hover ? (_baseZIndex + 10) : _baseZIndex;
        var tw = CreateTween();
        tw.TweenProperty(this, "scale", hover ? new Vector2(1.08f, 1.08f) : Vector2.One, 0.08f)
          .SetTrans(Tween.TransitionType.Sine);
    }
}
