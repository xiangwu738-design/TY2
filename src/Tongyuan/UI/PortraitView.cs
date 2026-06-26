using Godot;
using System.Linq;
using Tongyuan.Core.Core;
using Tongyuan.Views;

namespace Tongyuan.UI;

/// <summary>
/// 头像组件（UI 重置阶段2）：统一角色/敌人头像——立绘(PortraitController) + 名牌 + HP条 +
/// 位置/意图 + 蓄力 + 易伤(附魔只读显示器) + 激活/被瞄边框 + 点击。
/// 复用 PortraitController 状态机；事件经 OnEvent 转发。
/// </summary>
public partial class PortraitView : PanelContainer
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

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(132, 250);
        if (!_built) Build();
        MouseFilter = MouseFilterEnum.Stop;
    }

    private void Build()
    {
        _built = true;
        // 全身立绘（顶部，居中）——Node2D 手动定位（不进 VBox 流式）
        Portrait = new PortraitController { DrawW = 76, DrawH = 150, Position = new Vector2(66, 82) };
        AddChild(Portrait);

        // 信息区（立绘下方，手动定位）
        _name = new Label { Position = new Vector2(0, 168), Size = new Vector2(132, 20), HorizontalAlignment = HorizontalAlignment.Center };
        _name.AddThemeFontSizeOverride("font_size", 14);
        AddChild(_name);

        _sub = new Label { Position = new Vector2(0, 188), Size = new Vector2(132, 16), HorizontalAlignment = HorizontalAlignment.Center };
        _sub.AddThemeFontSizeOverride("font_size", 11);
        _sub.AddThemeColorOverride("font_color", new Color(0.85f, 0.7f, 0.4f));
        AddChild(_sub);

        _hp = new ProgressBar { Position = new Vector2(12, 206), Size = new Vector2(108, 12), MinValue = 0 };
        AddChild(_hp);

        _hpText = new Label { Position = new Vector2(0, 220), Size = new Vector2(132, 14), HorizontalAlignment = HorizontalAlignment.Center };
        _hpText.AddThemeFontSizeOverride("font_size", 10);
        AddChild(_hpText);

        _status = new Label { Position = new Vector2(0, 234), Size = new Vector2(132, 14), HorizontalAlignment = HorizontalAlignment.Center };
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
        // 被指向高亮（玩家近战牌悬停：标记将受影响的敌人）
        _targetGlow = new ColorRect { Color = new Color(UiPalette.VulnGold.R, UiPalette.VulnGold.G, UiPalette.VulnGold.B, 0f), MouseFilter = MouseFilterEnum.Ignore };
        _targetGlow.SetAnchorsPreset(LayoutPreset.FullRect);
        _targetGlow.Visible = false;
        AddChild(_targetGlow);
    }

    /// <summary>玩家指向高亮（近战牌悬停预读：标记将挨打的敌人）。</summary>
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
            float a = 0.18f + 0.18f * Mathf.Sin(_time * 4f); // 缓慢呼吸
            _aimGlow.Color = new Color(UiPalette.WarnOrange.R, UiPalette.WarnOrange.G, UiPalette.WarnOrange.B, a);
        }
    }

    public void SetupCharacter(Character c, bool isActive, bool aimed)
    {
        if (!_built) Build();
        Portrait.BoundCharacterId = c.Id;
        Portrait.BoundEnemyId = -1;
        Portrait.IdleBreath = c.IsAlive;
        if (!c.IsAlive) Portrait.ToDown(); else Portrait.ToIdle();

        _name.Text = (isActive ? "▶ " : "") + c.Name;
        _name.AddThemeColorOverride("font_color", UiPalette.ColorOf(c.Color).Lightened(0.2f));
        _sub.Text = $"位{c.Position}";
        _hp.MaxValue = c.MaxHp > 0 ? c.MaxHp : 1;
        _hp.Value = c.Hp;
        _hpText.Text = $"HP {c.Hp}/{c.MaxHp}";
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
        ApplyStyle(bg, border, clickable ? 3 : 3);
        _aimed = false; // 敌人不做被瞄准脉动
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

    /// <summary>受击闪白（文档 §七：闪白）。</summary>
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
