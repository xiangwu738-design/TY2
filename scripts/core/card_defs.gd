# 牌库与敌人编排的工厂。
# 这里所有数值都是“待调”的占位，规则 §7/§10 反复强调这些只能原型调。
class_name CardDefs
extends RefCounted

# ---- 敌人节点构造 ----
static func _node(type: int, dmg: int, label: String) -> Dictionary:
	return { "type": type, "damage": dmg, "label": label }

# 敌人“谱子”：一串格子，null=空格，否则是该格的敌人节点。
# 节点的疏密编码敌人节奏（§5）。本 pattern 循环铺在时间轴上。
static func enemy_pattern() -> Array:
	return [
		_node(CoreEnums.NodeType.SLASH, 8, "斩8"),   # 打 1 位
		null, null,
		_node(CoreEnums.NodeType.THRUST, 6, "突6"),  # 打 2 位
		null,
		_node(CoreEnums.NodeType.CHARGE, 0, "蓄"),   # 蓄力：敌力+1（敌人侧附魔）
		null,
		_node(CoreEnums.NodeType.SLASH, 8, "斩8"),
		null,
		_node(CoreEnums.NodeType.STRIKE, 4, "打4"),  # 打全体
		null, null,
	]

# ---- 起手牌组（四人共用同一套，便于先验内核）----
static func starter_deck() -> Array:
	var deck: Array = []
	deck.append(_strike())   # 占1·敌3伤
	deck.append(_strike())
	deck.append(_strike())
	deck.append(_slash())    # 占2·敌6伤
	deck.append(_slash())
	deck.append(_slash())
	deck.append(_heavy())    # 占3·敌11伤（高占位=慢=沿途更易挨打）
	deck.append(_breath())   # 占1·自愈4
	deck.append(_maneuver()) # 占1·后退1位（走位规避）
	deck.append(_shatter())  # 占2·敌易伤3次(每次+2) —— 附魔
	deck.append(_focus())    # 占2·自力+1 —— 附魔
	return deck

# ---- 单卡 ----
static func _strike() -> CardDef:
	return CardDef.new("strike", "刺", 1, CoreEnums.Kind.ATTACK,
		func(gs, c): gs.deal_damage_to_enemy(3, c), "占1·敌3伤")

static func _slash() -> CardDef:
	return CardDef.new("slash", "斩", 2, CoreEnums.Kind.ATTACK,
		func(gs, c): gs.deal_damage_to_enemy(6, c), "占2·敌6伤")

static func _heavy() -> CardDef:
	return CardDef.new("heavy", "重击", 3, CoreEnums.Kind.ATTACK,
		func(gs, c): gs.deal_damage_to_enemy(11, c), "占3·敌11伤·慢")

static func _breath() -> CardDef:
	return CardDef.new("breath", "调息", 1, CoreEnums.Kind.SKILL,
		func(gs, c): gs.heal_char(c, 4), "占1·自愈4")

static func _bandage() -> CardDef:
	return CardDef.new("bandage", "包扎", 2, CoreEnums.Kind.SKILL,
		func(gs, c): gs.heal_char(c, 7), "占2·自愈7")

static func _maneuver() -> CardDef:
	return CardDef.new("maneuver", "挪位", 1, CoreEnums.Kind.SKILL,
		func(gs, c): gs.move_back(c, 1), "占1·后退1位")

# 附魔牌（§4）
static func _shatter() -> CardDef:
	# 敌人侧附魔：易伤——对敌攻击 +2，持续下 3 次
	return CardDef.new("shatter", "破甲", 2, CoreEnums.Kind.SKILL,
		func(gs, c): gs.apply_vulnerable(2, 3), "占2·敌易伤3次(每次+2)")

static func _focus() -> CardDef:
	# 玩家侧附魔：力量——该角色攻击牌 +1
	return CardDef.new("focus", "聚力", 2, CoreEnums.Kind.SKILL,
		func(gs, c): gs.apply_strength(c, 1), "占2·自力+1")
