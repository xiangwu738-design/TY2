namespace Tongyuan.Core.Roguelike;

using Tongyuan.Core.Core;

/// <summary>
/// roguelike 一局运行状态（规格 §4.9）。串联 地图→战斗→加牌→商店→休息→Boss。
/// P3：跑通单局流程；地图层数默认 3 + 1 Boss（§7）。
/// </summary>
public sealed class RunState
{
    public MapGraph Map { get; init; } = new();
    public int CurrentNodeId { get; set; }
    public int Gold { get; set; }
    public List<Relic> Relics { get; } = new();
    /// <summary>玩家牌组（跨战斗携带，构筑主轴=占位/时间，规模 12-18，§7）。</summary>
    public List<CardDef> Deck { get; } = new();
    public int BattlesWon { get; set; }
    public bool RunOver { get; set; }
    public bool Victory { get; set; }

    public const int MapLayers = 3;
    public const int DeckMin = 12;
    public const int DeckMax = 18;
}

/// <summary>地图生成（尖塔式分层选路，占位）。层数默认 3 + 1 Boss（§7）。</summary>
public static class MapGenerator
{
    public static MapGraph Generate(int layers, int seed)
    {
        var rng = new Core.DeterministicRng(seed);
        var g = new MapGraph();
        int id = 0;
        var prev = new List<int>();
        // 每层一个占位节点（P3 简化：线性+分支占位）
        var types = new[]
        {
            MapNodeType.Combat, MapNodeType.Shop, MapNodeType.Rest,
            MapNodeType.Event, MapNodeType.Elite, MapNodeType.Combat,
        };
        for (int layer = 0; layer < layers; layer++)
        {
            int nid = id++;
            var node = new MapNode { Id = nid, Layer = layer, Type = types[layer % types.Length] };
            foreach (var p in prev) g.Nodes.Find(n => n.Id == p)!.NextIds.Add(nid);
            g.Nodes.Add(node);
            prev.Clear(); prev.Add(nid);
        }
        // Boss 节点
        int bossId = id++;
        var boss = new MapNode { Id = bossId, Layer = layers, Type = MapNodeType.Boss };
        foreach (var p in prev) g.Nodes.Find(n => n.Id == p)!.NextIds.Add(bossId);
        g.Nodes.Add(boss);
        g.StartNodeId = g.Nodes[0].Id;
        g.BossNodeId = bossId;
        _ = rng; // 预留：未来分支/随机节点用
        return g;
    }
}

/// <summary>遗物注册表（数据驱动，留注册接口，规格 §4.9）。</summary>
public sealed class RelicRegistry : IRelicRegistry
{
    private readonly Dictionary<string, Relic> _all = new();
    public void Register(Relic relic) => _all[relic.Id] = relic;
    public Relic? Get(string id) => _all.TryGetValue(id, out var r) ? r : null;
    public IEnumerable<Relic> All => _all.Values;

    public static RelicRegistry Default()
    {
        var r = new RelicRegistry();
        r.Register(new Relic { Id = "relic_placeholder", Name = "占位遗物", EffectDesc = "效果待定（§7）" });
        return r;
    }
}
