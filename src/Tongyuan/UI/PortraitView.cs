using Godot;
using System.Linq;
using Tongyuan.Core.Core;
using Tongyuan.Views;

namespace Tongyuan.UI;

/// <summary>
/// 头像组件：立绘(PortraitController) + 名牌 + HP条 + 位置/意图 + 状态。
/// 使用 Panel（非 PanelContainer）以避免容器强制覆盖手动布局的子节点位置。
/// 布局：立绘区（上）→ 名称 → 子信息 → HP条 → HP文字 → 状态（下）。
/// </summary>
public partial class PortraitView : Panel
{
    [Signal] public delegate void ClickedEventHandler();

    public PortraitController Portrait { get; private set; } = null!;
    private Label _name = null!;
    private Label _sub = null!;
    private ProgressBar _hp = null!;
    private Label _hpText = null!;
    private Label _status = null!;
    private ColorRect _aimGlow = null!;
    private ColorRect _flash = null!;
    private ColorRect _targetGlow = null!;
    private bool _aimed;
    private float _time;
    private bool _built;

    // 布局常量
    private const int W = 140;
    private const int PortraitCx = W / 2;        // 立绘中心 x
    private const int PortraitCy = 68;            // 立绘中心 y
    private const int PortraitW = 80;
    private const int PortraitH = 112;            // 半高 = 56 → 占 y=12..124
    private const int InfoY = 130;                // 信息区起始 y（立绘底 y=124 + 6px gap）
    private const int TotalH = 240;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(W, TotalH);
        if (!_built) Build();
        MouseFilter = MouseFilterEnum.Stop;
    }

    private void Build()
    {
        _built = true;

        // 立绘（Node2D，手动定位；Panel 不干预 Node2D 子节点）
        Portrait = new PortraitController { DrawW = PortraitW, DrawH = PortraitH, Position = new Vector2(PortraitCx, PortraitCy) };
        AddChild(Portrait);

        // 名称
        _name = new Label { Position = new Vector2(0, InfoY), Size = new Vector2(W, 22), HorizontalAlignment = HorizontalAlignment.Center };
        _name.AddThemeFontSizeOverride("font_size", 13);
        AddChild(_name);

        // 副信息（位置/意图）
        _sub = new Label { Position = new Vector2(2, InfoY + 24), Size = new Vector2(W - 4, 32), HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _sub.AddThemeFontSizeOverride("font_size", 10);
        _sub.AddThemeColorOverride("font_color", new Color(0.85f, 0.7f, 0.4f));
        AddChild(_sub);

        // HP 条（更高更醒目，经典 RPG 风格）
        _hp = new ProgressBar { Position = new Vector2(8, InfoY + 60), Size = new Vector2(W - 16, 18), MinValue = 0 };
        AddChild(_hp);

        // HP 数字
        _hpText = new Label { Position = new Vector2(0, InfoY + 80), Size = new Vector2(W, 14), HorizontalAlignment = HorizontalAlignment.Center };
        _hpText.AddThemeFontSizeOverride("font_size", 10);
        AddChild(_hpText);

        // 状态栏
        _status = new Label { Position = new Vector2(0, InfoY + 96), Size = new Vector2(W, 14), HorizontalAlignment = HorizontalAlignment.Center };
        _status.AddThemeFontSizeOverride("font_size", 10);
        AddChild(_status);

        // 被瞄准脉动光晕
        _aimGlow = new ColorRect { Color = new Color(UiPalette.WarnOrange.R, UiPalette.WarnOrange.G, UiPalette.WarnOrange.B, 0f), MouseFilter = MouseFilterEnum.Ignore };
        _aimGlow.SetAnchorsPreset(LayoutPreset.FullRect);
        _aimGlow.Visible = false;
        AddChild(_aimGlow);
        // 受击闪白
        _flash = new ColorRect { Color = new Color(1, 1, 1, 0), MouseFilter = MouseFilterEnum.Ignore };
        _flash.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_flash);
        // 被指向高亮
        _targetGlow = new ColorRect { Color = new Color(UiPalette.VulnGold.R, UiPalette.VulnGold.G, UiPalette.VulnGold.B, 0f), MouseFilter = MouseFilterEnum.Ignore };
        _targetGlow.SetAnchorsPreset(LayoutPreset.FullRect);
        _targetGlow.Visible = false;
        AddChild(_targetGlow);
    }

    public void Highlight(bool on)
    {
        _targetGlow.Visible = on;
        if (on) _targetGlow.Color = new Color(UiPalette.VulnGold.R, UiPalette.VulnGold.G, UiPalette.VulnGold.B, 0.30f);
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        if (_aimed && _aimGlow.Visible)
        {
            float a = 0.18f + 0.18f * Mathf.Sin(_time * 4f);
            _aimGlow.Color = new Color(UiPalette.WarnOrange.R, UiPalette.WarnOrange.G, UiPalette.WarnOrange.B, a);
        }
    }

    public void SetupCharacter(Character c, bool isActive, bool aimed)
    {
        if (!_built) Build();
        Portrait.BoundCharacterId = c.Id;
        Portrait.BoundEnemyId = -1;
        Portrait.IdleBreath = c.IsAlive;
        Portrait.Tint = UiPalette.ColorOf(c.Color);
        Portrait.SetArt(c.PortraitArt);
        if (!c.IsAlive) Portrait.ToDown(); else Portrait.ToIdle();

        _name.Text = (isActive ? "▶ " : "") + c.Name;
        _name.AddThemeColorOverride("font_color", UiPalette.ColorOf(c.Color).Lightened(0.2f));
        _sub.Text = $"位 {c.Position}";
        _hp.MaxValue = c.MaxHp > 0 ? c.MaxHp : 1;
        _hp.Value = c.Hp;
        _hpText.Text = $"HP {c.Hp} / {c.MaxHp}";
        _status.Text = "";

        var bg = UiPalette.ColorOf(c.Color).Darkened(isActive ? 0.45f : 0.72f);
        var border = aimed ? UiPalette.WarnOrange : (isActive ? Colors.White : UiPalette.ColorOf(c.Color));
        ApplyStyle(bg, border, 3);
        _aimed = aimed;
        _aimGlow.Visible = aimed;
    }

    public void SetupEnemy(Enemy e, bool clickable, bool aimed)
    {
        if (!_built) Build();
        Portrait.BoundEnemyId = e.Id;
        Portrait.BoundCharacterId = -1;
        Portrait.IdleBreath = e.IsAlive;
        Portrait.Tint = UiPalette.EnemyColor(e.Kind);
        Portrait.SetArt(e.PortraitArt);
        if (!e.IsAlive) Portrait.ToDown(); else Portrait.ToIdle();

        _name.Text = e.IsAlive ? e.Name : $"{e.Name}×";
        _name.AddThemeColorOverride("font_color", new Color(1f, 0.6f, 0.6f));
        _sub.Text = $"{UiPalette.KindText(e.Kind)} · {IntentText(e)}";
        _hp.MaxValue = e.Hp > 0 ? e.Hp : 1;
        _hp.Value = e.Hp;
        _hpText.Text = $"HP {e.Hp}";
        int vuln = e.Statuses.Where(s => s.Type == EnchantmentType.Vulnerable).Sum(s => s.Magnitude);
        int vulnT = e.Statuses.Where(s => s.Type == EnchantmentType.Vulnerable).Sum(s => s.Remaining);
        var parts = new System.Collections.Generic.List<string>();
        if (e.Charge > 0) parts.Add($"蓄+{e.Charge}");
        if (vuln > 0) parts.Add($"易+{vuln}×{vulnT}");
        _status.Text = string.Join(" ", parts);
        _status.AddThemeColorOverride("font_color", UiPalette.VulnGold);

        var bg = clickable ? new Color(0.30f, 0.16f, 0.10f) : new Color(0.20f, 0.10f, 0.10f);
        var border = clickable ? UiPalette.VulnGold : (aimed ? UiPalette.WarnOrange : new Color(0.85f, 0.35f, 0.35f));
        ApplyStyle(bg, border, 3);
        _aimed = false;
        _aimGlow.Visible = false;
    }

    public void OnEvent(GameEvent ev)
    {
        Portrait.OnEvent(ev);
        if (ev is GameEvent.DamageDealt dd && dd.Amount > 0)
        {
            bool me = dd.TargetIsEnemy ? dd.TargetId == Portrait.BoundEnemyId : dd.TargetId == Portrait.BoundCharacterId;
            if (me) Flash();
        }
    }

    private void Flash()
    {
        _flash.Color = new Color(1, 1, 1, 0.75f);
        var tw = CreateTween();
        tw.TweenProperty(_flash, "color:a", 0f, 0.22f).SetTrans(Tween.TransitionType.Cubic);
    }

    private void ApplyStyle(Color bg, Color border, int w)
    {
        var sb = new StyleBoxFlat { BgColor = bg, BorderColor = border };
        sb.SetBorderWidthAll(w); sb.SetCornerRadiusAll(5);
        sb.ContentMarginLeft = 4; sb.ContentMarginTop = 3; sb.ContentMarginRight = 4; sb.ContentMarginBottom = 3;
        AddThemeStyleboxOverride("panel", sb);
    }

    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            EmitSignal(SignalName.Clicked);
    }

    private static string IntentText(Enemy e)
    {
        if (!e.IsAlive) return "—";
        return e.NextAction switch
        {
            EnemyAction.Attack a => a.TargetPos == -1 ? $"打全体 {a.Amount + e.Charge}"
                                   : a.TargetPos == 1 ? $"斩位1 {a.Amount + e.Charge}"
                                   : a.TargetPos == 2 ? $"突位2 {a.Amount + e.Charge}"
                                   : $"{a.Amount + e.Charge}伤",
            EnemyAction.Charge c => $"蓄力+{c.Amount}",
            EnemyAction.Idle => "待机",
            _ => "?",
        };
    }
}
