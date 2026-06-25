using Godot;
using Tongyuan.Core.Core;

namespace Tongyuan.UI;

/// <summary>
/// UI 调色板与样式盒工厂（UI 重置规格 §3/§0）：集中颜色/StyleBox，消除散落的 new StyleBoxFlat。
/// 代码构建全局 Theme（SystemFont 雅黑+Segoe UI 做 CJK，零包体；Linux 无雅黑会回退，Windows 导出正常）。
/// </summary>
public static class UiPalette
{
    // ---- 角色色（镜像 CharacterTemplates）----
    public static readonly Color DamageColor = new(0.81f, 0.23f, 0.23f);
    public static readonly Color DefenseColor = new(0.23f, 0.42f, 0.81f);
    public static readonly Color ControlColor = new(0.56f, 0.23f, 0.81f);
    public static readonly Color SupportColor = new(0.23f, 0.81f, 0.42f);

    // ---- 卡牌底色 / 描边（按类型 + 伤害类型）----
    public static Color CardBg(CardDef def) => def.Type switch
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

    public static Color CardAccent(CardDef def) => def.Type == CardType.Attack
        ? (def.DamageType == DamageType.Ranged ? new Color(0.5f, 0.8f, 1f) : new Color(0.9f, 0.5f, 0.4f))
        : new Color(0.6f, 0.7f, 0.85f);

    /// <summary>稀有度边色（卡框边框）。</summary>
    public static Color RarityBorder(Rarity r) => r switch
    {
        Rarity.Rare => new Color(0.95f, 0.78f, 0.2f),
        _ => new Color(0.55f, 0.6f, 0.7f),
    };

    // ---- 敌人 ----
    public static Color EnemyColor(EnemyKind k) => k switch
    {
        EnemyKind.Slash => new Color(1f, 0.35f, 0.35f),
        EnemyKind.Thrust => new Color(0.95f, 0.65f, 0.25f),
        EnemyKind.Strike => new Color(0.70f, 0.45f, 0.95f),
        _ => Colors.White,
    };

    public static string KindText(EnemyKind k) => k switch
    {
        EnemyKind.Slash => "斩",
        EnemyKind.Thrust => "突",
        EnemyKind.Strike => "打",
        _ => "?",
    };

    public static Color ColorOf(int argb)
    {
        float a = ((argb >> 24) & 0xFF) / 255f;
        float r = ((argb >> 16) & 0xFF) / 255f;
        float g = ((argb >> 8) & 0xFF) / 255f;
        float b = (argb & 0xFF) / 255f;
        return new Color(r, g, b, a);
    }

    // ---- 样式盒工厂 ----
    public static StyleBoxFlat Flat(Color bg, Color? border = null, int borderWidth = 2, int radius = 6)
    {
        var sb = new StyleBoxFlat { BgColor = bg, BorderColor = border ?? new Color(0.5f, 0.5f, 0.55f) };
        sb.SetBorderWidthAll(borderWidth);
        sb.SetCornerRadiusAll(radius);
        sb.ContentMarginLeft = 5; sb.ContentMarginTop = 4; sb.ContentMarginRight = 5; sb.ContentMarginBottom = 4;
        return sb;
    }

    public static StyleBoxFlat CardStyle(CardDef def) =>
        Flat(CardBg(def), RarityBorder(def.Rarity), borderWidth: 3, radius: 8);

    /// <summary>构建全局 Theme：SystemFont（CJK）+ 默认 Button/Panel 样式。</summary>
    public static Theme BuildTheme()
    {
        var t = new Theme();
        // CJK 字体：Windows 自带雅黑+Segoe UI；零包体增量。Linux 无则回退默认。
        var font = new SystemFont();
        font.FontNames = new string[] { "Microsoft YaHei", "Segoe UI", "Noto Sans CJK SC", "DejaVu Sans" };
        t.DefaultFont = font;

        var btnNormal = Flat(new Color(0.20f, 0.22f, 0.27f), new Color(0.45f, 0.5f, 0.6f));
        var btnHover = Flat(new Color(0.28f, 0.30f, 0.36f), Colors.White);
        var btnPressed = Flat(new Color(0.16f, 0.18f, 0.23f), Colors.White);
        t.SetStylebox("normal", "Button", btnNormal);
        t.SetStylebox("hover", "Button", btnHover);
        t.SetStylebox("pressed", "Button", btnPressed);
        t.SetStylebox("disabled", "Button", Flat(new Color(0.14f, 0.14f, 0.16f), new Color(0.3f, 0.3f, 0.32f)));

        t.SetStylebox("panel", "Panel", Flat(new Color(0.13f, 0.14f, 0.17f), new Color(0.3f, 0.32f, 0.38f), 1, 6));
        t.SetColor("font_color", "Label", new Color(0.86f, 0.88f, 0.92f));
        t.SetColor("font_hover_color", "LinkButton", Colors.White);
        return t;
    }
}
