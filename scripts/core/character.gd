# 角色状态：血量、站位、手牌 / 抽牌堆 / 弃牌堆。
# 纯数据 + 简单牌堆操作；所有“产生事件”的编排都在 GameState 里。
class_name Character
extends RefCounted

const HAND_START: int = 5   # 起手 5（§2）
const HAND_LIMIT: int = 8   # 上限 8（§2）

var id: String
var name: String
var color: Color
var max_hp: int
var hp: int
var position: int           # 1..4，1=前线；0=已倒下/离场
var draw_pile: Array = []
var hand: Array = []
var discard_pile: Array = []
var is_down: bool = false
var last_reshuffled: bool = false  # 上次 draw 是否触发了洗回
# 附魔列表（§4）：{type, mag, charges}。charges=-1=永久；>0=次数，用尽移除。
# 附魔是机制层；下面的 *_markers() 只是显示层，不持有独立数据。
var enchantments: Array = []

func _init(p_id: String, p_name: String, p_color: Color, p_max_hp: int, p_pos: int) -> void:
	id = p_id
	name = p_name
	color = p_color
	max_hp = p_max_hp
	hp = p_max_hp
	position = p_pos

# 抽 n 张。打出无限制、打一张补一张；抽牌堆空 → 弃牌堆整叠放回抽牌堆。
# （这里用“反转”代替随机洗，保持确定性、便于调试；
# 属输入端随机，正式版可换带种子的洗牌，见 README 待删改点）。
func draw(n: int) -> Array:
	last_reshuffled = false
	var drawn: Array = []
	for i in n:
		if hand.size() >= HAND_LIMIT:
			break
		if draw_pile.is_empty():
			if discard_pile.is_empty():
				break
			draw_pile = discard_pile.duplicate()
			discard_pile.clear()
			draw_pile.reverse()
			last_reshuffled = true
		var c = draw_pile.pop_back()
		hand.append(c)
		drawn.append(c)
	return drawn

# ---- 附魔（§4）----
func add_enchantment(p_type: String, p_mag: int, p_charges: int) -> void:
	# 同类型附魔叠加：合并 mag，次数型累加 charges
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

# 显示层：把附魔汇总成标记（不持有独立数据，只反映附魔）
func enchant_markers() -> Array:
	var out: Array = []
	var s := get_strength()
	if s != 0:
		out.append({ "label": "力+%d" % s, "color": Color(1.0, 0.8, 0.3) })
	return out

func clone() -> Character:
	var c = Character.new(id, name, color, max_hp, position)
	c.hp = hp
	c.is_down = is_down
	c.enchantments = enchantments.duplicate(true)
	c.draw_pile = draw_pile.duplicate()
	c.hand = hand.duplicate()
	c.discard_pile = discard_pile.duplicate()
	c.last_reshuffled = last_reshuffled
	return c
