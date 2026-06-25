# 《同渊》UI 系统重置要求（交付给执行端）

> 本文件是 UI 重置任务的完整执行规格，自包含。执行端据此独立开发，无需上下文。
> 已与项目所有者确认：范围=战斗+Roguelike 外壳+主菜单；美术=结构化占位；架构=.tscn 场景+Theme；分辨率=1920×1080 16:9。

---

## 0. 背景：为什么重置

当前界面不像正式卡牌游戏，根因（已核对源码）：

1. **只有 1 个空场景 `main.tscn`**，所有控件在 `src/Tongyuan/Views/GameView.cs`（926 行）里用 VBox/HBox/Button/Label 过程式堆叠，像调试面板。
2. **卡牌 = 168×92 的 Button**，文字塞满 `〔类型〕名\n占X｜效果`，无卡框/费用球/类型条/卡图区/稀有度/悬浮抬升（见 `GameView.cs:517 MakeCardButton`）。
3. **立绘 = 色块 + 文字标签**（`▶ 名字\n位N\nHP x/y`），非头像（`GameView.cs:248 MakePortrait` / `:287 MakeEnemyBlock`）。
4. **顶栏把调试功能**（IP/LAN/加自定义卡）和游戏 chrome 混排（`GameView.cs:97-126`）。
5. **`src/Tongyuan.Core/Layout/BattleLayout.cs` 的确定性 side-view 布局模块是死的**——GameView 用流式布局，规格里的像素布局从未被运行时使用。
6. **无 Theme / 无字体资源**——颜色/StyleBox 在每个控件里硬编码重复；Godot 4 默认字体不含 CJK，导出版中文可能 tofu。
7. **Roguelike 外壳零 UI**：`RunController`/`RunState`/`MapGraph`/`MapGenerator`（`src/Tongyuan.Core/Roguelike/`）数据层完整但无界面，目前只有战斗屏。

目标：可在编辑器可视化调整、风格统一、卡牌化观感、三屏完整可玩的正式版 UI，**Core 逻辑零改动**（除 `CardDef` 加 `Rarity` 字段）。

---

## 1. 工具链（已确认）

- 引擎：Godot 4.7 mono，编辑器 exe 在 `build/godot_mono/Godot_v4.7-stable_mono_win64/Godot_v4.7-stable_mono_win64.exe`（不在 PATH，用绝对路径调用）。
- .NET：`dotnet 10.0.100`。
- 工程结构：
  - `Tongyuan.csproj`（根，Godot 工程）编译 `src/Tongyuan/` 下所有 .cs，引用 `src/Tongyuan.Core/Tongyuan.Core.csproj`，排除 Core/tests。
  - `src/Tongyuan.Core/Tongyuan.Core.csproj` = Core（结算/数据/联机）。
  - `tests/Tongyuan.Core.Tests/Tongyuan.Core.Tests.csproj` = Core 单测。
  - **新增的 UI C# 脚本放 `src/Tongyuan/UI/`，由根 Tongyuan.csproj 自动编译。**
- 导出：`export_presets.cfg` preset.0 → `build/tongyuan_formal.exe`（Windows，embed_pck）。
- 验证命令：
  - 编译 C#：`dotnet build Tongyuan.sln`
  - 单测：`dotnet test tests/Tongyuan.Core.Tests/Tongyuan.Core.Tests.csproj`
  - Godot 导入/检查：`<godot_exe> --headless --path . --import`（首次会生成 .godot/import）
  - 导出：`<godot_exe> --headless --path . --export-release "Windows Desktop"`

## 2. CJK 字体方案（重要）

不要下载大字库。用 Godot 的 **SystemFont** 资源指向 Windows 自带字体：
- Theme 默认字体 = SystemFont，`font_names = ["Microsoft YaHei", "Segoe UI"]`，中文走雅黑、英文走 Segoe UI。
- 零包体增量；Windows 目标机自带雅黑，导出后正常显示。将 SystemFont 存为 `assets/fonts/cjk_font.tres`，Theme 引用它。

---

## 3. 目标架构

```
res://
├ project.godot                      # 分辨率 1920×1080；主场景保持 main.tscn（作路由根）
├ assets/
│  ├ theme/default_theme.tres        # 全局 Theme：SystemFont 字体 / 配色 / StyleBox
│  ├ fonts/cjk_font.tres             # SystemFont（雅黑+Segoe UI）
│  └ art/                            # 占位美术（运行时 _Draw 生成；ArtPath 留真贴图槽）
├ scenes/
│  ├ MainMenu.tscn                   # 标题/开始/联机/设置/退出
│  ├ Run.tscn                        # 外壳路由：按 RunController.CurrentType 切子屏
│  ├ battle/Battle.tscn              # 战斗屏（替换 GameView 过程式构建）
│  ├ map/Map.tscn                    # 地图选路
│  ├ shop/Shop.tscn                  # 商店
│  └ reward/Reward.tscn              # 战后三选一加牌
└ src/Tongyuan/UI/                   # 可复用视图组件（场景+脚本）
   ├ CardView.tscn + CardView.cs     # 卡牌：卡框/费用球/类型条/卡图/效果/稀有度/悬浮
   ├ PortraitView.tscn + PortraitView.cs  # 头像：立绘+HP条+位置+意图+激活+护盾
   ├ TimelineView.tscn + TimelineView.cs  # 时间轴：指针/遍历预览/触发高亮
   ├ IntentIcon.cs                   # 敌人意图矢量图标（剑=攻击/闪电=蓄力/…）
   ├ StatBar.cs                      # 分段 HP 条
   └ UiPalette.cs                    # 调色板/样式盒工厂（Theme 与代码共用）
```

