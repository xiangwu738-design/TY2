using System.Collections.Generic;

namespace Tongyuan.Core.Core;

/// <summary>
/// 游戏内核状态（规格 §3）。持有角色/敌人/时间轴/指针/事件流/护盾。
/// 分层铁律：Core 纯 C#，不 using Godot，可单测、可 Clone() 预演、确定性。
/// 确定性：除开局带种子洗牌外，结算零 RNG（规格 §2）。
/// </summary>
public sealed class GameState
{
    public Timeline Timeline { get; set; } = new();
    public List<Character> Characters { get; init; } = new();
    public List<Enemy> Enemies => Timeline.Enemies;
    public List<Shield> Shields { get; } = new(); // 活跃护盾（关系持续态）

    public int Seed { get; init; }
    public DeterministicRng Rng { get; set; }

    /// <summary>当前回合事件流（Apply 产出，供 Views/Net）。</summary>
    public List<GameEvent> Events { get; } = new();

    /// <summary>历史动作序列（联机重放/确定性校验用）。</summary>
    public List<PlayerAction> ActionHistory { get; } = new();

    public GameState() => Rng = new DeterministicRng(Seed);
    public GameState(int seed) { Seed = seed; Rng = new DeterministicRng(seed); }

    public int Pointer => Timeline.Pointer;
    public IEnumerable<Character> AliveCharacters => Characters.Where(c => c.IsAlive);

    // ---- 预演（不改本体）----
    public GameState Clone()
    {
        var s = new GameState(Seed);
        s.Rng = Rng.Clone(); // 复制已推进的随机状态，保证预演确定性
        s.Timeline = Timeline.Clone();
        foreach (var c in Characters) s.Characters.Add(c.Clone());
        foreach (var sh in Shields) s.Shields.Add(sh.Clone());
        return s;
    }

    public List<GameEvent> Preview(PlayerAction action) => Clone().Apply(action);

    // ---- 执行 ----
    public List<GameEvent> Apply(PlayerAction action)
    {
        Events.Clear();
        ActionHistory.Add(action);
        var ch = Characters.Find(c => c.Id == action.CharacterId);
        if (ch is null || !ch.IsAlive) return Events;

        switch (action.Type)
        {
            case ActionType.Skip: DoSkip(ch); break;
            case ActionType.UsePrep: DoUsePrep(ch); break;
            case ActionType.PlayCard: DoPlayCard(ch, action); break;
        }
        Events.Add(new GameEvent.TurnEnded());
        return Events;
    }

    // ---- 空过：推进1格，无效果 ----
    private void DoSkip(Character ch)
    {
        AdvanceAndSettle(ch, 1, settleEndpoint: false);
    }

    // ---- 整备：回手，抽牌（占位消耗时间）----
    private void DoUsePrep(Character ch)
    {
        var prep = ch.PrepCard;
        if (prep is null) return;
        int cost = prep.Def.Cost;
        AdvanceAndSettle(ch, cost, settleEndpoint: true, endpointCard: prep, action: null);
        // 整备牌回手（不从手牌消失/不进弃牌堆），效果=抽牌
        int before = ch.Hand.Count;
        ch.Draw(prep.Def.Magnitude, Rng);
        int drawn = ch.Hand.Count - before;
        Events.Add(new GameEvent.PrepUsed(ch.Id, drawn));
        if (drawn > 0) Events.Add(new GameEvent.CardsDrawn(ch.Id, drawn));
    }

    // ---- 出牌：占位推进，沿途逐格结算，终点结算牌效果，牌进弃牌堆 ----
    private void DoPlayCard(Character ch, PlayerAction action)
    {
        var card = ch.Hand.Find(c => c.InstanceId == action.CardInstanceId);
        if (card is null) return;
        Events.Add(new GameEvent.CardPlayed(ch.Id, card.InstanceId));
        int cost = card.Def.Cost;
        AdvanceAndSettle(ch, cost, settleEndpoint: true, endpointCard: card, action: action);
        // 非整备牌打出→进弃牌堆（整备牌由 UsePrep 处理，不走这里）
        ch.Hand.Remove(card);
        ch.DiscardPile.Add(card);
    }

    /// <summary>
    /// 推进 cost 格，逐格结算：进格→①敌人节点→②护盾检查；
    /// 若 settleEndpoint 且当前是终点（最后一格），结算牌效果（③）。
    /// </summary>
    private void AdvanceAndSettle(Character ch, int cost, bool settleEndpoint,
        Card? endpointCard = null, PlayerAction? action = null)
    {
        for (int step = 1; step <= cost; step++)
        {
            if (Timeline.AtEnd) break;
            int from = Timeline.Pointer;
            Timeline.Pointer++;
            Events.Add(new GameEvent.PointerMoved(from, Timeline.Pointer));
            SettleEnemyAndShield(Timeline.Pointer);
            if (settleEndpoint && step == cost && endpointCard is not null)
                SettleCardEffect(ch, endpointCard, action);
        }
        // cost 为 0 时仍结算一次牌效果（即时牌）
        if (settleEndpoint && cost == 0 && endpointCard is not null)
            SettleCardEffect(ch, endpointCard, action);
    }

