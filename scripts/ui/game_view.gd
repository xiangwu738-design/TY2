# =============================================================================
# GameView —— 一个完整的对局视图（战场 + 时间轴 + 本角色牌 + 预览 + 日志）。
# 四窗口模式下每个角色窗口放一个 GameView，各自独立（独立悬停预览、独立 OS 窗口）。
# 仅渲染 + 转发动作，规则逻辑全在 controller（main.gd）/内核里。
# =============================================================================
class_name GameView
extends Control

var controller: Node
var char_id: String = ""          # 本视图所归属/当前操控的角色
var allow_switch: bool = false    # 单窗口模式：点头像切换角色

# 本视图自己的悬停预览状态（窗口间独立）
var hover_action: Dictionary = {}
var hover_preview: Dictionary = {}

var top_label: Label
var battle_field: HBoxContainer
var timeline_row: HBoxContainer
var preview_label: Label
var log_text: RichTextLabel
var active_label: Label
var hand_flow: HFlowContainer
var btns_box: HBoxContainer

func setup(p_controller: Node, p_char_id: String, p_allow_switch: bool = false) -> void:
	controller = p_controller
	char_id = p_char_id
	allow_switch = p_allow_switch
	_build()
	render()

func append_log(s: String) -> void:
	log_text.append_text(s + "\n")

func clear_log() -> void:
	log_text.clear()

# ---------------------------------------------------------------------------
# 布局：战场（上）→ 时间轴 → 牌（下）→ 日志
# ---------------------------------------------------------------------------
func _build() -> void:
	set_anchors_preset(Control.PRESET_FULL_RECT)
	var margin = MarginContainer.new()
	margin.set_anchors_preset(Control.PRESET_FULL_RECT)
	margin.add_theme_constant_override("margin_left", 8)
	margin.add_theme_constant_override("margin_right", 8)
	margin.add_theme_constant_override("margin_top", 6)
	margin.add_theme_constant_override("margin_bottom", 6)
	add_child(margin)
	var scroll = ScrollContainer.new()
	scroll.set_v_size_flags(Control.SIZE_EXPAND_FILL)
	scroll.set_h_size_flags(Control.SIZE_EXPAND_FILL)
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	scroll.vertical_scroll_mode = ScrollContainer.SCROLL_MODE_AUTO
	margin.add_child(scroll)
	var content = VBoxContainer.new()
	content.set_h_size_flags(Control.SIZE_EXPAND_FILL)
	content.add_theme_constant_override("separation", 8)
	scroll.add_child(content)

	# 顶栏
	var topbar = HBoxContainer.new()
	topbar.add_theme_constant_override("separation", 8)
	content.add_child(topbar)
	top_label = Label.new()
	top_label.set_h_size_flags(Control.SIZE_EXPAND_FILL)
	top_label.add_theme_font_size_override("font_size", 14)
	topbar.add_child(top_label)
	var nb = Button.new()
	nb.text = "新局"
	nb.pressed.connect(controller.new_game)
	topbar.add_child(nb)

	# 战场
	content.add_child(_section_label("战场（… 位1 前线 ··· 敌人）"))
	battle_field = HBoxContainer.new()
	battle_field.add_theme_constant_override("separation", 6)
	battle_field.set_h_size_flags(Control.SIZE_EXPAND_FILL)
	content.add_child(battle_field)

	# 时间轴
	content.add_child(_section_label("时间轴 / 行动条（▶ 当前指针；彩=将推进·各角色色，红⚠=将触发）"))
	var tl = ScrollContainer.new()
	tl.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_AUTO
	tl.vertical_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	tl.custom_minimum_size = Vector2(0, 100)
	content.add_child(tl)
	timeline_row = HBoxContainer.new()
	timeline_row.add_theme_constant_override("separation", 4)
	timeline_row.set_v_size_flags(Control.SIZE_EXPAND_FILL)
	tl.add_child(timeline_row)

	# 牌区（在下）
	content.add_child(_section_label("行动（牌）" + ("—— 点上方头像切换角色" if allow_switch else "")))
	active_label = Label.new()
	active_label.add_theme_font_size_override("font_size", 13)
	content.add_child(active_label)
	hand_flow = HFlowContainer.new()
	hand_flow.add_theme_constant_override("separation", 6)
	hand_flow.add_theme_constant_override("v_separation", 6)
	content.add_child(hand_flow)
	btns_box = HBoxContainer.new()
	btns_box.add_theme_constant_override("separation", 6)
	content.add_child(btns_box)
	content.add_child(_make_preview_panel())

	# 日志
	content.add_child(_section_label("事件日志"))
	log_text = RichTextLabel.new()
	log_text.custom_minimum_size = Vector2(0, 110)
	log_text.bbcode_enabled = true
	log_text.scroll_following = true
	log_text.add_theme_font_size_override("font_size", 12)
	content.add_child(log_text)

