# =============================================================================
# GameState —— 《同渊》内核的核心。
#
# 关键语义（务必与规则 §1 对齐）：
#   时间轴 = 一串格子，指针从左向右逐格推进。占位 = 推进的格数 = 时间成本。
#   指针每“新进入”一格（出发格不重复结算）：
#     1. 触发该格的敌人事件点（经过即触发一次）；
#     2. 检查该格的持续态（护盾，本 MVP 暂为占位）；
#     3. 若该格是本次占位的终点 → 结算本牌效果。
#   ⇒ 牌效果在占位末尾才落下，沿途敌人节点先触发 ⇒
#     “打牌时，敌人可能抢在你的效果生效前先出手”。占位越高、效果越迟、途中越可能先挨打。
#
# 确定性：除开局带种子的洗牌外，结算全程无 RNG，可克隆、可重放。
# 预演（完全信息）：preview_action() 在克隆状态上跑一遍，返回事件流与结果快照，
#   供 UI 在玩家“松手前”展示后果（§3 预打出预览）。
# =============================================================================
class_name GameState
extends RefCounted

var characters: Array = []      # Array[Character]
var enemy: Enemy
var pointer: int = -1           # 当前指针格；-1 = 起跑前
var action_count: int = 0       # 已执行的动作数（出牌/整备/空过）
var battle_over: bool = false
var battle_won: bool = false
var rng_seed: int = 0

var events: Array = []          # 最近一次动作的事件流
var _triggered_cells: Dictionary = {}  # cell -> true，已触发过的格子

func _init() -> void:
	_triggered_cells = {}
	events = []

# ---------------------------------------------------------------------------
# 初始化一局
# ---------------------------------------------------------------------------
func setup(p_seed: int) -> void:
	rng_seed = p_seed
	var rng = RandomNumberGenerator.new()
	rng.seed = p_seed
	characters = []
	var names = ["甲", "乙", "丙", "丁"]
	var colors = [Color(0.95, 0.35, 0.35), Color(0.35, 0.55, 0.95),
				  Color(0.40, 0.80, 0.45), Color(0.95, 0.70, 0.30)]
	for i in 4:
		var ch = Character.new("c%d" % i, names[i], colors[i], 20, i + 1)
		ch.draw_pile = CardDefs.starter_deck().duplicate()
		_shuffle(ch.draw_pile, rng)
		ch.draw(Character.HAND_START)
		characters.append(ch)
	enemy = Enemy.new("渊兽", 80, CardDefs.enemy_pattern())
	pointer = -1
	action_count = 0
	battle_over = false
	battle_won = false
	_triggered_cells = {}
	events = []

func _shuffle(arr: Array, rng: RandomNumberGenerator) -> void:
	for i in range(arr.size() - 1, 0, -1):
		var j = rng.randi_range(0, i)
		var t = arr[i]
		arr[i] = arr[j]
		arr[j] = t

# ---------------------------------------------------------------------------
# 公开动作：出牌 / 空过。均返回本次事件流。
# ---------------------------------------------------------------------------
func play_card(ch: Character, card_idx: int) -> Array:
	events = []
	if battle_over or ch.is_down or card_idx < 0 or card_idx >= ch.hand.size():
		return events
	action_count += 1
	var card: CardDef = ch.hand[card_idx]
	ch.hand.remove_at(card_idx)
	ch.discard_pile.append(card)
	_log(CoreEnums.EventKind.PLAY_CARD, { "char_id": ch.id, "card_name": card.name, "occupancy": card.occupancy })
	# 出攻击牌 → 该玩家移到 1 位（输出即暴露，§3）。移位在推进前发生，
	# 因此本次推进沿途的敌人节点会看到你已在前线。
	if card.kind == CoreEnums.Kind.ATTACK:
		_move_to_front(ch)
	_advance(card.occupancy, func() -> void: _settle_card(card, ch))
	# 打一张补一张：打出无限制（无能量、无整备），手牌靠循环补回。
	# 抽牌堆空 → 弃牌堆整叠放回抽牌堆（见 Character.draw）。
	if not battle_over:
		_draw_after_play(ch)
	return events

func _draw_after_play(ch: Character) -> void:
	var drawn = ch.draw(1)
	for c in drawn:
		_log(CoreEnums.EventKind.CARD_DRAWN, { "char_id": ch.id, "card_name": c.name })
	if ch.last_reshuffled:
		_log(CoreEnums.EventKind.PILE_RESHUFFLED, { "char_id": ch.id })

