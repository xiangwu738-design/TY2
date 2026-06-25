namespace Tongyuan.Core.Core;

/// <summary>
/// 事件流（规格 §3）。Core 产出 List&lt;GameEvent&gt;；Views 据事件播放动画/更新 UI；Net 同步 action（不同步状态）。
/// </summary>
public abstract record GameEvent
{
    public sealed record PointerMoved(int From, int To) : GameEvent;
    public sealed record EnemyTriggered(int EnemyId, int TargetPosition, int Damage) : GameEvent;
    public sealed record EnemyCharged(int EnemyId, int Amount) : GameEvent;   // 蓄力（示警，下次攻击附带）
    public sealed record EnemyIdle(int EnemyId) : GameEvent;                   // 待机/示警
    public sealed record ShieldAbsorbed(int ProtectedCharacterId, int Amount, bool Exhausted) : GameEvent;
    public sealed record CardPlayed(int CharacterId, Guid CardInstanceId) : GameEvent;
    public sealed record PrepUsed(int CharacterId, int Drawn) : GameEvent;
    public sealed record CardsDrawn(int CharacterId, int Count) : GameEvent;
    public sealed record DamageDealt(int TargetId, bool TargetIsEnemy, int Amount) : GameEvent;
    public sealed record PositionChanged(int CharacterId, int From, int To) : GameEvent;
    public sealed record CharacterDowned(int CharacterId) : GameEvent;
    public sealed record CharacterDied(int CharacterId) : GameEvent;
    public sealed record EnemyDied(int EnemyId) : GameEvent;
    public sealed record EnchantmentApplied(Enchantment Enchantment, int? TargetCharacterId, int? TargetEnemyId) : GameEvent;
    public sealed record ShieldPlaced(int GuardianId, int ProtectedId, ShieldType Type) : GameEvent;
    public sealed record TurnEnded() : GameEvent;
}

/// <summary>
/// 玩家动作（Net 同步单元，主机权威执行）。确定性：同 seed + 同 action 序列 → 同结果。
/// </summary>
public sealed record PlayerAction(
    int CharacterId,
    ActionType Type,
    Guid? CardInstanceId = null,
    int? TargetCharacterId = null,
    int? TargetEnemyId = null,
    Guid? TargetCardInstanceId = null);
