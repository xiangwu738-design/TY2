using Tongyuan.Core.Core;
using Xunit;

namespace Tongyuan.Core.Tests;

public class SmokeTest
{
    [Fact]
    public void GameState_Clone_ReturnsDistinctInstance()
    {
        var gs = new GameState { Seed = 42 };
        var copy = gs.Clone();
        Assert.NotSame(gs, copy); // 预演在独立克隆上跑，不改本体
        Assert.Equal(42, copy.Seed);
    }

    [Fact]
    public void Enemy_TargetPosition_MatchesKind()
    {
        var slash = new Enemy { Kind = EnemyKind.Slash };
        var thrust = new Enemy { Kind = EnemyKind.Thrust };
        var strike = new Enemy { Kind = EnemyKind.Strike };
        Assert.Equal(1, slash.TargetPosition);
        Assert.Equal(2, thrust.TargetPosition);
        Assert.Equal(-1, strike.TargetPosition); // 全体
    }
}
