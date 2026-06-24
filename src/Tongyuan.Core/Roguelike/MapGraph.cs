namespace Tongyuan.Core.Roguelike;

/// <summary>roguelike 地图节点类型（规格 §4.9），区别于 Core 时间轴 NodeType。</summary>
public enum MapNodeType
{
    Combat,
    Elite,
    Shop,
    Rest,
    Event,
    Boss,
}

/// <summary>地图节点（尖塔式分层选路）。层数/节点数用模板参数（§7）。</summary>
public sealed class MapNode
{
    public int Id { get; init; }
    public int Layer { get; init; }
    public MapNodeType Type { get; init; }
    public List<int> NextIds { get; } = new(); // 可选下一步
}

/// <summary>分层地图。层数默认 3 层 + 1 Boss（§7）。</summary>
public sealed class MapGraph
{
    public List<MapNode> Nodes { get; } = new();
    public int StartNodeId { get; set; }
    public int BossNodeId { get; set; }
}
