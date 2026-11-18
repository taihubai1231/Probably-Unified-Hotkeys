# 模块图谱与代码归属映射 (docs/handoff/map.md)

> **版本**：`v1.5.0`  
> **文档定位**：跨阶段与跨智能代理交接三件套之「模块图谱」。记录当前 `1.5.0` 架构的分层拓扑、物理文件路径、组件职责映射及巨石解耦对照关系。

---

## 1. 7 层架构物理映射总表

| 分层 (Layer) | 物理路径 (Path) | 核心类/文件 | 核心职责 |
|---|---|---|---|
| **1. Diagnostics** | `src/ProbablyUnifiedHotkeys/Diagnostics/` | `ModLogger.cs`<br/>`SafeExecution.cs` | 统一结构化日志输出（`[ProbablyUnifiedHotkeys][LEVEL]`）、异常隔离网（`SafeExecute` / `SafeFunc`） |
| **2. Config** | `src/ProbablyUnifiedHotkeys/Config/` | `ModFeatureConfig.cs` | 官方 `ModConfig` 注册、7 项核心特性开关管理与热生效回调 |
| **3. Core** | `src/ProbablyUnifiedHotkeys/Core/` | `Model/ModModel.cs`<br/>`Conflict/ConflictDetector.cs`<br/>`Search/PinyinEngine.cs`<br/>`Search/TradSimpTable.cs` | **纯领域数据模型（零 UI 控件依赖）**、冲突精确分组判重、20,902 字离线拼音与繁简检索、键码词元边界匹配 |
| **4. Persistence** | `src/ProbablyUnifiedHotkeys/Persistence/` | `ComboKeyStore.cs`<br/>`HotkeyProfileManager.cs` | Unity `PlayerPrefs` 存储契约（`combos.v1`、`forced`、`ignored`）、`KeyCode.None` 严格清空修饰位、3 套预设快照与剪贴板 JSON（`puh_profile_v1`） |
| **5. Adapter** | `src/ProbablyUnifiedHotkeys/Adapter/` | `Scanner/ModOptionScanner.cs`<br/>`Scanner/ModCatalogCache.cs`<br/>`ExternalMods/KnownModAdapters.cs`<br/>`StaticProbe/DllKeyProbe.cs` | 扫描 `GameOptionRegistry` 与各模组 `ModConfig`、委托归属回收、主菜单空闲预热、11 款工坊模组桥接、基于 DseCecil 的可选静态热键探测 |
| **6. Patches** | `src/ProbablyUnifiedHotkeys/Patches/` | `Lifecycle/ModEntry.cs`<br/>`Performance/PerformancePatches.cs` | 官方 `IMod` 生命周期、选项与按键捕获薄钩子（Thin Hooks）、选项开窗离线装配预热、改造件 `RowPool` 对象池复用、极速秒退旁路 |
| **7. Client** | `src/ProbablyUnifiedHotkeys/Client/` | `State/UiSessionState.cs`<br/>`I18n/I18n.cs`<br/>`I18n/SectionLocalizer.cs`<br/>`UI/`（10 个正交子组件） | **运行时 UI 句柄集中承载**、`BaselineSnapshot` 基准快照、三层模组分区本地化、正交拆解后的 10 个 UGUI/TMP 表现层视图 |

---

## 2. 巨石 UI (`UnifiedOptionUI.cs`) 拆解映射对照表

原版 `UnifiedOptionUI.cs` 达 5,137 行（256 KB+），单文件严重超载。在 `v1.5.0` 中按职责正交剥离为以下独立模块：

