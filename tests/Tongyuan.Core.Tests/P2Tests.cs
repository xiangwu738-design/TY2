using Tongyuan.Core.Core;
using Xunit;

namespace Tongyuan.Core.Tests;

public class P2Tests
{
    // ---- 血量上限：战斗结算按受伤下调上限，回满血，清 debuff（§4.6）----
    [Fact]
    public void EndOfFight_ReducesMaxHp_AndRefills()
    {
        var c1 = GameStateFixture.Char(1, hp: 8, pos: 1, maxHp: 20, prep: GameStateFixture.Prep());
        c1.DamageTakenThisFight = 10; // 本场累计受伤 10

        HealthSystem.SettleEndOfFight(c1);

        // 默认按受伤 20% 下调：10 * 0.2 = 2 → MaxHp 20→18
        Assert.Equal(18, c1.MaxHp);
        Assert.Equal(18, c1.Hp);              // 回满当前血
        Assert.False(c1.IsDown);              // 本场 debuff 清除
        Assert.Equal(0, c1.DamageTakenThisFight);
    }

    // ---- 休息恢复血量上限（默认回 25% 基础上限，§4.6/§4.9）----
    [Fact]
    public void Rest_RestoresMaxHp_CappedAtBase()
    {
        var c1 = GameStateFixture.Char(1, hp: 10, pos: 1, maxHp: 14, prep: GameStateFixture.Prep());
        int baseMaxHp = 20;

        HealthSystem.Rest(c1, baseMaxHp); // 回 25% * 20 = 5 → 14+5=19

        Assert.Equal(19, c1.MaxHp);
        Assert.Equal(19, c1.Hp);

        HealthSystem.Rest(c1, baseMaxHp); // 再回 5 → 24 但 cap 在 20

        Assert.Equal(20, c1.MaxHp);       // 不超基础上限
    }

    // ---- 重伤阶梯占位：上限被压到 ≤25% 触发 ----
    [Fact]
    public void CriticalWound_WhenMaxHpLow()
    {
        var c1 = GameStateFixture.Char(1, hp: 5, pos: 1, maxHp: 4, prep: GameStateFixture.Prep());
        Assert.True(HealthSystem.IsCriticalWound(c1, baseMaxHp: 20));
    }

    // ---- 双端动作同步：主机执行 → 序列化广播 → 客户端重放，指针/状态一致（§4.11）----
    [Fact]
    public void ActionSync_HostAndClient_Converge()
    {
        var tl = GameStateFixture.TimelineOf(5);
        tl.Enemies.Add(GameStateFixture.Enemy(1, slot: 2, EnemyKind.Slash, power: 5, hp: 100));
        var atk = GameStateFixture.Card(GameStateFixture.Attack(cost: 2, damage: 10));
        var c1 = GameStateFixture.Char(1, hp: 20, pos: 1, prep: GameStateFixture.Prep());
        c1.Hand.Add(atk);
        var host = GameStateFixture.State(99, tl, c1);

        // 客户端从中途加入：拿到主机初始状态的克隆（同种子确定性重建的等价）
        var client = host.Clone();

        // 主机执行动作
        var action = new PlayerAction(1, ActionType.PlayCard, atk.InstanceId, TargetEnemyId: 1);
        host.Apply(action);

        // 广播：序列化 → 客户端反序列化重放
        var wire = ActionCodec.Serialize(action);
        client.Apply(ActionCodec.Deserialize(wire));

        // 两端指针与关键状态一致
        Assert.Equal(host.Pointer, client.Pointer);
        Assert.Equal(host.Characters[0].Hp, client.Characters[0].Hp);
        Assert.Equal(host.Enemies[0].Hp, client.Enemies[0].Hp);
    }

    // ---- 中途加入快照：seed + 历史动作重放追平主机（§4.11）----
    [Fact]
    public void MidJoin_SnapshotReplay_CatchesUpToHost()
    {
        var tl = GameStateFixture.TimelineOf(5);
        tl.Enemies.Add(GameStateFixture.Enemy(1, slot: 2, EnemyKind.Slash, power: 5, hp: 100));
        var atk = GameStateFixture.Card(GameStateFixture.Attack(cost: 2, damage: 10));
        var c1 = GameStateFixture.Char(1, hp: 20, pos: 1, prep: GameStateFixture.Prep());
        c1.Hand.Add(atk);
        var host = GameStateFixture.State(55, tl, c1);

        var a1 = new PlayerAction(1, ActionType.PlayCard, atk.InstanceId, TargetEnemyId: 1);
        host.Apply(a1);
        host.Apply(new PlayerAction(1, ActionType.Skip));

        // 新加入客户端：仅拿到 seed（重建需确定性 setup，此处用 host 初始 Clone 等价）+ 历史动作
        // 模拟 NetController.ReceiveSnapshot：seed + '#'-分隔的历史动作
        var sb = new System.Text.StringBuilder();
        sb.Append(host.Seed);
        foreach (var a in host.ActionHistory) sb.Append('#').Append(ActionCodec.Serialize(a));

        var parts = sb.ToString().Split('#');
        var fresh = new GameState(int.Parse(parts[0]));
        // 重建初始 setup（同种子确定性；此处复用 host 初始结构以聚焦重放正确性）
        fresh.Timeline = GameStateFixture.TimelineOf(5);
        fresh.Timeline.Enemies.Add(GameStateFixture.Enemy(1, slot: 2, EnemyKind.Slash, power: 5, hp: 100));
        var nc = GameStateFixture.Char(1, hp: 20, pos: 1, prep: GameStateFixture.Prep());
        nc.Hand.Add(atk); // 同一张牌实例（同 InstanceId）
        fresh.Characters.Add(nc);
        for (int i = 1; i < parts.Length; i++)
            if (!string.IsNullOrEmpty(parts[i])) fresh.Apply(ActionCodec.Deserialize(parts[i]));

        Assert.Equal(host.Pointer, fresh.Pointer);
        Assert.Equal(host.Characters[0].Hp, fresh.Characters[0].Hp);
    }
}