func _make_preview_panel() -> Panel:
	var pp = Panel.new()
	pp.custom_minimum_size = Vector2(0, 56)
	var psb = StyleBoxFlat.new()
	psb.bg_color = Color(0.14, 0.16, 0.20)
	psb.border_width_left = 3
	psb.border_width_top = 1
	psb.border_width_right = 1
	psb.border_width_bottom = 1
	psb.border_color = Color(0.45, 0.5, 0.6)
	psb.content_margin_left = 8
	psb.content_margin_right = 8
	psb.content_margin_top = 5
	psb.content_margin_bottom = 5
	pp.add_theme_stylebox_override("panel", psb)
	preview_label = Label.new()
	preview_label.set_anchors_preset(Control.PRESET_FULL_RECT)
	preview_label.add_theme_constant_override("offset_left", 8)
	preview_label.add_theme_constant_override("offset_right", -8)
	preview_label.add_theme_constant_override("offset_top", 5)
	preview_label.add_theme_constant_override("offset_bottom", -5)
	preview_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	preview_label.add_theme_font_size_override("font_size", 12)
	pp.add_child(preview_label)
	return pp

func _section_label(text: String) -> Label:
	var l = Label.new()
	l.text = text
	l.add_theme_font_size_override("font_size", 12)
	l.add_theme_color_override("font_color", Color(0.75, 0.78, 0.85))
	return l

# ---------------------------------------------------------------------------
# 本视图独立的悬停预览
# ---------------------------------------------------------------------------
func set_hover(action: Dictionary) -> void:
	var g = controller.gs
	if g.battle_over:
		return
	hover_action = action
	if action.get("type") == "card":
		var c: Character = g.get_char_by_id(action.get("char_id", ""))
		if c == null or action.get("card_idx", -1) >= c.hand.size():
			hover_action = {}
			hover_preview = {}
			render_hover()
			return
	hover_preview = g.preview_action(action)
	render_hover()

func clear_hover() -> void:
	hover_action = {}
	hover_preview = {}
	render_hover()

func action_occupancy(a: Dictionary) -> int:
	match a.get("type"):
		"card":
			var c: Character = controller.gs.get_char_by_id(a.get("char_id", ""))
			if c == null or a.get("card_idx", -1) >= c.hand.size():
				return 0
			return c.hand[a.card_idx].occupancy
		"skip": return 1
	return 0

# ---------------------------------------------------------------------------
# 渲染
# ---------------------------------------------------------------------------
func render() -> void:
	_render_top()
	_render_battle_field()
	_render_timeline()
	_render_hand_area()
	_render_preview()

func render_hover() -> void:
	_render_timeline()
	_render_preview()

func _render_top() -> void:
	var g = controller.gs
	top_label.text = "动作%d · 格%d · %s" % [
		g.action_count, g.pointer,
		("结束—" + ("胜" if g.battle_won else "败")) if g.battle_over else "进行中"]

func _render_battle_field() -> void:
	for ch in battle_field.get_children():
		ch.queue_free()
	var g = controller.gs
	# 只渲染存活人数个位置：N 人 → 位 N … 位1（阵亡后阵型向前收缩补位）
	var n := 0
	for cc in g.characters:
		if not cc.is_down:
			n += 1
	for pos in range(n, 0, -1):
		var c: Character = g.live_char_at(pos)
		if c != null:
			battle_field.add_child(_make_portrait(c, pos, c.id == char_id))
		else:
			battle_field.add_child(_make_empty_slot(pos))
	var spacer = Control.new()
	spacer.set_h_size_flags(Control.SIZE_EXPAND_FILL)
	battle_field.add_child(spacer)
	var arrow = Label.new()
	arrow.text = "⚔"
	arrow.add_theme_font_size_override("font_size", 20)
	battle_field.add_child(arrow)
	battle_field.add_child(_make_enemy_block())

