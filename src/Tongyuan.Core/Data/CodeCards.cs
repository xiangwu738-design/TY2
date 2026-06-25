namespace Tongyuan.Core.Data;

using Tongyuan.Core.Core;

/// <summary>
/// 示例代码卡（卡牌独立逻辑，规格 §6 可扩展）。用 ICardEffect 实现纯数据表达不了的逻辑。
/// 数据字段（Magnitude/Cost/DamageType）仍用于 UI 显示与 EffectiveAttack；结算走 CustomEffect。
/// </summary>
public static class CodeCards
{
    // 吸血斩：斩击位1 + 回血（近战位移）
    public sealed class LifestealSlashEffect : ICardEffect
    {
        public int Heal { get; init; } = 3;
        public void Apply(GameState gs, Character caster, Card card, PlayerAction? action)
        {
            gs.MoveToFront(caster);                          // 近战位移
            var enemy = gs.EnemyAtPosition(1);                // 斩击位1
            if (enemy is null) return;
            gs.DamageEnemy(enemy, card.EffectiveAttack);      // 含力量附魔
            if (caster.IsAlive) gs.HealCharacter(caster, Heal); // 吸血
        }
    }

    // 玉碎：对全体敌人造成大量伤害，自身受反伤
    public sealed class SelfDestructEffect : ICardEffect
    {
        public int SelfDamage { get; init; } = 5;
        public void Apply(GameState gs, Character caster, Card card, PlayerAction? action)
        {
            int dmg = card.EffectiveAttack;
            foreach (var e in gs.Enemies.Where(e => e.IsAlive).ToList())
                gs.DamageEnemy(e, dmg);
            // 自身反伤（不位移）
            if (caster.IsAlive)
            {
                caster.Hp -= SelfDamage;
                caster.DamageTakenThisFight += SelfDamage;
                gs.Emit(new GameEvent.DamageDealt(caster.Id, false, SelfDamage));
            }
        }
    }

    // 精准狙击：远程自选敌，对该敌 + 其后一位的敌各造成伤害（不位移）
    public sealed class PrecisionSnipeEffect : ICardEffect
    {
        public void Apply(GameState gs, Character caster, Card card, PlayerAction? action)
        {
            if (action?.TargetEnemyId is not int eid) return;
            var first = gs.Enemies.FirstOrDefault(e => e.Id == eid);
            if (first is null || !first.IsAlive) return;
            gs.DamageEnemy(first, card.EffectiveAttack);
            // 贯穿：同位置+1 的敌也挨一半
            var second = gs.EnemyAtPosition(first.Position + 1);
            if (second is not null && second.IsAlive)
                gs.DamageEnemy(second, card.EffectiveAttack / 2);
        }
    }

    public static readonly CardDef[] All =
    {
        new CardBuilder("code_lifesteal", "吸血斩")
            .Attack(DamageType.Slash, 6, cost: 2)
            .WithDesc("斩击位1 6伤，自身吸血+3（代码卡）")
            .WithCustomEffect(new LifestealSlashEffect { Heal = 3 })
            .Build(),

        new CardBuilder("code_selfdestruct", "玉碎")
            .Attack(DamageType.Blunt, 9, cost: 3)
            .WithDesc("全体9伤，自身受5反伤（代码卡）")
            .WithCustomEffect(new SelfDestructEffect { SelfDamage = 5 })
            .Rarity(Rarity.Rare)
            .Build(),

        new CardBuilder("code_snipe", "精准狙击")
            .Attack(DamageType.Ranged, 8, cost: 2)
            .NeedsTargetEnemy()
            .WithDesc("远程自选敌8伤，贯穿其后一位4伤（代码卡）")
            .WithCustomEffect(new PrecisionSnipeEffect())
            .Build(),
    };

    public static void RegisterInto(CardRegistry registry)
    {
        foreach (var c in All) registry.Register(c);
    }
}
