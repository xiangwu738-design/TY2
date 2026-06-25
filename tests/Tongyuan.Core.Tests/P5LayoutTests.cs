using Tongyuan.Core.Layout;
using Xunit;

namespace Tongyuan.Core.Tests;

public class P5LayoutTests
{
    // ---- P5 截图矩形检查：典型战场布局无重叠 ----
    [Theory]
    [InlineData(1, 1, 6, 3)]
    [InlineData(4, 2, 8, 5)]
    [InlineData(4, 3, 6, 7)]
    [InlineData(2, 1, 4, 0)]
    public void Layout_NoOverlaps_AndWithinViewport(int nChars, int nEnemies, int tlLen, int hand)
    {
        var l = BattleLayout.Compute(nChars, nEnemies, tlLen, hand, pointer: tlLen / 2);
        Assert.True(l.NoOverlaps(), "布局存在重叠矩形");
        Assert.True(l.AllWithinViewport(), "布局超出视口");
    }

    // ---- 角色与敌人分居两侧（不相邻）----
    [Fact]
    public void Layout_SidesAreSeparated()
    {
        var l = BattleLayout.Compute(4, 2, 6, 5, 3);
        foreach (var c in l.CharacterPortraits)
            foreach (var e in l.EnemyPortraits)
                Assert.True(c.X + c.W < e.X, "角色与敌人水平区间重叠");
    }

    // ---- 指针标记位于某个时间轴格上方 ----
    [Fact]
    public void Layout_PointerAboveACell()
    {
        var l = BattleLayout.Compute(2, 1, 6, 3, 2);
        bool above = l.TimelineCells.Any(cell =>
            l.PointerMarker.X >= cell.X - 1 && l.PointerMarker.X + l.PointerMarker.W <= cell.X + cell.W + 1 &&
            l.PointerMarker.Y < cell.Y);
        Assert.True(above, "指针未对齐时间轴格");
    }
}