| 原巨石逻辑区段 | 行数区间预估 | 拆解后目标文件 | 新归属命名空间与职责 |
|---|---|---|---|
| 运行时 UI 句柄缓存与快照 | L120-L240 | `src/ProbablyUnifiedHotkeys/Client/State/UiSessionState.cs` | 集中维护 `boundElements`、`rowObjects`、`BaselineSnapshot` 与脏检测状态机 |
| 三层分区标题本地化引擎 | L250-L480 | `src/ProbablyUnifiedHotkeys/Client/I18n/SectionLocalizer.cs` | 原生 `LoadedMod.Localization` 直查、工坊全生态词典与 Key 净化 |
| 主窗口装配与生命周期调度 | L490-L1100 | `src/ProbablyUnifiedHotkeys/Client/UI/UnifiedOptionWindow.cs` | 接管原版选项页、`SetActive(false)` 离线批量装配调度、窗口开关事件 |
| 退出未保存防丢确认弹窗 | L1110-L1350 | `src/ProbablyUnifiedHotkeys/Client/UI/UnsavedConfirmDialog.cs` | 居中原生模态框（保存并退出 / 放弃修改 / 取消）、按键录入缓冲豁免 |
| 顶部搜索栏与统一操作栏 | L1360-L1780 | `src/ProbablyUnifiedHotkeys/Client/UI/TopSearchAndActionBar.cs` | 四模式搜索输入框、仅看冲突切换、模组排查按钮、全部展开/收起 |
| 原版与模组统一折叠卡片 | L1790-L2600 | `src/ProbablyUnifiedHotkeys/Client/UI/CollapsibleCardView.cs` | 卡片头部渲染、36x36 折叠箭头、双区分段、长文本自适应输入框 |
| 按键/组合键录入状态机 | L2610-L3100 | `src/ProbablyUnifiedHotkeys/Client/UI/KeyCaptureController.cs` | 按键/组合键/侧键录入轮询、`Esc` 解绑清空为无及防误关窗三重吸收 |
| `[!]` 仅看冲突聚合视图 | L3110-L3520 | `src/ProbablyUnifiedHotkeys/Client/UI/ConflictAggregationView.cs` | 冲突条目聚合过滤展示、冲突徽章高亮与就地改键联动 |
| 模组排查诊断子视图 | L3530-L4150 | `src/ProbablyUnifiedHotkeys/Client/UI/ModDiagnosticView.cs` | 四分类视图（疑难/未配官方/正常/纯资源）、设为按键绑定、忽略、复制汇总 |
| 3 槽位方案快照卡片 | L4160-L4600 | `src/ProbablyUnifiedHotkeys/Client/UI/ProfilePresetCardView.cs` | 方案一~三切换与保存按钮、剪贴板 JSON 一键导出/导入交互界面 |
| TMP 字体与排版辅助工具 | L4610-L5137 | `src/ProbablyUnifiedHotkeys/Client/UI/UiLayoutAndFontHelper.cs` | `FittedFontSizeCache` 单次字号拟合、`AddFixedHorizontalSpace` 锁定布局 |

---

## 3. 依赖方向与防污染调用矩阵

```plaintext
[Diagnostics]  <-- 仅作为基础设施被所有层单向依赖
      ▲
[Config]       <-- 被 Core、Persistence、Adapter、Client 依赖
      ▲
[Core]         <-- 纯领域逻辑，被 Persistence、Adapter、Client 依赖；禁止依赖 Client / Patches
      ▲
[Persistence]  <-- 持久化数据契约，被 Adapter、Client、Patches 依赖
      ▲
[Adapter]      <-- 第三方与引擎适配，被 Client、Patches 依赖
      ▲
[Patches]      <-- Harmony 薄钩子，仅做适配转发，依赖 Client、Adapter、Persistence
      ▲
[Client]       <-- 表现层顶层，持有 UiSessionState，驱动 UI 交互；禁止将 UI 句柄泄露给 Core
```

- **铁律校验**：在任何阶段的代码审查中，若在 `src/ProbablyUnifiedHotkeys/Core/` 目录下出现 `using UnityEngine.UI;` 或 `using TMPro;`，直接判定为架构违规并回滚。

---

## 4. 阶段 3 第 1 批 (Batch 1) 已落地资产与单测覆盖图谱

> **状态**：阶段 3 第 1 批（基础底座、纯领域模型、检索引擎、持久化核心与单测套件）已全部落地完成并通过 22 项 xUnit 离线单元测试与 ModScanner 官方门禁。

