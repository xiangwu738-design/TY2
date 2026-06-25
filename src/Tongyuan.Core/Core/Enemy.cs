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
    public int Hp { get; set; }
    public bool IsAlive => Hp > 0;

    /// <summary>敌人身上的持续状态（如易伤），来自附魔牌。</summary>
    public List<Enchantment> Statuses { get; } = new();

    /// <summary>行动链（规格：理论可无限→过长用循环）。链尽则循环回开头。</summary>
    public List<EnemyAction> ActionChain { get; } = new();
    public int ChainIndex { get; set; }

    /// <summary>下一步将执行的行动（不推进）。链空则回退为默认攻击。</summary>
    public EnemyAction NextAction =>
        ActionChain.Count > 0 ? ActionChain[ChainIndex % ActionChain.Count]
                              : new EnemyAction.Attack(EffectivePower, null);

    /// <summary>推进并返回本次执行的行动（链尽循环）。</summary>
    public EnemyAction AdvanceChain()
    {
        if (ActionChain.Count == 0)
            return new EnemyAction.Attack(EffectivePower, null);
        var a = ActionChain[ChainIndex % ActionChain.Count];
        ChainIndex = (ChainIndex + 1) % ActionChain.Count;
        return a;
    }

    /// <summary>易伤加成：受击时额外伤害（消耗一层）。</summary>
    public int ConsumeVulnerableBonus()
    {
        int bonus = 0;
        for (int i = Statuses.Count - 1; i >= 0; i--)
        {
            var s = Statuses[i];
            if (s.Type == EnchantmentType.Vulnerable)
            {
                bonus += s.Magnitude;
                s.Remaining--;
                if (s.Remaining <= 0) Statuses.RemoveAt(i);
            }
        }
        return bonus;
    }

    /// <summary>实际伤害（含蓄力）。</summary>
    public int EffectivePower => Power + Charge;

    public int TargetPosition => Kind switch
    {
        EnemyKind.Slash => 1,
        EnemyKind.Thrust => 2,
        EnemyKind.Strike => -1, // 全体
        _ => 1,
    };

    public Enemy Clone()
    {
        var e = new Enemy
        {
            Id = Id,
            Name = Name,
            Kind = Kind,
            Power = Power,
            Charge = Charge,
            NodeSlot = NodeSlot,
            Hp = Hp,
            ChainIndex = ChainIndex,
        };
        foreach (var a in ActionChain) e.ActionChain.Add(a); // 行动链是 record，不可变，浅拷贝即可
        foreach (var s in Statuses) e.Statuses.Add(s.Clone());
        return e;
    }
}