func _make_portrait(c: Character, pos: int, is_active: bool) -> PanelContainer:
	var p = PanelContainer.new()
	p.custom_minimum_size = Vector2(96, 90)
	var sb = StyleBoxFlat.new()
	sb.bg_color = c.color.darkened(0.72) if not is_active else c.color.darkened(0.45)
	sb.border_width_left = 3
	sb.border_width_right = 3
	sb.border_width_top = 3
	sb.border_width_bottom = 3
	sb.border_color = Color.WHITE if is_active else c.color
	sb.corner_radius_top_left = 4
	sb.corner_radius_top_right = 4
	sb.corner_radius_bottom_left = 4
	sb.corner_radius_bottom_right = 4
	sb.content_margin_left = 5
	sb.content_margin_right = 5
	sb.content_margin_top = 3
	sb.content_margin_bottom = 3
	p.add_theme_stylebox_override("panel", sb)
	var vb = VBoxContainer.new()
	vb.add_theme_constant_override("separation", 1)
	p.add_child(vb)
	var nl = Label.new()
	nl.text = ("▶ %s" % c.name) if is_active else c.name
	nl.add_theme_color_override("font_color", c.color.lightened(0.35))
	nl.add_theme_font_size_override("font_size", 13)
	if c.is_down:
		nl.text = "%s [倒]" % c.name
		nl.add_theme_color_override("font_color", Color.DIM_GRAY)
	vb.add_child(nl)
	var pl = Label.new()
	pl.text = "位%d" % pos
	pl.add_theme_font_size_override("font_size", 10)
	pl.add_theme_color_override("font_color", Color.DIM_GRAY)
	vb.add_child(pl)
	var bar = ProgressBar.new()
	bar.min_value = 0
	bar.max_value = c.max_hp
	bar.value = c.hp
	bar.custom_minimum_size = Vector2(74, 0)
	vb.add_child(bar)
	var hl = Label.new()
	hl.text = "HP %d" % c.hp
	hl.add_theme_font_size_override("font_size", 10)
	vb.add_child(hl)
	vb.add_child(_markers_row(c.enchant_markers()))
	if allow_switch and not c.is_down:
		p.mouse_filter = Control.MOUSE_FILTER_STOP
		var cid = c.id
		p.gui_input.connect(func(e):
			if e is InputEventMouseButton and e.pressed and e.button_index == MOUSE_BUTTON_LEFT:
				controller.set_active(cid))
	return p

func _make_empty_slot(pos: int) -> PanelContainer:
	var p = PanelContainer.new()
	p.custom_minimum_size = Vector2(96, 90)
	var sb = StyleBoxFlat.new()
	sb.bg_color = Color(0.10, 0.10, 0.12)
	sb.corner_radius_top_left = 4
	sb.corner_radius_top_right = 4
	sb.corner_radius_bottom_left = 4
	sb.corner_radius_bottom_right = 4
	p.add_theme_stylebox_override("panel", sb)
	var l = Label.new()
	l.text = "位%d\n—" % pos
	l.add_theme_color_override("font_color", Color.DIM_GRAY)
	l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	p.add_child(l)
	return p

func _make_enemy_block() -> PanelContainer:
	var p = PanelContainer.new()
	p.custom_minimum_size = Vector2(130, 90)
	var g = controller.gs
	var sb = StyleBoxFlat.new()
	sb.bg_color = Color(0.20, 0.10, 0.10)
	sb.border_width_left = 3
	sb.border_width_right = 3
	sb.border_width_top = 3
	sb.border_width_bottom = 3
	sb.border_color = Color(0.85, 0.35, 0.35)
	sb.corner_radius_top_left = 4
	sb.corner_radius_top_right = 4
	sb.corner_radius_bottom_left = 4
	sb.corner_radius_bottom_right = 4
	sb.content_margin_left = 5
	sb.content_margin_right = 5
	sb.content_margin_top = 3
	sb.content_margin_bottom = 3
	p.add_theme_stylebox_override("panel", sb)
	var vb = VBoxContainer.new()
	vb.add_theme_constant_override("separation", 1)
	p.add_child(vb)
	var nl = Label.new()
	nl.text = g.enemy.name
	nl.add_theme_color_override("font_color", Color(1.0, 0.6, 0.6))
	nl.add_theme_font_size_override("font_size", 14)
	vb.add_child(nl)
	var bar = ProgressBar.new()
	bar.min_value = 0
	bar.max_value = g.enemy.max_hp
	bar.value = g.enemy.hp
	bar.custom_minimum_size = Vector2(104, 0)
	vb.add_child(bar)
	var hl = Label.new()
	hl.text = "HP %d/%d" % [g.enemy.hp, g.enemy.max_hp]
	hl.add_theme_font_size_override("font_size", 10)
	vb.add_child(hl)
	vb.add_child(_markers_row(g.enemy.enchant_markers()))
	return p