### 4.1 落地源码清单
- `src/ProbablyUnifiedHotkeys/Diagnostics/`：
  - `AssemblyAttributes.cs`：声明 `InternalsVisibleTo("ProbablyUnifiedHotkeys.Tests")`。
  - `ModLogger.cs`：四级结构化日志输出，测试模式无头拦截（`SuppressUnityLogForTests` / `TestLogSink`）。
  - `SafeExecution.cs`：统一异常隔离保护网（`SafeExecute` / `SafeFunc`）。
- `src/ProbablyUnifiedHotkeys/Config/`：
  - `BilingualText.cs`：纯文本中英双语内核（`UiLanguageMode`、`IsChinese`、`Get`、`Format`）。
  - `ModFeatureConfig.cs`：官方配置选项管理、单向事件管道与测试注入开关。
- `src/ProbablyUnifiedHotkeys/Core/`：
  - `Model/KeyCombo.cs`：纯值类型组合键模型，强制空键零修饰位契约（`KeyModifierMask`、`KeyCombo`）。
  - `Model/ModModel.cs`：纯领域模型聚合（`ModOptionGroup`、`ModHotkeyEntry`、`ModGeneralEntry`、`ConflictGroup` 等，零 UI 依赖）。
  - `Conflict/ConflictDetector.cs`：按键冲突检测引擎，支持空键排除、修饰键精确比对、只读探测免误报与双语提示。
  - `Search/TradSimpTable.cs`：2,473 组常用汉字繁简规范化映射表。
  - `Search/PinyinEngine.cs`：嵌入式 `pinyin.bin` 20,902 字 CJK 全量拼音与首字母检索。
  - `Search/KeySearchMatcher.cs`：高性能按键与功能检索匹配器，锁死词元边界防误中。
- `src/ProbablyUnifiedHotkeys/Persistence/`：
  - `PrefsStorage.cs`：底层 `PlayerPrefs` 读写门面与测试纯内存字典（`UseInMemoryPrefsForTests`）。
  - `ComboKeyStore.cs`：组合键与侧键持久化、运行时重映射分发、防误触抑制与无头测试桩。
  - `HotkeyProfileManager.cs`：3 槽位方案快照管理、紧凑 JSON 导出/导入（兼容 `1.4.x` 旧快照）。

### 4.2 单元测试套件清单 (`tests/ProbablyUnifiedHotkeys.Tests/`)
- `SequentialStateTestsCollection.cs`：声明 `[CollectionDefinition("SequentialStateTests", DisableParallelization = true)]` 确保状态隔离。
- `TestBase.cs`：测试基类，统一初始化 `ModLogger`、`PrefsStorage`、`ComboKeyStore` 与双语模式覆盖。
- `KeyComboAndStoreTests.cs`（5 项用例）：
  - `KeyCombo_WithNoneKey_ClearsModifiers_EnforcesInvariant`：空键强制清空修饰位不变式。
  - `ComboKeyStore_SaveCtrlE_ThenSetNone_ClearsModifiers_LockingFakeDirtyFlagRootCause`：锁死 `v1.4.7` 假脏标记根因。
  - `MouseSideButtons_FormattingAndBilingualSupport`：Mouse3~6 侧键中英双语与组合键格式化。
  - `ForcedAndIgnoredCandidateIds_PersistenceAndSetOperations`：强制纳管与忽略集合持久化。
  - `ModifierKeySuppression_AndInputRemapEvaluation`：按键重映射与防误触单键抑制仲裁。
- `ConflictDetectorTests.cs`（4 项用例）：
  - `MultipleNoneEntries_NeverDeemedConflict`：空键条目绝不判定为冲突。
  - `DifferentModifiers_DoNotConflict_SameModifiers_DoConflict`：不同修饰键互不冲突，同修饰键精准报警。
  - `ReadOnlyProbedEntries_PureProbedDoNotConflict_WithEditableEntryConflicts`：全只读探测豁免，含可编辑项时正常提示。
  - `ConflictTooltip_Bilingual_AndSortedConflictGroups`：中英双语悬浮提示与按冲突量稳定排序。
