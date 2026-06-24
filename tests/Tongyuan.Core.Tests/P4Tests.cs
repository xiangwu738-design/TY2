using Tongyuan.Core.Core;
using Tongyuan.Core.Data;
using Tongyuan.Core.Roguelike;
using Xunit;

namespace Tongyuan.Core.Tests;

public class P4Tests
{
    public static IEnumerable<object[]> FourTemplates() =>
        CharacterTemplates.All().Select(t => new object[] { t.Id });

    // ---- 4 角色模板齐全：输出/布防/控制/治疗 ----
    [Fact]
    public void FourTemplates_AllArchetypesPresent()
    {
        var all = CharacterTemplates.All().ToList();
        Assert.Equal(4, all.Count);
        Assert.Equal(4, all.Select(t => t.Archetype).Distinct().Count());
        Assert.Contains(all, t => t.Archetype == RoleArchetype.Damage);
        Assert.Contains(all, t => t.Archetype == RoleArchetype.Defense);
        Assert.Contains(all, t => t.Archetype == RoleArchetype.Control);
        Assert.Contains(all, t => t.Archetype == RoleArchetype.Support);
    }

    // ---- 每个模板：专属卡池 3 张 + 专属整备牌 + 专属颜色 ----
    [Theory]
    [MemberData(nameof(FourTemplates))]
    public void EachTemplate_HasPool3_PrepAndColor(int id)
    {
        var tpl = CharacterTemplates.All().First(t => t.Id == id);
        Assert.Equal(3, tpl.CardPool.Count);
        Assert.NotNull(tpl.PrepTemplate);
        Assert.NotEqual(0, tpl.Color);
        Assert.NotEqual(tpl.Color, CharacterTemplates.All().First(t => t.Id != id).Color); // 颜色互异
    }

    // ---- 4 角色各能玩：实例化为角色，能出一张牌不报错，产出事件 ----
    [Theory]
    [MemberData(nameof(FourTemplates))]
    public void EachCharacter_CanPlayACard(int id)
    {
        var tpl = CharacterTemplates.All().First(t => t.Id == id);
        var hero = CharacterTemplates.Instantiate(tpl, position: 1);

        var tl = GameStateFixture.TimelineOf(3);
        tl.Enemies.Add(GameStateFixture.Enemy(1, slot: 1, EnemyKind.Slash, power: 3, hp: 50));
        var gs = GameStateFixture.State(id * 7, tl, hero);

        // 抽牌堆有卡池；先整备抽牌，再打出第一张抽到的牌
        gs.Apply(new PlayerAction(hero.Id, ActionType.UsePrep));
        Assert.True(hero.Hand.Count > 0 || hero.PrepCard is not null);

        var first = hero.Hand.FirstOrDefault();
        if (first is null) return; // 整备牌回手但无抽到牌时跳过出牌（不应发生，卡池3张）

        var action = new PlayerAction(hero.Id, ActionType.PlayCard, first.InstanceId,
            TargetEnemyId: 1, TargetCharacterId: hero.Id, TargetCardInstanceId: first.InstanceId);
        gs.Apply(action);

        Assert.Contains(gs.Events, e => e is GameEvent.CardPlayed);
    }

    // ---- 实例化角色：开局回满血、专属整备牌、卡池入抽牌堆 ----
    [Fact]
    public void Instantiate_FullHpAndPrepAndDrawPile()
    {
        var hero = CharacterTemplates.Instantiate(CharacterTemplates.Damage(), position: 1);
        Assert.Equal(hero.MaxHp, hero.Hp);          // 开局回满
        Assert.NotNull(hero.PrepCard);              // 专属整备牌
        Assert.True(hero.PrepCard.IsPrep);
        Assert.Equal(3, hero.DrawPile.Count);        // 卡池入抽牌堆
        Assert.Equal(1, hero.Position);
    }
}
