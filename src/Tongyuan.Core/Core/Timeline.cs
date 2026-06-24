namespace Tongyuan.Core.Core;

/// <summary>
/// 时间轴（规格 §4.1）。单层格子，指针逐格推进；占位=推进格数=时间成本。
/// 逐格结算：进格→①触发敌人节点→②检查持续态(护盾)→③若是占位终点则结算牌效果。
/// </summary>
public sealed class Timeline
{
    public List<NodeType> Nodes { get; init; } = new();
    public List<Enemy> Enemies { get; } = new(); // 节点上的敌人
    public int Pointer { get; set; }

    public int Length => Nodes.Count;

    /// <summary>推进 n 格（占位消耗）。返回沿途触发的敌人节点。</summary>
    public List<Enemy> Advance(int n)
    {
        var hit = new List<Enemy>();
        for (int i = 0; i < n && Pointer < Length - 1; i++)
        {
            Pointer++;
            var e = Enemies.Find(x => x.NodeSlot == Pointer);
            if (e is not null) hit.Add(e);
        }
        return hit;
    }
}