# 标记行（显示层，只反映附魔，§4）
func _markers_row(markers: Array) -> HBoxContainer:
	var hb = HBoxContainer.new()
	hb.add_theme_constant_override("separation", 3)
	for m in markers:
		var l = Label.new()
		l.text = m.label
		l.add_theme_color_override("font_color", m.color)
		l.add_theme_font_size_override("font_size", 10)
		hb.add_child(l)
	if markers.is_empty():
		var l = Label.new()
		l.text = " "
		l.add_theme_font_size_override("font_size", 10)
		hb.add_child(l)
	return hb

func _render_hand_area() -> void:
	var g = controller.gs
	var c: Character = g.get_char_by_id(char_id)
	if c == null:
		return
	if c.is_down:
		active_label.text = "（%s 已倒下）" % c.name
		active_label.add_theme_color_override("font_color", Color.DIM_GRAY)
		_clear_container(hand_flow)
		_clear_container(btns_box)
		return
	active_label.text = "%s（位%d · HP %d/%d · 抽%d 弃%d 手%d）" % [c.name, c.position, c.hp, c.max_hp, c.draw_pile.size(), c.discard_pile.size(), c.hand.size()]
	active_label.add_theme_color_override("font_color", c.color)
	_clear_container(hand_flow)
	for i in c.hand.size():
		var card: CardDef = c.hand[i]
		var btn = Button.new()
		btn.text = "%s 占%d" % [card.name, card.occupancy]
		btn.custom_minimum_size = Vector2(80, 36)
		btn.add_theme_font_size_override("font_size", 12)
		var action = {"type": "card", "char_id": char_id, "card_idx": i}
		btn.mouse_entered.connect(set_hover.bind(action))
		btn.mouse_exited.connect(clear_hover)
		btn.pressed.connect(controller.do_action.bind(action))
		hand_flow.add_child(btn)
	_clear_container(btns_box)
	var sb = Button.new()
	sb.text = "空过"
	sb.custom_minimum_size = Vector2(72, 30)
	sb.add_theme_font_size_override("font_size", 12)
	var sa = {"type": "skip", "char_id": char_id}
	sb.mouse_entered.connect(set_hover.bind(sa))
	sb.mouse_exited.connect(clear_hover)
	sb.pressed.connect(controller.do_action.bind(sa))
	btns_box.add_child(sb)

func _clear_container(cont: Control) -> void:
	for ch in cont.get_children():
		ch.queue_free()

func _render_timeline() -> void:
	for ch in timeline_row.get_children():
		ch.queue_free()
	var g = controller.gs
	if g.pointer < 0:
		timeline_row.add_child(_label_box("▶起跑", Color.WHITE_SMOKE))
	var start = max(0, g.pointer)
	var trav = _traversed_set()
	var trig = _triggered_preview_set()
	var pcolor := _preview_color()
	for i in range(13):
		timeline_row.add_child(_make_cell(start + i, trav, trig, pcolor))

func _make_cell(cell: int, trav: Dictionary, trig: Dictionary, preview_color: Color) -> Panel:
	var g = controller.gs
	var p = Panel.new()
	p.custom_minimum_size = Vector2(66, 92)
	var sb = StyleBoxFlat.new()
	sb.corner_radius_top_left = 4
	sb.corner_radius_top_right = 4
	sb.corner_radius_bottom_left = 4
	sb.corner_radius_bottom_right = 4
	var bg = Color(0.12, 0.12, 0.15)
	if cell == g.pointer:
		bg = Color(0.30, 0.30, 0.42)
	if trav.has(cell):
		bg = preview_color
	sb.bg_color = bg
	p.add_theme_stylebox_override("panel", sb)
	var vb = VBoxContainer.new()
	vb.set_v_size_flags(Control.SIZE_EXPAND_FILL)
	vb.add_theme_constant_override("separation", 2)
	p.add_child(vb)
	var l1 = Label.new()
	l1.text = ("▶格%d" % cell) if cell == g.pointer else ("格%d" % cell)
	l1.add_theme_font_size_override("font_size", 11)
	vb.add_child(l1)
	var node = g.enemy.node_at(cell)
	if node != null:
		var nl = Label.new()
		nl.add_theme_font_size_override("font_size", 12)
		var col = _node_color(node.type)
		if g.cell_triggered(cell):
			col = col.darkened(0.6)
			nl.text = "·%s" % node.label
		else:
			nl.text = node.label
		nl.add_theme_color_override("font_color", col)
		vb.add_child(nl)
	if trig.has(cell):
		var t = Label.new()
		t.text = "⚠触发"
		t.add_theme_color_override("font_color", Color(1.0, 0.35, 0.35))
		t.add_theme_font_size_override("font_size", 10)
		vb.add_child(t)
	return p

