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

    /// <summary>战斗开始时的敌人槽数（1..4）。UI 据此渲染固定槽位，位置不因死亡收缩。</summary>
    public int EnemySlotCount { get; set; }
    /// <summary>战斗开始时的角色槽数（通常为4）。</summary>
    public int CharSlotCount { get; set; }

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
        s.EnemySlotCount = EnemySlotCount;
        s.CharSlotCount = CharSlotCount;
        foreach (var c in Characters) s.Characters.Add(c.Clone());
        foreach (var sh in Shields) s.Shields.Add(sh.Clone());
        s.ActionHistory.AddRange(ActionHistory);
        return s;
    }

    public List<GameEvent> Preview(PlayerAction action) => Clone().Apply(action);

    // ---- 执行 ----
    public List<GameEvent> Apply(PlayerAction action)
    {
        Events.Clear();
        var ch = Characters.Find(c => c.Id == action.CharacterId);
        if (ch is null || !ch.IsAlive) return Events;

        bool applied = false;
        switch (action.Type)
        {
            case ActionType.Skip: applied = DoSkip(ch); break;
            case ActionType.UsePrep: applied = DoUsePrep(ch); break;
            case ActionType.PlayCard: applied = DoPlayCard(ch, action); break;
        }
        if (!applied) return Events;
        ActionHistory.Add(action);
        Events.Add(new GameEvent.TurnEnded());
        return Events;
    }

    // ---- 空过：推进1格，无效果 ----
    private bool DoSkip(Character ch)
    {
        AdvanceAndSettle(ch, 1, settleEndpoint: false);
        return true;
    }

    // ---- 整备：回手，抽牌（占位消耗时间）----
    private bool DoUsePrep(Character ch)
    {
        var prep = ch.PrepCard;
        if (prep is null) return false;
        int cost = prep.Def.Cost;
        int before = ch.Hand.Count;
        // 整备效果（抽牌）在占位第一格（指针当前位置）即时触发
        SettleCardEffect(ch, prep, null);
        // 推进 cost+1 格（占位 cost 格 + 后一格），沿途触发敌人
        AdvanceAndSettle(ch, cost + 1, settleEndpoint: false);
        int drawn = ch.Hand.Count - before;
        Events.Add(new GameEvent.PrepUsed(ch.Id, drawn));
        return true;
    }

    // ---- 出牌：占位推进，沿途逐格结算，终点结算牌效果，牌进弃牌堆 ----
    private bool DoPlayCard(Character ch, PlayerAction action)
    {
        var card = ch.Hand.Find(c => c.InstanceId == action.CardInstanceId);
        if (card is null || !HasRequiredTargets(ch, card, action)) return false;
        Events.Add(new GameEvent.CardPlayed(ch.Id, card.InstanceId));
        ch.TickVulnerable();   // 出牌=行动：消耗一层易伤
        // 近战攻击牌：先暴露到前排（位1）再推进指针——沿途触发的敌人打的就会是已暴露的出牌者，
        // 而非原前排（修复“出牌者暴露前敌人先打到别人”的时序问题）
        if (card.Def.Effect == EffectKind.AttackDamage && card.Def.DamageType != DamageType.Ranged)
            MoveToFront(ch);
        // 牌效果在占位第一格（指针当前位置）即时触发
        SettleCardEffect(ch, card, action);
        // 推进 cost+1 格（占位 cost 格 + 后一格），沿途触发敌人
        AdvanceAndSettle(ch, card.Def.Cost + 1, settleEndpoint: false);
        // 非整备牌打出→进弃牌堆（整备牌由 UsePrep 处理，不走这里）
        ch.Hand.Remove(card);
        ch.DiscardPile.Add(card);
        return true;
    }

    private bool HasRequiredTargets(Character ch, Card card, PlayerAction action)
    {
        var def = card.Def;
        if (def.NeedsTargetEnemy && !IsAliveEnemy(action.TargetEnemyId)) return false;

        return def.Effect switch
        {
            EffectKind.AttackDamage when def.DamageType == DamageType.Ranged => IsAliveEnemy(action.TargetEnemyId),
            EffectKind.ApplyEnchantment when def.EnchantType == EnchantmentType.Power
                && def.EnchantScope == EnchantmentScope.SpecificCard => IsOwnedCardTarget(ch, action.TargetCardInstanceId),
            EffectKind.ApplyEnchantment when def.EnchantType is EnchantmentType.Vulnerable or EnchantmentType.Charge
                => IsAliveEnemy(action.TargetEnemyId) || IsAliveCharacter(action.TargetCharacterId),
            EffectKind.ApplyShield when action.TargetCharacterId is int id => IsAliveCharacter(id),
            _ => true,
        };
    }

    private bool IsAliveEnemy(int? id) => id is int eid && Enemies.Any(e => e.Id == eid && e.IsAlive);

    private bool IsAliveCharacter(int? id) => id is int cid && Characters.Any(c => c.Id == cid && c.IsAlive);

    private static bool IsOwnedCardTarget(Character ch, Guid? id) =>
        id is Guid cid && (ch.Hand.Any(c => c.InstanceId == cid) || ch.DiscardPile.Any(c => c.InstanceId == cid));

    /// <summary>
    /// 推进 cost 格，逐格结算：进格→①敌人节点→②护盾检查；
    /// 若 settleEndpoint 且当前是终点（最后一格），结算牌效果（③）。
    /// 时间轴循环（规格：行动条理论可无限→用循环）：到头绕回 0，敌人节点每轮重触发、行动链随之循环。
    /// </summary>
    private void AdvanceAndSettle(Character ch, int cost, bool settleEndpoint,
        Card? endpointCard = null, PlayerAction? action = null)
    {
        if (Timeline.Length == 0) return;
        for (int step = 1; step <= cost; step++)
        {
            int from = Timeline.Pointer;
            Timeline.Pointer = (Timeline.Pointer + 1) % Timeline.Length; // 循环
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
        foreach (var enemy in Timeline.EnemiesAt(slot))
        {
            if (!enemy.IsAlive) continue;
            var act = enemy.AdvanceChain();
            ExecuteEnemyAction(enemy, act);
        }
    }

    private void ExecuteEnemyAction(Enemy enemy, EnemyAction act)
    {
        enemy.TickVulnerable();   // 行动=消耗一层易伤
        switch (act)
        {
            case EnemyAction.Attack atk:
                int dmg = atk.Amount + enemy.Charge; // 蓄力附带，释放后清零
                enemy.Charge = 0;
                int targetPos = atk.TargetPos ?? enemy.TargetPosition;
                if (targetPos == 2 && Characters.Count(c => c.IsAlive) < 2)
                    targetPos = 1;
                Events.Add(new GameEvent.EnemyTriggered(enemy.Id, targetPos, dmg));
                if (targetPos == -1)
                {
                    foreach (var t in Characters.Where(c => c.IsAlive).ToList())
                        ApplyEnemyHit(enemy, t, dmg);
                }
                else
                {
                    var t = Characters.Find(c => c.IsAlive && c.Position == targetPos);
                    if (t is null && targetPos > 1) // 突=位2，不足2人打前排（不落空）
                        t = Characters.Find(c => c.IsAlive && c.Position == 1);
                    if (t is null) return;
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
        int vuln = target.VulnerableBonus();
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
        // 卡牌独立逻辑（ICardEffect）：优先派发，覆盖纯数据效果
        if (card.Def.CustomEffect is not null)
        {
            card.Def.CustomEffect.Apply(this, ch, card, action);
            return;
        }

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

    // 攻击（伤害类型对称映射，双方相同：打击=全体 / 穿刺=位2 / 斩击=位1 / 远程=自选且不位移）。
    private void DoAttack(Character ch, Card card, PlayerAction? action)
    {
        var dt = card.Def.DamageType;
        // 近战位移（MoveToFront）已提前到 DoPlayCard：推进指针前先暴露，
        // 沿途触发的敌人打的是已暴露的出牌者，而非原前排
        var targets = new List<Enemy>();
        switch (dt)
        {
            case DamageType.Slash: var e1 = AliveEnemyAtPosition(1); if (e1 is not null) targets.Add(e1); break;
            case DamageType.Thrust:
                var e2 = AliveEnemyAtPosition(2) ?? AliveEnemyAtPosition(1); // 突=次排，不足2人打前排（不落空）
                if (e2 is not null) targets.Add(e2);
                break;
            case DamageType.Blunt: targets.AddRange(Enemies.Where(e => e.IsAlive)); break;        // 打全体
            case DamageType.Ranged:
                if (action?.TargetEnemyId is int eid)
                {
                    var er = Enemies.Find(e => e.Id == eid);
                    if (er is not null && er.IsAlive) targets.Add(er);
                }
                break;
        }

        int baseDmg = card.EffectiveAttack; // 含力量附魔
        foreach (var enemy in targets) DamageEnemy(enemy, baseDmg);
    }

    private Enemy? AliveEnemyAtPosition(int pos) => Enemies.FirstOrDefault(e => e.IsAlive && e.Position == pos);

    // ---- 暴露给 ICardEffect（卡牌独立逻辑）的结算辅助 API ----

    /// <summary>对敌人造伤（含易伤结算/死亡事件/阵亡收缩）。代码卡用此造伤。</summary>
    public void DamageEnemy(Enemy enemy, int amount)
    {
        if (!enemy.IsAlive || amount <= 0) return;
        int dmg = amount + enemy.VulnerableBonus();
        enemy.Hp -= dmg;
        Emit(new GameEvent.DamageDealt(enemy.Id, TargetIsEnemy: true, dmg));
        if (!enemy.IsAlive)
        {
            Emit(new GameEvent.EnemyDied(enemy.Id));
            ContractEnemyPositions();
        }
    }

    /// <summary>治疗角色（不超上限）。代码卡用此（如吸血）。</summary>
    public void HealCharacter(Character c, int amount)
    {
        if (!c.IsAlive || amount <= 0) return;
        c.Hp = Math.Min(c.MaxHp, c.Hp + amount);
        // 复用 DamageDealt 负值不便；用 CardsDrawn 之外…这里发一个通用事件占位
        Emit(new GameEvent.DamageDealt(c.Id, TargetIsEnemy: false, -amount)); // 负值=治疗（UI 可据此显示）
    }

    /// <summary>近战位移到位1（暴露）。代码卡按需调用。</summary>
    // MoveToFront 见位置系统段（public）

    /// <summary>取某位置的存活敌人。代码卡用此选目标。</summary>
    public Enemy? EnemyAtPosition(int pos) => AliveEnemyAtPosition(pos);

    /// <summary>追加一个事件到当前回合事件流。代码卡用此发事件。</summary>
    public void Emit(GameEvent e) => Events.Add(e);

    /// <summary>角色抽 n 张（带种子洗牌）。代码卡可用。</summary>
    public void CharacterDraw(Character c, int n) => c.Draw(n, Rng);

    /// <summary>敌方阵亡收缩：存活敌人向前补位（1..N），末位变封禁；从时间轴移除死亡节点。</summary>
    public void ContractEnemyPositions()
    {
        var ordered = Enemies
            .OrderBy(e => e.IsAlive ? 0 : 1)
            .ThenBy(e => e.Position)
            .ThenBy(e => e.Id)
            .ToList();
        for (int i = 0; i < ordered.Count; i++) ordered[i].Position = i + 1;
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
    public void MoveToFront(Character ch)
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

    /// <summary>角色阵亡收缩：存活角色向前补位（1..N），末位变封禁；清理护盾关系。</summary>
    public void ContractPositions()
    {
        var alive = Characters.Where(c => c.IsAlive).OrderBy(c => c.Position).ToList();
        var dead = Characters.Where(c => !c.IsAlive).OrderBy(c => c.Position).ThenBy(c => c.Id).ToList();
        var ordered = alive.Concat(dead).ToList();
        for (int i = 0; i < ordered.Count; i++) ordered[i].Position = i + 1;
        var deadIds = Characters.Where(c => !c.IsAlive).Select(c => c.Id).ToHashSet();
        Shields.RemoveAll(s => deadIds.Contains(s.ProtectedCharacterId) || deadIds.Contains(s.GuardianCharacterId));
    }
}
