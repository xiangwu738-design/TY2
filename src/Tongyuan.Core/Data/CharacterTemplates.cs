namespace Tongyuan.Core.Data;

using Tongyuan.Core.Core;

/// <summary>
/// 4 角色模板（规格 §4.10）：输出 / 布防 / 控制 / 治疗。
/// 专属卡池（各3张占位牌）、专属整备牌（模板：占2抽2不弃回手）、专属颜色。
/// 具体卡牌内容登记 §7，用户后填；此处用模板占位。
/// </summary>
public static class CharacterTemplates
{
    // 四色系统（文档 §五）：玩家1朱红/2湛蓝/3翠绿/4琥珀（ARGB int）
    public const int ColorDamage = unchecked((int)0xFFD4453A);   // 朱红
    public const int ColorDefense = unchecked((int)0xFF3D7BD4);  // 湛蓝
    public const int ColorControl = unchecked((int)0xFF3DA862);  // 翠绿
    public const int ColorSupport = unchecked((int)0xFFE0A82E);  // 琥珀

    public static CharacterTemplate Damage() => new()
    {
        Id = 1, Name = "输出", Archetype = RoleArchetype.Damage, Color = ColorDamage, BaseHp = 28,
        PrepTemplate = new PrepCardTemplate(),
        CardPool =
        {
            new CardDef { Id = "dmg_atk1", Name = "重击", Type = CardType.Attack, Cost = 2, Effect = EffectKind.AttackDamage, Magnitude = 8, DamageType = DamageType.Blunt },
            new CardDef { Id = "dmg_atk2", Name = "连斩", Type = CardType.Attack, Cost = 1, Effect = EffectKind.AttackDamage, Magnitude = 4, DamageType = DamageType.Slash },
            new CardDef { Id = "dmg_ranged", Name = "远射", Type = CardType.Attack, Cost = 1, Effect = EffectKind.AttackDamage, Magnitude = 5, DamageType = DamageType.Ranged },
            new CardDef { Id = "dmg_skill", Name = "蓄力", Type = CardType.Skill, Cost = 0, Effect = EffectKind.ApplyEnchantment, EnchantType = EnchantmentType.Power, EnchantScope = EnchantmentScope.SpecificCard, Magnitude = 2 },
        },
    };

    public static CharacterTemplate Defense() => new()
    {
        Id = 2, Name = "布防", Archetype = RoleArchetype.Defense, Color = ColorDefense, BaseHp = 36,
        PrepTemplate = new PrepCardTemplate(),
        CardPool =
        {
            new CardDef { Id = "def_shd1", Name = "坚壁", Type = CardType.Defense, Cost = 1, Effect = EffectKind.ApplyShield, Magnitude = 8, ShieldType = ShieldType.Fixed },
            new CardDef { Id = "def_shd2", Name = "屏障", Type = CardType.Defense, Cost = 1, Effect = EffectKind.ApplyShield, Magnitude = 5, ShieldType = ShieldType.Count, ShieldHits = 2 },
            new CardDef { Id = "def_atk", Name = "反击", Type = CardType.Attack, Cost = 2, Effect = EffectKind.AttackDamage, Magnitude = 5, DamageType = DamageType.Thrust },
        },
    };

    public static CharacterTemplate Control() => new()
    {
        Id = 3, Name = "控制", Archetype = RoleArchetype.Control, Color = ColorControl, BaseHp = 30,
        PrepTemplate = new PrepCardTemplate(),
        CardPool =
        {
            new CardDef { Id = "ctl_enc1", Name = "易伤", Type = CardType.Skill, Cost = 0, Effect = EffectKind.ApplyEnchantment, EnchantType = EnchantmentType.Vulnerable, EnchantScope = EnchantmentScope.SpecificCard, Magnitude = 2 },
            new CardDef { Id = "ctl_enc2", Name = "敌蓄力", Type = CardType.Skill, Cost = 1, Effect = EffectKind.ApplyEnchantment, EnchantType = EnchantmentType.Charge, Magnitude = 1 },
            new CardDef { Id = "ctl_atk", Name = "点穴", Type = CardType.Attack, Cost = 2, Effect = EffectKind.AttackDamage, Magnitude = 5, DamageType = DamageType.Thrust },
        },
    };

