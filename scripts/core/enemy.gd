# 敌人 = 关卡（§5）：没有牌堆循环，它的“牌”是设计师预先编排、铺在时间轴上的序列。
# 敌人不是玩家镜像；它只在被指针经过的格子上触发节点。
class_name Enemy
extends RefCounted

var name: String
var hp: int
var max_hp: int
var pattern: Array          # Array[Dictionary|null]，循环铺在时间轴上
# 附魔列表（§4）：{type, mag, charges}。敌人侧附魔经编排触发（如蓄力）或被玩家施加（如易伤）。
var enchantments: Array = []

func _init(p_name: String, p_max_hp: int, p_pattern: Array) -> void:
	name = p_name
	max_hp = p_max_hp
	hp = p_max_hp
	pattern = p_pattern

# ---- 附魔（§4）----
func add_enchantment(p_type: String, p_mag: int, p_charges: int) -> void:
	for e in enchantments:
		if e.type == p_type:
			e.mag += p_mag
			if p_charges != -1:
				e.charges += p_charges
			return
	enchantments.append({ "type": p_type, "mag": p_mag, "charges": p_charges })

func get_enchantment(p_type: String) -> Variant:
	for e in enchantments:
		if e.type == p_type:
			return e
	return null

func get_strength() -> int:
	var s := 0
	for e in enchantments:
		if e.type == "strength":
			s += e.mag
	return s

func remove_enchantment(e: Dictionary) -> void:
	enchantments.erase(e)

# 显示层：标记汇总（易伤次数 / 力量）——只反映附魔，不存独立数据
func enchant_markers() -> Array:
	var out: Array = []
	var vuln = get_enchantment("vulnerable")
	if vuln != null and vuln.charges > 0:
		out.append({ "label": "易伤%d" % vuln.charges, "color": Color(1.0, 0.4, 0.4) })
	var s := get_strength()
	if s != 0:
		out.append({ "label": "力+%d" % s, "color": Color(1.0, 0.8, 0.3) })
	return out

# 取某格的节点（按 pattern 循环）。返回 null 表示空格。
func node_at(cell_index: int) -> Variant:
	if pattern.is_empty():
		return null
	return pattern[cell_index % pattern.size()]

func clone() -> Enemy:
	var e = Enemy.new(name, max_hp, pattern.duplicate(true))
	e.hp = hp
	e.enchantments = enchantments.duplicate(true)
	return e
