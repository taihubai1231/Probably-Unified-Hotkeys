# 模块图谱与代码归属映射 (docs/handoff/map.md)

> **版本**：`v1.5.1`  
> **文档定位**：跨阶段与跨智能代理交接三件套之「模块图谱」。记录当前 `1.5.1` 架构的分层拓扑、物理文件路径、组件职责映射及巨石解耦对照关系。

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
| **7. Client** | `src/ProbablyUnifiedHotkeys/Client/` | `State/UiSessionState.cs`<br/>`I18n/I18n.cs`<br/>`I18n/SectionLocalizer.cs`<br/>`UI/`（11 个正交子组件） | **运行时 UI 句柄集中承载**、`BaselineSnapshot` 基准快照、三层模组分区本地化、正交拆解后的 11 个 UGUI/TMP 表现层视图 |

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
| 卡片拖拽自由排序控制器 | 新增独立组件 | `src/ProbablyUnifiedHotkeys/Client/UI/CardDragReorderController.cs` | 卡片右侧 `≡` 拖动手柄事件驱动、插入指示线轻量渲染、视口平滑滚屏与 8px 防误触阈值 |

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
- `tests/ProbablyUnifiedHotkeys.Tests/Batch2AdapterAndBridgeTests.cs`（10 项新增用例）：
  - `SectionLocalizer_KnownDictionary_BilingualMapping_Chinese`：验证已知分类词典（General, Hotkeys, Inventory, Tooltips）中文模式精准转换。
  - `SectionLocalizer_KnownDictionary_BilingualMapping_English`：验证已知分类词典英文模式精准对齐。
  - `SectionLocalizer_StructuredKey_IntelligentHumanization`：验证 `speed.config.dialogue` 词典命中、`some_mod.settings_section_combat` 净化为 `Combat`、前缀剥离与空安全。
  - `OptionUiHost_NullSafe_And_ComboKeyStore_ExternalBypass_Linkage`：验证 `OptionUiHost.Active == null` 空安全，以及接入 Mock 桥梁后开窗与录键对 `ComboKeyStore.IsBypassed` 的精准旁路联动。
  - `ModCatalogCache_EventSubscriptions_And_ResetSession`：验证事件订阅幂等性与会话重置委托通知。
  - `KnownModAdapters_ConflictScanner_And_ShowItemNames_UnbindToNone_Persistence`：验证 `ConflictScanner` v1.6.5 默认键为 `F7`、解绑为 `None` 时强制写入与持久化、`ShowItemNames` 解绑放行及启动期同步门面。
  - `FastQuit_And_ModEntryQuittingState_Safety`：验证 `PrepareFastQuit` 幂等安全执行、`ModEntry.IsApplicationQuitting` 状态跃迁与退出保护。
  - `WorkshopModPatcher_SiaTrySet_NullSafe`：验证 `SmartInventoryArrange.SettingsHotkeys.TrySet` 拦截解绑前置补丁的空安全与形参名 `target` 精准匹配。
  - `ConflictScanner_LegacyPrefsFallback_DoesNotOverwriteToF7`：验证旧版原生单键（如 `F10` 或 `None`）在 `custom_hotkeys.v1` 为空时绝不误覆盖回 `F7`。
  - `MouseButtons_RightAndMiddleClick_FormattingAndBilingualSupport`：验证 `Mouse1`（右键）与 `Mouse2`（中键）的友好文本格式化与中英双语对齐（ADR-010）。
- **全套单测当前状态**：33/33 项测试 100% 绿灯通过，0 错误 0 警告。

---

## 6. 阶段 3 第 3 批 (Batch 3) 已落地资产与单测覆盖图谱

> **状态**：阶段 3 第 3 批（表现层正交解耦、5,137 行巨石彻底拆解、折叠卡片按需懒构建、右键绑定增强、全量单测与实机热部署门禁）已全部圆满落地并通过 39 项 xUnit 离线单元测试（100% 全绿），成功真实热部署至游戏 `Mods/probably_unified_hotkeys/` 且通过官方 `ModScanner` 0 权限纯净门禁（1,343 个方法扫描通过）。