func _node_color(t: int) -> Color:
	match t:
		CoreEnums.NodeType.SLASH:  return Color(1.0, 0.35, 0.35)
		CoreEnums.NodeType.THRUST: return Color(0.95, 0.65, 0.25)
		CoreEnums.NodeType.STRIKE: return Color(0.70, 0.45, 0.95)
		CoreEnums.NodeType.CHARGE: return Color(0.55, 0.85, 0.55)   # 蓄力 绿
	return Color.WHITE

func _label_box(text: String, col: Color) -> Panel:
	var p = Panel.new()
	p.custom_minimum_size = Vector2(66, 92)
	var sb = StyleBoxFlat.new()
	sb.bg_color = Color(0.18, 0.18, 0.22)
	p.add_theme_stylebox_override("panel", sb)
	var l = Label.new()
	l.text = text
	l.add_theme_color_override("font_color", col)
	p.add_child(l)
	return p

func _traversed_set() -> Dictionary:
	var d: Dictionary = {}
	if hover_action.is_empty():
		return d
	var occ: int = action_occupancy(hover_action)
	for i in range(1, occ + 1):
		d[controller.gs.pointer + i] = true
	return d

func _triggered_preview_set() -> Dictionary:
	var d: Dictionary = {}
	if hover_preview.is_empty():
		return d
	for c in hover_preview.get("triggered_cells", []):
		d[c] = true
	return d

# 预占位高亮色 = 当前预演角色的颜色（每角色不同）；无预演时回退灰黄。
func _preview_color() -> Color:
	if hover_action.is_empty():
		return Color(0.52, 0.46, 0.16)
	var c: Character = controller.gs.get_char_by_id(hover_action.get("char_id", ""))
	if c != null:
		return c.color.darkened(0.5)
	return Color(0.52, 0.46, 0.16)

func _render_preview() -> void:
	if hover_action.is_empty() or hover_preview.is_empty():
		preview_label.text = "（悬停手牌 / 空过 查看预计后果）"
		preview_label.add_theme_color_override("font_color", Color.DIM_GRAY)
		return
	preview_label.remove_theme_color_override("font_color")
	var g = controller.gs
	var occ: int = action_occupancy(hover_action)
	var head = ""
	match hover_action.get("type"):
		"card":
			var c: Character = g.get_char_by_id(hover_action.get("char_id", ""))
			if c != null and hover_action.get("card_idx", -1) < c.hand.size():
				var card: CardDef = c.hand[hover_action.card_idx]
				head = "%s · 占%d→格%d" % [card.name, occ, g.pointer + occ]
		"skip": head = "空过 · →格%d" % (g.pointer + occ)
	var nodes: Array = []
	var bonus := ""
	for e in hover_preview.get("events", []):
		if e.kind == CoreEnums.EventKind.NODE_TRIGGER:
			nodes.append(e.data.label)
		elif e.kind == CoreEnums.EventKind.DAMAGE_DEALT_ENEMY:
			var b = e.data.get("bonus", "")
			if b != "":
				bonus = "（含%s）" % b
	var parts: Array = [head]
	if nodes.size() > 0:
		parts.append("触发: " + ", ".join(nodes))
	var dmg: Array = []
	for ca in hover_preview.get("chars_after", []):
		var before: Character = g.get_char_by_id(ca.id)
		if before != null and ca.hp < before.hp:
			dmg.append("%s -%d" % [before.name, before.hp - ca.hp])
	if dmg.size() > 0:
		parts.append("受伤: " + ", ".join(dmg))
	if hover_preview.get("enemy_hp_after", g.enemy.hp) < g.enemy.hp:
		parts.append("敌HP %d→%d%s" % [g.enemy.hp, hover_preview.enemy_hp_after, bonus])
	if hover_preview.get("over", false):
		parts.append("【%s】" % ("胜" if hover_preview.get("won", false) else "败"))
	preview_label.text = "  ".join(parts)
