using Godot;
using System.Globalization;
using Tongyuan.Core.Core;

namespace Tongyuan.UI;

/// <summary>
/// UI 调色板与样式盒工厂（UI 重置规格 §3/§0）：集中颜色/StyleBox，消除散落的 new StyleBoxFlat。
/// 代码构建全局 Theme（SystemFont 雅黑+Segoe UI 做 CJK，零包体；Linux 无雅黑会回退，Windows 导出正常）。
/// </summary>
public static class UiPalette
{
    // ---- 角色色（四色系统，文档 §五：玩家1朱红/2湛蓝/3翠绿/4琥珀）----
    public static readonly Color DamageColor = Hex("#D4453A");
    public static readonly Color DefenseColor = Hex("#3D7BD4");
    public static readonly Color ControlColor = Hex("#3DA862");
    public static readonly Color SupportColor = Hex("#E0A82E");
    public static readonly Color[] PlayerColors = { DamageColor, DefenseColor, ControlColor, SupportColor };

    // ---- 文档 §六 配色板 ----
    public static readonly Color BgBase = Hex("#14181F");
    public static readonly Color PanelBg = Hex("#1E2530");
    public static readonly Color GoldBorder = Hex("#C8A858");
    public static readonly Color TextMain = Hex("#ECE4D2");
    public static readonly Color TextDim = Hex("#9A9486");
    public static readonly Color EnemyRed = Hex("#A32D2D");
    public static readonly Color WarnOrange = Hex("#D85A30");
    public static readonly Color VulnGold = Hex("#EF9F27");
    public static readonly Color ShieldTeal = Hex("#5DCAA5");
    public static readonly Color PointerGold = Hex("#F5E6C0");

    public static Color Hex(string h)
    {
        h = h.TrimStart('#');
        int r = int.Parse(h.Substring(0, 2), NumberStyles.HexNumber);
        int g = int.Parse(h.Substring(2, 2), NumberStyles.HexNumber);
        int b = int.Parse(h.Substring(4, 2), NumberStyles.HexNumber);
        return new Color(r / 255f, g / 255f, b / 255f);
    }

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

        var btnNormal = Flat(PanelBg, GoldBorder with { A = 0.6f });
        var btnHover = Flat(PanelBg.Lightened(0.12f), GoldBorder);
        var btnPressed = Flat(PanelBg.Darkened(0.1f), GoldBorder);
        t.SetStylebox("normal", "Button", btnNormal);
        t.SetStylebox("hover", "Button", btnHover);
        t.SetStylebox("pressed", "Button", btnPressed);
        t.SetStylebox("disabled", "Button", Flat(BgBase, TextDim with { A = 0.3f }));

        t.SetStylebox("panel", "Panel", Flat(PanelBg, GoldBorder with { A = 0.5f }, 1, 6));
        t.SetColor("font_color", "Label", TextMain);
        t.SetColor("font_hover_color", "LinkButton", Colors.White);
        return t;
    }
}
