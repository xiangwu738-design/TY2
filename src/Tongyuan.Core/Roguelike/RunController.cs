namespace Tongyuan.Core.Roguelike;

using Tongyuan.Core.Core;

/// <summary>
/// roguelike 单局流程控制（规格 §4.9）。
/// 串联：进入节点 → 战斗/商店/休息/事件/Boss → 战后加牌移牌 → 推进下一节点。
/// P3：跑通一局 地图→战斗→加牌→商店→休息→Boss。
/// </summary>
public sealed class RunController
{
    public RunState State { get; }
    public RelicRegistry Relics { get; } = RelicRegistry.Default();

    public RunController(RunState state) => State = state;

    public MapNode? CurrentNode => State.Map.Nodes.Find(n => n.Id == State.CurrentNodeId);

    /// <summary>开始一局：定位到起点。</summary>
    public void Start() => State.CurrentNodeId = State.Map.StartNodeId;

    public MapNodeType CurrentType => CurrentNode?.Type ?? MapNodeType.Combat;

    // ---- 战斗胜利：加牌（受控，质量保底）+ 货币奖励 ----
    public void WinCombat(CardDef rewardCard, int goldReward = 15)
    {
        State.BattlesWon++;
        State.Gold += goldReward;
        if (State.Deck.Count < RunState.DeckMax)
            State.Deck.Add(rewardCard); // 战后加牌（受控，§4.9）
    }

    /// <summary>移牌（战后/商店，§4.9）。</summary>
    public bool RemoveCard(string cardId)
    {
        if (State.Deck.Count <= RunState.DeckMin) return false; // 卡组偏小保护
        int idx = State.Deck.FindIndex(c => c.Id == cardId);
        if (idx < 0) return false;
        State.Deck.RemoveAt(idx);
        return true;
    }

    // ---- 商店（§4.9）：牌50/遗物150/移牌30/恢复上限40 ----
    public enum ShopBuy { Card, Relic, Remove, RestoreMax }

    public bool ShopBuyItem(ShopBuy item, CardDef? card = null, Character? target = null, int baseMaxHp = 0)
    {
        int price = item switch
        {
            ShopBuy.Card => Shop.PriceCard,
            ShopBuy.Relic => Shop.PriceRelic,
            ShopBuy.Remove => Shop.PriceRemove,
            ShopBuy.RestoreMax => Shop.PriceRestoreMax,
            _ => int.MaxValue,
        };
        if (State.Gold < price) return false;
        State.Gold -= price;
        switch (item)
        {
            case ShopBuy.Card when card is not null && State.Deck.Count < RunState.DeckMax:
                State.Deck.Add(card); break;
            case ShopBuy.Relic:
                State.Relics.Add(Relics.All.First()); break; // 占位遗物
            case ShopBuy.Remove:
                // 商店移牌（简化：移最后一张，受卡组下限保护）
                if (State.Deck.Count > RunState.DeckMin) State.Deck.RemoveAt(State.Deck.Count - 1);
                break;
            case ShopBuy.RestoreMax when target is not null:
                HealthSystem.Rest(target, baseMaxHp); break;
        }
        return true;
    }

    // ---- 休息（§4.9/§4.6）：恢复血量上限 ----
    public void Rest(Character c, int baseMaxHp) => HealthSystem.Rest(c, baseMaxHp);

    // ---- 事件（占位，§4.9）：返回描述供 UI ----
    public EventNode RollEvent(int seed)
    {
        return new EventNode { Id = "evt_" + seed, Title = "占位事件", Body = "效果待定（§7）" };
    }

    // ---- 推进到下一节点 ----
    public bool Advance()
    {
        var node = CurrentNode;
        if (node is null) return false;
        if (node.Type == MapNodeType.Boss)
        {
            State.RunOver = true;
            State.Victory = true; // Boss 击败 → 胜利
            return false;
        }
        if (node.NextIds.Count == 0)
        {
            State.RunOver = true;
            return false;
        }
        State.CurrentNodeId = node.NextIds[0]; // 占位：选第一条路
        return true;
    }
}
