# 《深空当铺》模组开发踩坑全景账本 (docs/PITFALLS.md)

> **维护说明**：本文件为《深空当铺》（*Probably Stolen*）模组开发、重构与长期维护的最高避坑指南。以下记录的所有深坑案例均来自实机测试、历史崩溃复盘、官方 `ModScanner` 逆向审计与社区玩家反馈。  
> **强制规范**：所有条目严格遵循 **`【现象 Symptom】 -> 【根因 Root Cause】 -> 【解法 Remedy】 -> 【严禁反模式 Anti-pattern】`** 四元组结构编写。任何协作者在编写或重构相关模块时必须严格对照自检。

---

## 目录索引
- [一、 官方安全门禁与静态合规类 (ModScanner / Sandbox)](#一-官方安全门禁与静态合规类-modscanner--sandbox)
  - [1. 官方 2026-12-10 零权限门禁与白名单准入机制](#1-官方-2026-12-10-零权限门禁与白名单准入机制)
  - [2. `ModScanner` 静态 IL 审计对 `computed type` 动态反射的拦截](#2-modscanner-静态-il-审计对-computed-type-动态反射的拦截)
  - [3. 零外部无命名空间反射红线 (`reflection-external`)](#3-零外部无命名空间反射红线-reflection-external)
- [二、 游戏底层生命周期与引擎陷阱类 (Unity / UGUI / Lifecycle)](#二-游戏底层生命周期与引擎陷阱类-unity--ugui--lifecycle)
  - [4. 主菜单生命周期与局内单例 NRE 闪退](#4-主菜单生命周期与局内单例-nre-闪退)
  - [5. UI 离线装配 `SetActive(false)` 与改造件 `RowPool` 对象池](#5-ui-离线装配-setactivefalse-与改造件-rowpool-对象池)
  - [6. TextMeshPro 动态字号测量性能灾难与 `FittedFontSizeCache`](#6-textmeshpro-动态字号测量性能灾难与-fittedfontsizecache)
  - [7. UGUI 布局引擎三大排版冲突（内边距/占位挤压/输入框吞字）](#7-ugui-布局引擎三大排版冲突内边距占位挤压输入框吞字)
- [三、 状态机、交互竞态与数据流陷阱 (Input / State Machine / Dirty-State)](#三-状态机交互竞态与数据流陷阱-input--state-machine--dirty-state)
  - [8. 搜索框 `TMP_InputField` 失焦事件与列表点击竞态](#8-搜索框-tmp_inputfield-失焦事件与列表点击竞态)
  - [9. 组合键精确抑键机制与单双键互斥隔离](#9-组合键精确抑键机制与单双键互斥隔离)
  - [10. Esc 改键录制缓冲三重拦截与主菜单禁设 `closeOnEscape=false`](#10-esc-改键录制缓冲三重拦截与主菜单禁设-closeonescapefalse)
  - [11. `KeyCode.None` 解绑清空残留修饰位与 `BaselineSnapshot` 刷新](#11-keycodenone-解绑清空残留修饰位与-baselinesnapshot-刷新)
  - [12. 键码词元边界前缀匹配（防止无边界子串误命中）](#12-键码词元边界前缀匹配防止无边界子串误命中)
- [四、 Harmony 注入与底层执行流陷阱 (Harmony 2.4.2 / Mono JIT)](#四-harmony-注入与底层执行流陷阱-harmony-242--mono-jit)
  - [13. Harmony 重载方法匹配失败与显式 `Type[]` 签名声明](#13-harmony-重载方法匹配失败与显式-type-签名声明)
  - [14. Mono JIT 编译器方法内联优化导致钩子静默失效](#14-mono-jit-编译器方法内联优化导致钩子静默失效)
  - [15. 热路径反射性能损耗与强类型委托缓存（`assemblyEpoch`）](#15-热路径反射性能损耗与强类型委托缓存assemblyepoch)
  - [16. Harmony 全量卸载污染与退出极速秒退链路](#16-harmony-全量卸载污染与退出极速秒退链路)
- [五、 构建工具链与分发平台陷阱 (PowerShell / Steamworks)](#五-构建工具链与分发平台陷阱-powershell--steamworks)
  - [17. Windows PowerShell 5.1 加载 Mono `mscorlib.dll` 死递归崩栈](#17-windows-powershell-51-加载-mono-mscorlibdll-死递归崩栈)
  - [18. Steamworks 创意工坊 `schinese` 语言槽位不可注销与覆盖陷阱](#18-steamworks-创意工坊-schinese-语言槽位不可注销与覆盖陷阱)

---

## 一、 官方安全门禁与静态合规类 (ModScanner / Sandbox)

### 1. 官方 2026-12-10 零权限门禁与白名单准入机制
- **【现象 Symptom】**：声明了 `<Capability>files</Capability>` 的模组在开发阶段正常加载，但在 2026 年 12 月 10 日官方更新后，玩家启动游戏直接弹出模组被封禁提示，控制台报错 `Mod is refused loading: capabilities require trust approval`，`Refused=True`。
- **【根因 Root Cause】**：官方自 2026-12-10 起启动安全白名单制度（`trust-list.json`）。所有声明外部文件读写（`files`）或网络（`network`）权限的模组，必须由官方开发者人工审查签名；未被签名认证的模组强制阻断加载。
- **【解法 Remedy】**：确立**分级降级架构（Tiered Architecture）**与**默认 0 权限纯净模式**。
  1. `src/ProbablyUnifiedHotkeys/ProbablyUnifiedHotkeys.csproj` 默认开启 `<DisableDllKeyProbe>true</DisableDllKeyProbe>`（定义编译宏 `DISABLE_DLL_KEY_PROBE`），剔除外部扫描模块。
  2. 发布用的 `manifest.xml` 中保持 `<Capabilities />` 为空，确保静态扫描结果为 `0 Capabilities / 0 HardFlags / 0 Deprecated / Refused=False`。
  3. 带有 `files` 权限的跨目录静态热键探测仅作为可选分支保留，待官方信任申请通过后再行合入。
- **【严禁反模式 Anti-pattern】**：严禁在未获官方 `trust-list.json` 认证前，将声明了 `<Capability>files</Capability>` 的构建包推送到创意工坊默认更新通道。

### 2. `ModScanner` 静态 IL 审计对 `computed type` 动态反射的拦截
- **【现象 Symptom】**：模组在本地编译顺利通过，但在经过 `tools/ScannerCheck` 扫描时触发安全警告：`AccessTools.Field on a computed type: type cannot be determined statically`，导致门禁检查失败。
- **【根因 Root Cause】**：在 Harmony 补丁或初始化代码中，直接调用了 `__instance.GetType()`、`Type.GetType(variable)` 等动态运行时表达式求取类型，官方静态扫描器（基于 Cecil 解析 IL 指令）无法在静态分析时推断出确定目标类型，因此标记为不可控的动态计算类型告警。
- **【解法 Remedy】**：一律使用常量字面量显式解析类型或强类型 `typeof`：
  ```csharp
  // 正确写法：字面量显式解析或强类型声明
  var targetType = AccessTools.TypeByName("ProbablyStolen.VanillaNamespace.TargetClass");
  var field = AccessTools.Field(typeof(TargetClass), "fieldName");
  ```
- **【严禁反模式 Anti-pattern】**：严禁在 `[HarmonyPatch]` 或方法体中编写 `__instance.GetType().GetField(...)` 或 `AccessTools.Method(__instance.GetType(), ...)`。

### 3. 零外部无命名空间反射红线 (`reflection-external`)
- **【现象 Symptom】**：模组尝试对工坊中无命名空间的第三方插件（如 `probablystolen.trainer`、`RealTimeClock` 的 `ClockAlarm` 类）挂载补丁，静态扫描器将其直接标记为高危废弃特性 `reflection-external`，并在加载时判定 `Refused=True`。
- **【根因 Root Cause】**：游戏官方为防范无序反射劫持，禁止对不带合法命名空间（点号分隔的全局根命名空间）的外部程序集执行反射或补丁注入，官方加载器将其视为潜在的恶意篡改。
- **【解法 Remedy】**：
  1. 对此类第三方模组，严禁使用 Harmony 补丁拦截。
  2. 统一通过游戏官方提供的合法安全通道交互：读取其在 `ModData` 沙盒内生成的只读配置文件，或通过 Unity 标准组件事件和只读配置项进行桥接。
- **【严禁反模式 Anti-pattern】**：严禁在 `AccessTools.Method`、`Type.GetType` 或 `HarmonyPatch` 中以无命名空间类名（如单一标识符 `trainer`）作为目标。

---

## 二、 游戏底层生命周期与引擎陷阱类 (Unity / UGUI / Lifecycle)

### 4. 主菜单生命周期与局内单例 NRE 闪退
- **【现象 Symptom】**：安装模组后启动游戏，在到达主菜单阶段游戏主进程直接闪退（CTD）或黑屏，`Player.log` 中记录大量的 `NullReferenceException`。
- **【根因 Root Cause】**：官方模组接口 `IMod.Init(ModManifest)`、`OnEnable()` 在主菜单场景初始化阶段即被调用。若在此时提前访问局内专属单例（如 `StoreUIManager.Instance`、`GameSlotInventory.Instance`、`PlayerData.Instance`），由于局内存档与场景尚未构建，这些单例全部为 `null`，从而触发未捕获的空引用异常崩溃。
- **【解法 Remedy】**：
  1. 严格区分**生命周期两阶段**：主菜单阶段仅允许初始化静态配置、语言包、Harmony 薄钩子与空闲预热器。
  2. 涉及局内单例的逻辑必须延迟到局内特定事件触发或在调用前进行显式判空防护：
  ```csharp
  if (StoreUIManager.Instance == null) return;
  ```
- **【严禁反模式 Anti-pattern】**：严禁在 `ModEntry.Init` 或 `OnEnable` 中直接读取或缓存任何局内玩家存档单例。

### 5. UI 离线装配 `SetActive(false)` 与改造件 `RowPool` 对象池
- **【现象 Symptom】**：玩家订阅了 40+ 个创意工坊模组时，打开「改造件 (Mods)」列表或切换筛选标签时出现严重卡顿，掉帧至 10 FPS 以下，伴随数十兆 GC 内存分配尖刺。
- **【根因 Root Cause】**：原版 `ModUIManager.PopulateLayoutList` 在每次标签切换或重排时，暴力循环调用 `Destroy()` 销毁全部 Prefab 实例，然后重新循环 `Instantiate()`。同时在构建包含数十个卡片的「选项」窗口时，如果在根节点激活状态下逐个动态添加控件，每个控件的挂载均会触发 UGUI 树的重新计算（`Canvas.BuildBatch` 与 `LayoutRebuilder`）。
- **【解法 Remedy】**：
  1. **离线批量装配**：在装配增强选项窗口前，先将根节点设为非激活状态（`windowRoot.SetActive(false)`），待全部卡片、操作栏、按钮添加完毕后再单次设置 `SetActive(true)`。
  2. **改造件行对象池 (`RowPool`)**：在 `PerformancePatches.cs` 中实现按模组 ID 索引的行对象池，切页或过滤时仅做显隐（`SetActive`）与数据重新绑定，避免频繁销毁和实例化。
- **【严禁反模式 Anti-pattern】**：严禁在父节点 `activeInHierarchy=true` 的情况下连续在循环内动态挂载大量带有 `LayoutGroup` / `ContentSizeFitter` 的子控件。

### 6. TextMeshPro 动态字号测量性能灾难与 `FittedFontSizeCache`
- **【现象 Symptom】**：在选项界面展开所有模组卡片时，游戏发生持续 1~3 秒的深度卡死；若高频点击全部展开/收起，帧率出现雪崩。
- **【根因 Root Cause】**：模组卡片内有 100+ 个快捷键按钮与标签，若为适应长文本而开启了 `TMP_Text.enableAutoSizing = true`，TextMeshPro 在每次排版刷新时会对每个文本组件执行 5~10 轮内部二分测量来计算合适字号，导致巨大的 CPU 测量开销。
- **【解法 Remedy】**：
  1. 全面禁止使用 `TMP_Text.enableAutoSizing`。
  2. 采用高效的单次拟合测量引擎 `FittedFontSizeCache`：利用 `TMP_Text.GetPreferredValues` 在预估字号区间内做单次宽高双约束计算，并将计算结果缓存到容量上限为 `2048` 条的 LRU/字典中。
- **【严禁反模式 Anti-pattern】**：严禁在动态生成的循环行文本组件上设置 `tmp.enableAutoSizing = true`。

### 7. UGUI 布局引擎三大排版冲突（内边距/占位挤压/输入框吞字）
- **【现象 Symptom】**：
  - 现象 A：`36x36` 方形折叠按钮内的文字或箭头图标被压扁变形，甚至完全看不见。
  - 现象 B：多行排查诊断标签换行时，左侧的缩进距离忽宽忽窄，极不规整。
  - 现象 C：设置项中的长文本输入框（如物品 ID 过滤器）将其左侧的文字挤扁，导致左侧标题垂直单字换行。
- **【根因 Root Cause】**：
  - 根因 A：游戏原生 `Meta_Button` 预制体自带 `HorizontalLayoutGroup.padding = (16, 16, 4, 4)`，在 `36px` 极窄尺寸下，内边距侵占了可用空间。
  - 根因 B：官方 `CustomUIBuilder.AddSpace(pixels)` 只设置了 `preferredWidth`，未锁死 `minWidth`（保持 `-1`），同行文字换行时，`HorizontalLayoutGroup` 会优先压缩该占位组件。
  - 根因 C：`TMP_InputField` 自身实现了 `ILayoutElement`，内部计算出的 `preferredWidth` 常常超过 1000px，完全覆盖了调用方声明的 `SetFixedWidth(306f)`。
- **【解法 Remedy】**：
  - 解法 A：方形折叠按钮必须显式清零内边距（`padding = (0, 0, 0, 0)`），并由外部布局组管控外边距。
  - 解法 B：实现并使用 `AddFixedHorizontalSpace`，同时锁死 `minWidth = preferredWidth` 且设置 `flexibleWidth = 0`。
  - 解法 C：在长文本输入框挂载的 `LayoutElement` 上显式设置 `layoutPriority = 100`，强制使声明的固定宽度（如 `306f`）获得最高仲裁权重。
- **【严禁反模式 Anti-pattern】**：严禁使用 `AddSpace` 作为同行换行标签的左侧对齐缩进锚点；严禁直接对 `TMP_InputField` 仅设置 `preferredWidth` 而不指定 `layoutPriority`。

---

## 三、 状态机、交互竞态与数据流陷阱 (Input / State Machine / Dirty-State)

### 8. 搜索框 `TMP_InputField` 失焦事件与列表点击竞态
- **【现象 Symptom】**：用户在顶部搜索框输入文字后，直接用鼠标点击下方卡片中的「前往设置」或「录制按键」按钮，发现点击被吞或者错位点击了相邻的卡片。
- **【根因 Root Cause】**：鼠标按下时，搜索框触发失焦并分发 `onEndEdit` / `OnSearchChanged` 事件，该事件在同一帧内驱动了下方列表的重新过滤和排版重构，导致点击松开时原本位于鼠标光标下的控件已被重新定位或销毁，引发点击竞态与事件丢失。
- **【解法 Remedy】**：
  1. 将搜索框的变动分发解耦为轻量节流防抖机制（Debounce，如 `150ms` 延迟判定）。
  2. 若当前正在分发 UI 交互事件，延迟一帧在 `LateUpdate` 或协程中执行全量卡片重新布局。
- **【严禁反模式 Anti-pattern】**：严禁在 `TMP_InputField.onValueChanged` 或 `onEndEdit` 的回调同步函数体内直接执行整树 `Destroy` 与重排。

### 9. 组合键精确抑键机制与单双键互斥隔离
- **【现象 Symptom】**：玩家为某功能绑定了 `Ctrl + C`，当按下 `Ctrl + C` 时，不仅触发了该功能，游戏内的单键 `C`（如打开状态面板）也被同时触发；或者单独按单键 `C` 时，意外触发了 `Ctrl + C` 绑定的操作。
- **【根因 Root Cause】**：游戏默认采用原始的 `Input.GetKeyDown(KeyCode.C)` 轮询，未检查当前修饰键（`Ctrl` / `Shift` / `Alt`）的按下状态；输入拦截如果只处理单键映射，未做前置条件互斥仲裁。
- **【解法 Remedy】**：在 `KeyBindRuntimeHelper.EvaluateKeyBind` 中建立**精确抑键模型**（受 `enable_key_suppression` 开关控制）：
  1. 当且仅当所需修饰键完全匹配时才触发组合键功能。
  2. 若某主键已被注册为某种组合键（如 `Ctrl + C`），当检测到修饰键激活且按下该主键时，拦截底层原生单键 `C` 的输入分发，彻底阻断其向下穿透。
  3. 当没有修饰键按下时，正常放行裸键单击。
- **【严禁反模式 Anti-pattern】**：严禁在 `GetKey` / `GetKeyDown` 的补丁中直接粗暴吞没所有同名键，必须判断修饰键位掩码。

### 10. Esc 改键录制缓冲三重拦截与主菜单禁设 `closeOnEscape=false`
- **【现象 Symptom】**：
  - 现象 A：在按键录制状态（提示 `请按键... (Esc=无)`）下按下 `Esc`，期望是将该键解绑为 `无`，结果不仅按键没清空，反而直接弹出了「检测到未保存的更改」退出确认弹窗，甚至直接关闭了选项窗口。
  - 现象 B：若为了解决现象 A 简单粗暴将选项窗口的 `closeOnEscape` 设置为 `false`，在主菜单打开选项窗口后按 `Esc` 键，整个界面毫无反应彻底假死。
- **【根因 Root Cause】**：
  - 根因 A：`StoreUIManager.Update` 与 `GameOptionUIManager.Update` 在同一帧执行时，`Esc` 键的消费标记（`ConsumedEscapeThisFrame`）若未在前置钩子中立即可见，会导致后续层级继续捕获 `Esc` 并触发 `CloseUI()`。
  - 根因 B：主菜单的 `Esc` 处理完全由顶层的 `CustomUIManager.HandleEscape` 接管，若窗口属性禁止了 `closeOnEscape`，顶层逻辑将直接放弃处理，导致无法通过键盘退出。
- **【解法 Remedy】**：
  1. **三重前置拦截**：在 `CustomUIManager.HandleEscape`、`GameOptionUIManager.ConsumedEscapeThisFrame` 与 `CloseUI` 三个钩子中联合检查当前是否处于 `IsCapturingKey` 录制缓冲期；若处于录制状态，消费当帧 `Esc` 信号将其设为 `KeyCode.None`，并在后续缓冲帧（如 `2 帧`）内继续吸收残留 `Esc` 信号。
  2. **保留窗口可退出性**：绝不随意修改窗口原生 `closeOnEscape` 属性，交由增强状态机统一仲裁。
- **【严禁反模式 Anti-pattern】**：严禁在主菜单选项窗口上直接执行 `window.closeOnEscape = false`。

### 11. `KeyCode.None` 解绑清空残留修饰位与 `BaselineSnapshot` 刷新
- **【现象 Symptom】**：玩家将某个快捷键解绑为「无」（`KeyCode.None`），点击「应用」保存成功后，按 `Esc` 退出选项界面，系统依然弹出警告：“检测到未保存的更改”。
- **【根因 Root Cause】**：
  1. `ComboKeyStore.GetSavedModifiers` 在目标键为 `KeyCode.None` 时，未严格重置，意外回落返回了该键历史保存的残留修饰键掩码（如残留了 `Ctrl`）。
  2. 部分第三方模组的属性 Getter 存在单帧读取滞后，导致脏标记判定（`HasUnsavedChanges()`）对比基准与当前值不一致。
- **【解法 Remedy】**（v1.4.7 核心规范）：
  1. `ComboKeyStore.GetSavedModifiers` 查询 `KeyCode.None` 时必须坚决返回 `None`（零修饰位）。
  2. `HasUnsavedChanges()` 中加入显式解绑豁免：若当前键位与基准均为解绑状态，无视修饰位波动。
  3. 在 `ApplyAllSettings()` 执行完成后，强制立即抓取当前全量状态作为新的基准快照（`BaselineSnapshot`），并将脏状态标志彻底复位为 `false`。
- **【严禁反模式 Anti-pattern】**：严禁在按键被置为 `KeyCode.None` 后，仍然保留或序列化其旧有的 `Shift/Ctrl/Alt` 修饰键信息。

### 12. 键码词元边界前缀匹配（防止无边界子串误命中）
- **【现象 Symptom】**：在搜索栏输入英文 `yp` 时，原本只想搜索包含“样片”拼音的选项，结果意外匹配出了包含快捷键 `KeyCode.Keypad6` 的无关设置项。
- **【根因 Root Cause】**：直接使用 `KeyCode.ToString().IndexOf(token, StringComparison.OrdinalIgnoreCase)` 进行无边界子串包含搜索，导致 `Keypad` 中的 `yp` 字符被意外命中。
- **【解法 Remedy】**：实现 `MatchesKeyCodeSearchToken` 边界算法：
  1. 仅允许在词元边界处（如词首、下划线、驼峰大写字母处）进行匹配，或将 `Keypad6` 规范化为 `小键盘 6` / `KP6` 别名后再行比对。
  2. 严格区分显示文本边界与纯键码标识符。
- **【严禁反模式 Anti-pattern】**：严禁直接对 `KeyCode.ToString()` 执行无前缀/无词元边界约束的纯子串 `Contains()` 判定。

---

## 四、 Harmony 注入与底层执行流陷阱 (Harmony 2.4.2 / Mono JIT)

### 13. Harmony 重载方法匹配失败与显式 `Type[]` 签名声明
- **【现象 Symptom】**：启动游戏时补丁加载失败，日志报错：`HarmonyLib.AmbiguousMatchException: Multiple matches found for method StartCapture`，模组按键捕获完全瘫痪。
- **【根因 Root Cause】**：目标类（如 `GameOptionUIManager`、`CustomUIWindow`）内部存在多个同名重载方法（例如 `Get(string)` 与 `Get(int)`），在通过 `AccessTools.Method` 寻找目标方法时未指明参数类型列表，导致 Harmony 反射查找歧义。
- **【解法 Remedy】**：凡是被修补的方法存在重载，必须在 `AccessTools.Method` 或 `[HarmonyPatch]` 属性中显式指明完整的参数类型签名数组：
  ```csharp
  // 正确写法：显式指定参数类型
  [HarmonyPatch(typeof(GameOptionUIManager), nameof(GameOptionUIManager.StartCapture), new Type[] { typeof(string) })]
  ```
- **【严禁反模式 Anti-pattern】**：严禁仅凭借方法名字符串 `AccessTools.Method(type, "MethodName")` 查找具有多个重载的目标方法。

### 14. Mono JIT 编译器方法内联优化导致钩子静默失效
- **【现象 Symptom】**：针对第三方模组的属性 Getter（如 `ConfigEntry<KeyCode>.Value`）挂载了 Harmony 补丁，但游戏运行时补丁一次都没有被执行，改键逻辑完全失效，且没有任何报错。
- **【根因 Root Cause】**：Unity Mono BleedingEdge 的 JIT 编译器在优化时，对代码非常简短的属性 Getter（通常仅有 1~2 条 IL 指令，如 `ldfld` + `ret`）实施了方法内联（Inlining）。内联后调用点直接读取了字段内存，不再经过该方法入口，因此挂载在该方法头部的 Harmony 补丁被静默绕过。
- **【解法 Remedy】**：
  1. 严禁依赖可能被 JIT 内联的极简属性 Getter。
  2. 将钩子下沉到无法被内联的更底层、逻辑更复杂的输入分发入口：如 `KeyBindHelper.GetKeyDown`、`KeyBindHelper.GetKey` 或 Unity 原生 `Input.GetKeyInt`。
- **【严禁反模式 Anti-pattern】**：严禁将核心改键转发逻辑寄托在第三方极简属性 `ConfigEntry.Value` 的 Getter 补丁上。

### 15. 热路径反射性能损耗与强类型委托缓存（`assemblyEpoch`）
- **【现象 Symptom】**：游戏进入局内后，平时帧率平稳，但只要开始频繁按键操作或处于高频按键轮询时，出现肉眼可见的微卡顿，Profiler 显示大量 CPU 耗时停留在 `System.Reflection.MethodBase.Invoke`。
- **【根因 Root Cause】**：在 `Update`、`GetKeyDown` 或 `PollCapture` 等每秒调用数十次甚至数百次的热路径中，反复通过反射调用 `MethodInfo.Invoke`，产生严重的装箱与调用开销。
- **【解法 Remedy】**：
  1. 在模组初始化或首次调用时，通过 `Delegate.CreateDelegate` 将目标反射方法转换为强类型的开放/封闭委托并保存在静态字段中。
  2. 建立 `assemblyEpoch` 版本控制：当发生动态程序集热重载或重新初始化时刷新委托缓存。
- **【严禁反模式 Anti-pattern】**：严禁在每帧 Update 循环或按键检测钩子内直接调用 `MethodInfo.Invoke`。

### 16. Harmony 全量卸载污染与退出极速秒退链路
- **【现象 Symptom】**：
  - 现象 A：模组在 `OnDisable` 时调用了 `Harmony.UnpatchAll()`，导致玩家安装的其他 10 几个模组的补丁被一并暴力抹除，游戏瞬间崩溃。
  - 现象 B：点击「退出游戏」后，游戏画面卡死假死 2~5 秒才真正关闭。
- **【根因 Root Cause】**：
  - 根因 A：无参的 `UnpatchAll(null)` 会卸载当前应用程序域内**所有模组**的所有补丁。
  - 根因 B：官方与第三方模组在退出阶段逐个反注册数十个动态生成的补丁并等待遥测网络请求超时，造成退出极度卡顿。
- **【解法 Remedy】**：
  1. 卸载必须限定自身专属的 Harmony ID：
     ```csharp
     harmonyInstance?.UnpatchAll(harmonyInstance.Id);
     ```
  2. 实现退出秒退旁路（`PerformancePatches.cs`）：监听 `Application.wantsToQuit` 与 `Application.quitting`，当玩家点击确认退出时，在持久化配置安全落盘后，跳过冗余的反注册与遥测等待，直接走秒退链路。
- **【严禁反模式 Anti-pattern】**：严禁调用无参 `UnpatchAll()`；严禁在退出流程中执行阻塞式网络请求或繁重的逐个反编译卸载。

---

## 五、 构建工具链与分发平台陷阱 (PowerShell / Steamworks)

### 17. Windows PowerShell 5.1 加载 Mono `mscorlib.dll` 死递归崩栈
- **【现象 Symptom】**：在 Windows PowerShell 终端中执行检查脚本时，终端突然崩溃闪退，或者系统弹出致命弹窗：“`powershell.exe - 系统错误：无法创建新的堆栈防护页面`”。
- **【根因 Root Cause】**：Windows PowerShell 5.1 运行于宿主 .NET Framework 4.8 运行时环境。当脚本中注册了 `AppDomain.CurrentDomain.add_AssemblyResolve` 并尝试动态加载游戏 `Managed\` 目录下的程序集时，由于该目录包含 Unity Mono BleedingEdge 定制的 `mscorlib.dll`、`System.Core.dll`，.NET Framework 运行时加载了不兼容的 Mono 核心库，导致类型加载器发生死递归陷入死锁，最终耗尽线程栈空间并爆破堆栈防护页（`StackOverflowException`）。
- **【解法 Remedy】**：
  1. **绝对红线**：严禁在任何 PowerShell 脚本中直接向宿主 CLR 反射加载游戏 `Managed\` 目录。
  2. 所有针对游戏 `Assembly-CSharp.dll` 的元数据签名分析、类型检查及工坊扫描，统一交由基于 `DseCecil` 静态读取的独立 **.NET 8 控制台工具**（`tools/ScannerCheck`）执行。
- **【严禁反模式 Anti-pattern】**：严禁在 `.ps1` 脚本中调用 `[System.Reflection.Assembly]::LoadFrom(".../Managed/Assembly-CSharp.dll")`。

### 18. Steamworks 创意工坊 `schinese` 语言槽位不可注销与覆盖陷阱
- **【现象 Symptom】**：通过 `steamcmd` 命令行脚本发布了新版本模组并成功上传更新日志，但国内使用简体中文 Steam 客户端的玩家更新后，在模组详情页看到的依然是数月前的旧版更新说明。
- **【根因 Root Cause】**：Valve Steamworks UGC API 对工坊物品的语言本地化槽位（如 `schinese`、`english`）**没有提供注销或删除接口**；一旦某物品历史上写入过 `schinese` 槽位，中文客户端将永远优先展示该槽位的内容。而普通的 `steamcmd` 上传脚本默认只会覆盖全局 `default` 槽位，无法更新既有的 `schinese` 槽位。
- **【解法 Remedy】**：确立并严格执行 **`SteamTagger` 三步闭环发布 SOP**：
  1. 第一步：运行 `publish_workshop.ps1 -Step default`，完成文件与默认槽位更新。
  2. 第二步：在桌面端重新登录 Steam，并运行 `-Step query` 验证当前登录态为 `BLoggedOn=True`。
  3. 第三步：运行 `-Step schinese -SkipCheck`，显式调用 Steamworks API 精准覆写 `schinese` 语言槽位。
- **【严禁反模式 Anti-pattern】**：严禁仅依赖 `steamcmd` 的单一上传命令作为最终的创意工坊发布，否则会导致简体中文玩家看到过期的历史文案。
