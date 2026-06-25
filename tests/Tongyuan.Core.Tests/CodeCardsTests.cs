using Tongyuan.Core.Core;
using Tongyuan.Core.Data;
using Xunit;

namespace Tongyuan.Core.Tests;

public class CodeCardsTests
{
    // 用长时间轴 + 敌人放在 cost 推进范围之外，隔离卡牌效果（不被沿途敌人触发干扰）
    private static GameState Setup(out Character c, params Enemy[] enemies)
    {
        var tl = GameStateFixture.TimelineOf(10);
        foreach (var e in enemies) tl.Enemies.Add(e);
        c = GameStateFixture.Char(1, hp: 20, pos: 1, maxHp: 20, prep: GameStateFixture.Prep());
        return GameStateFixture.State(1, tl, c);
    }

    [Fact]
    public void LifestealSlash_DamagesAndHealsCaster()
    {
        var enemy = new Enemy { Id = 1, Kind = EnemyKind.Slash, Power = 99, NodeSlot = 5, Hp = 100, Position = 1 };
        var gs = Setup(out var c, enemy);
        var card = new Card { Def = CodeCards.All[0] }; // 吸血斩：6伤+吸血3
        c.Hand.Add(card);
        c.Hp = 10; // 受过伤，验证吸血
        gs.Apply(new PlayerAction(1, ActionType.PlayCard, card.InstanceId));
        Assert.Equal(94, enemy.Hp);   // 100-6
        Assert.Equal(13, c.Hp);       // 10+3 吸血
    }

    [Fact]
    public void SelfDestruct_HitsAllAndSelfDamage()
    {
        var e1 = new Enemy { Id = 1, Kind = EnemyKind.Slash, Power = 99, NodeSlot = 5, Hp = 100, Position = 1 };
        var e2 = new Enemy { Id = 2, Kind = EnemyKind.Thrust, Power = 99, NodeSlot = 6, Hp = 100, Position = 2 };
        var gs = Setup(out var c, e1, e2);
        var card = new Card { Def = CodeCards.All[1] }; // 玉碎：全体9伤，自伤5
        c.Hand.Add(card);
        gs.Apply(new PlayerAction(1, ActionType.PlayCard, card.InstanceId));
        Assert.Equal(91, e1.Hp);
        Assert.Equal(91, e2.Hp);
        Assert.Equal(15, c.Hp);       // 20-5
    }

    [Fact]
    public void PrecisionSnipe_HitsTargetAndPenetratesNext()
    {
        var e1 = new Enemy { Id = 1, Kind = EnemyKind.Slash, Power = 99, NodeSlot = 5, Hp = 100, Position = 1 };
        var e2 = new Enemy { Id = 2, Kind = EnemyKind.Thrust, Power = 99, NodeSlot = 6, Hp = 100, Position = 2 };
        var gs = Setup(out var c, e1, e2);
        var card = new Card { Def = CodeCards.All[2] }; // 精准狙击：8伤+贯穿4
        c.Hand.Add(card);
        gs.Apply(new PlayerAction(1, ActionType.PlayCard, card.InstanceId, TargetEnemyId: 1));
        Assert.Equal(92, e1.Hp);      // 100-8
        Assert.Equal(96, e2.Hp);      // 100-4（位2 贯穿）
        Assert.Equal(1, c.Position);  // 远程不位移
    }

    [Fact]
    public void CodeCard_PreviewRunsCustomLogic()
    {
        var enemy = new Enemy { Id = 1, Kind = EnemyKind.Slash, Power = 99, NodeSlot = 5, Hp = 100, Position = 1 };
        var gs = Setup(out var c, enemy);
        var card = new Card { Def = CodeCards.All[0] };
        c.Hand.Add(card);
        c.Hp = 10;
        var preview = gs.Preview(new PlayerAction(1, ActionType.PlayCard, card.InstanceId));
        Assert.Equal(10, c.Hp); // 本体未被预演改变
        Assert.Contains(preview, e => e is GameEvent.DamageDealt d && d.TargetIsEnemy && d.Amount == 6);
    }

    [Fact]
    public void CodeCards_RegisterAndUnique()
    {
        var reg = CardRegistry.LoadDefaults();
        CodeCards.RegisterInto(reg);
        var ids = CodeCards.All.Select(c => c.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        foreach (var c in CodeCards.All) Assert.NotNull(reg.Get(c.Id));
    }
}