func skip(ch: Character) -> Array:
	events = []
	if battle_over or ch.is_down:
		return events
	action_count += 1
	_log(CoreEnums.EventKind.SKIP, { "char_id": ch.id })
	_advance(1, Callable())  # 空过：只推进 1 格，无落点效果（防死锁，§2）
	return events

# ---------------------------------------------------------------------------
# 核心：逐格推进 + 结算（§1）
# ---------------------------------------------------------------------------
func _advance(amount: int, on_end: Callable) -> void:
	var old = pointer
	for step in range(1, amount + 1):
		var cell = old + step
		pointer = cell
		# 1. 触发该格的敌人节点（经过即触发一次）
		var node = enemy.node_at(cell)
		if node != null and not _triggered_cells.has(cell):
			_triggered_cells[cell] = true
			_trigger_node(node, cell)
			if battle_over:
				return
		# 2. 检查持续态（护盾，本 MVP 占位：无）
		# 3. 若是本次占位终点 → 结算本牌效果
		if step == amount and on_end.is_valid():
			on_end.call()
			if battle_over:
				return
	_check_battle_end()

func _trigger_node(node: Dictionary, cell: int) -> void:
	_log(CoreEnums.EventKind.NODE_TRIGGER, {
		"cell": cell, "node_type": node.type, "damage": node.damage, "label": node.label })
	# 蓄力节点：敌人获得力量（敌人侧附魔，经编排触发，§4/§5）
	if node.type == CoreEnums.NodeType.CHARGE:
		enemy.add_enchantment("strength", 1, -1)
		_log(CoreEnums.EventKind.ENCHANT_APPLIED, { "target": "enemy", "type": "strength", "mag": 1 })
		return
	var positions: Array
	match node.type:
		CoreEnums.NodeType.SLASH:  positions = [1]              # 斩击=打 1 位
		CoreEnums.NodeType.THRUST: positions = [2]              # 突刺=打 2 位
		CoreEnums.NodeType.STRIKE: positions = [1, 2, 3, 4]     # 打击=打全体
		_: positions = []
	# 敌人“力量”附魔加伤
	var dmg: int = node.damage + enemy.get_strength()
	for pos in positions:
		var c = live_char_at(pos)
		if c != null:
			_deal_damage_to_char(c, dmg)
			if battle_over:
				return

func _settle_card(card: CardDef, ch: Character) -> void:
	_log(CoreEnums.EventKind.CARD_LAND, { "card_name": card.name, "char_id": ch.id })
	card.effect.call(self, ch)
	_check_battle_end()

# ---------------------------------------------------------------------------
# 伤害 / 治疗 / 站位（被牌的 effect 回调使用）
# ---------------------------------------------------------------------------
func deal_damage_to_enemy(amount: int, source: Character = null) -> void:
	# 附魔结算（§4）：来源角色的“力量”加伤；敌人的“易伤”按次数加伤并扣次数。
	var bonus: Array = []
	if source != null:
		var s := source.get_strength()
		if s != 0:
			amount += s
			bonus.append("力+%d" % s)
	var vuln: Dictionary = enemy.get_enchantment("vulnerable")
	if vuln != null and vuln.charges > 0:
		amount += vuln.mag
		bonus.append("易伤+%d" % vuln.mag)
		vuln.charges -= 1
		if vuln.charges <= 0:
			enemy.remove_enchantment(vuln)
	enemy.hp -= amount
	_log(CoreEnums.EventKind.DAMAGE_DEALT_ENEMY, { "amount": amount, "hp_after": enemy.hp, "bonus": " ".join(bonus) })
	if enemy.hp <= 0:
		enemy.hp = 0
		_log(CoreEnums.EventKind.ENEMY_DEAD, {})

# 附魔施加接口（供牌效果调用，统一记事件）
func apply_vulnerable(mag: int, charges: int) -> void:
	enemy.add_enchantment("vulnerable", mag, charges)
	_log(CoreEnums.EventKind.ENCHANT_APPLIED, { "target": "enemy", "type": "vulnerable", "mag": mag, "charges": charges })

func apply_strength(ch: Character, mag: int) -> void:
	ch.add_enchantment("strength", mag, -1)
	_log(CoreEnums.EventKind.ENCHANT_APPLIED, { "target": ch.id, "type": "strength", "mag": mag })

func heal_char(ch: Character, amount: int) -> void:
	ch.hp = min(ch.max_hp, ch.hp + amount)
	_log(CoreEnums.EventKind.HEAL, { "char_id": ch.id, "amount": amount, "hp_after": ch.hp })

