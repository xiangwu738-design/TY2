namespace Tongyuan.Core.Core;

/// <summary>
/// 角色（规格 §4.3/§4.6）。个人血，非共享；位置 1..N（1=前线）。
/// 专属卡池/整备牌/颜色（模板，§7 用户后填）。
/// </summary>
public sealed class Character
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public int Color { get; init; } // 占位色（ARGB int）

    public int Hp { get; set; }
    public int MaxHp { get; set; }
    public int Position { get; set; } // 1..N，1=前线
    public bool IsDown { get; set; } // 被击倒：本场临时 debuff（弱而不废）
    public int DamageTakenThisFight { get; set; } // 本场累计受伤（结算下调上限用）

    public Card PrepCard { get; init; } = null!;      // 固定整备牌，常驻手牌
    public List<Card> Hand { get; } = new();          // 抽到的牌（不含整备）
    public List<Card> DrawPile { get; } = new();
    public List<Card> DiscardPile { get; } = new();

    /// <summary>角色身上的持续状态（如易伤），来自附魔牌。</summary>
    public List<Enchantment> Statuses { get; } = new();

    public bool IsAlive => Hp > 0;

    /// <summary>抽一张；抽牌堆空则弃牌堆带种子洗牌回抽牌堆（规格 §4.2）。</summary>
    public Card? DrawOne(DeterministicRng rng)
    {
        if (DrawPile.Count == 0)
        {
            if (DiscardPile.Count == 0) return null;
            DrawPile.AddRange(DiscardPile);
            DiscardPile.Clear();
            rng.Shuffle(DrawPile);
        }
        var c = DrawPile[^1];
        DrawPile.RemoveAt(DrawPile.Count - 1);
        return c;
    }

    public void Draw(int n, DeterministicRng rng)
    {
        for (int i = 0; i < n; i++)
        {
            var c = DrawOne(rng);
            if (c is null) break;
            Hand.Add(c);
        }
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

    public Character Clone()
    {
        var c = new Character
        {
            Id = Id,
            Name = Name,
            Color = Color,
            Hp = Hp,
            MaxHp = MaxHp,
            Position = Position,
            IsDown = IsDown,
            DamageTakenThisFight = DamageTakenThisFight,
            PrepCard = PrepCard?.Clone()!,
        };
        foreach (var x in Hand) c.Hand.Add(x.Clone());
        foreach (var x in DrawPile) c.DrawPile.Add(x.Clone());
        foreach (var x in DiscardPile) c.DiscardPile.Add(x.Clone());
        foreach (var x in Statuses) c.Statuses.Add(x.Clone());
        return c;
    }
}
