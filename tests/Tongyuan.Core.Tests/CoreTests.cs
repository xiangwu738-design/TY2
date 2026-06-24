using Tongyuan.Core.Core;
using Xunit;

namespace Tongyuan.Core.Tests;

public class CoreTests
{
    // ---- 逐格结算：占位推进沿途先挨打，终点才结算牌效果 ----
    [Fact]
    public void PerCellSettlement_EnemyHitsBeforeCardEffect()
    {
        var tl = GameStateFixture.TimelineOf(5);
        var enemy = GameStateFixture.Enemy(1, slot: 2, EnemyKind.Slash, power: 5, hp: 100);
        tl.Enemies.Add(enemy);
        var atk = GameStateFixture.Card(GameStateFixture.Attack(cost: 2, damage: 10));
        var c1 = GameStateFixture.Char(1, hp: 20, pos: 1, prep: GameStateFixture.Prep());
        c1.Hand.Add(atk);
        var gs = GameStateFixture.State(42, tl, c1);

        gs.Apply(new PlayerAction(1, ActionType.PlayCard, atk.InstanceId, TargetEnemyId: 1));

        Assert.Equal(2, gs.Pointer);                 // 推进 2 格
        Assert.Equal(15, c1.Hp);                      // 沿途被敌人打 5
        Assert.Equal(90, enemy.Hp);                   // 终点牌效果打敌 10
        Assert.Contains(gs.Events, e => e is GameEvent.EnemyTriggered);
        Assert.Contains(gs.Events, e => e is GameEvent.DamageDealt d && d.TargetIsEnemy);
    }

    // ---- 整备牌回手 + 抽牌 ----
    [Fact]
    public void PrepCard_ReturnsToHandAndDraws()
    {
        var tl = GameStateFixture.TimelineOf(3);
        var c1 = GameStateFixture.Char(1, hp: 20, pos: 1, prep: GameStateFixture.Prep(cost: 1, draw: 2));
        c1.DrawPile.Add(GameStateFixture.Card(GameStateFixture.Attack(1, 5)));
        c1.DrawPile.Add(GameStateFixture.Card(GameStateFixture.Attack(1, 5)));
        var gs = GameStateFixture.State(7, tl, c1);

        gs.Apply(new PlayerAction(1, ActionType.UsePrep));

        Assert.Equal(1, gs.Pointer);                  // 占位推进 1
        Assert.NotNull(c1.PrepCard);                  // 整备牌仍在手
        Assert.Equal(2, c1.Hand.Count);               // 抽了 2
        Assert.Equal(0, c1.DrawPile.Count);
        Assert.Contains(gs.Events, e => e is GameEvent.PrepUsed);
    }

    // ---- 附魔挂具体牌实例生效（力量 +3 → 攻击牌伤害提升）----
    [Fact]
    public void Enchantment_OnSpecificCard_BoostsAttack()
    {
        var tl = GameStateFixture.TimelineOf(3);
        var atk = GameStateFixture.Card(GameStateFixture.Attack(cost: 1, damage: 10));
        var enc = GameStateFixture.Card(GameStateFixture.Enchant(cost: 0, EnchantmentType.Power, mag: 3));
        var c1 = GameStateFixture.Char(1, hp: 20, pos: 1, prep: GameStateFixture.Prep());
        c1.Hand.Add(atk);
        c1.Hand.Add(enc);
        var gs = GameStateFixture.State(1, tl, c1);

        int before = atk.EffectiveAttack;
        gs.Apply(new PlayerAction(1, ActionType.PlayCard, enc.InstanceId, TargetCardInstanceId: atk.InstanceId));

        Assert.Equal(10, before);
        Assert.Equal(13, atk.EffectiveAttack);        // 力量附魔 +3
        Assert.Contains(gs.Events, e => e is GameEvent.EnchantmentApplied);
    }

    // ---- 护盾吸收（固定吸收量，挡到耗尽）----
    [Fact]
    public void Shield_FixedAbsorb_ReducesDamage()
    {
        var tl = GameStateFixture.TimelineOf(3);
        var enemy = GameStateFixture.Enemy(1, slot: 1, EnemyKind.Slash, power: 10);
        tl.Enemies.Add(enemy);
        var shd = GameStateFixture.Card(GameStateFixture.Shield(cost: 0, amount: 8, ShieldType.Fixed));
        var c1 = GameStateFixture.Char(1, hp: 20, pos: 1, prep: GameStateFixture.Prep());
        c1.Hand.Add(shd);
        var gs = GameStateFixture.State(1, tl, c1);

        gs.Apply(new PlayerAction(1, ActionType.PlayCard, shd.InstanceId, TargetCharacterId: 1)); // 铺盾
        Assert.Single(gs.Shields);
        gs.Apply(new PlayerAction(1, ActionType.Skip));                                // 推进触发敌人

        Assert.Equal(18, c1.Hp);                      // 10 - 8 吸收 = 2 实伤
        Assert.Empty(gs.Shields);                     // 耗尽移除
    }

