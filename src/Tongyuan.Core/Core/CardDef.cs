namespace Tongyuan.Core.Core;

/// <summary>
/// 卡牌定义（静态模板）。具体数值/卡牌内容用模板占位，登记 §7，由用户后填（规格 §6）。
/// </summary>
public sealed class CardDef
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public CardType Type { get; init; }
    public int Cost { get; init; } // 占位=推进格数=时间成本

    // 效果参数（占位，P1 按类型细化）
    public int Magnitude { get; init; }
}

/// <summary>
/// 整备牌模板参数（规格 §4.2 / §7，默认：占2·抽2·不弃·回手）。
/// </summary>
public sealed class PrepCardTemplate
{
    public int SlotCost { get; init; } = 2;   // 占位格数
    public int DrawCount { get; init; } = 2;  // 抽牌数
    public bool DiscardAfter { get; init; } = false; // 不弃
    public bool ReturnToHand { get; init; } = true;  // 回手
}