### 路由方式
`src/Tongyuan/Main.cs`（`main.tscn` 根节点）从「直接挂 GameView」改为**屏幕路由器**：持 `RunController`，按当前节点类型把对应场景 `PackedScene` 实例化为子节点并切换；保留 `GetViewport().size_changed` → `ResizeTo` 的尺寸驱动（运行时 AddChild 的 Control 须显式设尺寸）。子节点交换优于 `ChangeScene`，便于保留 LAN `NetController` 与跨屏状态。LAN 当前是战斗屏作用域，可继续挂在 BattleController 下。

### 必须复用（不要重造）
- `BattleLayout.Compute/NoOverlaps/AllWithinViewport`（`src/Tongyuan.Core/Layout/BattleLayout.cs`）→ 喂场景节点位置 + 保留 `tests/P5LayoutTests.cs`。
- `PortraitController` 状态机 + `OnEvent`（`src/Tongyuan/Views/PortraitController.cs`）→ `PortraitView` 直接挂用；增强其 `_Draw`（画轮廓+状态着色+ArtPath 真贴图槽），接口不变。
- `CardDef.ArtPath`（`src/Tongyuan.Core/Core/CardDef.cs:47`）、`EffectDescription()`、`DamageText()` → `CardView` 直接调。
- `GameView` 全部 Core 交互逻辑（`Play`/`Hover`/`BeginTargeting`/`ActionForCard`/`NeedsTarget`/`RenderPreview`/`ActionCost`/`TraversedSet`/`TriggeredPreviewSet`/LAN `StartLanHost`/`StartLanClient`/`OnNetApplied`/`PlayCardAnimation`/`AnimatePortraits`）→ **原样搬进 `BattleController`**，仅替换渲染出口（构建控件 → 填充场景节点）。
- `RunController`（`src/Tongyuan.Core/Roguelike/RunController.cs`）全部方法 → 外壳屏直接调，Core 零改。
- 调色复用 `GameView` 现有 `CardBgColor`/`CardAccentColor`/`EnemyColor`/`ColorOf`/`KindText`/`EnemyColor`（`GameView.cs:549-904`）抽进 `UiPalette`。

### 不改动
- `Tongyuan.Core` 全部结算/数据/联机逻辑（除 `CardDef` 加 `Rarity` 枚举字段）。
- Core 单测保持绿；`P5LayoutTests` 若因新布局断言失败，同步更新断言（布局语义不变）。

---

## 4. 分阶段交付

### 阶段 0 — 基建
- `project.godot`：`display/window/size/viewport_width=1920`、`viewport_height=1080`。
- `assets/theme/default_theme.tres`：默认字体绑 `cjk_font.tres`；Button/Panel/Label 的 StyleBoxFlat；角色色/卡牌类型色/敌人种类色（沿用 GameView 调色，抽进 `UiPalette`）。设为项目默认 Theme（`project.godot` 的 `[gui] theme/custom` 或运行时 `ThemeDB`）。
- `src/Tongyuan/UI/UiPalette.cs`：集中颜色与样式盒工厂，消除 GameView 散落的 `new StyleBoxFlat{...}`。
- **调试功能收纳**：LAN（建主/加入/IP）、加自定义卡 → 移入 **F1 呼出的「开发者面板」叠加层**，不进主 chrome。沿用 `GameView.AddCustomCard`/`StartLanHost`/`StartLanClient` 逻辑。
- `Main.cs` 改为路由器骨架（持 RunController，按节点类型切屏）。

### 阶段 1 — 卡牌组件 `CardView`
- 场景结构：`Panel`(卡框, 圆角, 类型色边框) → 顶部费用球(圆形底 Label) / 卡图区(TextureRect 或 `_Draw` 渐变占位, 按 `DamageType`/`Type` 着色) / 名称条 / 类型标签 / 效果文本 / 底部稀有度宝石。
- 交互：`MouseEntered` → Tween 抬升+放大+置顶(`z_index`)；`MouseExited` → 回落。点击 → 出牌或进目标选取（沿用 `NeedsTarget`/`BeginTargeting`）。
- `ArtPath` 非空 → `TextureRect.Texture = Load(ArtPath)`；否则占位渐变。
- `CardDef` 加 `Rarity` 枚举（Common/Rare），卡框边色按稀有度。给 `SampleCards`/`CodeCards` 标注示例稀有度。
- 替换 `GameView.MakeCardButton`(`:517`) → 实例化 `CardView`。