    /// <summary>逐格结算 ①②：敌人节点触发，护盾吸收。</summary>
    private void SettleEnemyAndShield(int slot)
    {
        var enemy = Timeline.EnemyAt(slot);
        if (enemy is null || !enemy.IsAlive) return;
        // 行动链：按链执行，链尽循环（规格：理论可无限→过长用循环）
        var act = enemy.AdvanceChain();
        ExecuteEnemyAction(enemy, act);
    }

    private void ExecuteEnemyAction(Enemy enemy, EnemyAction act)
    {
        switch (act)
        {
            case EnemyAction.Attack atk:
                int dmg = atk.Amount + enemy.Charge; // 蓄力附带，释放后清零
                enemy.Charge = 0;
                int targetPos = atk.TargetPos ?? enemy.TargetPosition;
                Events.Add(new GameEvent.EnemyTriggered(enemy.Id, targetPos, dmg));
                if (targetPos == -1)
                {
                    foreach (var t in Characters.Where(c => c.IsAlive).ToList())
                        ApplyEnemyHit(enemy, t, dmg);
                }
                else
                {
                    var t = Characters.Find(c => c.IsAlive && c.Position == targetPos);
                    if (t is null) return; // 突=位2，N<2 落空
                    ApplyEnemyHit(enemy, t, dmg);
                }
                break;
            case EnemyAction.Charge ch:
                enemy.Charge += ch.Amount;
                Events.Add(new GameEvent.EnemyCharged(enemy.Id, ch.Amount));
                break;
            case EnemyAction.Idle:
                Events.Add(new GameEvent.EnemyIdle(enemy.Id));
                break;
        }
    }

    private void ApplyEnemyHit(Enemy enemy, Character target, int raw)
    {
        // ② 护盾吸收（被守护者）
        int absorbed = 0;
        foreach (var sh in Shields.Where(s => s.ProtectedCharacterId == target.Id && !s.IsExhausted).ToList())
        {
            int a = sh.Absorb(raw - absorbed);
            if (a > 0)
            {
                absorbed += a;
                Events.Add(new GameEvent.ShieldAbsorbed(target.Id, a, sh.IsExhausted));
            }
            if (absorbed >= raw) break;
        }
        Shields.RemoveAll(s => s.IsExhausted);

        int remaining = raw - absorbed;
        int vuln = target.ConsumeVulnerableBonus();
        int dmg = remaining + vuln;
        bool wasAlive = target.IsAlive;
        if (dmg > 0)
        {
            target.Hp -= dmg;
            target.DamageTakenThisFight += dmg; // 累计本场受伤（结算下调上限用，§4.6）
            Events.Add(new GameEvent.DamageDealt(target.Id, TargetIsEnemy: false, dmg));
        }
        if (wasAlive && target.Hp <= 0)
        {
            target.Hp = 0;
            Events.Add(new GameEvent.CharacterDied(target.Id));
            ContractPositions();
        }
    }

    /// <summary>结算牌效果（③，占位终点）。</summary>
    private void SettleCardEffect(Character ch, Card card, PlayerAction? action)
    {
        switch (card.Def.Effect)
        {
            case EffectKind.AttackDamage:
                DoAttack(ch, card, action);
                break;
            case EffectKind.ApplyShield:
                DoShield(ch, card, action);
                break;
            case EffectKind.ApplyEnchantment:
                DoEnchant(ch, card, action);
                break;
            case EffectKind.DrawCards:
                int before = ch.Hand.Count;
                ch.Draw(card.Def.Magnitude, Rng);
                Events.Add(new GameEvent.CardsDrawn(ch.Id, ch.Hand.Count - before));
                break;
        }
    }

    // 攻击：近战（打击/斩击/突刺）打出者移到位1（暴露）；远程不位移、自选敌（规格）。
    private void DoAttack(Character ch, Card card, PlayerAction? action)
    {
        bool ranged = card.Def.DamageType == DamageType.Ranged;
        if (!ranged) MoveToFront(ch);
        if (action?.TargetEnemyId is int eid)
        {
            var enemy = Enemies.Find(e => e.Id == eid);
            if (enemy is not null && enemy.IsAlive)
            {
                int dmg = card.EffectiveAttack + enemy.ConsumeVulnerableBonus(); // 含力量附魔 + 敌人易伤
                enemy.Hp -= dmg;
                Events.Add(new GameEvent.DamageDealt(enemy.Id, TargetIsEnemy: true, dmg));
                if (!enemy.IsAlive)
                    Events.Add(new GameEvent.EnemyDied(enemy.Id));
            }
        }
    }

