using Godot;
using System;
using Tongyuan.Core.Core;

namespace Tongyuan.Views;

public partial class PortraitController : Node2D
{
    private enum SvMotion
    {
        Walk = 0,
        Wait = 1,
        Chant = 2,
        Guard = 3,
        Damage = 4,
        Thrust = 6,
        Swing = 7,
        Missile = 8,
        Skill = 9,
        Spell = 10,
        Item = 11,
        Dead = 17,
    }

    private const SvMotion IdleMotion = SvMotion.Walk;

    public int BoundCharacterId { get; set; } = -1;
    public int BoundEnemyId { get; set; } = -1;
    public PortraitState State { get; private set; } = PortraitState.Idle;

    public float DrawW { get; set; } = 48f;
    public float DrawH { get; set; } = 64f;
    public bool IdleBreath { get; set; } = true;
    public Color Tint { get; set; } = new(0.5f, 0.55f, 0.65f);

    public string? ArtPath { get; private set; }
    public string? SheetPath { get; private set; }

    private Texture2D? _art;
    private Texture2D? _sheet;
    private SvMotion _motion = IdleMotion;
    private int _frame;
    private float _frameTimer;
    private bool _loopMotion = true;
    private int _cellW = 64;
    private int _cellH = 64;
    private float _stateTimer;
    private float _time;

    public void SetArt(string? path)
    {
        ArtPath = path;
        _art = null;
        if (!string.IsNullOrEmpty(path) && ResourceLoader.Exists(path))
            _art = ResourceLoader.Load(path) as Texture2D;
        QueueRedraw();
    }

    public void SetSpriteSheet(string? path)
    {
        SheetPath = path;
        _sheet = null;
        if (!string.IsNullOrEmpty(path) && ResourceLoader.Exists(path))
        {
            _sheet = ResourceLoader.Load(path) as Texture2D;
            if (_sheet is not null)
            {
                var size = _sheet.GetSize();
                _cellW = Math.Max(1, (int)(size.X / 9f));
                _cellH = Math.Max(1, (int)(size.Y / 6f));
            }
        }
        PlayMotion(IdleMotion, loop: true);
        QueueRedraw();
    }

    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.Nearest;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;

        if (_sheet is not null)
        {
            _frameTimer += (float)delta;
            float frameSeconds = _loopMotion ? 0.26f : 0.10f;
            if (_frameTimer >= frameSeconds)
            {
                _frameTimer = 0f;
                _frame++;
                if (_frame >= 3)
                {
                    if (_loopMotion) _frame = 0;
                    else if (_motion == SvMotion.Dead)
                    {
                        _frame = 2;
                        _frameTimer = 0f;
                    }
                    else ToIdle();
                }
                QueueRedraw();
            }
        }
        else if ((State == PortraitState.Skill || State == PortraitState.Hit) && _stateTimer > 0)
        {
            _stateTimer -= (float)delta;
            if (_stateTimer <= 0) ToIdle();
        }

        if (Scale != Vector2.One)
        {
            Scale = Vector2.One;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        var color = State switch
        {
            PortraitState.Idle => new Color(0.34f, 0.40f, 0.52f),
            PortraitState.Skill => new Color(0.95f, 0.78f, 0.2f),
            PortraitState.Hit => new Color(0.92f, 0.3f, 0.3f),
            PortraitState.Down => new Color(0.30f, 0.30f, 0.32f),
            _ => new Color(0.5f, 0.5f, 0.5f),
        };
        if (State == PortraitState.Idle && IdleBreath)
            color = color.Lightened(0.05f + 0.05f * Mathf.Sin(_time * 2.2f));

        if (_sheet is not null)
            DrawSheetFrame(color);
        else if (_art is not null)
            DrawStaticArt(color);
        else
            DrawFallbackSilhouette(color);
    }

    public void PlayCard(CardDef def) => PlayMotion(MotionFor(def));

    public void PlayPrep(CardDef def) => PlayMotion(MotionFor(def));

    public void PlayEnemyAction(EnemyAction action)
    {
        switch (action)
        {
            case EnemyAction.Attack:
                PlayMotion(SvMotion.Swing);
                break;
            case EnemyAction.Charge:
                PlayMotion(SvMotion.Chant);
                break;
            case EnemyAction.Idle:
                PlayMotion(IdleMotion, loop: true);
                break;
        }
    }

    public void PlaySkill(float duration = 0.45f)
    {
        if (_sheet is not null) PlayMotion(SvMotion.Skill);
        else Set(PortraitState.Skill, duration);
    }

    public void PlayHit(float duration = 0.25f)
    {
        if (_sheet is not null) PlayMotion(SvMotion.Damage);
        else Set(PortraitState.Hit, duration);
    }

    public void ToDown()
    {
        State = PortraitState.Down;
        _stateTimer = 0;
        Scale = Vector2.One;
        if (_sheet is not null) PlayMotion(SvMotion.Dead, loop: false);
        QueueRedraw();
    }

    public void ToIdle()
    {
        State = PortraitState.Idle;
        _stateTimer = 0;
        if (_sheet is not null) PlayMotion(IdleMotion, loop: true);
        QueueRedraw();
    }