- `PinyinAndKeySearchTests.cs`（4 项用例）：
  - `EmbeddedPinyinResource_LoadedSuccessfully_FullCJKCoverage`：断言 `pinyin.bin` 20,902 字真装载。
  - `TraditionalAndSimplified_MutualSearch`：繁简双向互搜与拼音缩写检索。
  - `TokenBoundary_NoFalsePositives_LockingPitfallsRedline`：锁死 `"yp"`/`Keypad6`、`"ow"`/`UpArrow`、`"on"`/`None` 词元边界防误中红线。
  - `FormattedKeyTextAndHotkeyBinding_Matching`：组合键文本与条目多维度检索匹配。
- `HotkeyProfileManagerTests.cs`（3 项用例）：
  - `SlotCycleSwitching_AndOutOfRangeReset`：3 槽位循环切换与越界安全归零。
  - `SaveToSlot_ExportToJson_Clear_Import_Apply_RoundTrip`：全类型项无损保存导出与恢复应用全链路。
  - `BackwardCompatibility_V147_ProfileImport`：`v1.4.7` 旧版 `puh_profile_v1` JSON 100% 兼容导入。
- `ArchitecturePurityTests.cs`（2 项用例）：
  - `CoreDomainModels_ZeroUIAndHarmonyReferences`：反射审计 Core 领域类型零 UI/TMP/Harmony 依赖。
  - `AdapterLayer_ZeroTMProAndUnityEngineUIPurity`：反射审计 Adapter 适配层类型零 TMPro / TextMeshPro / UnityEngine.UI 依赖。
- `FoundationAndConfigTests.cs`（5 项用例）：
  - `ModLogger_AndSafeExecution_HandleExceptionsGracefully`：日志各级别输出与 SafeExecution 异常保护。
  - `BilingualText_ModesAndLanguageSwitching`：语言模式切换、解析与事件派发。
  - `ModFeatureConfig_SwitchesAndTestOverrides`：特性开关与单测覆盖注入。
  - `TradSimpTable_Exact2473MappingsIntegrity`：2,473 组繁简字符映射完整度。
  - `ModGeneralEntry_ApplyPendingAndResetToSnapshot`：各配置类型待定应用与快照复位。

---

## 5. 阶段 3 第 2 批 (Batch 2) 已落地资产与单测覆盖图谱

> **状态**：阶段 3 第 2 批（模组扫描引擎、三层本地化引擎、工坊模组桥接、静态探测双分支、Harmony 薄钩子、解耦 UI 桥梁与流畅度优化）已全部落地完成并通过 28 项 xUnit 离线单元测试，成功热部署至游戏 `Mods/probably_unified_hotkeys/` 且通过官方 `ModScanner` 0 权限纯净门禁。

### 5.1 落地源码清单
- `src/ProbablyUnifiedHotkeys/Client/I18n/`：
  - `I18n.cs`：`BilingualText` 完整超集兼容门面，保留 `v1/I18n.cs` 全量公开常量与 API，实现 UGUI/TMP 字体实时回退辅助（`ApplyFontIfRuntime`、`ApplyFontToDropdown`）。
- `src/ProbablyUnifiedHotkeys/Adapter/Scanner/`：
  - `SectionLocalizer.cs`：三层模组分区与条目标题本地化引擎独立抽离，**零 TMPro / UnityEngine.UI 逆向依赖**，支持原生 `LoadedMod.Localization` 直查与错位对齐、内置全生态词典映射及点分/下划线结构化键名智能净化。
  - `ModCatalogCache.cs`：模组配置轻量化扫描缓存与指纹校验，提供 `EnsureEventSubscriptions` 幂等事件监听，`ResetPendingSessionState` 重置纯数据待定字段并通过 `SessionResetRequested` 解耦通知 UI 表现层。
  - `ModOptionScanner.cs`：全生态配置扫描引擎，100% 保留外部程序集归属判定、首次会话快照、工坊适配桥接、条件编译探测合并与 `ModScanReport` 诊断报告。
