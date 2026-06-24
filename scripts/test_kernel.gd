# Headless 内核自检：用 `--script` 运行，验证内核能跑、预演与实战一致、同种子可复现。
extends SceneTree

func _init() -> void:
	print("== [1] setup ==")
	var gs := GameState.new()
	gs.setup(12345)
	print("   pointer=%d  chars=%d  enemy_hp=%d" % [gs.pointer, gs.characters.size(), gs.enemy.hp])
	var c0: Character = gs.characters[0]
	print("   甲 手牌: ", c0.hand.map(func(x): return x.name))
	print("   甲 位: ", c0.position)

	print("== [2] preview（悬停预演，不改动真实状态）==")
	var pv: Dictionary = gs.preview_action({"type": "card", "char_id": c0.id, "card_idx": 0})
	print("   将触发格子: ", pv.triggered_cells)
	print("   敌HP 预演后: ", pv.enemy_hp_after, "  pointer 预演后: ", pv.end_pointer)
	print("   真实状态未变? pointer=", gs.pointer, " enemy_hp=", gs.enemy.hp)

	print("== [3] 实战出牌 ==")
	var ev: Array = gs.play_card(c0, 0)
	print("   事件数: ", ev.size(), "  pointer=", gs.pointer, "  enemy_hp=", gs.enemy.hp)
	for e in ev:
		print("     - ", e.kind, " ", e.data)

	print("== [4] 确定性：同种子同动作应完全一致 ==")
	var gs2 := GameState.new()
	gs2.setup(12345)
	var c0b: Character = gs2.characters[0]
	var ev2: Array = gs2.play_card(c0b, 0)
	print("   事件数相等: ", ev.size() == ev2.size())
	print("   pointer 相等: ", gs.pointer == gs2.pointer)
	print("   enemy_hp 相等: ", gs.enemy.hp == gs2.enemy.hp)

	print("== [5] 打一张补一张 / 空过 / 位置 ==")
	var yi: Character = gs.characters[1]
	var before_hand: int = yi.hand.size()
	gs.play_card(yi, 0)
	print("   乙 出牌前手牌=", before_hand, " 出牌补一张后=", yi.hand.size(), "（应相等） pointer=", gs.pointer)
	gs.skip(gs.characters[2])
	print("   丙 空过 pointer=", gs.pointer)
	# 让丁攻击，看是否移到 1 位
	var ding: Character = gs.characters[3]
	var pos_before := ding.position
	var atk_idx := -1
	for i in ding.hand.size():
		if ding.hand[i].kind == CoreEnums.Kind.ATTACK:
			atk_idx = i
			break
	if atk_idx >= 0:
		gs.play_card(ding, atk_idx)
		print("   丁(原位%d)攻击后位: %d（应=1）" % [pos_before, ding.position])
	else:
		print("   丁无攻击牌可测位置")

	print("== [6] 阵亡后阵型向前补位 ==")
	var gs3 := GameState.new()
	gs3.setup(999)
	# 初始四人位 1..4
	print("   初始位: ", gs3.characters.map(func(x): return x.position))
	# 把 1 位(甲)打倒，其余应向前补位 → 乙丙丁变 1,2,3
	gs3._deal_damage_to_char(gs3.characters[0], 999)
	print("   甲倒下后位: ", gs3.characters.map(func(x): return x.position if not x.is_down else 0))
	var alive_pos := []
	for c in gs3.characters:
		if not c.is_down:
			alive_pos.append(c.position)
	alive_pos.sort()
	print("   存活位集合: ", alive_pos, "（应为 [1,2,3]）")
	# 再倒一个，应变 [1,2]
	gs3._deal_damage_to_char(gs3.get_char_by_id("c1"), 999)
	alive_pos.clear()
	for c in gs3.characters:
		if not c.is_down:
			alive_pos.append(c.position)
	alive_pos.sort()
	print("   再倒一个后存活位: ", alive_pos, "（应为 [1,2]）")

	print("== [7] 附魔：力量 / 易伤 / 标记 ==")
	var gs4 := GameState.new()
	gs4.setup(7)
	var hero: Character = gs4.characters[0]
	gs4.apply_strength(hero, 2)         # 英雄力+2
	gs4.apply_vulnerable(2, 3)          # 敌易伤 +2 ×3次
	print("   英雄标记: ", hero.enchant_markers(), " 敌标记: ", gs4.enemy.enchant_markers())
	var hp0 := gs4.enemy.hp
	gs4.deal_damage_to_enemy(5, hero)   # 5 + 力2 + 易伤2 = 9
	print("   敌HP %d→%d（应 -9）" % [hp0, gs4.enemy.hp])
	var vuln = gs4.enemy.get_enchantment("vulnerable")
	print("   易伤剩余次数: ", vuln.charges if vuln != null else 0, "（应 2）")
	# 再打一次（仍有易伤）：5+2+2=9
	gs4.deal_damage_to_enemy(5, hero)
	print("   再打敌HP→", gs4.enemy.hp, " 易伤次数→", (gs4.enemy.get_enchantment("vulnerable").charges if gs4.enemy.get_enchantment("vulnerable") != null else 0), "（应 1）")

	print("== ALL OK ==")
	quit()
