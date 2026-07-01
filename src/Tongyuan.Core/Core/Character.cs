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
    /// <summary>立绘美术占位口（null=占位色块）。规格 §6：美术后填。</summary>
    public string? PortraitArt { get; set; }
    public string? PortraitSheet { get; set; }

    public Card PrepCard { get; init; } = null!;      // 固定整备牌，常驻手牌
    public List<Card> Hand { get; } = new();          // 抽到的牌（不含整备）
    public List<Card> DrawPile { get; } = new();
    public List<Card> DiscardPile { get; } = new();

    /// <summary>角色身上的持续状态（如易伤），来自附魔牌。</summary>
    public List<Enchantment> Statuses { get; } = new();

    public bool IsAlive => Hp > 0;

    public const int HandLimit = 8;

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
            if (Hand.Count >= HandLimit) break;
            var c = DrawOne(rng);
            if (c is null) break;
            Hand.Add(c);
        }
    }

    /// <summary>易伤加成：受击时额外伤害（仅读取，不消耗——消耗改在行动时由 TickVulnerable 处理）。</summary>
    public int VulnerableBonus()
    {
        int bonus = 0;
        foreach (var s in Statuses)
            if (s.Type == EnchantmentType.Vulnerable) bonus += s.Magnitude;
        return bonus;
    }

    /// <summary>行动时消耗一层易伤（角色每次出牌 / 敌人每次行动调用）。</summary>
    public void TickVulnerable()
    {
        for (int i = Statuses.Count - 1; i >= 0; i--)
        {
            var s = Statuses[i];
            if (s.Type == EnchantmentType.Vulnerable)
            {
                s.Remaining--;
                if (s.Remaining <= 0) Statuses.RemoveAt(i);
            }
        }
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
            PortraitArt = PortraitArt,
            PortraitSheet = PortraitSheet,
            PrepCard = PrepCard?.Clone()!,
        };
        foreach (var x in Hand) c.Hand.Add(x.Clone());
        foreach (var x in DrawPile) c.DrawPile.Add(x.Clone());
        foreach (var x in DiscardPile) c.DiscardPile.Add(x.Clone());
        foreach (var x in Statuses) c.Statuses.Add(x.Clone());
        return c;
    }
}
