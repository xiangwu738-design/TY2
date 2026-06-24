namespace Tongyuan.Core.Core;

/// <summary>
/// 卡牌实例（规格 §4.4 引入实例层）。
/// 手牌/弃牌堆/抽牌堆里的每张牌是独立实例，可挂 <see cref="Enchantment"/>。
/// </summary>
public sealed class Card
{
    public CardDef Def { get; init; } = null!;
    public Guid InstanceId { get; } = Guid.NewGuid();
    public List<Enchantment> Enchantments { get; } = new();

    public bool IsPrep => Def.Type == CardType.Prep;
}
