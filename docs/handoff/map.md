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