- `src/ProbablyUnifiedHotkeys/Adapter/ExternalMods/`：
  - `KnownModAdapters.cs`：11 款主流创意工坊模组专属适配器与强类型委托池（`ShowItemNames`、`ConflictScanner`、`SmartInventoryArrange`、`chiqingsan.BetterPerformance`、`item_details` 等），纯只读接入 `probablystolen.trainer` 维持官方 0 反射审计红线。
- `src/ProbablyUnifiedHotkeys/Adapter/StaticProbe/`：
  - `DllKeyProbe.cs`：基于 DseCecil 的静态黑盒探测引擎，严格执行 `#if !DISABLE_DLL_KEY_PROBE ... #else ... #endif` 编译隔离，默认构建绝对 0 引用 `System.IO.File` / `Directory` / `DseCecil`。
- `src/ProbablyUnifiedHotkeys/Patches/Lifecycle/`：
  - `IOptionUiBridge.cs`：`IOptionUiBridge` 与 `OptionUiHost` 抽象桥梁，切断 Harmony 钩子对巨石 UI 的强依赖，在 UI 未挂载时优雅降级放行原版，杜绝黑屏死锁。
  - `ModEntry.cs`：官方原生 `IMod` 契约（`Init`、`OnEnable`、`OnDisable`），按序装配底座、绑定 `ComboKeyStore.ExternalBypassPredicate`，退出期快速短路与限定自身 ID 的 `UnpatchAll`。
  - `OptionAndKeyBindPatches.cs`：原版选项与改键系统薄拦截钩子，显式声明参数类型数组杜绝 `AmbiguousMatchException`，集成 Esc 录制与未保存仲裁。
  - `WorkshopModPatcher.cs`：主流工坊模组输入拦截与 `ConfigEntry<T>` / `UnityEngine.Input` 底层重定向，异常统一走规范日志隔离。
- `src/ProbablyUnifiedHotkeys/Patches/Performance/`：
  - `PerformancePatches.cs`：退出游戏极速秒退、主菜单闲置分帧预热 `IdleWarmupRunner`、改造件 `RowPool` 行对象池与缺失元素静默钩子。

### 5.2 补充单元测试套件 (`Batch2AdapterAndBridgeTests.cs`)
- `tests/ProbablyUnifiedHotkeys.Tests/Batch2AdapterAndBridgeTests.cs`（8 项新增用例）：
  - `SectionLocalizer_KnownDictionary_BilingualMapping_Chinese`：验证已知分类词典（General, Hotkeys, Inventory, Tooltips）中文模式精准转换。
  - `SectionLocalizer_KnownDictionary_BilingualMapping_English`：验证已知分类词典英文模式精准对齐。
  - `SectionLocalizer_StructuredKey_IntelligentHumanization`：验证 `speed.config.dialogue` 词典命中、`some_mod.settings_section_combat` 净化为 `Combat`、前缀剥离与空安全。
  - `OptionUiHost_NullSafe_And_ComboKeyStore_ExternalBypass_Linkage`：验证 `OptionUiHost.Active == null` 空安全，以及接入 Mock 桥梁后开窗与录键对 `ComboKeyStore.IsBypassed` 的精准旁路联动。
  - `ModCatalogCache_EventSubscriptions_And_ResetSession`：验证事件订阅幂等性与会话重置委托通知。
  - `KnownModAdapters_ConflictScanner_And_ShowItemNames_UnbindToNone_Persistence`：验证 `ConflictScanner` v1.6.5 默认键为 `F7`、解绑为 `None` 时强制写入与持久化、`ShowItemNames` 解绑放行及启动期同步门面。
  - `FastQuit_And_ModEntryQuittingState_Safety`：验证 `PrepareFastQuit` 幂等安全执行、`ModEntry.IsApplicationQuitting` 状态跃迁与退出保护。
  - `WorkshopModPatcher_SiaTrySet_NullSafe`：验证 `SmartInventoryArrange.SettingsHotkeys.TrySet` 拦截解绑前置补丁的空安全与非空放行。
- **全套单测当前状态**：31/31 项测试 100% 绿灯通过，0 错误 0 警告。
