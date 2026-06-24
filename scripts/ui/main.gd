# =============================================================================
# 控制器 + 联机（main.gd）—— 主机权威 + 动作同步（§9）。
#
# 拓扑：每进程 = 一个玩家。
#   - 主机(host)：持有权威 GameState，开 ENet 服务，玩 c0；执行动作后广播给所有客户端。
#   - 客户端(client)：连主机，本地维护镜像 gs（同种子 + 重放广播动作），玩被分配的角色；
#     出牌/空过 → RPC 提交主机 → 主机执行并广播 → 各端确定性重放。
#   - 单机(offline)：一窗控制全部 4 角色（点头像切换），无联机。
#
# 启动菜单：开主局 / 加入(IP) / 单机试玩。也支持 CLI：--host / --client <ip> / --offline。
# =============================================================================
extends Control

const PORT: int = 5005
const SEED: int = 12345

enum Mode { MENU, OFFLINE, HOST, CLIENT }

var gs: GameState
var _mode: int = Mode.MENU
var _view: GameView
var my_char_id: String = "c0"
var _debug: bool = false

# 联机
var action_history: Array = []     # 主机记录全部动作，供中途加入者重放
var _next_client_char: int = 1     # 主机分配：c1,c2,c3
var _menu_status: Label
var _ip_edit: LineEdit

func _ready() -> void:
	_debug = "--debugnet" in OS.get_cmdline_args()
	get_window().min_size = Vector2i(420, 360)
	get_window().size = Vector2i(480, 380)
	get_window().title = "同渊 · 联机"
	# CLI 快捷
	if "--host" in OS.get_cmdline_args():
		_start_host()
	elif "--offline" in OS.get_cmdline_args():
		_start_offline()
	else:
		var args = OS.get_cmdline_args()
		var ci = args.find("--client")
		if ci >= 0 and ci + 1 < args.size():
			_start_client(args[ci + 1])
		else:
			_show_menu()

# ---------------------------------------------------------------------------
# 启动菜单
# ---------------------------------------------------------------------------
func _show_menu() -> void:
	_mode = Mode.MENU
	var c = VBoxContainer.new()
	c.set_anchors_preset(Control.PRESET_FULL_RECT)
	c.add_theme_constant_override("separation", 12)
	c.alignment = BoxContainer.ALIGNMENT_CENTER
	var mg = MarginContainer.new()
	mg.add_theme_constant_override("margin_left", 40)
	mg.add_theme_constant_override("margin_right", 40)
	mg.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(mg)
	var mc = CenterContainer.new()
	mg.add_child(mc)
	mc.add_child(c)
	var t = Label.new()
	t.text = "《同渊》内核 MVP"
	t.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	t.add_theme_font_size_override("font_size", 22)
	c.add_child(t)
	var host_btn = Button.new()
	host_btn.text = "开主局（局域网）"
	host_btn.pressed.connect(_start_host)
	c.add_child(host_btn)
	c.add_child(_spacer())
	var join_row = HBoxContainer.new()
	join_row.add_theme_constant_override("separation", 6)
	_ip_edit = LineEdit.new()
	_ip_edit.text = "127.0.0.1"
	_ip_edit.placeholder_text = "主机 IP"
	_ip_edit.custom_minimum_size = Vector2(180, 0)
	join_row.add_child(_ip_edit)
	var join_btn = Button.new()
	join_btn.text = "加入"
	join_btn.pressed.connect(func(): _start_client(_ip_edit.text.strip_edges()))
	join_row.add_child(join_btn)
	c.add_child(join_row)
	c.add_child(_spacer())
	var off_btn = Button.new()
	off_btn.text = "单机试玩（本地 4 角色）"
	off_btn.pressed.connect(_start_offline)
	c.add_child(off_btn)
	c.add_child(_spacer())
	_menu_status = Label.new()
	_menu_status.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_menu_status.add_theme_color_override("font_color", Color(0.7, 0.75, 0.8))
	c.add_child(_menu_status)

func _spacer() -> Control:
	var s = Control.new()
	s.custom_minimum_size = Vector2(0, 6)
	return s

