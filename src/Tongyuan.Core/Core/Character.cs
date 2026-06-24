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

    public Card PrepCard { get; init; } = null!; // 每角色一张固定整备牌
    public List<Card> Hand { get; } = new();
    public List<Card> DrawPile { get; } = new();
    public List<Card> DiscardPile { get; } = new();

    public bool IsAlive => Hp > 0;
}
