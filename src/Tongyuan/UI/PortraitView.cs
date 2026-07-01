using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Tongyuan.Core.Core;
using Tongyuan.Views;

namespace Tongyuan.UI;

/// <summary>
/// Battle portrait panel: portrait art, name, HP bar, enemy intent badges, and hit feedback.
/// The children are positioned manually so battle layout can use a stable fixed slot size.
/// </summary>
public partial class PortraitView : Panel
{
    [Signal] public delegate void ClickedEventHandler();
    [Signal] public delegate void RightClickedEventHandler(Vector2 screenPos);

    public static readonly Vector2 SlotSize = new(156, 278);

    public PortraitController Portrait { get; private set; } = null!;

    private Label _name = null!;
    private Label _sub = null!;
    private ColorRect _hpBg = null!;
    private ColorRect _hpFill = null!;
    private ColorRect _shieldFill = null!;
    private Label _hpText = null!;
    private Label _status = null!;
    private ColorRect _aimGlow = null!;
    private ColorRect _flash = null!;
    private ColorRect _targetGlow = null!;
    private PanelContainer _intentBadge = null!;
    private Label _intentLabel = null!;
    private PanelContainer _countdownBadge = null!;
    private Label _countdownLabel = null!;

    private bool _aimed;
    private float _time;
    private bool _built;
    private Tween? _punchTween;

    private const int W = 156;
    private const int PortraitCx = W / 2;
    private const int PortraitCy = 102;
    private const int PortraitW = 128;
    private const int PortraitH = 128;
    private const int InfoY = 166;
    private const int BarX = 10;
    private const int BarW = W - 16;
    private const int BarH = 20;
    private const int BarY = InfoY + 58;

    public override void _Ready()
    {
        CustomMinimumSize = SlotSize;
        Size = SlotSize;
        if (!_built) Build();
        MouseFilter = MouseFilterEnum.Stop;
    }

