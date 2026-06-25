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

    private float _stateTimer;   // Skill/Hit 剩余时长，到 0 回 Idle
    private float _time;         // 累计时间，驱动呼吸

    public override void _Ready() => QueueRedraw();

    public override void _Process(double delta)
    {
        _time += (float)delta;

        // Skill/Hit 播完回 Idle（Down 持续，不回）
        if ((State == PortraitState.Skill || State == PortraitState.Hit) && _stateTimer > 0)
        {
            _stateTimer -= (float)delta;
            if (_stateTimer <= 0) ToIdle();
        }

        // 待机循环：轻微呼吸（占位，对应规格“待机状态循环播放几张立绘”）
        if (IdleBreath && State == PortraitState.Idle)
        {
            float b = 1f + 0.03f * Mathf.Sin(_time * 2.2f);
            if (Scale != new Vector2(b, b))
            {
                Scale = new Vector2(b, b);
                QueueRedraw();
            }
        }
        else if (Scale != Vector2.One)
        {
            Scale = Vector2.One;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        var color = State switch
        {
            PortraitState.Idle => new Color(0.3f, 0.6f, 0.9f),   // 蓝
            PortraitState.Skill => new Color(0.95f, 0.78f, 0.2f), // 黄
            PortraitState.Hit => new Color(0.92f, 0.3f, 0.3f),    // 红
            PortraitState.Down => new Color(0.42f, 0.42f, 0.42f), // 灰
            _ => new Color(0.5f, 0.5f, 0.5f),
        };
        // 待机时轻微明度循环（色相循环占位）
        if (State == PortraitState.Idle && IdleBreath)
            color = color.Lightened(0.04f + 0.04f * Mathf.Sin(_time * 2.2f));

        var rect = new Rect2(-DrawW / 2f, -DrawH / 2f, DrawW, DrawH);
        DrawRect(rect, color, filled: true);
        DrawRect(rect, new Color(1, 1, 1, 0.5f), filled: false); // 描边
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
