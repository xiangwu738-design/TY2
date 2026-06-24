namespace Tongyuan.Core.Core;

/// <summary>
/// 敌人（规格 §4.3）。三型：斩=位1、突=位2、打=全体（N<2 突可落空）。
/// 占位时间轴上的敌节点。
/// </summary>
public sealed class Enemy
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public EnemyKind Kind { get; init; }
    public int Power { get; set; }       // 基础伤害
    public int Charge { get; set; }      // 蓄力层数（敌力+1）
    public int NodeSlot { get; init; }   // 所在时间轴格

    public int TargetPosition => Kind switch
    {
        EnemyKind.Slash => 1,
        EnemyKind.Thrust => 2,
        EnemyKind.Strike => -1, // 全体
        _ => 1,
    };
}