    public static CharacterTemplate Support() => new()
    {
        Id = 4, Name = "治疗", Archetype = RoleArchetype.Support, Color = ColorSupport, BaseHp = 32,
        PrepTemplate = new PrepCardTemplate(),
        CardPool =
        {
            new CardDef { Id = "sup_shd", Name = "守护", Type = CardType.Defense, Cost = 1, Effect = EffectKind.ApplyShield, Magnitude = 6, ShieldType = ShieldType.Fixed },
            new CardDef { Id = "sup_enc", Name = "授能", Type = CardType.Skill, Cost = 0, Effect = EffectKind.ApplyEnchantment, EnchantType = EnchantmentType.Power, EnchantScope = EnchantmentScope.AllInDiscard, Magnitude = 1 },
            new CardDef { Id = "sup_atk", Name = "惩戒", Type = CardType.Attack, Cost = 2, Effect = EffectKind.AttackDamage, Magnitude = 4, DamageType = DamageType.Ranged },
        },
    };

    public static IEnumerable<CharacterTemplate> All() =>
        new[] { Damage(), Defense(), Control(), Support() };

    /// <summary>由模板实例化为可参战角色（开局回满血，专属整备牌，初始手牌=整备+卡池洗入抽牌堆）。</summary>
    public static Character Instantiate(CharacterTemplate tpl, int position)
    {
        var prepDef = new CardDef
        {
            Id = $"prep_{tpl.Id}", Name = tpl.Name + "·整备", Type = CardType.Prep,
            Cost = tpl.PrepTemplate.SlotCost, Effect = EffectKind.DrawCards,
            Magnitude = tpl.PrepTemplate.DrawCount,
            Animation = CardAnimation.Item,
        };
        var c = new Character
        {
            Id = tpl.Id, Name = tpl.Name, Color = tpl.Color,
            Hp = tpl.BaseHp, MaxHp = tpl.BaseHp, Position = position,
            PortraitArt = tpl.Archetype switch
            {
                RoleArchetype.Damage => "res://art/rmmz_sv_portraits/sv_damage.png",
                RoleArchetype.Defense => "res://art/rmmz_sv_portraits/sv_defense.png",
                RoleArchetype.Control => "res://art/rmmz_sv_portraits/sv_control.png",
                RoleArchetype.Support => "res://art/rmmz_sv_portraits/sv_support.png",
                _ => null,
            },
            PortraitSheet = tpl.Archetype switch
            {
                RoleArchetype.Damage => "res://art/rmmz_sv_sheets/sv_damage_sheet.png",
                RoleArchetype.Defense => "res://art/rmmz_sv_sheets/sv_defense_sheet.png",
                RoleArchetype.Control => "res://art/rmmz_sv_sheets/sv_control_sheet.png",
                RoleArchetype.Support => "res://art/rmmz_sv_sheets/sv_support_sheet.png",
                _ => null,
            },
            PrepCard = new Card { Def = prepDef },
        };
        foreach (var def in tpl.CardPool)
        {
            ApplyDefaultAnimation(def);
            c.DrawPile.Add(new Card { Def = def });
        }
        return c;
    }

    private static void ApplyDefaultAnimation(CardDef def)
    {
        if (def.Animation != CardAnimation.Auto) return;
        def.Animation = def.Id switch
        {
            "dmg_atk1" => CardAnimation.Swing,
            "dmg_atk2" => CardAnimation.Swing,
            "dmg_ranged" => CardAnimation.Missile,
            "dmg_skill" => CardAnimation.Chant,
            "def_shd1" => CardAnimation.Guard,
            "def_shd2" => CardAnimation.Guard,
            "def_atk" => CardAnimation.Thrust,
            "ctl_enc1" => CardAnimation.Chant,
            "ctl_enc2" => CardAnimation.Spell,
            "ctl_atk" => CardAnimation.Thrust,
            "sup_shd" => CardAnimation.Guard,
            "sup_enc" => CardAnimation.Chant,
            "sup_atk" => CardAnimation.Missile,
            _ => AutoAnimation(def),
        };
    }

    private static CardAnimation AutoAnimation(CardDef def) => def.Effect switch
    {
        EffectKind.ApplyShield => CardAnimation.Guard,
        EffectKind.ApplyEnchantment => CardAnimation.Chant,
        EffectKind.DrawCards => CardAnimation.Item,
        EffectKind.AttackDamage => def.DamageType switch
        {
            DamageType.Thrust => CardAnimation.Thrust,
            DamageType.Ranged => CardAnimation.Missile,
            _ => CardAnimation.Swing,
        },
        _ => CardAnimation.Skill,
    };
}