    private void Build()
    {
        _built = true;
        CustomMinimumSize = SlotSize;
        Size = SlotSize;

        Portrait = new PortraitController
        {
            DrawW = PortraitW,
            DrawH = PortraitH,
            Position = new Vector2(PortraitCx, PortraitCy),
        };
        AddChild(Portrait);

        _name = new Label
        {
            Position = new Vector2(0, InfoY),
            Size = new Vector2(W, 22),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _name.AddThemeFontSizeOverride("font_size", 14);
        AddChild(_name);

        _sub = new Label
        {
            Position = new Vector2(2, InfoY + 24),
            Size = new Vector2(W - 4, 30),
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _sub.AddThemeFontSizeOverride("font_size", 11);
        _sub.AddThemeColorOverride("font_color", new Color(0.85f, 0.7f, 0.4f));
        AddChild(_sub);

        _hpBg = new ColorRect
        {
            Color = new Color(0.12f, 0.12f, 0.12f),
            Position = new Vector2(BarX, BarY),
            Size = new Vector2(BarW, BarH),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_hpBg);

        _hpFill = new ColorRect
        {
            Color = new Color(0.75f, 0.18f, 0.18f),
            Position = new Vector2(BarX, BarY),
            Size = new Vector2(BarW, BarH),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(_hpFill);

        _shieldFill = new ColorRect
        {
            Color = new Color(0.28f, 0.58f, 1f, 0.65f),
            Position = new Vector2(BarX, BarY),
            Size = Vector2.Zero,
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
        };
        AddChild(_shieldFill);

        _hpText = new Label
        {
            Position = new Vector2(BarX, BarY),
            Size = new Vector2(BarW, BarH),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _hpText.AddThemeFontSizeOverride("font_size", 11);
        _hpText.AddThemeColorOverride("font_color", Colors.White);
        _hpText.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.85f));
        _hpText.AddThemeConstantOverride("shadow_offset_x", 1);
        _hpText.AddThemeConstantOverride("shadow_offset_y", 1);
        AddChild(_hpText);

        _status = new Label
        {
            Position = new Vector2(0, BarY + BarH + 4),
            Size = new Vector2(W, 14),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _status.AddThemeFontSizeOverride("font_size", 10);
        AddChild(_status);

        _aimGlow = new ColorRect
        {
            Color = new Color(UiPalette.WarnOrange.R, UiPalette.WarnOrange.G, UiPalette.WarnOrange.B, 0f),
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
        };
        _aimGlow.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_aimGlow);

        _flash = new ColorRect { Color = new Color(1, 1, 1, 0), MouseFilter = MouseFilterEnum.Ignore };
        _flash.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_flash);

        _targetGlow = new ColorRect
        {
            Color = new Color(UiPalette.VulnGold.R, UiPalette.VulnGold.G, UiPalette.VulnGold.B, 0f),
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false,
        };
        _targetGlow.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_targetGlow);

        _intentBadge = new PanelContainer
        {
            Position = new Vector2(58f, 10f),
            CustomMinimumSize = new Vector2(86f, 30f),
            Visible = false,
        };
        _intentLabel = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _intentLabel.AddThemeFontSizeOverride("font_size", 15);
        _intentLabel.AddThemeColorOverride("font_color", Colors.White);
        _intentBadge.AddChild(_intentLabel);
        AddChild(_intentBadge);

        _countdownBadge = new PanelContainer
        {
            Position = new Vector2(12f, 8f),
            CustomMinimumSize = new Vector2(38f, 34f),
            Visible = false,
        };
        _countdownLabel = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _countdownLabel.AddThemeFontSizeOverride("font_size", 20);
        _countdownLabel.AddThemeColorOverride("font_color", Colors.White);
        _countdownLabel.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.85f));
        _countdownLabel.AddThemeConstantOverride("shadow_offset_x", 1);
        _countdownLabel.AddThemeConstantOverride("shadow_offset_y", 1);
        _countdownBadge.AddChild(_countdownLabel);
        AddChild(_countdownBadge);
    }

    public void Highlight(bool on)
    {
        _targetGlow.Visible = on;
        if (on)
            _targetGlow.Color = new Color(UiPalette.VulnGold.R, UiPalette.VulnGold.G, UiPalette.VulnGold.B, 0.30f);
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

    public void SetupCharacter(Character c, bool isActive, bool aimed, IReadOnlyList<Shield>? shields = null)
    {
        if (!_built) Build();
        MouseFilter = MouseFilterEnum.Stop;
        Portrait.Visible = true;
        Portrait.BoundCharacterId = c.Id;
        Portrait.BoundEnemyId = -1;
        Portrait.IdleBreath = c.IsAlive;
        Portrait.Tint = UiPalette.ColorOf(c.Color);
        Portrait.SetArt(c.PortraitArt);
        Portrait.SetSpriteSheet(c.PortraitSheet);
        if (!c.IsAlive) Portrait.ToDown(); else Portrait.ToIdle();

        var roleColor = UiPalette.ColorOf(c.Color);
        _name.Text = (isActive ? "> " : "") + c.Name;
        _name.AddThemeColorOverride("font_color", roleColor.Lightened(0.2f));
        _sub.Text = $"位 {c.Position}";

        float hpRatio = c.MaxHp > 0 ? Math.Clamp((float)c.Hp / c.MaxHp, 0f, 1f) : 0f;
        _hpFill.Color = hpRatio > 0.4f ? roleColor.Darkened(0.2f) : new Color(0.80f, 0.15f, 0.15f);
        _hpFill.Size = new Vector2(BarW * hpRatio, BarH);

        int totalShield = 0;
        if (shields is not null)
        {
            foreach (var sh in shields)
                if (!sh.IsExhausted)
                    totalShield += sh.Type == ShieldType.Fixed ? sh.Amount : sh.Amount * sh.RemainingHits;
        }

        float shieldRatio = c.MaxHp > 0 ? Math.Clamp((float)totalShield / c.MaxHp, 0f, 1f) : 0f;
        _shieldFill.Size = new Vector2(BarW * shieldRatio, BarH);
        _shieldFill.Visible = totalShield > 0;
        _hpText.Text = totalShield > 0 ? $"{c.Hp}/{c.MaxHp} +{totalShield}" : $"{c.Hp}/{c.MaxHp}";
        _status.Text = "";
        _intentBadge.Visible = false;
        _countdownBadge.Visible = false;

        ApplyStyle(roleColor.Darkened(isActive ? 0.45f : 0.72f), aimed ? UiPalette.WarnOrange : (isActive ? Colors.White : roleColor), 3);
        _aimed = aimed;
        _aimGlow.Visible = aimed;
    }

    public void SetupEnemy(Enemy e, bool clickable, bool aimed, int countdown = 0)
    {
        if (!_built) Build();
        MouseFilter = MouseFilterEnum.Stop;
        Portrait.Visible = true;
        Portrait.BoundEnemyId = e.Id;
        Portrait.BoundCharacterId = -1;
        Portrait.IdleBreath = e.IsAlive;
        Portrait.Tint = UiPalette.EnemyColor(e.Kind);
        Portrait.SetArt(e.PortraitArt);
        Portrait.SetSpriteSheet(e.PortraitSheet);
        if (!e.IsAlive) Portrait.ToDown(); else Portrait.ToIdle();

        _name.Text = e.IsAlive ? e.Name : $"{e.Name} X";
        _name.AddThemeColorOverride("font_color", new Color(1f, 0.6f, 0.6f));
        _sub.Text = "";

        float hpRatio = e.MaxHp > 0 ? Math.Clamp((float)e.Hp / e.MaxHp, 0f, 1f) : (e.Hp > 0 ? 1f : 0f);
        _hpFill.Color = hpRatio > 0.4f ? new Color(0.72f, 0.16f, 0.16f) : new Color(0.85f, 0.20f, 0.16f);
        _hpFill.Size = new Vector2(BarW * hpRatio, BarH);
        _hpText.Text = e.MaxHp > 0 ? $"{e.Hp}/{e.MaxHp}" : $"生命 {e.Hp}";
        _shieldFill.Visible = false;
        _status.Text = "";

        var bg = clickable ? new Color(0.30f, 0.16f, 0.10f) : new Color(0.20f, 0.10f, 0.10f);
        var border = clickable ? UiPalette.VulnGold : (aimed ? UiPalette.WarnOrange : new Color(0.85f, 0.35f, 0.35f));
        ApplyStyle(bg, border, 3);
        _aimed = false;
        _aimGlow.Visible = false;
        UpdateIntentBadge(e, countdown);
    }

    public void SetupDeadCharacter(Character c)
    {
        if (!_built) Build();
        Portrait.Visible = true;
        Portrait.BoundCharacterId = c.Id;
        Portrait.BoundEnemyId = -1;
        Portrait.IdleBreath = false;
        Portrait.Tint = new Color(0.4f, 0.4f, 0.4f);
        Portrait.SetArt(c.PortraitArt);
        Portrait.SetSpriteSheet(c.PortraitSheet);
        Portrait.ToDown();

        _name.Text = $"{c.Name} 倒地";
        _name.AddThemeColorOverride("font_color", new Color(0.72f, 0.40f, 0.40f));
        _sub.Text = $"位 {c.Position}";
        _hpFill.Color = new Color(0.3f, 0.1f, 0.1f);
        _hpFill.Size = Vector2.Zero;
        _shieldFill.Visible = false;
        _hpText.Text = "-";
        _status.Text = "";
        _intentBadge.Visible = false;
        _countdownBadge.Visible = false;
        ApplyStyle(new Color(0.18f, 0.08f, 0.08f), new Color(0.58f, 0.22f, 0.22f), 2);
        _aimed = false;
        _aimGlow.Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void SetupDeadEnemy(Enemy e)
    {
        if (!_built) Build();
        Portrait.Visible = true;
        Portrait.BoundEnemyId = e.Id;
        Portrait.BoundCharacterId = -1;
        Portrait.IdleBreath = false;
        Portrait.Tint = new Color(0.35f, 0.35f, 0.35f);
        Portrait.SetArt(e.PortraitArt);
        Portrait.SetSpriteSheet(e.PortraitSheet);
        Portrait.ToDown();

        _name.Text = $"{e.Name} 倒地";
        _name.AddThemeColorOverride("font_color", new Color(0.72f, 0.40f, 0.40f));
        _sub.Text = "";
        _hpFill.Color = new Color(0.25f, 0.1f, 0.1f);
        _hpFill.Size = Vector2.Zero;
        _shieldFill.Visible = false;
        _hpText.Text = e.MaxHp > 0 ? $"0/{e.MaxHp}" : "生命 0";
        _status.Text = "";
        UpdateDeadIntentBadge();
        ApplyStyle(new Color(0.18f, 0.08f, 0.08f), new Color(0.58f, 0.22f, 0.22f), 2);
        _aimed = false;
        _aimGlow.Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void SetupBannedSlot(int slotNumber)
    {
        if (!_built) Build();
        Portrait.Visible = false;
        _name.Text = $"位 {slotNumber}";
        _name.AddThemeColorOverride("font_color", new Color(0.3f, 0.3f, 0.3f));
        _sub.Text = "不可用";
        _sub.AddThemeColorOverride("font_color", new Color(0.3f, 0.3f, 0.3f));
        _hpBg.Color = new Color(0.08f, 0.08f, 0.08f);
        _hpFill.Size = Vector2.Zero;
        _shieldFill.Visible = false;
        _hpText.Text = "-";
        _hpText.AddThemeColorOverride("font_color", new Color(0.3f, 0.3f, 0.3f));
        _status.Text = "";
        _intentBadge.Visible = false;
        _countdownBadge.Visible = false;
        ApplyStyle(new Color(0.08f, 0.08f, 0.08f), new Color(0.2f, 0.2f, 0.2f), 1);
        _aimed = false;
        _aimGlow.Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void OnEvent(GameEvent ev)
    {
        Portrait.OnEvent(ev);
        if (ev is GameEvent.DamageDealt dd && dd.Amount > 0)
        {
            bool me = dd.TargetIsEnemy
                ? dd.TargetId == Portrait.BoundEnemyId
                : dd.TargetId == Portrait.BoundCharacterId;
            if (me) Flash();
        }
    }

    private void Flash()
    {
        _flash.Color = new Color(1, 1, 1, 0.85f);
        var tw = CreateTween();
        tw.TweenProperty(_flash, "color:a", 0f, 0.22f).SetTrans(Tween.TransitionType.Cubic);
    }

    public void PunchAttack(bool toRight)
    {
        _punchTween?.Kill();
        Portrait.Position = new Vector2(PortraitCx, PortraitCy);
        Portrait.Modulate = Colors.White;

        _punchTween = CreateTween().SetParallel(true);
        _punchTween.TweenProperty(Portrait, "modulate", new Color(1.35f, 1.18f, 0.88f), 0.07f);
        var chain = _punchTween.Chain().SetParallel(true);
        chain.TweenProperty(Portrait, "modulate", Colors.White, 0.12f);
    }

    public void PlayCardAnimation(CardDef def) => Portrait.PlayCard(def);

    public override void _GuiInput(InputEvent e)
    {
        if (e is not InputEventMouseButton mb || !mb.Pressed) return;
        if (mb.ButtonIndex == MouseButton.Left)
            EmitSignal(SignalName.Clicked);
        else if (mb.ButtonIndex == MouseButton.Right)
            EmitSignal(SignalName.RightClicked, GetGlobalMousePosition());
    }

    private void ApplyStyle(Color bg, Color border, int w)
    {
        var sb = new StyleBoxFlat { BgColor = bg, BorderColor = border };
        sb.SetBorderWidthAll(w);
        sb.SetCornerRadiusAll(4);
        sb.ContentMarginLeft = 6;
        sb.ContentMarginTop = 6;
        sb.ContentMarginRight = 6;
        sb.ContentMarginBottom = 6;
        AddThemeStyleboxOverride("panel", sb);
    }

    private void UpdateIntentBadge(Enemy e, int countdown)
    {
        var (text, bg) = e.NextAction switch
        {
            EnemyAction.Attack a => ($"攻 {a.Amount + e.Charge}", e.Charge > 0 ? new Color(0.30f, 0.72f, 0.38f) : new Color(0.80f, 0.18f, 0.15f)),
            EnemyAction.Charge c => ($"蓄 +{c.Amount}", new Color(0.78f, 0.58f, 0.16f)),
            EnemyAction.Idle => ("待机", new Color(0.30f, 0.30f, 0.34f)),
            _ => ("?", new Color(0.30f, 0.30f, 0.34f)),
        };
        _intentLabel.Text = text;
        var sb = new StyleBoxFlat { BgColor = bg, BorderColor = new Color(1f, 1f, 1f, 0.55f) };
        sb.SetCornerRadiusAll(6);
        sb.SetBorderWidthAll(1);
        sb.ContentMarginLeft = 9;
        sb.ContentMarginTop = 4;
        sb.ContentMarginRight = 9;
        sb.ContentMarginBottom = 4;
        _intentBadge.AddThemeStyleboxOverride("panel", sb);
        _intentBadge.Visible = true;

        _countdownLabel.Text = countdown <= 0 ? "行" : countdown.ToString();
        var csb = new StyleBoxFlat { BgColor = new Color(0.18f, 0.30f, 0.52f), BorderColor = new Color(1f, 1f, 1f, 0.5f) };
        csb.SetCornerRadiusAll(10);
        csb.SetBorderWidthAll(1);
        csb.ContentMarginLeft = 8;
        csb.ContentMarginTop = 2;
        csb.ContentMarginRight = 8;
        csb.ContentMarginBottom = 2;
        _countdownBadge.AddThemeStyleboxOverride("panel", csb);
        _countdownBadge.Visible = true;
    }

    private void UpdateDeadIntentBadge()
    {
        _intentLabel.Text = "倒地";
        var sb = new StyleBoxFlat { BgColor = new Color(0.24f, 0.24f, 0.26f), BorderColor = new Color(1f, 1f, 1f, 0.35f) };
        sb.SetCornerRadiusAll(6);
        sb.SetBorderWidthAll(1);
        sb.ContentMarginLeft = 9;
        sb.ContentMarginTop = 4;
        sb.ContentMarginRight = 9;
        sb.ContentMarginBottom = 4;
        _intentBadge.AddThemeStyleboxOverride("panel", sb);
        _intentBadge.Visible = true;

        _countdownLabel.Text = "X";
        var csb = new StyleBoxFlat { BgColor = new Color(0.16f, 0.18f, 0.22f), BorderColor = new Color(1f, 1f, 1f, 0.35f) };
        csb.SetCornerRadiusAll(10);
        csb.SetBorderWidthAll(1);
        csb.ContentMarginLeft = 8;
        csb.ContentMarginTop = 2;
        csb.ContentMarginRight = 8;
        csb.ContentMarginBottom = 2;
        _countdownBadge.AddThemeStyleboxOverride("panel", csb);
        _countdownBadge.Visible = true;
    }
}