### 6.1 落地源码清单与行数统计
- `src/ProbablyUnifiedHotkeys/Client/State/`：
  - `UiSessionState.cs` (1,054 行)：集中维护全生命周期运行时 UI 句柄（`CardParts`、`HotkeyUiMap`、`GeneralUiMap`）、`BaselineSnapshot` 基准快照、锁死 `v1.4.7` 空键解绑防假脏状态机，以及响应 `ModCatalogCache.SessionResetRequested` 的句柄自清理。
- `src/ProbablyUnifiedHotkeys/Client/UI/`（9 大正交组件 + 1 状态机，共 6,991 行）：
  - `UnifiedOptionWindow.cs` (597 行)：实现 `IOptionUiBridge` 18 项契约，主窗口生命周期与单帧瞬开离线批量装配总控。
  - `UiLayoutAndFontHelper.cs` (1,026 行)：单次字号拟合缓存（`FittedFontSizeCache`，容量上限 <= 2048）、折叠箭头 36px 防挤压清零内边距（`StyleFoldButton`）、`WithSuspendedRootLayout` 批量挂起重排。
  - `UnsavedConfirmDialog.cs` (236 行)：居中模态防丢弹窗（保存并退出 / 放弃修改 / 取消），按键录制状态安全豁免。
  - `TopSearchAndActionBar.cs` (827 行)：四模式高效搜索、拼音与词元边界内核复用、「全部展开」离线预建与单帧瞬切。
  - `CollapsibleCardView.cs` (979 行)：原版与模组统一折叠卡片渲染、ADR-009 离线非激活态按需懒构建（Lazy Content Build）、24px/16px 像素级排版缩进。
  - `KeyCaptureController.cs` (381 行)：按键/组合键录入控制器、ADR-010 鼠标右键（`Mouse1`）与中键（`Mouse2`）捕获增强、Esc 解绑清空为无及防误关窗三重吸收、闲置帧预算静默预建（<= 1.8ms）。
  - `ConflictAggregationView.cs` (238 行)：冲突专属聚合视图、冲突条目增量懒构建、双语高亮与就地联动改键。
  - `ModDiagnosticView.cs` (1,318 行)：四分类模组排查疑难接管、原地热重扫（保持展开记忆）、合规调用 `GUIUtility.systemCopyBuffer` 汇总复制。
  - `ProfilePresetCardView.cs` (335 行)：3 套场景预设槽位与剪贴板 JSON 一键导出/导入卡片界面。

### 6.2 补充单元测试套件 (`Batch3ClientArchitectureAndStateTests.cs`)
- `tests/ProbablyUnifiedHotkeys.Tests/Batch3ClientArchitectureAndStateTests.cs`（6 项新增用例）：
  - `FinalAssembly_CoreLayer_ZeroUIAndHarmonyReferences`：全程序集反射扫描，断言 Core 层所有类 0 引用 `GameObject` / `TMPro` / `UnityEngine.UI` / `HarmonyLib` / `CustomUIElement`。
  - `FinalAssembly_AdapterLayer_ZeroTMProAndUnityEngineUIPurity`：断言 Adapter 层所有类 0 引用 `TMPro` / `UnityEngine.UI`。
  - `FinalAssembly_ClientArchitecture_NineUiComponentsAndStateAreIndependent`：断言全程序集不再存在单文件巨石（验证 Client/UI 9 大组件与 UiSessionState 独立成型，且 `UnifiedOptionWindow.Instance` 完整实现 `IOptionUiBridge` 全部 18 项契约）。
  - `UnifiedOptionWindow_EnsureBridgeRegistered_MountsOptionUiHostActive`：验证 `EnsureBridgeRegistered` 幂等挂载 `OptionUiHost.Active` 并注入全部解耦生命周期委托。
  - `UiSessionState_ModelHandleDictionaries_ClearOnCacheInvalidateAndReset`：验证模型句柄字典映射在触发 `ModCatalogCache.Invalidate()` 与 `SessionResetRequested` 时自动清空，杜绝内存陈旧句柄泄露。
  - `UiSessionState_HasUnsavedChanges_KeyCodeNone_Unbind_ReturnsFalse_LockingV147Contract`：验证 `KeyCode.None` 显式解绑场景下 `HasUnsavedChanges()` 严格返回 `false`，锁死 `v1.4.7` 防假脏弹窗交互契约。
- **全套单测最终收官状态**：39/39 项测试 100% 绿灯全量通过，0 错误 0 警告。

---

## 7. 规范基建与文案治理资产图谱 (Docs & Skills)

