namespace Tongyuan.Core.Core;

/// <summary>
/// 时间轴（规格 §4.1）。单层格子，指针逐格推进；占位=推进格数=时间成本。
/// 逐格结算：进格→①触发敌人节点→②检查持续态(护盾)→③若是占位终点则结算牌效果。
/// </summary>
public sealed class Timeline
{
    public List<NodeType> Nodes { get; init; } = new();
    public List<Enemy> Enemies { get; } = new();
    public int Pointer { get; set; }

    public int Length => Nodes.Count;
    public bool AtEnd => Pointer >= Length - 1;

    public Enemy? EnemyAt(int slot) => Enemies.Find(e => e.NodeSlot == slot);

    public List<Enemy> EnemiesAt(int slot) => Enemies.FindAll(e => e.NodeSlot == slot);

    public Timeline Clone()
    {
        var t = new Timeline { Pointer = Pointer };
        t.Nodes.AddRange(Nodes);
        foreach (var e in Enemies) t.Enemies.Add(e.Clone());
        return t;
    }
}
