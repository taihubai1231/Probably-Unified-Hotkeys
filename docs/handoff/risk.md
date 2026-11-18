# 阶段 3 三批次拆解风险边界与应对预案 (docs/handoff/risk.md)

> **版本**：`v1.5.0`  
> **文档定位**：跨阶段与跨智能代理交接三件套之「风险边界」。深入剖析阶段 3 源码落地过程中三大批次的实施难点、状态机时序竞态、第三方兼容性陷阱与防退化应对预案。

---

## 一、 阶段 3 三批次拆解路线与风险控制矩阵

```mermaid
flowchart TD
    subgraph Batch1["批次一：底座与纯业务层 (Batch 1)"]
        direction TB
        B1_1["Diagnostics (日志与 SafeExecute)"]
        B1_2["Config (ModFeatureConfig)"]
        B1_3["Core (纯领域模型与算法)"]
        B1_4["Persistence (存储与快照)"]
        B1_5["Tests (算法脱机单测)"]
        B1_1 --> B1_2 --> B1_3 --> B1_4 --> B1_5
    end

    subgraph Batch2["批次二：适配与注入层 (Batch 2)"]
        direction TB
        B2_1["Adapter (ModOptionScanner / 缓存)"]
        B2_2["Adapter (KnownModAdapters 11 款桥接)"]
        B2_3["Patches (ModEntry 薄钩子)"]
        B2_4["Patches (PerformancePatches 行池与秒退)"]
        B2_1 --> B2_2 --> B2_3 --> B2_4
    end

    subgraph Batch3["批次三：表现层与巨石拆解 (Batch 3)"]
        direction TB
        B3_1["Client/State (UiSessionState 独立承载)"]
        B3_2["Client/I18n (双语与 SectionLocalizer)"]
        B3_3["Client/UI (10 个正交视图组件)"]
        B3_4["全量实机回归与门禁验收"]
        B3_1 --> B3_2 --> B3_3 --> B3_4
    end

    Batch1 ==>|"坚实底座与无UI纯模型"| Batch2
    Batch2 ==>|"稳定事件管道与适配数据"| Batch3
```

---

## 二、 批次详细风险分析与护栏设计

### 1. 批次一：基础设施与纯业务层迁移 (Batch 1)
- **涵盖模块**：`Diagnostics/`、`Config/`、`Core/`、`Persistence/`、`tests/`。
- **高危风险点**：
  1. **数据模型剥离 UI 控件断裂**：从旧版 `ModModel.cs` 中强行剔除 `GameObject` 与 `CustomUIElement` 字段时，若编译期漏掉下游引用的字段，可能导致后续 UI 接入时语义不一致。
  2. **`KeyCode.None` 修饰位残留漏杀**：在重构 `ComboKeyStore.cs` 时，若未严格执行对 `KeyCode.None` 目标键返回 `Modifiers.None`，将直接破坏 v1.4.7 的核心防假脏成果。
  3. **嵌入式资源 `pinyin.bin` 读取失败**：在 `netstandard2.1` 下使用 `GetManifestResourceStream` 时，若程序集名称或资源路径未正确配置，导致拼音引擎静默失效。
- **应对护栏**：
  - 在 `tests/` 中编写针对 `ConflictDetector`、`ComboKeyStore`、`HotkeyProfileManager` 的独立 NUnit/xUnit 脱机单元测试，不依赖 Unity 运行时验证算法往返一致性。
  - 在 `src/ProbablyUnifiedHotkeys/ProbablyUnifiedHotkeys.csproj` 中显式核对 `<EmbeddedResource Include="assets\pinyin.bin" LogicalName="ProbablyUnifiedHotkeys.pinyin.bin" />`。

### 2. 批次二：适配层与 Harmony 补丁层迁移 (Batch 2)
- **涵盖模块**：`Adapter/`、`Patches/`。
- **高危风险点**：
  1. **反射签名失效导致 `AmbiguousMatchException`**：游戏原版或第三方模组存在同名重载方法（如 `GameOptionUIManager.StartCapture`），若未在 Harmony 补丁中显式声明 `new Type[] { ... }`，游戏启动即报崩溃。
  2. **Mono JIT 方法内联导致钩子静默失效**：若将补丁挂载在第三方极简属性 Getter 上，被 Mono JIT 编译器内联优化后钩子不再触发。
  3. **全量卸载抹除全局补丁**：在 `ModEntry.OnDisable` 中若误调用无参 `UnpatchAll()`，将导致其他工坊模组的补丁被暴力注销引发连环崩溃。
- **应对护栏**：
  - 严格执行 `docs/HARMONY_SPEC.md`：所有重载显式注明参数类型数组；核心按键拦截下沉至底层 `KeyBindHelper`；卸载限定自身专属 ID（`_harmony.UnpatchAll(_harmony.Id)`）。
  - 所有跨模组调用强制包裹在 `SafeExecution.SafeExecute` 隔离网内，严禁裸调。

### 3. 批次三：表现层解耦与巨石 UI 拆解 (Batch 3)
- **涵盖模块**：`Client/State/UiSessionState.cs`、`Client/I18n/`、`Client/UI/`（10 个独立视图组件）。
- **高危风险点**：
  1. **5,137 行巨石拆解时的状态竞态**：原 `UnifiedOptionUI.cs` 内部共享了大量私有字段，拆解为 10 个独立文件后，若状态传递设计不当，极易出现跨组件空指针或事件丢失。
  2. **搜索框失焦点击竞争 (Race Condition)**：`TMP_InputField` 失焦瞬间触发 `OnSearchChanged`，在同一帧内重置下方卡片树，导致用户鼠标点击穿透或目标错位。
  3. **Esc 录键与防误关窗三重拦截失效**：录制按键状态下按 Esc，若吸收信号不及时，同帧穿透到顶层 `CustomUIManager.HandleEscape`，导致选项窗口被误关闭。
  4. **UGUI 布局冲突吞字**：方形折叠按钮内边距未清零、`TMP_InputField` 覆盖 `preferredWidth` 挤压左侧标签等排版回退。
- **应对护栏**：
  - 引入 `UiSessionState.cs` 集中维护运行时 UI 句柄、`BaselineSnapshot` 基准快照与脏状态标志，各 UI 子组件仅作为无状态或弱状态的渲染器。
  - 搜索框变更加入节流防抖处理，卡片树重新排版延迟至帧末执行。
  - 严格对照 `docs/PITFALLS.md` 中的 UGUI 排版三法则（清零内边距、`AddFixedHorizontalSpace`、`layoutPriority=100`）。

---

## 三、 生态高压兼容与官方安全门禁风险

1. **40+ 模组高压环境下的卡顿风险**：
   - 必须通过 `PerformancePatches.cs` 保留 `RowPool` 行对象池复用，禁止动态销毁重建 Prefab；
   - 拦截并消除 `ConflictScanner` 的同帧双重 DseCecil 扫描。
2. **官方 2026-12-10 零权限生死线风险**：
   - 默认发布包必须保持 `DisableDllKeyProbe=true` 编译宏，`manifest.xml` 中 `<Capabilities />` 保持为空；
   - 严禁将 `DseCecil.dll` 打包进发布目录，确保 `tools/ScannerCheck` 扫描达到 `0 Capabilities / 0 HardFlags / 0 Deprecated / Refused=False`。
