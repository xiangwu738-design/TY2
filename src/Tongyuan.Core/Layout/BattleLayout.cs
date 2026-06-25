namespace Tongyuan.Core.Layout;

/// <summary>
/// 战场布局（纯 C#，可单测）。规格 §4.8：side-view 回合制——角色一侧、敌人另一侧、时间轴+牌区在下。
/// 占位美术：色块/简单帧；布局确定性、无重叠（P5 自验：截图矩形检查）。
/// </summary>
public sealed record RectF(float X, float Y, float W, float H);

public sealed class BattleLayout
{
    public const int ViewW = 1280;
    public const int ViewH = 820;

    public const float PortraitW = 64;
    public const float PortraitH = 128;
    public const float PortraitGap = 16;
    public const float SideMargin = 16;

    public const float CardW = 80;
    public const float CardH = 110;
    public const float CardGap = 8;

    public const float CellW = 44;
    public const float CellH = 44;
    public const float CellGap = 4;

    public List<RectF> CharacterPortraits { get; } = new();
    public List<RectF> EnemyPortraits { get; } = new();
    public List<RectF> TimelineCells { get; } = new();
    public RectF PointerMarker { get; private set; }
    public List<RectF> HandCards { get; } = new();

    public static BattleLayout Compute(int nChars, int nEnemies, int timelineLen, int handCount, int pointer = 0)
    {
        var l = new BattleLayout();

        // 角色一侧（左）
        float chTotal = nChars * PortraitH + Math.Max(0, nChars - 1) * PortraitGap;
        float chStartY = (ViewH - chTotal) / 2f;
        for (int i = 0; i < nChars; i++)
            l.CharacterPortraits.Add(new RectF(SideMargin, chStartY + i * (PortraitH + PortraitGap), PortraitW, PortraitH));

        // 敌人一侧（右）
        float enTotal = nEnemies * PortraitH + Math.Max(0, nEnemies - 1) * PortraitGap;
        float enStartY = (ViewH - enTotal) / 2f;
        for (int i = 0; i < nEnemies; i++)
            l.EnemyPortraits.Add(new RectF(ViewW - SideMargin - PortraitW, enStartY + i * (PortraitH + PortraitGap), PortraitW, PortraitH));

        // 时间轴（下中部）
        float tlTotal = timelineLen * CellW + Math.Max(0, timelineLen - 1) * CellGap;
        float tlStartX = (ViewW - tlTotal) / 2f;
        float tlY = ViewH - 60;
        for (int i = 0; i < timelineLen; i++)
            l.TimelineCells.Add(new RectF(tlStartX + i * (CellW + CellGap), tlY, CellW, CellH));
        if (timelineLen > 0)
        {
            int p = Math.Clamp(pointer, 0, timelineLen - 1);
            l.PointerMarker = new RectF(tlStartX + p * (CellW + CellGap) + CellW / 2f - 4, tlY - 10, 8, 8);
        }

        // 手牌（下中部，时间轴上方）
        float handTotal = handCount * CardW + Math.Max(0, handCount - 1) * CardGap;
        float handStartX = (ViewW - handTotal) / 2f;
        float handY = ViewH - 200;
        for (int i = 0; i < handCount; i++)
            l.HandCards.Add(new RectF(handStartX + i * (CardW + CardGap), handY, CardW, CardH));

        return l;
    }

    /// <summary>所有矩形两两不重叠（P5 自验：无重叠）。</summary>
    public bool NoOverlaps()
    {
        var all = AllRects().ToList();
        for (int i = 0; i < all.Count; i++)
            for (int j = i + 1; j < all.Count; j++)
                if (Overlaps(all[i], all[j])) return false;
        return true;
    }

    /// <summary>所有矩形在视口内。</summary>
    public bool AllWithinViewport()
    {
        foreach (var r in AllRects())
            if (r.X < 0 || r.Y < 0 || r.X + r.W > ViewW || r.Y + r.H > ViewH) return false;
        return true;
    }

    public IEnumerable<RectF> AllRects()
    {
        foreach (var r in CharacterPortraits) yield return r;
        foreach (var r in EnemyPortraits) yield return r;
        foreach (var r in TimelineCells) yield return r;
        if (TimelineCells.Count > 0) yield return PointerMarker;
        foreach (var r in HandCards) yield return r;
    }

    public static bool Overlaps(RectF a, RectF b)
    {
        if (a.W <= 0 || a.H <= 0 || b.W <= 0 || b.H <= 0) return false;
        return !(a.X + a.W <= b.X || b.X + b.W <= a.X || a.Y + a.H <= b.Y || b.Y + b.H <= a.Y);
    }
}
