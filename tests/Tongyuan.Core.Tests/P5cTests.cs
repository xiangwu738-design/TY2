using Tongyuan.Core.Core;
using Xunit;

namespace Tongyuan.Core.Tests;

public class P5cTests
{
    private static CardDef Atk(DamageType t, int dmg = 5) => new()
    {
        Id = "a", Name = t.ToString(), Type = CardType.Attack, Cost = 0,
        Effect = EffectKind.AttackDamage, Magnitude = dmg, DamageType = t,
    };

    private static GameState ThreeEnemies(out Enemy e1, out Enemy e2, out Enemy e3)
    {
        var tl = GameStateFixture.TimelineOf(3);
        e1 = new Enemy { Id = 1, Kind = EnemyKind.Slash, Power = 99, NodeSlot = 1, Hp = 100, Position = 1 };
        e2 = new Enemy { Id = 2, Kind = EnemyKind.Thrust, Power = 99, NodeSlot = 1, Hp = 100, Position = 2 };
        e3 = new Enemy { Id = 3, Kind = EnemyKind.Strike, Power = 99, NodeSlot = 1, Hp = 100, Position = 3 };
        tl.Enemies.Add(e1); tl.Enemies.Add(e2); tl.Enemies.Add(e3);
        var c = GameStateFixture.Char(1, hp: 50, pos: 1, prep: GameStateFixture.Prep());
        return GameStateFixture.State(1, tl, c);
    }

    // 斩击 → 位1
    [Fact]
    public void Slash_HitsPosition1Only()
    {
        var gs = ThreeEnemies(out var e1, out var e2, out var e3);
        var card = GameStateFixture.Card(Atk(DamageType.Slash));
        gs.Characters[0].Hand.Add(card);
        gs.Apply(new PlayerAction(1, ActionType.PlayCard, card.InstanceId));
        Assert.Equal(95, e1.Hp);
        Assert.Equal(100, e2.Hp);
        Assert.Equal(100, e3.Hp);
    }

    // 穿刺 → 位2
    [Fact]
    public void Thrust_HitsPosition2Only()
    {
        var gs = ThreeEnemies(out var e1, out var e2, out var e3);
        var card = GameStateFixture.Card(Atk(DamageType.Thrust));
        gs.Characters[0].Hand.Add(card);
        gs.Apply(new PlayerAction(1, ActionType.PlayCard, card.InstanceId));
        Assert.Equal(100, e1.Hp);
        Assert.Equal(95, e2.Hp);
        Assert.Equal(100, e3.Hp);
    }

    // 打击 → 全体
    [Fact]
    public void Blunt_HitsAllEnemies()
    {
        var gs = ThreeEnemies(out var e1, out var e2, out var e3);
        var card = GameStateFixture.Card(Atk(DamageType.Blunt));
        gs.Characters[0].Hand.Add(card);
        gs.Apply(new PlayerAction(1, ActionType.PlayCard, card.InstanceId));
        Assert.Equal(95, e1.Hp);
        Assert.Equal(95, e2.Hp);
        Assert.Equal(95, e3.Hp);
    }

    // 穿刺：仅 1 敌时改打前排（位2 无人→位1，不落空）
    [Fact]
    public void Thrust_HitsFrontWhenSingleEnemy()
    {
        var tl = GameStateFixture.TimelineOf(3);
        var e1 = new Enemy { Id = 1, Kind = EnemyKind.Slash, Power = 99, NodeSlot = 1, Hp = 100, Position = 1 };
        tl.Enemies.Add(e1);
        var c = GameStateFixture.Char(1, hp: 50, pos: 1, prep: GameStateFixture.Prep());
        var gs = GameStateFixture.State(1, tl, c);
        var card = GameStateFixture.Card(Atk(DamageType.Thrust));
        c.Hand.Add(card);
        gs.Apply(new PlayerAction(1, ActionType.PlayCard, card.InstanceId));
        Assert.Equal(95, e1.Hp); // 位2无人→打前排，受 5 伤
    }

    // 敌人阵亡收缩：保持 1..M 连续
    [Fact]
    public void EnemyDeath_ContractsPositions()
    {
        var gs = ThreeEnemies(out var e1, out var e2, out var e3);
        e1.Hp = 1;
        var card = GameStateFixture.Card(Atk(DamageType.Slash, dmg: 5)); // 斩位1，击杀 e1
        gs.Characters[0].Hand.Add(card);
        gs.Apply(new PlayerAction(1, ActionType.PlayCard, card.InstanceId));
        Assert.False(e1.IsAlive);
        Assert.Equal(1, e2.Position); // 补位
        Assert.Equal(2, e3.Position);
    }

    // 远程：自选敌人且不位移
    [Fact]
    public void Ranged_HitsChosenAndNoDisplacement()
    {
        var gs = ThreeEnemies(out _, out _, out var e3);
        var c = gs.Characters[0];
        c.Position = 2; // 让位移可被察觉
        gs.Characters.Add(GameStateFixture.Char(9, hp: 50, pos: 1, prep: GameStateFixture.Prep()));
        var card = GameStateFixture.Card(Atk(DamageType.Ranged));
        c.Hand.Add(card);
        gs.Apply(new PlayerAction(1, ActionType.PlayCard, card.InstanceId, TargetEnemyId: 3));
        Assert.Equal(95, e3.Hp);   // 命中自选的 e3
        Assert.Equal(2, c.Position); // 远程不位移
    }
}
