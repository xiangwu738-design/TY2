namespace Tongyuan.Core.Core;

/// <summary>
/// 护盾（规格 §4.5）。两型：固定吸收量 / 次数型；绑守护关系，被守护者离位则盾消失。
/// 护盾是时间轴上的“关系持续态”，在逐格结算第②步检查并吸收。
/// </summary>
public sealed class Shield
{
    public ShieldType Type { get; init; }
    public int Amount { get; set; }    // Fixed：剩余吸收量；Count：每次吸收量
    public int RemainingHits { get; set; } // Count：剩余次数
    public int GuardianCharacterId { get; init; }  // 守护者
    public int ProtectedCharacterId { get; init; } // 被守护者

    public bool IsExhausted => Type == ShieldType.Fixed ? Amount <= 0 : RemainingHits <= 0;
}