> **状态**：已正式建立面向工作区全体 Agent 的对外文案去 AI 化（去人机味）全套规范、标准技能与治理宪章关联。

### 7.1 资产清单与职责映射
- `docs/ANTI_AI_WRITING_GUIDE.md`：
  - **定位**：对外文案去 AI 化工程参考手册（全维度工程指南）。
  - **核心内容**：深度拆解中文维基百科 `WP:AISIGNS`（《AI生成文的特征》）；剖析均值回归、论证坍缩、过度防御等底层机理；结构化提炼 6 种内容逻辑病症与改写处方；制定高频 AI 虚浮词黑名单、否定平行句破除法、标题去对称法与去括号举例规范；收录《深空当铺》老板亲自批改的 5 大黄金前后对比案例；提供 8 项自检清单。
- `.agents/skills/anti-ai-writing/SKILL.md`：
  - **定位**：工作区级标准 Antigravity Skill。
  - **核心内容**：为所有进入工作区撰写或审查 `README.md`、创意工坊简介（VDF）、`manifest.xml`、`CHANGELOG.md` 及 Release Notes 的 Agent 提供自动化发现能力与 7 步去人机味执行清单（Check 1~7）、快速 Bad/Good 对照表与违规阻断门禁。
- `AGENTS.md`：
  - **定位**：工程协作宪章。
  - **核心内容**：在第二章正式植入第 2.3 节「对外文案去 AI 化（去人机味）铁律」，强制关联上述指南与技能，确立零禁词、零伪深刻对比句、零机械加粗列表、审阅净版与代码分离等强制性验收标准。

---

## 8. v1.5.1 第三方异常隔离与稳定性加固图谱 (Fault Isolation & Stability)

> **状态**：针对工坊模组「物品堆叠增强」（Workshop ID: `3830647974`）及动态委托类模组在主菜单空引用导致选项界面回退原版缺陷，已完成四级故障隔离网与脱机回归单测落地。

### 8.1 加固模块与职责映射
1. `src/ProbablyUnifiedHotkeys/Adapter/Scanner/ModCatalogCache.cs`：
   - 新增 `HasDegradedEntries` 静态标志：在扫描捕获到第三方异常降级时置为 `true`，在 `Invalidate()` 与 `ScanAll` 入口复位。
2. `src/ProbablyUnifiedHotkeys/Adapter/Scanner/SectionLocalizer.cs`：
   - `TryResolveFromModLocalization`：补充查询 `"section_" + rawSection` 分区键；
   - 新增 `TryResolveConfigLabelFromModLocalization` 与 `TryResolveConfigTooltipFromModLocalization`：在第三方委托崩溃时，自动从模组本地化表取回中英双语规范标题与提示。
3. `src/ProbablyUnifiedHotkeys/Adapter/Scanner/ModOptionScanner.cs`：
   - 建立单条目独立沙盒（`SafeInvokeString`、`SafeGetBoxedValue`、`SafeGetBoxedDefault`、`SafeGetChoices`），捕获异常时记录降级标志并做模组级限流日志；
   - 在 `ScanModConfigIntoGroup`、`IsHotkeyEntry`、`CreateHotkeyFromConfigEntry` 与 `CreateGeneralFromConfigEntry` 中全量接入安全防穿透包装，同时保留 `using (ComboKeyStore.EnterBypass())` 与 `ExplicitUnbound` 核心逻辑。
4. `src/ProbablyUnifiedHotkeys/Client/State/UiSessionState.cs`：
   - 在 `CaptureOpenBaselineSnapshot`、`HasAnyPendingGeneralChanged`、`HasAnyVanillaBaselineChanged` 与 `HasAnyHotkeyChanged` 中引入确定性默认值安全求值，第三方异常 Getter 不向外抛出异常且不误触发假脏弹窗。
5. `src/ProbablyUnifiedHotkeys/Client/UI/UnifiedOptionWindow.cs` 与 `CollapsibleCardView.cs`：
   - 增加单卡片与单行级异常隔离沙盒；
   - 在 `BeginRow` 与 `BeginColumn` 块使用 `try-finally { builder.End(); }` 严格保护 `CustomUIBuilder` 容器栈平衡；
   - 按键外部宽度严格锁定原版 `160f`。