    // 防御：铺护盾，绑守护关系
    private void DoShield(Character ch, Card card, PlayerAction? action)
    {
        int protectedId = action?.TargetCharacterId ?? ch.Id; // 默认自铺
        var shield = new Shield
        {
            Type = card.Def.ShieldType,
            Amount = card.Def.Magnitude,
            RemainingHits = card.Def.ShieldHits,
            GuardianCharacterId = ch.Id,
            ProtectedCharacterId = protectedId,
        };
        Shields.Add(shield);
        Events.Add(new GameEvent.ShieldPlaced(ch.Id, protectedId, shield.Type));
    }

    // 附魔：按作用域挂牌
    private void DoEnchant(Character ch, Card card, PlayerAction? action)
    {
        var def = card.Def;
        var ench = new Enchantment
        {
            Type = def.EnchantType,
            Magnitude = def.Magnitude,
            Scope = def.EnchantScope,
            Remaining = def.Magnitude, // 易伤次数默认=量
        };

        switch (def.EnchantScope)
        {
            case EnchantmentScope.SpecificCard:
                var tid = action?.TargetCardInstanceId;
                var target = tid is null ? null : ch.Hand.Find(c => c.InstanceId == tid)
                             ?? ch.DiscardPile.Find(c => c.InstanceId == tid);
                ench.TargetCardInstanceId = tid;
                target?.Enchantments.Add(ench.Clone());
                Events.Add(new GameEvent.EnchantmentApplied(ench, null, null));
                break;
            case EnchantmentScope.AllInDiscard:
                foreach (var c in ch.DiscardPile) c.Enchantments.Add(ench.Clone());
                Events.Add(new GameEvent.EnchantmentApplied(ench, null, null));
                break;
            case EnchantmentScope.AllInDraw:
                foreach (var c in ch.DrawPile) c.Enchantments.Add(ench.Clone());
                Events.Add(new GameEvent.EnchantmentApplied(ench, null, null));
                break;
        }

        // 易伤/蓄力作用到目标单位（角色/敌人）
        if (def.EnchantType == EnchantmentType.Vulnerable)
        {
            if (action?.TargetEnemyId is int eid)
            {
                var enemy = Enemies.Find(e => e.Id == eid);
                enemy?.Statuses.Add(ench.Clone());
                Events.Add(new GameEvent.EnchantmentApplied(ench, null, eid));
            }
            else if (action?.TargetCharacterId is int cid)
            {
                var c = Characters.Find(x => x.Id == cid);
                c?.Statuses.Add(ench.Clone());
                Events.Add(new GameEvent.EnchantmentApplied(ench, cid, null));
            }
        }
        else if (def.EnchantType == EnchantmentType.Charge)
        {
            if (action?.TargetEnemyId is int eid)
            {
                var enemy = Enemies.Find(e => e.Id == eid);
                if (enemy is not null)
                {
                    enemy.Charge += def.Magnitude;
                    Events.Add(new GameEvent.EnchantmentApplied(ench, null, eid));
                }
            }
        }
    }

    // ---- 位置系统（规格 §4.3）----
    /// <summary>出攻击牌→移到1位，身前的人后移（输出即暴露）。</summary>
    private void MoveToFront(Character ch)
    {
        if (ch.Position == 1) return;
        int from = ch.Position;
        // 身前（position < ch.Position 的人）后移？规格：身前的人后移。
        // 实现：把 ch 移到1，原 1..ch.Position-1 的角色各 +1。
        foreach (var c in Characters.Where(c => c.IsAlive && c.Position < ch.Position))
            c.Position++;
        ch.Position = 1;
        Events.Add(new GameEvent.PositionChanged(ch.Id, from, 1));
        // 守护关系：被守护者离位则盾消失（此处简化：位置变动不主动清盾，由 IsExhausted 自然清；
        // 真正离位清盾在角色阵亡收缩时处理）
    }

    /// <summary>阵亡收缩：死人后身后的人向前补位，保持 1..N 连续。</summary>
    public void ContractPositions()
    {
        var alive = Characters.Where(c => c.IsAlive).OrderBy(c => c.Position).ToList();
        for (int i = 0; i < alive.Count; i++)
            alive[i].Position = i + 1; // 1..N 连续
        // 被守护者阵亡→其盾消失
        var deadIds = Characters.Where(c => !c.IsAlive).Select(c => c.Id).ToHashSet();
        Shields.RemoveAll(s => deadIds.Contains(s.ProtectedCharacterId) || deadIds.Contains(s.GuardianCharacterId));
    }
}