func _clear_menu() -> void:
	for ch in get_children():
		ch.queue_free()

# ---------------------------------------------------------------------------
# 三种启动
# ---------------------------------------------------------------------------
func _start_offline() -> void:
	_mode = Mode.OFFLINE
	gs = GameState.new()
	gs.setup(SEED)
	_clear_menu()
	_open_window("同渊 · 单机")
	_build_view("c0", true)
	_log("[b]==== 单机试玩 · 种子 %d ====[/b]" % SEED)
	_view.render()

func _start_host() -> void:
	_mode = Mode.HOST
	my_char_id = "c0"
	var peer = ENetMultiplayerPeer.new()
	var err = peer.create_server(PORT, 4)
	if err != OK:
		_show_menu()
		_menu_status.text = "开服失败: %d" % err
		return
	multiplayer.multiplayer_peer = peer
	multiplayer.peer_connected.connect(_on_peer_connected)
	multiplayer.peer_disconnected.connect(_on_peer_disconnected)
	gs = GameState.new()
	gs.setup(SEED)
	action_history.clear()
	_next_client_char = 1
	_clear_menu()
	_open_window("同渊 · 主机(甲)")
	_build_view("c0", false)
	var ips = _lan_ips()
	_log("[b]==== 主局已开 · 端口 %d · 你=甲 ====[/b]" % PORT)
	_log("本机 IP: " + (", ".join(ips) if ips.size() > 0 else "?"))
	_log("等待玩家加入…")
	_view.render()

func _start_client(ip: String) -> void:
	_mode = Mode.CLIENT
	var peer = ENetMultiplayerPeer.new()
	var err = peer.create_client(ip, PORT)
	if err != OK:
		_show_menu()
		_menu_status.text = "连接失败(本地): %d" % err
		return
	multiplayer.multiplayer_peer = peer
	multiplayer.connected_to_server.connect(_on_connected)
	multiplayer.connection_failed.connect(_on_conn_failed)
	multiplayer.server_disconnected.connect(_on_server_disc)
	_clear_menu()
	_open_window("同渊 · 加入中…")
	# 临时等待界面
	var l = Label.new()
	l.text = "连接 %s:%d …" % [ip, PORT]
	l.set_anchors_preset(Control.PRESET_CENTER)
	add_child(l)
	_menu_status = l

func _open_window(title: String) -> void:
	get_window().title = title
	get_window().size = Vector2i(760, 720)
	get_window().min_size = Vector2i(560, 480)

func _build_view(char_id: String, allow_switch: bool) -> void:
	_view = GameView.new()
	_view.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(_view)
	_view.setup(self, char_id, allow_switch)

# ---------------------------------------------------------------------------
# 动作路由（GameView 调用入口）
# ---------------------------------------------------------------------------
func do_action(action: Dictionary) -> void:
	if gs == null or gs.battle_over:
		return
	match _mode:
		Mode.OFFLINE: _apply_action(action)
		Mode.HOST: _host_execute(action)
		Mode.CLIENT: rpc_id(1, "submit_action", action)

func set_active(cid: String) -> void:
	# 仅单机模式切换当前操控角色
	if _mode != Mode.OFFLINE:
		return
	my_char_id = cid
	if _view:
		_view.char_id = cid
		_view.render()

func new_game() -> void:
	match _mode:
		Mode.OFFLINE:
			gs = GameState.new()
			gs.setup(SEED)
			if _view:
				_view.clear_log()
			_log("[b]==== 新局 · 种子 %d ====[/b]" % SEED)
			_view.render()
		Mode.HOST:
			gs = GameState.new()
			gs.setup(SEED)
			action_history.clear()
			if _view:
				_view.clear_log()
			_log("[b]==== 新局 · 种子 %d ====[/b]" % SEED)
			_view.render()
			rpc("broadcast_new_game")
		Mode.CLIENT:
			rpc_id(1, "request_new_game")