6. `src/ProbablyUnifiedHotkeys/Patches/Lifecycle/OptionAndKeyBindPatches.cs`：
   - 显式声明 `[HarmonyPriority(Priority.First)]` 确保接管权；
   - 新增 `Patch_ModHook_FireOnGameLoadedLate`（Postfix），在进入局内存档第三方本地化就绪后自动触发 `ModCatalogCache.Invalidate()` 热刷新。

### 8.2 新增专项回归单测套件 (`FaultIsolationAndItemStackingTests.cs`)
- `tests/ProbablyUnifiedHotkeys.Tests/FaultIsolationAndItemStackingTests.cs`（4 项用例）：
  - `ItemStacking_NreThrowingDelegates_ScanSucceeds_And_FlagsDegraded`：1:1 模拟物品堆叠增强主菜单空引用，验证扫描零崩溃与降级标志置位；
  - `ItemStacking_LocalizationFallback_ResolvesBilingualLabelsAndTooltips`：验证动态委托崩溃时仍能精准回退至模组本地化表中英文译文；
  - `MultiMod_MixedScenario_FaultIsolation_NormalModsAndVanillaIntact`：验证损坏模组、异常 Getter 模组与正常模组混合场景下的故障隔离与 `HasUnsavedChanges()` 零假脏；
  - `ModCatalogCache_Invalidate_ResetsHasDegradedEntries`：验证缓存失效时降级标志重置。
- **全套单测最终收官状态**：63/63 项测试 100% 绿灯全量通过，0 错误 0 警告。

---

## 9. v1.5.1 细粒度配置、三态显示位置与卡片拖拽排序架构

> **状态**：双端 7 大分组配置体系、三态显示位置模式（合并至选项页、原版左下角独立按钮、仅在改造件齿轮打开）与 6 种卡片排序策略（含鼠标拖拽自由排序与图钉置顶锚点）已全部落地完成。

### 9.1 架构资产与模块职责
1. `src/ProbablyUnifiedHotkeys/Config/ModFeatureConfig.cs`：
   - 划分并注册 7 大配置分区：界面语言、选项页面布局、选项页顶部工具栏、快捷键绑定规则、改造件页面设置、预载及退出加速、调试日志；
   - 细粒度拆分解耦所有子开关（如按键录入三开关、搜索防抖毫秒数、各性能补丁独立启停、冲突按键同时生效控制等），全部支持热切换。
2. `src/ProbablyUnifiedHotkeys/Client/UI/CardDragReorderController.cs`：
   - 卡片右侧 `≡` 手柄事件响应，8px 拖拽阈值防误折叠；
   - 视口上下平滑滚动与动态青色插入指示线；
   - 拖拽事务完成后同步更新 `UiSessionState.ModGroups` 并触发脏状态判定。
3. `src/ProbablyUnifiedHotkeys/Persistence/ComboKeyStore.cs`：
   - 扩充 `SortModOptionGroups` 支持 6 种排序模式与图钉置顶协同；
   - 使用 PlayerPrefs 保存自定义顺序（`custom_card_order.v1`）与启用顺序（`mod_enabled_order.v1`），不产生外部文件读写权限。
4. `src/ProbablyUnifiedHotkeys/Persistence/HotkeyProfileManager.cs`：
   - 预设快照 Schema `puh_profile_v1` 扩充可选 `sortMode` 与 `cardOrder` 字段；
   - 过滤本模组自身设置防污染，导入旧版预设保持向下兼容。
5. `src/ProbablyUnifiedHotkeys/Client/State/UiSessionState.cs`：
   - 实现卡片排序 5 大防假脏状态机保护；
   - 拖动还原到基准顺序时自动清除脏标记；
   - 隔离 Transform 操作避免离线单元测试触发 Unity 引擎内部调用。

