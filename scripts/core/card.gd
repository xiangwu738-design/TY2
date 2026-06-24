# 牌定义（不可变数据 + 一个 effect 回调）。
# MVP 阶段：牌是共享的不可变定义，手牌/牌堆里只存引用。
# 将来“附魔”需要每张牌的独立状态时，再引入 Card 实例层（见 README 待删改点）。
class_name CardDef
extends RefCounted

var id: String
var name: String
var occupancy: int        # 占位 = 推进格数 = 时间成本（见 §1）
var kind: int             # CoreEnums.Kind
var effect: Callable      # func(gs: GameState, caster: Character) -> void
var desc: String          # 给 UI 看的一句话说明

func _init(p_id: String, p_name: String, p_occupancy: int, p_kind: int, p_effect: Callable, p_desc: String = "") -> void:
	id = p_id
	name = p_name
	occupancy = p_occupancy
	kind = p_kind
	effect = p_effect
	desc = p_desc
