# 架构决策记录 (docs/handoff/decision.md)

> **版本**：`v1.5.0`  
> **文档定位**：跨阶段与跨智能代理交接三件套之「架构决策记录 (ADR)」。收录经业务决策人（老板）与幕僚长正式拍板的核心工程决断，作为后续开发不可推翻的基准依据。

---

## ADR-001: 组织拓扑与扁平协作机制
- **决策状态**：已拍板并强制执行
- **决策内容**：确立“老板 — 幕僚长(Carbon) — 四大扁平化职能部门(3.8Flash)”单层星型拓扑结构。**严禁子代理再派生孙代子代理**；严格遵循零历史自包含派发原则，跨阶段流转统一通过 `docs/handoff/` 交接三件套沉淀，汇报采用只读结构化摘要，杜绝上下文污染。
- **决策理由**：历史会话证明多层套娃派生会导致上下文严重膨胀、指令扭曲失真且极易产生失控的并发写冲突。

---

## ADR-002: 编译期双态架构与默认 0 权限纯净发布
- **决策状态**：已拍板并强制执行
- **决策内容**：面向官方 2026-12-10 安全白名单生死线（`trust-list.json`），确立**分级降级架构**。工程默认采用 `DisableDllKeyProbe=true` 编译宏，`manifest.xml` 中 `<Capabilities />` 保持为空，达成 **0 Capabilities / 0 HardFlags / 0 Deprecated / Refused=False** 的纯净状态发布；带有 `files` 权限的跨目录只读探测仅作为开发可选分支保留。
- **决策理由**：确保玩家在任何官方版本更新下绝不遭遇模组被阻断加载（`Refused=True`）的重大可用性事故。

---

## ADR-003: 领域模型与 UI 状态彻底解耦（`UiSessionState`）
- **决策状态**：已拍板并强制执行
- **决策内容**：彻底废弃历史版本中数据模型强引用 `GameObject` 与 `CustomUIElement` 的反模式。`Core/Model/ModModel.cs` 确立为 100% 纯业务领域模型（零 Unity UI 依赖）；所有运行时的 UI 句柄、展开状态、`BaselineSnapshot` 基准快照与脏状态检测统一由 `Client/State/UiSessionState.cs` 独立集中维护。
- **决策理由**：消除 `ModCatalogCache` 缓存重置时的反向清理缺陷，消除内存泄漏隐患，并使 `Core` 层算法能够脱离 Unity 引擎独立运行脱机单元测试。

---

## ADR-004: 巨石 UI 模块（5,137 行）正交解耦为 10 个独立组件
- **决策状态**：已拍板并强制执行
- **决策内容**：将单文件超载 256 KB+ 的 `UnifiedOptionUI.cs` 彻底拆解，按职责分离为：
  1. `UnifiedOptionWindow`（主调度与生命周期）
  2. `UnsavedConfirmDialog`（未保存确认模态框）
  3. `TopSearchAndActionBar`（搜索与统一操作栏）
  4. `CollapsibleCardView`（折叠卡片渲染）
  5. `KeyCaptureController`（按键录入状态机与 Esc 吸收）
  6. `ConflictAggregationView`（[!] 冲突聚合视图）
  7. `ModDiagnosticView`（模组排查子视图）
  8. `ProfilePresetCardView`（3 槽位方案快照卡片）
  9. `UiLayoutAndFontHelper`（字号拟合与固定间距样式）
  10. `SectionLocalizer`（三层分区本地化引擎）
- **决策理由**：巨石文件导致多人协作冲突、代码理解心智负担过重、局部排查引发连锁退化。正交拆解后各子视图职责清晰，排障与扩展成本呈数量级降低。

---

## ADR-005: 独立 .NET 8 工具链替代 PowerShell 5.1 直接反射
- **决策状态**：已拍板并强制执行
- **决策内容**：绝对禁止在 Windows PowerShell 5.1 脚本中通过 `AssemblyResolve` 加载游戏 `Managed\` 目录；所有游戏元数据类型检查、签名核验与工坊扫描统一由独立的 .NET 8 工具（`tools/ScannerCheck`，底层基于 `DseCecil` 静态读取）承载。
- **决策理由**：PowerShell 5.1 运行于 .NET Framework 4.8，加载 Unity Mono 的 `mscorlib.dll` 会引发死递归 `StackOverflowException`（“无法创建新的堆栈防护页面”），引发宿主终端崩溃。

---

## ADR-006: Steam 创意工坊 `SteamTagger` 三步闭环发布 SOP
- **决策状态**：已拍板并强制执行
- **决策内容**：确立 `publish_workshop.ps1` 三步闭环发布流：
  1. `-Step default`：上传 4 个文件并写入默认/英文槽位；
  2. 桌面端打开 Steam 登录作者账号，运行 `-Step query` 确认 IPC 句柄 `BLoggedOn=True`；
  3. `-Step schinese -SkipCheck`：通过 Steamworks 原生 IPC 强制覆写不可注销的 `schinese` 语言槽位。
- **决策理由**：解决 Valve `ISteamUGC` 历史写入语言槽位无法删除导致的国内中文玩家永久停留在旧版更新文案的平台级缺陷。

---

## ADR-007: `PlayerPrefs` v1 6 项键契约与 `puh_profile_v1` 向下兼容承诺
- **决策状态**：已拍板并强制执行
- **决策内容**：严格保留 `PlayerPrefs` 历史的 6 项核心键（`combos.v1`、`forced_hotkeys.v1`、`ignored_candidates.v1`、`profile_active_slot.v1`、`profile_slot_json.v1.*`、`profile_slot_meta.v1.*`）以及 `puh_profile_v1` 紧凑 JSON Schema。重构过程中保证新版本无缝读取玩家旧档按键与方案配置，绝不导致玩家历史绑定丢失。
- **决策理由**：保障现有数万名创意工坊订阅玩家的升级体验平滑无感，零配置丢失与零重新绑定烦恼。
