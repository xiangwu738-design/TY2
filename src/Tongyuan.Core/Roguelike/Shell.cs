namespace Tongyuan.Core.Roguelike;

using Tongyuan.Core.Core;

/// <summary>商店（规格 §4.9）。货币=金币（占位）；价格默认 §7：牌50/遗物150/移牌30/恢复上限40。</summary>
public sealed class Shop
{
    public int Gold { get; set; }
    public List<CardDef> CardOffers { get; } = new();
    public List<Relic> RelicOffers { get; } = new();
    public const int PriceCard = 50;
    public const int PriceRelic = 150;
    public const int PriceRemove = 30;
    public const int PriceRestoreMax = 40;
}

/// <summary>遗物（规格 §4.9）。数据驱动，留注册接口；挂占位/时间轴机制。</summary>
public sealed class Relic
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string EffectDesc { get; init; } = string.Empty; // 占位描述
}

/// <summary>遗物注册接口（扩展位）。</summary>
public interface IRelicRegistry
{
    void Register(Relic relic);
    Relic? Get(string id);
    IEnumerable<Relic> All();
}

/// <summary>休息点（规格 §4.9/§4.6）。恢复血量上限，量用模板参数（默认回25%上限，§7）。</summary>
public sealed class RestSite
{
    public const float RestoreFraction = 0.25f;
}

/// <summary>roguelike 事件节点（叙事/选择，占位）。</summary>
public sealed class EventNode
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty; // 占位
}