### 9.2 新增单元测试套件 (`V151FeatureSwitchesAndCardSortTests.cs`)
- `tests/ProbablyUnifiedHotkeys.Tests/V151FeatureSwitchesAndCardSortTests.cs`（18 项核心用例）：
  - `V151_ConfigSectionsAndEnumRoundTrip_Verification`：7 大分区常量、显示位置三态往返、排序模式六态往返、搜索输入延时安全钳制；
  - `V151_ConflictScannerCache_DynamicOptionVisibility_Test`：冲突检测器缓存开关动态显隐；
  - `V151_CardSortMode_SixSortingStrategiesAndPinToTop_Verification`：6 种排序策略与模组卡片排布逻辑；
  - `V151_CardDrag_FiveDirtyStateGuards_Verification`：拖拽排序 5 大防假脏状态机闭环；
  - `V151_ResetToDefaults_ExemptsCardSortMode_Verification`：恢复默认豁免卡片排序设置；
  - `V151_ProfileSnapshot_ExpansionAndBackwardCompatibility_Verification`：预设快照扩展与旧快照向下兼容；
  - `V151_AllowConflictingKeys_SuppressionAndBypass_Verification`：冲突按键同时生效开关与次级抑制仲裁；
  - `V151_AntiAiWriting_StaticAssertionGuardrail`：静态扫描全量配置标题与说明，断言 0 命中人机味禁词；
  - `ModFeatureConfig_EnsureRegistered_MultipleCalls_NoDuplicateDelegates`：连续注册委托防重与防保存卡顿；
  - `ModFeatureConfig_HideOwnCoverInModsPanel_ToggledEventFires`：隐藏本模组封面图即时通知事件；
  - `CardDragReorderController_InterfacesAndStateReset_Verification`：拖拽手柄 5 项事件接口与状态复原；
  - `OptionAndKeyBindPatches_VanillaStandalone_FiltersNonVanillaSectionsAndEntries`：模组独立界面下原版纯净过滤与恢复；
  - `UiSessionState_VanillaStandaloneEscapeAndUnsavedChanges_Verification`：独立模组页 Esc 消费标记与原版未保存检测；
  - `SearchDebounceMs_ModGeneralEntry_ApplyPending_RoundTrip`：搜索延时毫秒数常规条目落盘与输入容错；
  - `TopSearchAndActionBar_ToolbarVisibilityToggle_AutoResetsFilters`：顶栏按钮关闭时自动复位搜索词与冲突视图；
  - `KeybindRules_SixteenStateCombinations_TopHintAndConflictArbitration`：四大改键规则 16 种布尔状态全排列穷举测试；
  - `V151_CardSortMode_DropdownSyncAndDisplayLocation_Integration`：排序模式与显示位置切换事件与中英双语列表完备；
  - `V151_CardDrag_RestrictedUnderSearchFilter_Verification`：搜索与非全部筛选模式下锁定拖拽防脏顺序；
  - `V151_VanillaKeybindWhitelist_FiltersNonVanillaKeybinds_Verification`：原版按键 17 键及工具槽白名单拦截第三方伪装注入键；
  - `V151_BuildSectionAndBuildEntry_InterceptsFaucetScannerInStandalone_Verification`：独立模式下 BuildSection 与 BuildEntry 双重前置拦截；
  - `V151_UiSessionState_ActiveSessionAndBaselineSnapshot_HandlesNotLost_Verification`：活动会话期 Invalidate 防丢失与基准快照防清空引用；
  - `V151_HotkeyProfileManager_LiveCardOrderCaptureAndRestore_Verification`：预设方案实时卡片排布顺序精准捕获与恢复；
  - `V151_DiagnosticAndConflictView_ToolbarVisibilityAndFilterReset_Verification`：进出排查与冲突视图自动复位筛选模式且不重显已隐藏按钮；
  - `Harmony_AllPatchMethods_FollowStandardNamingOrAttributes_Verification`：全程序集 Harmony 补丁方法标准命名与特性声明反射扫描；
  - `OptionAndKeyBindPatches_StandalonePost_TriggersOnVanillaOptionsWindowOpened`：独立模式下 StandalonePost 确保触发原版窗口模组按钮注入；
  - `OptionAndKeyBindPatches_PollKeyCapture_UnblocksVanillaCaptureWhenNotCapturingModHotkey`：原版按键录入与 Esc 退出轮询放行防死锁；
  - `StandaloneModWindow_VanillaHotkeyApplyAndPresetRestore_WritesKeyAndModifiers`：独立模组窗口下原版快捷键预设恢复与修改双向落盘；
  - `V151_VanillaStandaloneWindow_FiveAntiDirtyScenarios_Verification`：独立原版设置页单键、组合键、解绑、重绑及放弃修改 5 大防假脏场景全闭环。
- **全套单测最终总数**：91/91 项测试 100% 绿灯全量通过，0 错误 0 警告。
