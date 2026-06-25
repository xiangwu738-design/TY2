namespace Tongyuan.Core.Core;

/// <summary>
/// 时间轴节点类型。普通敌节点 + 预留破绽节点（拼刀扩展位，见规格 §4.7，不实现）。
/// </summary>
public enum NodeType
{
    Empty,
    Enemy,
    VULN_NODE, // 扩展位：破绽节点（伤害加成占位），不实现
}

/// <summary>卡牌大类。</summary>
public enum CardType
{
    Prep,    // 整备牌：打出回手，效果=抽牌
    Attack,  // 攻击牌：打出进弃牌堆，移位
    Defense, // 防御/护盾牌
    Skill,   // 技能/附魔牌
}

/// <summary>
/// 攻击伤害类型（规格：我方手牌分 打击/斩击/突刺/远程）。
/// 远程=自选敌人且不造成位移；其余近战=出牌移到位1（暴露）。
/// 预留：与敌方类型/护甲的克制关系（扩展位）。
/// </summary>
public enum DamageType
{
    Blunt,  // 打击
    Slash,  // 斩击
    Thrust, // 突刺
    Ranged, // 远程：自选敌人、不位移
}

/// <summary>敌人三型（规格 §4.3）：斩=位1、突=位2、打=全体。</summary>
public enum EnemyKind
{
    Slash,  // 斩：打位1
    Thrust, // 突：打位2（N<2 可落空）
    Strike, // 打：全体
}

/// <summary>附魔类型（规格 §4.4）。保留三种 + 预留扩展。</summary>
public enum EnchantmentType
{
    Power,       // 力量：攻击 +mag
    Vulnerable,  // 易伤：受击 +mag ×N 次
    Charge,      // 蓄力：敌力 +1
    // 预留扩展（不实现）：减伤 / 占位改动 / 抽牌扰动 / 条件增伤
    ReservedDamageReduce,
    ReservedTimingMod,
    ReservedDrawDisrupt,
    ReservedConditionalBoost,
}

/// <summary>护盾两型（规格 §4.5）。</summary>
public enum ShieldType
{
    Fixed,  // 固定吸收量：挡到耗尽
    Count,  // 次数型：挡 N 次
}

/// <summary>立绘状态机（规格 §4.8），预留 Hit/Down。</summary>
public enum PortraitState
{
    Idle,
    Skill,
    Hit,  // 预留
    Down, // 预留
}

/// <summary>玩家动作类型（Core 入口）。</summary>
public enum ActionType
{
    PlayCard,
    UsePrep,
    Skip, // 空过：推进1格，无效果
}