# ---------------------------------------------------------------------------
# 本地执行 + 日志 + 渲染（主机与客户端共用，确定性重放）
# ---------------------------------------------------------------------------
func _exec(action: Dictionary) -> Array:
	var c: Character = gs.get_char_by_id(action.get("char_id", ""))
	if c == null:
		return []
	match action.get("type"):
		"card":
			if action.get("card_idx", -1) < c.hand.size():
				return gs.play_card(c, action.card_idx)
		"skip": return gs.skip(c)
	return []

func _apply_action(action: Dictionary, silent: bool = false) -> void:
	var ev = _exec(action)
	for e in ev:
		if not silent:
			_log(_fmt_event(e))
	if _view:
		_view.render()
	if _debug:
		print("[%s] apply %s pointer=%d" % [mode_name(), action, gs.pointer])

func _host_execute(action: Dictionary) -> void:
	# 主机权威执行 + 广播
	action_history.append(action)
	_apply_action(action)
	rpc("broadcast_action", action)

func mode_name() -> String:
	match _mode:
		Mode.HOST: return "HOST"
		Mode.CLIENT: return "CLIENT"
		Mode.OFFLINE: return "OFFLINE"
	return "MENU"

# ---------------------------------------------------------------------------
# 联机：客户端回调
# ---------------------------------------------------------------------------
func _on_connected() -> void:
	rpc_id(1, "request_join")
	if _menu_status:
		_menu_status.text = "已连接，等待分配角色…"

func _on_conn_failed() -> void:
	multiplayer.multiplayer_peer = null
	_show_menu()
	_menu_status.text = "连接失败"

func _on_server_disc() -> void:
	multiplayer.multiplayer_peer = null
	_show_menu()
	_menu_status.text = "与主机断开"

func _on_peer_connected(pid: int) -> void:
	# 主机：客户端连入；角色分配在 request_join 时进行
	if _debug:
		print("HOST peer_connected ", pid)

func _on_peer_disconnected(pid: int) -> void:
	if _debug:
		print("HOST peer_disconnected ", pid)

# ---------------------------------------------------------------------------
# RPC
# ---------------------------------------------------------------------------
# 客户端 → 主机：请求加入
@rpc("any_peer", "call_remote", "reliable")
func request_join() -> void:
	if _mode != Mode.HOST:
		return
	var pid = multiplayer.get_remote_sender_id()
	if _next_client_char > 3:
		rpc_id(pid, "init_client", -1, "", [])
		return
	var cid = "c%d" % _next_client_char
	_next_client_char += 1
	rpc_id(pid, "init_client", SEED, cid, action_history.duplicate(true))
	_log("[color=#80ff80]%s 加入了（位 %s）[/color]" % [cid, cid])
	if _view:
		_view.render()

# 主机 → 客户端：分配角色 + 同步历史
@rpc("authority", "call_remote", "reliable")
func init_client(p_seed: int, char_id: String, history: Array) -> void:
	if _mode != Mode.CLIENT:
		return
	if p_seed == -1:
		_clear_menu()
		_show_menu()
		_menu_status.text = "房间已满"
		return
	# 建立本地镜像并重放历史
	gs = GameState.new()
	gs.setup(p_seed)
	my_char_id = char_id
	_clear_menu()
	_open_window("同渊 · %s" % _char_label(char_id))
	_build_view(char_id, false)
	for a in history:
		_apply_action(a, true)
	_log("[b]==== 已加入 · 你=%s · 同步 %d 步 ====[/b]" % [_char_label(char_id), history.size()])
	_view.render()

# 客户端 → 主机：提交动作
@rpc("any_peer", "call_remote", "reliable")
func submit_action(action: Dictionary) -> void:
	if _mode != Mode.HOST:
		return
	if gs == null or gs.battle_over:
		return
	_host_execute(action)

# 主机 → 客户端：广播已执行动作（确定性重放）
@rpc("authority", "call_remote", "reliable")
func broadcast_action(action: Dictionary) -> void:
	if _mode != Mode.CLIENT:
		return
	if gs == null:
		return
	_apply_action(action)

