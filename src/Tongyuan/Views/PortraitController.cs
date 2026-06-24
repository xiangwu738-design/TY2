using Godot;
using Tongyuan.Core.Core;

namespace Tongyuan.Views;

/// <summary>
/// 立绘状态机（规格 §4.8）。每角色/敌人一个。
/// 状态：Idle（循环帧）/ Skill（出招帧，播完回 Idle）/ 预留 Hit/Down。
/// 由 Core 事件驱动；动画异步、不阻塞结算（结算即时完成）。P1 占位色块/帧；P5 换立绘。
/// </summary>
public partial class PortraitController : Node2D
{
    public int BoundCharacterId { get; set; } = -1;
    public int BoundEnemyId { get; set; } = -1;
    public PortraitState State { get; private set; } = PortraitState.Idle;

    private float _skillTimer;

    public override void _Ready()
    {
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        // Skill 播完回 Idle（占位：固定时长）
        if (State == PortraitState.Skill)
        {
            _skillTimer -= (float)delta;
            if (_skillTimer <= 0) ToIdle();
        }
    }

    public override void _Draw()
    {
        var color = State switch
        {
            PortraitState.Idle => new Color(0.3f, 0.6f, 0.9f),
            PortraitState.Skill => new Color(0.9f, 0.7f, 0.2f),
            PortraitState.Hit => new Color(0.9f, 0.3f, 0.3f),
            PortraitState.Down => new Color(0.4f, 0.4f, 0.4f),
            _ => new Color(0.5f, 0.5f, 0.5f),
        };
        DrawRect(new Rect2(-32, -64, 64, 128), color);
    }

    public void PlaySkill(float duration = 0.4f)
    {
        State = PortraitState.Skill;
        _skillTimer = duration;
        QueueRedraw();
    }

    public void PlayHit()
    {
        State = PortraitState.Hit;
        QueueRedraw();
        // 占位：短暂后回 Idle
        PlaySkill(0.2f);
    }

    public void ToDown()
    {
        State = PortraitState.Down;
        QueueRedraw();
    }

    public void ToIdle()
    {
        State = PortraitState.Idle;
        QueueRedraw();
    }

    /// <summary>由 Core 事件驱动状态切换（占位接线；P1 仅核心事件）。</summary>
    public void OnEvent(GameEvent ev)
    {
        switch (ev)
        {
            case GameEvent.CardPlayed cp when cp.CharacterId == BoundCharacterId:
            case GameEvent.PrepUsed pu when pu.CharacterId == BoundCharacterId:
                PlaySkill();
                break;
            case GameEvent.EnemyTriggered et when et.EnemyId == BoundEnemyId:
                PlaySkill();
                break;
            case GameEvent.DamageDealt d when !d.TargetIsEnemy && d.TargetId == BoundCharacterId:
                PlayHit();
                break;
            case GameEvent.CharacterDied cd when cd.CharacterId == BoundCharacterId:
            case GameEvent.EnemyDied ed when ed.EnemyId == BoundEnemyId:
                ToDown();
                break;
        }
    }
}
