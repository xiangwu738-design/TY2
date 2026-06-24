# 核心枚举。不依赖任何 Godot 节点，纯数据。
class_name CoreEnums
extends RefCounted

# 牌种类
enum Kind { ATTACK, SKILL }

# 敌人节点类型（敌人攻击三型，见规则 §3 / §5）
#   SLASH  斩击 → 打 1 位（前线）
#   THRUST 突刺 → 打 2 位
#   STRIKE 打击 → 打全体
#   CHARGE 蓄力 → 敌人获得力量（敌人侧附魔，经编排触发）
enum NodeType { SLASH, THRUST, STRIKE, CHARGE }

# 事件种类：内核结算产生的“事件流”，供 UI 渲染与预演共用。
enum EventKind {
	PLAY_CARD,            # 出牌（推进前）
	CARD_LAND,            # 牌效果在占位末尾落地
	SKIP,                 # 空过
	NODE_TRIGGER,         # 敌人节点触发
	DAMAGE_DEALT_CHAR,    # 角色受伤
	DAMAGE_DEALT_ENEMY,   # 敌人受伤
	HEAL,                 # 治疗
	POSITION_CHANGE,      # 站位变化
	CARD_DRAWN,           # 抽牌
	CARD_DISCARDED,       # 弃牌
	PILE_RESHUFFLED,      # 弃牌堆洗回抽牌堆
	CHAR_DOWN,            # 角色倒下
	ENCHANT_APPLIED,      # 附魔施加（§4）
	ENEMY_DEAD,           # 敌人死亡
	BATTLE_END,           # 战斗结束
}