    public void OnEvent(GameEvent ev)
    {
        switch (ev)
        {
            case GameEvent.PrepUsed pu when pu.CharacterId == BoundCharacterId:
                PlayMotion(SvMotion.Item);
                break;
            case GameEvent.EnemyCharged ec when ec.EnemyId == BoundEnemyId:
                PlayMotion(SvMotion.Chant);
                break;
            case GameEvent.EnemyIdle ei when ei.EnemyId == BoundEnemyId:
                PlayMotion(IdleMotion, loop: true);
                break;
            case GameEvent.EnemyTriggered et when et.EnemyId == BoundEnemyId:
                PlayMotion(SvMotion.Swing);
                break;
            case GameEvent.DamageDealt d when !d.TargetIsEnemy && d.TargetId == BoundCharacterId:
            case GameEvent.DamageDealt d2 when d2.TargetIsEnemy && d2.TargetId == BoundEnemyId:
                PlayHit();
                break;
            case GameEvent.CharacterDied cd when cd.CharacterId == BoundCharacterId:
            case GameEvent.EnemyDied ed when ed.EnemyId == BoundEnemyId:
                ToDown();
                break;
        }
    }

    private void PlayMotion(SvMotion motion, bool loop = false)
    {
        if (_sheet is null)
        {
            Set(motion is SvMotion.Damage ? PortraitState.Hit : PortraitState.Skill, 0.35f);
            return;
        }

        _motion = motion;
        _frame = 0;
        _frameTimer = 0f;
        _loopMotion = loop;
        State = motion switch
        {
            SvMotion.Walk or SvMotion.Wait => PortraitState.Idle,
            SvMotion.Damage => PortraitState.Hit,
            SvMotion.Dead => PortraitState.Down,
            _ => PortraitState.Skill,
        };
        _stateTimer = loop ? 0f : 0.35f;
        QueueRedraw();
    }

    private void Set(PortraitState state, float duration)
    {
        State = state;
        _stateTimer = duration;
        Scale = Vector2.One;
        QueueRedraw();
    }

    private static SvMotion MotionFor(CardDef def)
    {
        if (def.Animation != CardAnimation.Auto)
            return def.Animation switch
            {
                CardAnimation.Wait => SvMotion.Wait,
                CardAnimation.Chant => SvMotion.Chant,
                CardAnimation.Guard => SvMotion.Guard,
                CardAnimation.Damage => SvMotion.Damage,
                CardAnimation.Thrust => SvMotion.Thrust,
                CardAnimation.Swing => SvMotion.Swing,
                CardAnimation.Missile => SvMotion.Missile,
                CardAnimation.Skill => SvMotion.Skill,
                CardAnimation.Spell => SvMotion.Spell,
                CardAnimation.Item => SvMotion.Item,
                _ => SvMotion.Skill,
            };

        return def.Effect switch
        {
            EffectKind.ApplyShield => SvMotion.Guard,
            EffectKind.ApplyEnchantment => SvMotion.Chant,
            EffectKind.DrawCards => SvMotion.Item,
            EffectKind.AttackDamage => def.DamageType switch
            {
                DamageType.Thrust => SvMotion.Thrust,
                DamageType.Ranged => SvMotion.Missile,
                _ => SvMotion.Swing,
            },
            _ => SvMotion.Skill,
        };
    }

    private void DrawSheetFrame(Color color)
    {
        if (_sheet is null) return;
        int motion = (int)_motion;
        int col = (motion % 3) * 3 + Math.Clamp(_frame, 0, 2);
        int row = motion / 3;
        var src = new Rect2(col * _cellW, row * _cellH, _cellW, _cellH);
        float scale = MathF.Floor(Math.Min(DrawW / _cellW, DrawH / _cellH));
        if (scale < 1f) scale = Math.Min(DrawW / _cellW, DrawH / _cellH);
        var drawSize = new Vector2(_cellW * scale, _cellH * scale);
        var dst = new Rect2(-drawSize.X / 2f, -drawSize.Y / 2f, drawSize.X, drawSize.Y);
        DrawTextureRectRegion(_sheet, dst, src);
        DrawRect(dst, color with { A = State == PortraitState.Idle ? 0.18f : 0.45f }, filled: false, 2f);
    }

    private void DrawStaticArt(Color color)
    {
        if (_art is null) return;
        var texSize = _art.GetSize();
        float scale = Math.Min(DrawW / texSize.X, DrawH / texSize.Y);
        var drawSize = texSize * scale;
        var rect = new Rect2(-drawSize.X / 2f, -drawSize.Y / 2f, drawSize.X, drawSize.Y);
        DrawTextureRect(_art, rect, false);
        DrawRect(rect, color with { A = State == PortraitState.Idle ? 0.25f : 0.55f }, filled: false, 2f);
    }

    private void DrawFallbackSilhouette(Color color)
    {
        float w = DrawW;
        float h = DrawH;
        DrawRect(new Rect2(-w / 2f, -h / 2f, w, h), color with { A = 0.5f }, filled: false, 2f);
        var sil = new Color(0.34f, 0.36f, 0.40f, 0.75f);
        float headR = w * 0.16f;
        var head = new Vector2(0, -h / 2f + headR + 2);
        DrawCircle(head, headR, sil);
        float bodyTop = head.Y + headR;
        float bodyH = h * 0.42f;
        DrawRect(new Rect2(-w * 0.20f, bodyTop, w * 0.40f, bodyH), sil, filled: true);
        DrawRect(new Rect2(-w * 0.16f, bodyTop + bodyH, w * 0.12f, h * 0.28f), sil, filled: true);
        DrawRect(new Rect2(w * 0.04f, bodyTop + bodyH, w * 0.12f, h * 0.28f), sil, filled: true);
    }
}
