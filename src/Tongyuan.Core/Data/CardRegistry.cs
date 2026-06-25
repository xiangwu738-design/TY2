namespace Tongyuan.Core.Data;

using Tongyuan.Core.Core;

/// <summary>
/// 卡牌注册表（自定义卡牌功能，规格 §6/§7 可扩展）。
/// 数据驱动：默认载入 4 角色模板卡池 + 整备牌；运行时可 Register 自定义卡牌。
/// 留足扩展位：卡牌效果/附魔/护盾/伤害类型全部参数化，未来可从 JSON/资源加载。
/// </summary>
public sealed class CardRegistry
{
    private readonly Dictionary<string, CardDef> _byId = new();

    public IEnumerable<CardDef> All => _byId.Values;
    public int Count => _byId.Count;

    public CardDef? Get(string id) => _byId.TryGetValue(id, out var d) ? d : null;

    public void Register(CardDef def)
    {
        if (string.IsNullOrEmpty(def.Id)) throw new ArgumentException("CardDef.Id 必须非空");
        _byId[def.Id] = def;
    }

    /// <summary>载入默认卡池（4 角色模板 + 整备）。</summary>
    public static CardRegistry LoadDefaults()
    {
        var r = new CardRegistry();
        foreach (var tpl in CharacterTemplates.All())
        {
            foreach (var c in tpl.CardPool) r.Register(c);
            r.Register(new CardDef
            {
                Id = $"prep_{tpl.Id}", Name = tpl.Name + "·整备", Type = CardType.Prep,
                Cost = tpl.PrepTemplate.SlotCost, Effect = EffectKind.DrawCards, Magnitude = tpl.PrepTemplate.DrawCount,
            });
        }
        return r;
    }
}

/// <summary>
/// 卡牌构建器（自定义卡牌的便捷工厂，链式参数化）。
/// 例：new CardBuilder("my_atk", "裂空").Attack(DamageType.Slash, 7, cost:2).WithArt("res://art/my_atk.png").Build()
/// </summary>
public sealed class CardBuilder
{
    private readonly CardDef _d;
    public CardBuilder(string id, string name)
    {
        _d = new CardDef { Id = id, Name = name };
    }

    public CardBuilder Attack(DamageType type, int magnitude, int cost = 1)
    { _d.Type = CardType.Attack; _d.Effect = EffectKind.AttackDamage; _d.DamageType = type; _d.Magnitude = magnitude; _d.Cost = cost; return this; }

    public CardBuilder Shield(int amount, ShieldType type = ShieldType.Fixed, int hits = 1, int cost = 1)
    { _d.Type = CardType.Defense; _d.Effect = EffectKind.ApplyShield; _d.Magnitude = amount; _d.ShieldType = type; _d.ShieldHits = hits; _d.Cost = cost; return this; }

    public CardBuilder Enchant(EnchantmentType t, int mag, EnchantmentScope scope = EnchantmentScope.SpecificCard, int cost = 0)
    { _d.Type = CardType.Skill; _d.Effect = EffectKind.ApplyEnchantment; _d.EnchantType = t; _d.Magnitude = mag; _d.EnchantScope = scope; _d.Cost = cost; return this; }

    public CardBuilder Draw(int count, int cost = 1)
    { _d.Type = CardType.Prep; _d.Effect = EffectKind.DrawCards; _d.Magnitude = count; _d.Cost = cost; return this; }

    public CardBuilder WithArt(string? artPath) { _d.ArtPath = artPath; return this; }
    public CardBuilder WithDesc(string desc) { _d.Description = desc; return this; }

    /// <summary>挂卡牌独立逻辑（代码卡）。数据字段（Magnitude/Cost/类型）仍用于 UI/预览/EffectiveAttack。</summary>
    public CardBuilder WithCustomEffect(ICardEffect effect) { _d.CustomEffect = effect; return this; }

    /// <summary>代码卡需要玩家点选敌人目标。</summary>
    public CardBuilder NeedsTargetEnemy() { _d.NeedsTargetEnemy = true; return this; }

    /// <summary>稀有度（卡框边色）。</summary>
    public CardBuilder Rarity(Rarity r) { _d.Rarity = r; return this; }

    public CardDef Build() => _d;
}