func _deal_damage_to_char(ch: Character, amount: int) -> void:
	ch.hp -= amount
	_log(CoreEnums.EventKind.DAMAGE_DEALT_CHAR, { "char_id": ch.id, "amount": amount, "hp_after": ch.hp })
	if ch.hp <= 0 and not ch.is_down:
		ch.hp = 0
		var dead_pos = ch.position
		ch.is_down = true
		ch.position = 0  # 离场
		# 阵亡后身后的人向前补位：保持 1..N 连续、前线锚定 1（"位置向前延伸"）
		for c in characters:
			if not c.is_down and c.position > dead_pos:
				var f = c.position
				c.position -= 1
				_log(CoreEnums.EventKind.POSITION_CHANGE, { "char_id": c.id, "from": f, "to": c.position })
		_log(CoreEnums.EventKind.CHAR_DOWN, { "char_id": ch.id })
	_check_battle_end()

# 出攻击牌 → 移到 1 位，身前的人依次后移（§3）
func _move_to_front(ch: Character) -> void:
	var old = ch.position
	for c in characters:
		if c == ch or c.is_down:
			continue
		if c.position >= 1 and c.position < old:
			var f = c.position
			c.position += 1
			_log(CoreEnums.EventKind.POSITION_CHANGE, { "char_id": c.id, "from": f, "to": c.position })
	ch.position = 1
	_log(CoreEnums.EventKind.POSITION_CHANGE, { "char_id": ch.id, "from": old, "to": 1 })

# 走位：后退 steps 位（与身后者换位）。供“挪位”等技能用。
func move_back(ch: Character, steps: int = 1) -> void:
	for i in steps:
		var target = ch.position + 1
		if target > 4:
			break
		var other = live_char_at(target)
		var f = ch.position
		if other != null:
			other.position = ch.position
			_log(CoreEnums.EventKind.POSITION_CHANGE, { "char_id": other.id, "from": target, "to": ch.position })
		ch.position = target
		_log(CoreEnums.EventKind.POSITION_CHANGE, { "char_id": ch.id, "from": f, "to": target })

# ---------------------------------------------------------------------------
# 查询
# ---------------------------------------------------------------------------
func live_char_at(pos: int) -> Character:
	for c in characters:
		if not c.is_down and c.position == pos:
			return c
	return null

func get_char_by_id(cid: String) -> Character:
	for c in characters:
		if c.id == cid:
			return c
	return null

func cell_triggered(cell: int) -> bool:
	return _triggered_cells.has(cell)

func any_alive() -> bool:
	for c in characters:
		if not c.is_down:
			return true
	return false

func _check_battle_end() -> void:
	if battle_over:
		return
	if enemy.hp <= 0:
		battle_over = true
		battle_won = true
		_log(CoreEnums.EventKind.BATTLE_END, { "won": true })
		return
	if not any_alive():
		battle_over = true
		battle_won = false
		_log(CoreEnums.EventKind.BATTLE_END, { "won": false })

# ---------------------------------------------------------------------------
# 预演（完全信息）：在克隆状态上执行动作，返回事件流 + 结果快照。
# ---------------------------------------------------------------------------
func preview_action(action: Dictionary) -> Dictionary:
	var s = clone()
	var ch = s.get_char_by_id(action.get("char_id", ""))
	if ch == null:
		return {}
	var ev: Array
	match action.get("type"):
		"card": ev = s.play_card(ch, int(action.get("card_idx", -1)))
		"skip": ev = s.skip(ch)
		_: ev = []
	var triggered: Array = []
	for e in ev:
		if e.kind == CoreEnums.EventKind.NODE_TRIGGER:
			triggered.append(e.data.cell)
	var chars_after: Array = []
	for c in s.characters:
		chars_after.append({ "id": c.id, "name": c.name, "hp": c.hp, "pos": c.position, "down": c.is_down })
	return {
		"events": ev,
		"end_pointer": s.pointer,
		"enemy_hp_after": s.enemy.hp,
		"triggered_cells": triggered,
		"chars_after": chars_after,
		"over": s.battle_over,
		"won": s.battle_won,
	}

# ---------------------------------------------------------------------------
# 克隆（深拷贝标量与牌堆；CardDef 共享不可变引用）
# ---------------------------------------------------------------------------
func clone() -> GameState:
	var s = GameState.new()
	s.rng_seed = rng_seed
	s.pointer = pointer
	s.action_count = action_count
	s.battle_over = battle_over
	s.battle_won = battle_won
	s._triggered_cells = _triggered_cells.duplicate()
	s.enemy = enemy.clone()
	s.characters = []
	for c in characters:
		s.characters.append(c.clone())
	return s

func _log(kind: int, data: Dictionary) -> void:
	events.append({ "kind": kind, "data": data })
