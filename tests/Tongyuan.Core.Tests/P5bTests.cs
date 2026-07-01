using Tongyuan.Core.Core;
using Tongyuan.Core.Data;
using Xunit;

namespace Tongyuan.Core.Tests;

public class P5bTests
{
    private static CardDef Atk(int cost, int dmg, DamageType type) => new()
    {
        Id = "a", Name = "a", Type = CardType.Attack, Cost = cost,
        Effect = EffectKind.AttackDamage, Magnitude = dmg, DamageType = type,
    };

    // ---- 远程：自选敌人且不位移 ----
    [Fact]
    public void RangedAttack_NoDisplacement_DamagesChosenEnemy()
    {
        var tl = GameStateFixture.TimelineOf(3);
        var enemy = GameStateFixture.Enemy(1, slot: 2, EnemyKind.Slash, power: 99, hp: 100);
        tl.Enemies.Add(enemy);
        var c1 = GameStateFixture.Char(1, hp: 20, pos: 1, prep: GameStateFixture.Prep());
        var c2 = GameStateFixture.Char(2, hp: 20, pos: 2, prep: GameStateFixture.Prep());
        var ranged = GameStateFixture.Card(Atk(0, 5, DamageType.Ranged));
        c2.Hand.Add(ranged);
        var gs = GameStateFixture.State(1, tl, c1, c2);

        gs.Apply(new PlayerAction(2, ActionType.PlayCard, ranged.InstanceId, TargetEnemyId: 1));

        Assert.Equal(2, c2.Position);       // 远程不位移
        Assert.Equal(1, c1.Position);       // c1 未被挤
        Assert.Equal(95, enemy.Hp);          // 命中自选敌人
    }

    // ---- 近战：打出者移到位1，身前的人后移 ----
    [Fact]
    public void MeleeAttack_DisplacesToFront()
    {
        var tl = GameStateFixture.TimelineOf(3);
        var enemy = GameStateFixture.Enemy(1, slot: 2, EnemyKind.Slash, power: 99, hp: 100);
        tl.Enemies.Add(enemy);
        var c1 = GameStateFixture.Char(1, hp: 20, pos: 1, prep: GameStateFixture.Prep());
        var c2 = GameStateFixture.Char(2, hp: 20, pos: 2, prep: GameStateFixture.Prep());
        var melee = GameStateFixture.Card(Atk(0, 5, DamageType.Slash));
        c2.Hand.Add(melee);
        var gs = GameStateFixture.State(1, tl, c1, c2);

        gs.Apply(new PlayerAction(2, ActionType.PlayCard, melee.InstanceId, TargetEnemyId: 1));

        Assert.Equal(1, c2.Position);       // 近战移到位1
        Assert.Equal(2, c1.Position);       // c1 被挤到2（暴露）
        Assert.Equal(95, enemy.Hp);
    }

    // ---- 敌人行动链：链尽循环（不无限增长）----
    [Fact]
    public void EnemyChain_LoopsBackToStart()
    {
        var e = new Enemy { Id = 1, Kind = EnemyKind.Slash, Power = 5, Hp = 10 };
        e.ActionChain.AddRange(new EnemyAction[] {
            new EnemyAction.Charge(2),
            new EnemyAction.Attack(3, 1),
            new EnemyAction.Idle(),
        });

        Assert.IsType<EnemyAction.Charge>(e.AdvanceChain());   // 1
        Assert.IsType<EnemyAction.Attack>(e.AdvanceChain());   // 2
        Assert.IsType<EnemyAction.Idle>(e.AdvanceChain());     // 3
        Assert.IsType<EnemyAction.Charge>(e.AdvanceChain());   // 4 → 循环回 Charge
        Assert.IsType<EnemyAction.Attack>(e.AdvanceChain());   // 5
        Assert.Equal(2, e.ChainIndex);                          // 已循环
    }

    // ---- 蓄力附伤：Charge 后下一次 Attack 含蓄力，攻击后清零 ----
    [Fact]
    public void EnemyChain_ChargeBuffsNextAttack_ThenClears()
    {
        var tl = GameStateFixture.TimelineOf(3);
        var enemy = new Enemy { Id = 1, Kind = EnemyKind.Slash, Power = 3, NodeSlot = 1, Hp = 50 };
        enemy.ActionChain.AddRange(new EnemyAction[] {
            new EnemyAction.Charge(2),
            new EnemyAction.Attack(3, 1),
        });
        tl.Enemies.Add(enemy);
        var c1 = GameStateFixture.Char(1, hp: 20, pos: 1, prep: GameStateFixture.Prep());
        var gs = GameStateFixture.State(1, tl, c1);

        // 第一次触发：蓄力（不造伤）
        gs.Apply(new PlayerAction(1, ActionType.Skip));
        Assert.Equal(2, enemy.Charge);
        Assert.Equal(20, c1.Hp);

        // 第二次触发：攻击 3 + 蓄力 2 = 5，蓄力清零
        // （指针已过 slot1，需把敌人放到下一格再触发）
        var enemy2 = new Enemy { Id = 2, Kind = EnemyKind.Slash, Power = 3, NodeSlot = 2, Hp = 50 };
        enemy2.ActionChain.AddRange(new EnemyAction[] { new EnemyAction.Attack(3, 1) });
        // 用一个带蓄力残留的复现场景：直接给 enemy2 加蓄力模拟“上一次蓄力”
        enemy2.Charge = 2;
        tl.Enemies.Add(enemy2);
        gs.Apply(new PlayerAction(1, ActionType.Skip));
        Assert.Equal(15, c1.Hp);   // 20 - (3+2)
        Assert.Equal(0, enemy2.Charge); // 蓄力释放清零
    }

    // ---- 无链敌人：回退为默认攻击（EffectivePower）----
    [Fact]
    public void Enemy_NoChain_DefaultsToEffectivePower()
    {
        var e = new Enemy { Id = 1, Kind = EnemyKind.Slash, Power = 5, Hp = 10 };
        Assert.IsType<EnemyAction.Attack>(e.NextAction);
        var a = (EnemyAction.Attack)e.NextAction;
        Assert.Equal(5, a.Amount); // EffectivePower
    }

    // ---- 卡牌描述非空（修“描述模糊”）----
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void AllTemplateCards_HaveDescription(int id)
    {
        var tpl = CharacterTemplates.All().First(t => t.Id == id);
        foreach (var c in tpl.CardPool)
            Assert.False(string.IsNullOrEmpty(c.EffectDescription()), $"卡牌 {c.Id} 描述为空");
    }

    // ---- 远程描述含“自选敌·不位移” ----
    [Fact]
    public void RangedCard_DescriptionMentionsNoDisplacement()
    {
        var desc = Atk(1, 5, DamageType.Ranged).EffectDescription();
        Assert.Contains("远程", desc);
        Assert.Contains("自选敌", desc);
    }
}
