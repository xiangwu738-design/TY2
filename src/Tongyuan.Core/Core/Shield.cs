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

    /// <summary>吸收一笔伤害，返回实际被吸收的量。</summary>
    public int Absorb(int incoming)
    {
        if (IsExhausted || incoming <= 0) return 0;
        if (Type == ShieldType.Fixed)
        {
            int a = Math.Min(Amount, incoming);
            Amount -= a;
            return a;
        }
        else // Count
        {
            RemainingHits--;
            return Math.Min(Amount, incoming);
        }
    }

    public Shield Clone() => new()
    {
        Type = Type,
        Amount = Amount,
        RemainingHits = RemainingHits,
        GuardianCharacterId = GuardianCharacterId,
        ProtectedCharacterId = ProtectedCharacterId,
    };
}