    // ---- 阵亡收缩：死人后身后的人向前补位，保持 1..N 连续 ----
    [Fact]
    public void DeathContraction_BackfillPositions()
    {
        var tl = GameStateFixture.TimelineOf(3);
        var enemy = GameStateFixture.Enemy(1, slot: 1, EnemyKind.Slash, power: 10);
        tl.Enemies.Add(enemy);
        var c1 = GameStateFixture.Char(1, hp: 5, pos: 1, prep: GameStateFixture.Prep());
        var c2 = GameStateFixture.Char(2, hp: 20, pos: 2, prep: GameStateFixture.Prep());
        var gs = GameStateFixture.State(1, tl, c1, c2);

        gs.Apply(new PlayerAction(1, ActionType.Skip)); // 推进，位1的 c1 被击杀

        Assert.False(c1.IsAlive);
        Assert.Equal(1, c2.Position);                 // 向前补位
        Assert.Equal(20, c2.Hp);
        Assert.Contains(gs.Events, e => e is GameEvent.CharacterDied);
    }

    // ---- 确定性预演：Preview 不改本体；同状态同动作结果一致 ----
    [Fact]
    public void Preview_DoesNotMutate_AndIsDeterministic()
    {
        var tl = GameStateFixture.TimelineOf(5);
        var enemy = GameStateFixture.Enemy(1, slot: 2, EnemyKind.Slash, power: 5, hp: 100);
        tl.Enemies.Add(enemy);
        var atk = GameStateFixture.Card(GameStateFixture.Attack(cost: 2, damage: 10));
        var c1 = GameStateFixture.Char(1, hp: 20, pos: 1, prep: GameStateFixture.Prep());
        c1.Hand.Add(atk);
        var gs = GameStateFixture.State(99, tl, c1);

        var action = new PlayerAction(1, ActionType.PlayCard, atk.InstanceId, TargetEnemyId: 1);
        var preview = gs.Preview(action);

        Assert.Equal(20, c1.Hp);                       // 本体未变
        Assert.Equal(0, gs.Pointer);
        Assert.NotEmpty(preview);

        // 两个独立克隆，同动作 → 同结果
        var a = gs.Clone(); var b = gs.Clone();
        a.Apply(action); b.Apply(action);
        Assert.Equal(a.Characters[0].Hp, b.Characters[0].Hp);
        Assert.Equal(a.Enemies[0].Hp, b.Enemies[0].Hp);
    }

    // ---- 抽牌堆空→弃牌堆带种子洗牌回抽牌堆 ----
    [Fact]
    public void DrawPile_ReshufflesDiscard_WhenEmpty()
    {
        var tl = GameStateFixture.TimelineOf(3);
        var c1 = GameStateFixture.Char(1, hp: 20, pos: 1, prep: GameStateFixture.Prep(cost: 1, draw: 3));
        var discarded = GameStateFixture.Card(GameStateFixture.Attack(1, 5));
        c1.DiscardPile.Add(discarded);
        c1.DiscardPile.Add(GameStateFixture.Card(GameStateFixture.Attack(1, 5)));
        c1.DiscardPile.Add(GameStateFixture.Card(GameStateFixture.Attack(1, 5)));
        var gs = GameStateFixture.State(123, tl, c1);

        gs.Apply(new PlayerAction(1, ActionType.UsePrep)); // 抽3，抽牌堆空→洗弃牌堆

        Assert.Equal(3, c1.Hand.Count);
        Assert.Empty(c1.DiscardPile);                  // 弃牌堆整叠移走
    }

    // ---- 突=位2，N<2 时落空 ----
    [Fact]
    public void ThrustEnemy_MissesWhenSingleCharacter()
    {
        var tl = GameStateFixture.TimelineOf(3);
        var enemy = GameStateFixture.Enemy(1, slot: 1, EnemyKind.Thrust, power: 10); // 打位2
        tl.Enemies.Add(enemy);
        var c1 = GameStateFixture.Char(1, hp: 20, pos: 1, prep: GameStateFixture.Prep()); // 仅1人
        var gs = GameStateFixture.State(1, tl, c1);

        gs.Apply(new PlayerAction(1, ActionType.Skip));

        Assert.Equal(20, c1.Hp);                       // 位2无人，落空，不受伤
    }
}
