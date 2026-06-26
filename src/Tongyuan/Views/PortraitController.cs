using Godot;
using Tongyuan.Core.Core;

namespace Tongyuan.Views;

/// <summary>
/// 立绘状态机（规格 §4.8）。每角色/敌人一个。
/// 状态：Idle（待机循环帧）/ Skill（出招帧，播完回 Idle）/ Hit（受击帧）/ Down（倒下）。
/// 由 Core 事件驱动；动画异步、不阻塞结算（结算即时完成）。
/// 占位：色块/简单帧（idle蓝/skill黄/hit红/down灰）；真正立绘帧以后换，留好接口。
/// </summary>
public partial class PortraitController : Node2D
{
    public int BoundCharacterId { get; set; } = -1;
    public int BoundEnemyId { get; set; } = -1;
    public PortraitState State { get; private set; } = PortraitState.Idle;

    /// <summary>占位立绘绘制尺寸（宽×高），按挂载点设置。</summary>
    public float DrawW { get; set; } = 48f;
    public float DrawH { get; set; } = 64f;

    /// <summary>待机呼吸幅度（占位：循环帧的简化）。</summary>
    public bool IdleBreath { get; set; } = true;

    /// <summary>角色/敌人主色（四色系统）：剪影染色，让不同单位一眼可辨。</summary>
    public Color Tint { get; set; } = new(0.5f, 0.55f, 0.65f);

    /// <summary>立绘贴图槽（Character.PortraitArt / Enemy.PortraitArt）。非空则画贴图，否则染色剪影。</summary>
    public string? ArtPath { get; private set; }
    private Texture2D? _art;
    public void SetArt(string? path)
    {
        ArtPath = path;
        _art = null;
        if (!string.IsNullOrEmpty(path) && ResourceLoader.Exists(path))
            _art = ResourceLoader.Load(path) as Texture2D;
        QueueRedraw();
    }

    private float _stateTimer;   // Skill/Hit 剩余时长，到 0 回 Idle
    private float _time;         // 累计时间，驱动呼吸

    public override void _Ready()
    {
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;

        // Skill/Hit 播完回 Idle（Down 持续，不回）
        if ((State == PortraitState.Skill || State == PortraitState.Hit) && _stateTimer > 0)
        {
            _stateTimer -= (float)delta;
            if (_stateTimer <= 0) ToIdle();
        }
        // 立绘不做呼吸感（按反馈），保持 Scale=1
        if (Scale != Vector2.One) { Scale = Vector2.One; QueueRedraw(); }
    }

    public override void _Draw()
    {
        var color = State switch
        {
            PortraitState.Idle => new Color(0.34f, 0.40f, 0.52f),  // 待机：冷灰蓝
            PortraitState.Skill => new Color(0.95f, 0.78f, 0.2f),   // 施法：金
            PortraitState.Hit => new Color(0.92f, 0.3f, 0.3f),      // 受击：红
            PortraitState.Down => new Color(0.30f, 0.30f, 0.32f),   // 倒下：深灰
            _ => new Color(0.5f, 0.5f, 0.5f),
        };
        if (State == PortraitState.Idle && IdleBreath)
            color = color.Lightened(0.05f + 0.05f * Mathf.Sin(_time * 2.2f));

        float w = DrawW, h = DrawH;

        // 真立绘贴图（PortraitArt）：有则直接画（纯粹图片区分），状态叠加边框
        if (_art is not null)
        {
            var texRect = new Rect2(-w / 2f, -h / 2f, w, h);
            DrawTextureRect(_art, texRect, false);
            DrawRect(texRect, color with { A = State == PortraitState.Idle ? 0.25f : 0.55f }, filled: false, 2f);
        }
        else
        {
            // 占位全身剪影：统一中性（不靠颜色/剪影区分角色；区分仅靠贴图）
            // 仅剪影 + 描边（去掉底色块）；信息写在下面（PortraitView）
            DrawRect(new Rect2(-w / 2f, -h / 2f, w, h), color with { A = 0.5f }, filled: false, 2f);
            var sil = new Color(0.34f, 0.36f, 0.40f, 0.75f);
            float headR = w * 0.16f;
            var head = new Vector2(0, -h / 2f + headR + 2);
            DrawCircle(head, headR, sil);
            float bodyTop = head.Y + headR;
            float bodyH = h * 0.42f;
            DrawRect(new Rect2(-w * 0.20f, bodyTop, w * 0.40f, bodyH), sil, filled: true);     // 躯干
            DrawRect(new Rect2(-w * 0.16f, bodyTop + bodyH, w * 0.12f, h * 0.28f), sil, filled: true); // 左腿
            DrawRect(new Rect2(w * 0.04f, bodyTop + bodyH, w * 0.12f, h * 0.28f), sil, filled: true);  // 右腿
        }

    }

    public void PlaySkill(float duration = 0.45f) => Set(PortraitState.Skill, duration);
    public void PlayHit(float duration = 0.25f) => Set(PortraitState.Hit, duration);
    public void ToDown() { State = PortraitState.Down; _stateTimer = 0; Scale = Vector2.One; QueueRedraw(); }
    public void ToIdle() { State = PortraitState.Idle; _stateTimer = 0; QueueRedraw(); }

    private void Set(PortraitState s, float duration)
    {
        State = s;
        _stateTimer = duration;
        Scale = Vector2.One;
        QueueRedraw();
    }

    /// <summary>由 Core 事件驱动状态切换。仅响应与本绑定 id 匹配的事件。</summary>
    public void OnEvent(GameEvent ev)
    {
        switch (ev)
        {
            // 角色：出牌/整备 → 技能帧
            case GameEvent.CardPlayed cp when cp.CharacterId == BoundCharacterId:
            case GameEvent.PrepUsed pu when pu.CharacterId == BoundCharacterId:
                PlaySkill();
                break;
            // 敌人：触发攻击 → 技能帧
            case GameEvent.EnemyTriggered et when et.EnemyId == BoundEnemyId:
            case GameEvent.EnemyCharged ec when ec.EnemyId == BoundEnemyId:
                PlaySkill();
                break;
            // 受击：角色或敌人挨打 → 受击帧
            case GameEvent.DamageDealt d when !d.TargetIsEnemy && d.TargetId == BoundCharacterId:
            case GameEvent.DamageDealt d2 when d2.TargetIsEnemy && d2.TargetId == BoundEnemyId:
                PlayHit();
                break;
            // 倒下
            case GameEvent.CharacterDied cd when cd.CharacterId == BoundCharacterId:
            case GameEvent.EnemyDied ed when ed.EnemyId == BoundEnemyId:
                ToDown();
                break;
        }
    }
}
