using Tongyuan.Core.Core;
using Tongyuan.Core.Roguelike;
using Xunit;

namespace Tongyuan.Core.Tests;

public class P3Tests
{
    private static CardDef Card(string id) => new() { Id = id, Name = id, Type = CardType.Attack, Cost = 1, Effect = EffectKind.AttackDamage, Magnitude = 5 };

    // ---- 一局跑通：地图→战斗→加牌→商店→休息→Boss 胜利 ----
    [Fact]
    public void FullRun_FlowCompletes_Victory()
    {
        var map = MapGenerator.Generate(layers: 3, seed: 1);
        var run = new RunState { Map = map, Gold = 200 };
        var rc = new RunController(run);
        rc.Start();

        // 节点0：战斗
        Assert.Equal(MapNodeType.Combat, rc.CurrentType);
        rc.WinCombat(Card("reward1"), goldReward: 20);
        Assert.Equal(1, run.Deck.Count);
        Assert.Equal(220, run.Gold); // 初始 200 + 奖励 20

        Assert.True(rc.Advance());
        // 节点1：商店 → 买牌
        Assert.Equal(MapNodeType.Shop, rc.CurrentType);
        Assert.True(rc.ShopBuyItem(RunController.ShopBuy.Card, Card("shop1")));
        Assert.Equal(2, run.Deck.Count);
        Assert.Equal(220 - Shop.PriceCard, run.Gold); // -50

        Assert.True(rc.Advance());
        // 节点2：休息 → 恢复上限
        Assert.Equal(MapNodeType.Rest, rc.CurrentType);
        var hero = GameStateFixture.Char(1, hp: 10, pos: 1, maxHp: 12, prep: GameStateFixture.Prep());
        rc.Rest(hero, baseMaxHp: 20); // 回 25%*20=5 → 12+5=17
        Assert.Equal(17, hero.MaxHp);

        Assert.True(rc.Advance());
        // 节点3：Boss → 击败 → 胜利
        Assert.Equal(MapNodeType.Boss, rc.CurrentType);
        rc.WinCombat(Card("boss_reward"));
        Assert.False(rc.Advance());           // Boss 后无下一节点
        Assert.True(run.RunOver);
        Assert.True(run.Victory);
    }

    // ---- 战后加牌受卡组上限保护 ----
    [Fact]
    public void AddCard_RespectsDeckMax()
    {
        var run = new RunState { Map = MapGenerator.Generate(3, 1), Gold = 0 };
        for (int i = 0; i < RunState.DeckMax; i++) run.Deck.Add(Card("c" + i));
        var rc = new RunController(run);
        rc.WinCombat(Card("overflow"));       // 已满，不加
        Assert.Equal(RunState.DeckMax, run.Deck.Count);
    }

    // ---- 移牌受卡组下限保护 ----
    [Fact]
    public void RemoveCard_RespectsDeckMin()
    {
        var run = new RunState { Map = MapGenerator.Generate(3, 1), Gold = 0 };
        for (int i = 0; i < RunState.DeckMin; i++) run.Deck.Add(Card("c" + i));
        var rc = new RunController(run);
        Assert.False(rc.RemoveCard("c0"));    // 恰好下限，不允许移
        run.Deck.Add(Card("extra"));
        Assert.True(rc.RemoveCard("extra"));  // 超下限可移
    }

    // ---- 地图：3 层 + 1 Boss，起点可达 Boss ----
    [Fact]
    public void Map_HasLayersAndBoss_Reachable()
    {
        var g = MapGenerator.Generate(3, 42);
        Assert.Equal(MapNodeType.Boss, g.Nodes.Find(n => n.Id == g.BossNodeId)!.Type);
        // 顺着 NextIds 能从起点走到 Boss
        int cur = g.StartNodeId;
        int steps = 0;
        while (cur != g.BossNodeId && steps++ < 20)
            cur = g.Nodes.Find(n => n.Id == cur)!.NextIds[0];
        Assert.Equal(g.BossNodeId, cur);
    }

    // ---- 商店：金币不足买不了 ----
    [Fact]
    public void Shop_CannotAfford_ReturnsFalse()
    {
        var run = new RunState { Map = MapGenerator.Generate(3, 1), Gold = 10 };
        var rc = new RunController(run);
        Assert.False(rc.ShopBuyItem(RunController.ShopBuy.Relic)); // 需 150
        Assert.Equal(10, run.Gold); // 不扣
    }
}