# 客户端 → 主机：请求新局
@rpc("any_peer", "call_remote", "reliable")
func request_new_game() -> void:
	if _mode != Mode.HOST:
		return
	new_game()

# 主机 → 客户端：新局
@rpc("authority", "call_remote", "reliable")
func broadcast_new_game() -> void:
	if _mode != Mode.CLIENT:
		return
	gs = GameState.new()
	gs.setup(SEED)
	if _view:
		_view.clear_log()
	_log("[b]==== 新局 · 种子 %d ====[/b]" % SEED)
	_view.render()

# ---------------------------------------------------------------------------
# 辅助
# ---------------------------------------------------------------------------
func _lan_ips() -> Array:
	var out: Array = []
	for a in IP.get_local_addresses():
		var s = str(a)
		if s.begins_with("127.") or s.contains(":"):
			continue
		out.append(s)
	return out

func _char_label(cid: String) -> String:
	var names = {"c0": "甲", "c1": "乙", "c2": "丙", "c3": "丁"}
	return names.get(cid, cid)

func _log(s: String) -> void:
	if _view:
		_view.append_log(s + "\n")

func _cname(id: String) -> String:
	if gs == null:
		return id
	var c: Character = gs.get_char_by_id(id)
	return c.name if c != null else id

func _fmt_event(e: Dictionary) -> String:
	var k: int = e.kind
	var d: Dictionary = e.data
	match k:
		CoreEnums.EventKind.PLAY_CARD:        return "▸ %s 出牌 [b]%s[/b]（占位%d）" % [_cname(d.char_id), d.card_name, d.occupancy]
		CoreEnums.EventKind.CARD_LAND:        return "    ↳ 落点：%s 生效" % d.card_name
		CoreEnums.EventKind.SKIP:             return "▸ %s 空过" % _cname(d.char_id)
		CoreEnums.EventKind.NODE_TRIGGER:     return "    [color=#ff8080]格%d 敌节点 %s 触发[/color]" % [d.cell, d.label]
		CoreEnums.EventKind.DAMAGE_DEALT_CHAR:return "       %s 受 %d 伤 → HP %d" % [_cname(d.char_id), d.amount, d.hp_after]
		CoreEnums.EventKind.DAMAGE_DEALT_ENEMY:return "    敌受 %d 伤 → HP %d %s" % [d.amount, d.hp_after, ("(" + d.bonus + ")") if d.get("bonus", "") != "" else ""]
		CoreEnums.EventKind.HEAL:             return "    %s 回 %d → HP %d" % [_cname(d.char_id), d.amount, d.hp_after]
		CoreEnums.EventKind.POSITION_CHANGE:  return "    %s 位 %d→%d" % [_cname(d.char_id), d.from, d.to]
		CoreEnums.EventKind.CARD_DRAWN:       return "    %s 抽 %s" % [_cname(d.char_id), d.card_name]
		CoreEnums.EventKind.PILE_RESHUFFLED:  return "    %s 弃牌堆放回抽牌堆" % _cname(d.char_id)
		CoreEnums.EventKind.CHAR_DOWN:        return "    [color=#ffaa55]%s 倒下[/color]" % _cname(d.char_id)
		CoreEnums.EventKind.ENCHANT_APPLIED:  return "    [color=#ffd070]✦ 附魔：%s 获得 %s[/color]" % [_target_name(d.target), _enchant_desc(d)]
		CoreEnums.EventKind.ENEMY_DEAD:       return "[color=#80ff80]★ 敌人被击败[/color]"
		CoreEnums.EventKind.BATTLE_END:       return "[color=#80ff80]★ 战斗结束 — 胜利[/color]" if d.won else "[color=#ff8080]★ 战斗结束 — 失败[/color]"
	return ""

func _target_name(t: Variant) -> String:
	if t == "enemy":
		return "敌人"
	return _cname(t)

func _enchant_desc(d: Dictionary) -> String:
	match d.get("type"):
		"vulnerable": return "易伤(+%d ×%d次)" % [d.mag, d.charges]
		"strength":   return "力量(+%d)" % d.mag
	return "%s" % d.get("type", "?")
