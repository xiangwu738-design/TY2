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
    public Action<CardView>? OnClicked;
    public Action<CardView>? OnHovered;
    public Action<CardView>? OnUnhovered;

    private Label _cost = null!;
    private Label _enchant = null!;
    private ColorRect _art = null!;
    private Label _name = null!;
    private Label _type = null!;
    private Label _desc = null!;
    private ColorRect _rarity = null!;
    private bool _built;
    private bool _hovering;

    public static readonly Vector2 CardSize = new(184, 268);

    public override void _Ready()
    {
        CustomMinimumSize = CardSize;
        if (!_built) BuildInternals();
        MouseFilter = MouseFilterEnum.Stop;
        MouseEntered += () => { _hovering = true; OnHovered?.Invoke(this); AnimateHover(true); };
        MouseExited += () => { _hovering = false; OnUnhovered?.Invoke(this); AnimateHover(false); };
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

        // 卡图区（占位渐变/贴图槽）
        _art = new ColorRect { CustomMinimumSize = new Vector2(0, 96) };
        vb.AddChild(_art);

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

        // ArtPath 真贴图槽：非空则把贴图作卡图（占位阶段一般为空；此处仅着色块，换贴图留接口）
        if (!string.IsNullOrEmpty(def.ArtPath) && ResourceLoader.Exists(def.ArtPath) && ResourceLoader.Load(def.ArtPath) is Texture2D tex)
        {
            // 占位阶段：贴图加载能力已验证，实际 TextureRect 替换在美术资源接入时启用
            _art.Color = UiPalette.CardBg(def);
        }
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            OnClicked?.Invoke(this);
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