### 阶段 2 — 头像组件 `PortraitView`
- 场景结构：`PanelContainer`(角色色边框, 激活高亮) → 内含 `PortraitController`(立绘居中) + 顶部名牌 + 底部 `StatBar`(HP) + 位置 pips + 护盾叠层 + (敌人) `IntentIcon` + 蓄力计数。
- 沿用 `PortraitController` 状态机；`PortraitView` 把 Core `GameEvent` 转发（复用 `AnimatePortraits` 逻辑 `GameView.cs:712`）。
- 敌人意图：`IntentIcon` 按 `EnemyAction` 类型画矢量图标，替换 `IntentText`(`:364`) 文本。
- 替换 `MakePortrait`(`:248`) / `MakeEnemyBlock`(`:287`)。

### 阶段 3 — 战斗场景 `Battle.tscn`
- 布局（1920×1080，side-view 贴合规格 §4.8）：
  - 顶栏（细）：指针格/历史/胜负 + 设置 + 退出。
  - 中部：左列 4 角色 `PortraitView`（竖排，激活高亮，点击切换）｜中 VS 分隔/出牌区｜右列敌人 `PortraitView`。
  - 时间轴：横向条 `TimelineView`，指针 + 悬停预览遍历/触发高亮，可滚。
  - 手牌：底部 `CardView` 一行，左为牌堆/弃牌堆指示（点击查看），右为整备牌 + 空过（动作芯片样式）。
  - 预览：悬停手牌时浮层显示预计后果（复用 `RenderPreview` `:750`）。
  - 日志：右侧可折叠侧栏，非常驻大块。
- 运行时用 `BattleLayout.Compute(...)` 算位置注入节点（让规格布局模块真正生效），场景容器负责自适应/锚点。
- `GameView` → `BattleController`（`Battle.tscn` 脚本）：保留全部 Core 调用，只换渲染出口。最大风险点，原则：Core 调用一行不改，分函数对照迁移。

### 阶段 4 — Roguelike 外壳
- `Run.tscn` 路由：读 `RunController.CurrentType` → 切 Map/Shop/Reward 或 Battle。
- `Map.tscn`：分层选路图（`MapGraph`/`MapGenerator`），节点按 `MapNodeType` 着色图标，点击可达节点 `RunController.Advance()`。
- `Shop.tscn`：金币 + 牌/遗物/移牌/恢复四档（`RunController.ShopBuyItem`），`CardView` 展示售卖牌。
- `Reward.tscn`：战斗胜后三选一加牌（`WinCombat`），`CardView` 呈现。
- 休息/事件节点：占位简单面板（`RestSite`/`RollEvent`）。

### 阶段 5 — 主菜单 `MainMenu.tscn`
- 标题/开始单局/联机(建主+加入)/设置(音量/分辨率占位)/退出。
- 「开始」→ 构造 `RunState`+`RunController`（`MapGenerator.Generate`）→ 进 `Run.tscn`。

### 阶段 6 — 打包验证
- 重新 export `build/tongyuan_formal.exe`（沿用 `export_presets.cfg`）。
- 冒烟：主菜单→地图→战斗→奖励→商店→Boss 全链路；LAN 建主/加入；F1 开发面板。

---

## 5. 验证清单

1. **编辑器运行**：`<godot_exe> --path .` 逐屏目视（主菜单/地图/战斗/商店/奖励），确认 CJK 不 tofu、卡牌悬浮抬升、意图图标、HP 条、时间轴预览高亮。
2. **布局自检**：启动时打印 `BattleLayout.NoOverlaps()/AllWithinViewport()` 仍为 true（沿用 `Main.cs:30-31` 打印）。
3. **单测**：`dotnet test tests/Tongyuan.Core.Tests/Tongyuan.Core.Tests.csproj` 全绿；新增 `Rarity` 不破坏 `SampleCardsTests`/`CodeCardsTests`。
4. **冒烟全链路**：开始→地图选路→战斗出牌/整备/空过/预演/目标选取→奖励加牌→商店购买→休息→Boss，单机 + LAN 双窗口。
5. **导出**：重打 `tongyuan_formal.exe`，实跑确认三屏正常、中文显示、F1 开发面板可用。

## 6. 约束与风险
- Core 零改（除 `CardDef.Rarity`）。所有新逻辑在 `src/Tongyuan/UI/` 与场景。
- 每阶段可独立编译运行，建议按阶段提交。
- `GameView.cs`→`BattleController` 搬迁是最大风险：分函数对照，Core 调用不改，只动渲染出口。
- CJK 走 SystemFont（雅黑），勿下载字库。
- `BattleLayout` 运行时必须真正用上（注入节点位置），否则规格布局模块仍是死的。
