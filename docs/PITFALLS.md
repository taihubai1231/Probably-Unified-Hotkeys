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
  - [19. 录键状态机右键误当取消键与无修饰左键取消契约 (ADR-010)](#19-录键状态机右键误当取消键与无修饰左键取消契约-adr-010)
- [四、 Harmony 注入与底层执行流陷阱 (Harmony 2.4.2 / Mono JIT)](#四-harmony-注入与底层执行流陷阱-harmony-242--mono-jit)
  - [13. Harmony 重载方法匹配失败与显式 `Type[]` 签名声明](#13-harmony-重载方法匹配失败与显式-type-签名声明)
  - [14. Mono JIT 编译器方法内联优化导致钩子静默失效](#14-mono-jit-编译器方法内联优化导致钩子静默失效)
  - [15. 热路径反射性能损耗与强类型委托缓存（`assemblyEpoch`）](#15-热路径反射性能损耗与强类型委托缓存assemblyepoch)
  - [16. Harmony 全量卸载污染与退出极速秒退链路](#16-harmony-全量卸载污染与退出极速秒退链路)
  - [20. Unity Mono extern internalcall 动态生成跳板 IL 校验崩溃 (`IL_0024: ret`)](#20-unity-mono-extern-internalcall-动态生成跳板-il-校验崩溃-il_0024-ret)
  - [21. 模组冲突检测器 53-DLL 连环重扫与环境签名缓存](#21-模组冲突检测器-53-dll-连环重扫与环境签名缓存)
- [五、 构建工具链与分发平台陷阱 (PowerShell / Steamworks)](#五-构建工具链与分发平台陷阱-powershell--steamworks)
  - [17. Windows PowerShell 5.1 加载 Mono `mscorlib.dll` 死递归崩栈](#17-windows-powershell-51-加载-mono-mscorlibdll-死递归崩栈)
  - [18. Steamworks 创意工坊 `schinese` 语言槽位不可注销与覆盖陷阱](#18-steamworks-创意工坊-schinese-语言槽位不可注销与覆盖陷阱)
- [六、 15 项实机反馈与高压性能深水坑 (In-Game Feedback & 40-Mod Latency)](#六-15-项实机反馈与高压性能深水坑-in-game-feedback--40-mod-latency)
  - [22. `PropertyInfo.SetValue` 只读属性（无 setter）反射写入致命崩溃](#22-propertyinfosetvalue-只读属性无-setter反射写入致命崩溃)
  - [23. `OverriddenDropdowns` 底层残留与原生控件未修改虚假脏状态误弹窗](#23-overriddendropdowns-底层残留与原生控件未修改虚假脏状态误弹窗)
  - [24. 搜索框文本变动每字符 12 次全树重排风暴与防抖缺失](#24-搜索框文本变动每字符-12-次全树重排风暴与防抖缺失)
  - [25. 40 模组 800 个 TMP 文本组件同帧首次激活卡死与脱机批量瞬开](#25-40-模组-800-个-tmp-文本组件同帧首次激活卡死与脱机批量瞬开)
  - [26. `ProfilePresetCardView` 误调 `SetKeyBindStatusDelegate` 污染原版卡片标题栏](#26-profilepresetcardview-误调-setkeybindstatusdelegate-污染原版卡片标题栏)
  - [27. 扫描与开窗未开启 `EnterBypass` 导致组合键退化单键与假脏弹窗](#27-扫描与开窗未开启-enterbypass-导致组合键退化单键与假脏弹窗)
  - [28. 改造件详情列隐藏封面后父 Row 强锁与 ContentSizeFitter 挤压错位灾难](#28-改造件详情列隐藏封面后父-row-强锁与-contentsizefitter-挤压错位灾难)
  - [29. 预编译拼音单音字典局限与高频多音字词元匹配引擎](#29-预编译拼音单音字典局限与高频多音字词元匹配引擎)
  - [30. 原版 InputActionHandler 绕过 GetKeyDown 直读 KeyCode 与同主键效果抵消](#30-原版-inputactionhandler-绕过-getkeydown-直读-keycode-与同主键效果抵消)
  - [31. 改大按钮外部物理宽度破坏界面原生统一感与收窄内留白自适应准则](#31-改大按钮外部物理宽度破坏界面原生统一感与收窄内留白自适应准则)
  - [32. 原版键重绑保存时未清除 ExplicitUnboundHotkeys 导致按键退回「无」与局内失效](#32-原版键重绑保存时未清除-explicitunboundhotkeys-导致按键退回无与局内失效)
  - [33. 离线构建期 `ComboKeyStore.IsBypassed == false` 穿透过滤导致局内重开选项页组合键误显为「无」](#33-离线构建期-combokeystoreisbypassed--false-穿透过滤导致局内重开选项页组合键误显为无)
  - [34. 改造件封面强制 `SetVisible(true)` 导致未配封面模组串图与网络超时卡死](#34-改造件封面强制-setvisibletrue-导致未配封面模组串图与网络超时卡死)
  - [35. 原版退回主菜单 `MainMenuWithAnalytics` 强制 1.0 秒协程空等阻塞与切场卡顿](#35-原版退回主菜单-mainmenuwithanalytics-强制-10-秒协程空等阻塞与切场卡顿)
  - [36. 第三方模组延迟本地化委托主菜单空引用击穿扫描导致选项界面回退原版及四级故障隔离](#36-第三方模组延迟本地化委托主菜单空引用击穿扫描导致选项界面回退原版及四级故障隔离)

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

### 19. 录键状态机右键误当取消键与无修饰左键取消契约 (ADR-010)
- **【现象 Symptom】**：玩家在选项界面点击某按键想要录入鼠标右键（`Mouse1`）作为快捷键时，按下鼠标右键界面直接退出录键状态并恢复为原键位，导致右键无法绑定。
- **【根因 Root Cause】**：旧版录键状态机将鼠标左键与右键均当作“取消录制”，未能区分有意识绑定与取消意图；而《深空当铺》游戏官方原生支持右键改键绑定。
- **【解法 Remedy】**：严格执行 ADR-010 录键契约：
  1. 仅将**无修饰鼠标左键（`Mouse0`）**作为唯一取消键，单击立即退出录制并还原原键位。
  2. **无修饰鼠标右键（`Mouse1`）**直接录入绑定为 `KeyCode.Mouse1`，并置位鼠标释放等待保护（`_waitingMouseRelease = true`），防止同帧穿透。
  3. 修饰键组合（如 `Alt + Mouse0` / `Ctrl + Mouse1`）与鼠标中键/侧键（`Mouse2~6`）均正常录入并绑定。
- **【严禁反模式 Anti-pattern】**：严禁在 `KeyCaptureController` 或 `KeyBindHelper.PollCapture` 中将鼠标右键无条件判定为 `CaptureResult.None` 取消动作。

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

### 20. Unity Mono extern internalcall 动态生成跳板 IL 校验崩溃 (`IL_0024: ret`)
- **【现象 Symptom】**：对 `UnityEngine.Input.GetMouseButton` 等挂载 Harmony Prefix 补丁时，控制台抛出 `Invalid IL code in (wrapper dynamic-method) MonoMod.Utils.DynamicMethodDefinition:UnityEngine.Input.GetMouseButton_Patch1 (int): IL_0024: ret` 异常，连坐导致整个按键拦截方法全部中断。
- **【根因 Root Cause】**：Unity 2021.3 Mono 下 `GetMouseButton(int)` / `Down` / `Up` 属于纯 `[MethodImpl(InternalCall)]` extern 原生方法，原方法没有托管 IL 指令体，MonoMod 生成跳板 IL 时因原方法指令为空触发 IL 校验崩溃。当多个方法置于同一个 try 块内时引发连坐。
- **【解法 Remedy】**：
  1. 将全部输入拦截拆解为独立的 try-catch 隔离块，保证 `GetKey`、`GetKeyDown`、`GetKeyUp` 等核心托管方法 100% 成功挂载。
  2. 对此类 extern internalcall 异常做静默处理（Debug 日志），不影响核心功能。
- **【严禁反模式 Anti-pattern】**：严禁将多个不同特性的 Harmony.Patch 挂载调用混在同一个 try 块中导致连坐中断。

### 21. 模组冲突检测器 53-DLL 连环重扫与环境签名缓存
- **【现象 Symptom】**：玩家订阅数十个模组时，打开「改造件」面板严重卡顿 2~3 秒，日志连续打印 5 次 `[模组冲突检测器]: Scan: 53 DLL(s) read`（高达 265 次反编译解析）。
- **【根因 Root Cause】**：`ConflictScanner` 在 `OpenPanel`、`PopulateLayoutList`、`RefreshRowIssues` 等多处路径重复调用 `ConflictEngine.EnsureBuilt()`，且旧版依赖的缓存判据在第三方版本更新后失效。
- **【解法 Remedy】**：
  1. 在 `ConflictScannerPerfHelper` 中建立全量模组环境签名（包含所有已加载模组的 `IsEnabled` 状态与版本）。
  2. 只要环境签名未改变，在 `EnsureBuilt` 与 `Invalidate` 头部统一拦截并返回 `false` 跳过清空与重扫。
  3. 当且仅当玩家在界面勾选/取消勾选模组导致签名变化时，立即放行重扫，确保功能完好。
- **【严禁反模式 Anti-pattern】**：严禁无条件放行第三方模组在主线程 UI 刷新循环中无节制触发昂贵的 Cecil 反编译与磁盘读取。

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

---

## 六、 15 项实机反馈与高压性能深水坑 (In-Game Feedback & 40-Mod Latency)

### 22. `PropertyInfo.SetValue` 只读属性（无 setter）反射写入致命崩溃
- **【现象 Symptom】**：在选项界面点击「应用」保存设置、保存预设槽位或从剪贴板导入时，界面卡死并抛出 `ArgumentException: Set Method not found for 'Key'` 致命异常（`Player.log` 中连续抛出 10+ 次），导致排在其后的 `ComboKeyStore.Save()`、预设槽位保存、基准快照刷新整条链路全部中途夭折。
- **【根因 Root Cause】**：部分第三方模组（如 `ConflictScanner.Keybind.Key`）的属性为仅带 getter 的只读属性（无 set accessor）。适配层代码若未加防护直接调用 `property.SetValue(null, value)`，CLR 反射引擎由于找不到 Setter 抛出 `ArgumentException`。
- **【解法 Remedy】**：
  1. 全仓地毯式排查对 `PropertyInfo` 的写入操作，写入前必须增加严格前置判断：
     ```csharp
     if (prop != null && prop.CanWrite && prop.GetSetMethod(true) != null)
     {
         prop.SetValue(null, value);
     }
     ```
  2. 对所有适配器的 `SetKey` / `SetCombo` 委托统一用 `try-catch (Exception ex)` 包裹并记录 `ModLogger.Warn`，杜绝任何第三方模组异常外溢。
- **【严禁反模式 Anti-pattern】**：严禁假设第三方公开属性必然存在可用 Setter，严禁裸调未做 `CanWrite` 检查的 `PropertyInfo.SetValue`。

### 23. `OverriddenDropdowns` 底层残留与原生控件未修改虚假脏状态误弹窗
- **【现象 Symptom】**：打开选项窗口未进行任何按键修改或常规设置变动，按 Esc 或点击返回时依然触发「存在未保存更改」确认弹窗。
- **【根因 Root Cause】**：原版 `GameOptionUIManager` 内部维护了 `overriddenDropdowns` 字典，当官方原版分辨率等下拉框因窗口尺寸自适应发生内部更新时，该字典被无辜置位；且旧检测逻辑直接将底层控件读数与原始默认值进行对比，只要原版出厂状态与默认值不同，立即误判为用户修改。
- **【解法 Remedy】**：
  1. 引入**开窗基准快照机制 (`CaptureOpenBaselineSnapshot`)**：在装配完成与规则应用的第一时间快照所有开关、滑块、下拉框的当前真实读数。
  2. 彻底剥离对 `overriddenDropdowns` 字典的污染判据；判定原版常规项脏状态时，要求控件读数必须**同时偏离快照值与底层值**才判定为用户实际修改。
  3. 针对 `KeyCode.None` 显式解绑条目提供豁免，无实质改动时不触发假脏。
- **【严禁反模式 Anti-pattern】**：严禁直接依赖原版 `overriddenDropdowns` 内部字段作为脏状态依据，严禁拿易受外部初始化的默认值直接做判脏基准。

### 24. 搜索框文本变动每字符 12 次全树重排风暴与防抖缺失
- **【现象 Symptom】**：在搜索框中连续输入字符时（如搜 `\"sort\"`），界面严重掉帧甚至卡顿 0.5 秒，打字极度不跟手；输入法候选拼音输入中提前触发过滤；按 Esc 清空时意外关闭窗口。
- **【根因 Root Cause】**：原搜索响应代码在每个字符输入事件触发时，嵌套调用了 `Canvas.ForceUpdateCanvases` 与 `LayoutRebuilder.ForceRebuildLayoutImmediate` 引发全树重排风暴；且防抖延迟过短无法覆盖人类敲键间隔；未识别 IME 拼音输入状态；未在聚焦态接管 Esc 导致主窗口 HandleEscape 误判退出。
- **【解法 Remedy】**：
  1. 升级为 300ms 轻量输入防抖（按回车或清空文本时 0 延迟即刻生效）；失焦防竞态避免吞掉下方按钮点击。
  2. 智能识别输入法合成态：通过 `Input.compositionString` 检测拼音候选串输入，合成期间自动推迟防抖计时器，绝不提前触发全量过滤。
  3. 建立会话级轻量预索引（小写文本 + 拼音首字母 + 全拼），引入前缀收窄短路优化（如 `"sp"` 追加为 `"spe"` 时直接复用上轮不可见标记）。
  4. 扩展 `IOptionUiBridge.IsSearchInputFocused`：当搜索框正处于输入聚焦态时，按 Esc 优先释放输入框聚焦（`DeactivateInputField`），严禁误触窗口关闭或弹出未保存弹窗。
- **【严禁反模式 Anti-pattern】**：严禁在 `onValueChanged` 频繁触发的输入事件中直接同步调用多层 `ForceUpdateCanvases` 与 `ForceRebuildLayoutImmediate`；严禁在文本框聚焦时不加拦截任由 Esc 关闭窗口。

### 25. 40 模组 800 个 TMP 文本组件同帧首次激活卡死与脱机批量瞬开
- **【现象 Symptom】**：玩家订阅 40+ 模组时，点击「全部展开」滑块跳动或出现排队分帧延迟，无法真正一次性原子展开。
- **【根因 Root Cause】**：旧分帧渐进展开队列跨多帧修改卡片激活态，导致右侧滚动条滑块随卡片逐帧加载而剧烈变形跳动，视觉体验割裂；且分帧时钟每帧产生调度开销。
- **【解法 Remedy】**：
  1. **脱机批量切换法（Offline Batch Switch）**：切换时首先将外层根容器 `NormalContentRoot.SetActive(false)` 临时脱机，整棵子树瞬间脱离 Canvas 激活层级，杜绝任何子对象状态变更触发 `CanvasUpdateRegistry` 注册开销。
  2. 在脱机静默状态下，一次性建齐尚未后台构建的模组卡片，并批量一次性设置所有卡片激活态与折叠箭头。
  3. 恢复 `NormalContentRoot.SetActive(true)` 后，仅对 `ScrollRect.content` 执行 1 次 `LayoutRebuilder.ForceRebuildLayoutImmediate`（全部展开 < 8ms 瞬成，全部收起 < 2ms 秒切，右侧滚动条滑块一步到位绝不变形跳动）。
- **【严禁反模式 Anti-pattern】**：严禁采用多帧渐进队列去展开模组列表，脱机批量一次性重排效率远高于分帧频繁激活。

### 26. `ProfilePresetCardView` 误调 `SetKeyBindStatusDelegate` 污染原版卡片标题栏
- **【现象 Symptom】**：点击方案卡片「保存/读取/清空」时，原版「按键绑定」卡片标题栏右侧的 `note_keybinds` 意外冒出预设反馈提示（如绿色「已保存」字样），挤占并污染原版标题排版。
- **【根因 Root Cause】**：`ProfilePresetCardView` 错误调用了 `UiSessionState.SetKeyBindStatusDelegate`，该委托绑定的是原生 `GameOptionUIManager.SetKeyBindStatus`，其专职向 `note_keybinds` 写入提示。
- **【解法 Remedy】**：彻底移除对 `SetKeyBindStatusDelegate` 的调用，预设卡片反馈严格限定于模组专属 UI 元素（`label_profile_action_status`）及窗口底栏。
- **【严禁反模式 Anti-pattern】**：严禁模组自定义业务逻辑复用原版 `GameOptionUIManager.SetKeyBindStatus` 向原版卡片标题写字。

### 27. 扫描与开窗未开启 `EnterBypass` 导致组合键退化单键与假脏弹窗
- **【现象 Symptom】**：绑定组合键（如 `Ctrl + Space` 或 `Shift + F`）后点击应用并关窗，重新打开选项窗口时，组合键退化为单键或清空为 None，甚至开窗即弹出「存在未保存更改」假脏弹窗。
- **【根因 Root Cause】**：
  1. `ModOptionScanner` / `ModCatalogCache` 扫描配置项时，未开启 `ComboKeyStore.EnterBypass()`，此时运行时组合键拦截器将主键掩盖为 None；
  2. `SyncVanillaPendingFromManager` 在 `!hk.HasPendingChange` 时暴力将 `PendingModifiers` 清零，导致原已持久化修饰键被冲刷为 None；
  3. `ExplicitUnbound` 检查裸键名导致一个模组解绑同名键污染其它所有模组。
- **【解法 Remedy】**：
  1. 在扫描与开窗最外层强制包裹 `using (ComboKeyStore.EnterBypass())`；
  2. 重构 `SyncVanillaPendingFromManager`：主键一致且无修改时保持已持久化修饰键；用户仅修改修饰键时自动回填 `pendingKeys` 驱动官方保存；
  3. `ExplicitUnbound` 严格限定于全量规范 ID（`canonicalId`），并在恢复默认时自动释放。
- **【严禁反模式 Anti-pattern】**：严禁在未开启 Bypass 状态下读取可能已被组合键运行时拦截的 `ConfigEntry.Value`；严禁依据裸键名处理显式解绑。

### 28. 改造件详情列隐藏封面后父 Row 强锁与 ContentSizeFitter 挤压错位灾难
- **【现象 Symptom】**：隐藏封面后，详情页上方 7 行模组元数据与正文文字严重重叠挤压，排版混乱不堪；或三个操作按钮被垂直拉伸为几百像素高的畸变巨物。
- **【根因 Root Cause】**：
  1. 初始修复时，错误给父 Row 挂载 `minHeight = 42f, preferredHeight = 42f`，强行将拥有 7 行文本的整行压扁为 42px，导致左侧文本溢出并挤压重叠；
  2. 错误给左侧元数据列 `column` 注入 `ContentSizeFitter`，导致 UGUI 布局引擎在纵向计算时发生环形依赖冲突；
  3. 未对右侧按钮自身的 `LayoutElement` 做出独立高度钳制，依赖父容器分配导致变形。
- **【解法 Remedy】**：
  1. 彻底移除给左侧 `column` 动态注入的 `ContentSizeFitter`，存在时自动销毁还原；
  2. 父 Row 保持原版 `HorizontalLayoutGroup.childControlHeight = true`，并将 `childForceExpandHeight = false`；
  3. 若父 Row 挂有 `LayoutElement`，仅设 `flexibleHeight = 0f`，保持 `minHeight = -1f, preferredHeight = -1f`，由左侧元数据 7 行真实高度自然决定 Row 高度；
  4. 仅在右侧按钮自身注入 `LayoutElement`：严格锁定 `preferredHeight = 42f, minHeight = 42f, flexibleHeight = 0f`（正方形图标按钮额外锁定 `preferredWidth/minWidth = 42f, flexibleWidth = 0f`），配合 `TextAnchor.MiddleLeft` 确保按钮 42px 零拉伸；
  5. 下方文字滚动区 `ScrollRect` 锁定 `flexibleHeight = 1f` 平稳吸收释放出的空间。
- **【严禁反模式 Anti-pattern】**：严禁在含有多行子文本的复杂横向容器上强锁 `preferredHeight` 或 `minHeight`，严禁在 UGUI 嵌套层级中随意添加 `ContentSizeFitter`。

### 29. 预编译拼音单音字典局限与高频多音字词元匹配引擎
- **【现象 Symptom】**：搜索栏输入「chong」搜不到「多重密码解序器」，搜「zhong」却可以搜到；搜索「dcmm」、「dcmmjxq」无法命中；输入「ong」却误匹配到包含「zhong」的不相关项目。
- **【根因 Root Cause】**：
  1. 嵌入式预编译资源 `pinyin.bin` 每个汉字仅编码单一首选读音（如「重」仅记录 `zhong`），多音分支读音全部丢失；
  2. 检索逻辑将拼音全串平铺匹配，未做词元边界判定，导致音节内部任意子串（如 `ong`）误命中词元。
- **【解法 Remedy】**：
  1. 引入静态零分配《高频汉字多音字补充读音字典》（`PolyphoneTable.cs`），收录 215+ 个高频多音字（如「重」覆盖 `chong/zhong`，「调」覆盖 `tiao/diao`，「行」覆盖 `hang/xing` 等）；
  2. 构建零分配多音字匹配引擎（`MatchesPolyphoneTokens`）：使用 `stackalloc ulong[64]` 剪枝位掩码，全面支持全拼、声母缩写、混合拼打与半截前缀容错（如「duoch」命中）；
  3. 严格执行音节边界安全防护，坚决杜绝音节内部任意子串（如 `ong`、`yp`）假阳性误判。
- **【严禁反模式 Anti-pattern】**：严禁假定中文单字仅有单一读音，严禁在拼音检索中使用无音节词元边界保护的平铺子串包含（`Contains`）。

### 30. 原版 InputActionHandler 绕过 GetKeyDown 直读 KeyCode 与同主键效果抵消
- **【现象 Symptom】**：
  1. 玩家将「翻转物品 (`flip_item`)」绑为 `Ctrl + Space`，实机中单按 `Space` 依然能翻转物品；
  2. 玩家将「顺时针旋转 (`rotate_cw`)」绑为 `Ctrl + F`，同时将「逆时针旋转 (`rotate_ccw`)」绑为 `Ctrl + Alt + F`，两个按键在局内同时失效；改成 `Ctrl + D` 后恢复但变成单键。
- **【根因 Root Cause】**：
  1. 游戏物品拖拽/旋转/翻转是由 `ItemMouseDragHandler` 负责，其通过 `InputActionManager.Update()` 在每帧收集各 Handler 的 `get_keyListeners()`，直接遍历执行 `Input.GetKeyDown(keyCode)` 并传给 `OnEventPress`；
  2. 原版此处**完全没有调用 `KeyBindHelper.GetKeyDown`**，而是直接调用 `KeyBindHelper.Get("flip_item")` 获取物理键；
  3. 之前补丁仅挂载了 `GetKeyDown`，未拦截 `KeyBindHelper.Get`，导致其永远返回裸物理键 `KeyCode.Space` 或 `KeyCode.F`；
  4. 当顺逆时针同主键 `F` 时，同一帧内 `RotCW()`（+90°）与 `RotCCW()`（-90°）同时触发，物理效果毫秒级完美抵消（`+90° - 90° = 0°`），视觉上表现为按键彻底失灵！
- **【解法 Remedy】**：
  1. 在 `OptionAndKeyBindPatches.cs` 中挂载底层原生 `KeyBindHelper.Get(string id)` Postfix 补丁；
  2. 处于局内运行期时，若当前动作配置了组合键，仅在修饰键掩码完全匹配时返回物理 KeyCode，否则直接返回 `KeyCode.None`；
  3. 单按物理主键 `F` 时两者均返回 `None`；按 `Ctrl + F` 时仅 `rotate_cw` 返回 `F`，`rotate_ccw` 返回 `None`，精准顺时针旋转绝不抵消；按 `Ctrl + Alt + F` 时仅 `rotate_ccw` 返回 `F`，精准逆时针旋转；
  4. 原版保存期（`KeyBindHelper.Save`）与槽位渲染（`ToolboxHelper.SlotLabel`）注入 `EnterBypass()`，确保底层 ES3 持久化与标签渲染始终拿到底层真实物理按键。
- **【严禁反模式 Anti-pattern】**：严禁只挂钩高级封装的 `GetKeyDown` 而遗漏底层物理键提供源 `KeyBindHelper.Get`；严禁在保存或存档序列化期返回 `KeyCode.None` 导致配置损坏。

### 31. 改大按钮外部物理宽度破坏界面原生统一感与收窄内留白自适应准则
- **【现象 Symptom】**：为了容纳 `Ctrl + Space`、`Ctrl+Alt+F` 等长组合键，将按键按钮外部宽度从 160f 暴力放大到 195f，破坏了原版选项界面规整统一的栅格排版美感，引起玩家视觉不适。
- **【根因 Root Cause】**：原版按钮内部留白（padding）过于宽大（左右各 16px，文本 margin 又占用若干像素），有效净宽仅 124px，导致在未深度挖掘内部空间前就草率修改了外部框体尺寸。
- **【解法 Remedy】**：
  1. 严格 100% 恢复 `UiLayoutAndFontHelper.KeybindButtonWidth = 160f` 原版原生尺寸，绝不动外部框体一分一毫；
  2. 将按钮自身 `HorizontalLayoutGroup.padding` 从左右各 16px 收窄为左右各 `4px`（上下各 `2px`）；
  3. 将文本 `margin` 设为 `(2, 0, 2, 0)`，在外部尺寸零改变的前提下，为内部文字释放出 **24px+** 的净可用宽度（达 148px）；
  4. 配合 `ButtonFitSpec` 与 `FittedFontSizeCache` 单次微缩自适应（`safeWidth = AvailWidth * 0.94f`，字号钳制在 `[8.5f, 15f]`），确保长组合键完整清晰显示，绝不触发截断省略号。
- **【严禁反模式 Anti-pattern】**：严禁在未穷尽内部留白压缩与微缩排版前擅自放大原生标准 UI 控件的外部物理包围盒。

### 32. 原版键重绑保存时未清除 ExplicitUnboundHotkeys 导致按键退回「无」与局内失效
- **【现象 Symptom】**：玩家曾经将某个原版按键（如翻转或视角复位键）设为空（`无`），之后在选项界面重新绑回某个单键或组合键（如 `Ctrl + Space`）并点击应用，界面或局内该键立刻又变回 `无`，按键完全不生效。
- **【根因 Root Cause】**：
  1. 历史解绑行为将键名（如 `vanilla.key_flip_item_alt` / `key_flip_item_alt` / `flip_item_alt`）写入了注册表 `ExplicitUnboundHotkeys` 集合中；
  2. 原版按键保存时不走 `ModOptionScanner` 的 `SetKey` 包装，而是走原版 `OriginalApplySettingDelegate`；
  3. `UnifiedOptionWindow.ApplyAllSettings` 在写入 `pendingKeys` 时，遗漏了同步调用 `ComboKeyStore.SetExplicitlyUnbound(..., false)`；
  4. 导致重新绑定非空物理键后，`IsExplicitlyUnbound` 依然命中返回 `true`，运行时和配置读取立即将有效键阻断为 `KeyCode.None`。
- **【解法 Remedy】**：
  1. 在 `UnifiedOptionWindow.ApplyAllSettings` 原版按键保存分支与模组按键保存分支中，显式同步执行：
     ```csharp
     bool isUnbind = hk.PendingKey == KeyCode.None;
     ComboKeyStore.SetExplicitlyUnbound(hk.Id, isUnbind);
     ComboKeyStore.SetExplicitlyUnbound(hk.RawOptionId, isUnbind);
     if (hk.RawOptionId.StartsWith("key_", StringComparison.OrdinalIgnoreCase))
     {
         ComboKeyStore.SetExplicitlyUnbound(hk.RawOptionId.Substring(4), isUnbind);
     }
     ```
  2. 在底层 `ComboKeyStore.SetSavedCombo` 中增加防脏守卫：当 `primaryKey != KeyCode.None` 时，自动调用 `SetExplicitlyUnbound(entryId, false)`，从持久化根源切断非空键与解绑黑名单并存的矛盾。
- **【严禁反模式 Anti-pattern】**：严禁在更新键位配置时仅更新键值而遗漏同步清除显式解绑黑名单 `ExplicitUnboundHotkeys`。

### 33. 离线构建期 `ComboKeyStore.IsBypassed == false` 穿透过滤导致局内重开选项页组合键误显为「无」
- **【现象 Symptom】**：玩家在局内将「翻转物品 (`flip_item`)」绑定为 `Ctrl + Space` 并点击应用。关闭选项页后，按 `Ctrl + Space` 能正常翻转，单按 `Space` 不误触；但若再次按 Esc 打开统一选项中心，`flip_item` 按钮上的文本却错误显示为「无」（灰色或红色空按键状态），但在后台其实按键并未丢失。
- **【根因 Root Cause】**：
  1. `UnifiedOptionWindow.OpenEnhancedUI(manager)` 在开头执行 `ResetSessionForOpen(manager)`，将 `CurrentWindow` 置为 `null`；
  2. 随后将 `windowRoot.SetActive(false)` 进行离线批量装配，直至最底部 `builder.Show()` 执行前，`CurrentWindow.IsVisible` 恒为 `false`（`IsEnhancedWindowOpen` 恒为 `false`）；且直至 `WindowField.SetValue(manager, ...)` 执行前，`GameOptionUIManager.Instance.IsOpen` 亦为 `false`；
  3. 旧版仅在局部几个操作套了 `EnterBypass()`，导致中间执行的原版卡片装配（`CollapsibleCardView.BuildVanillaSectionCard` -> `rawGetKey()` -> `KeyBindHelper.Get`）、`StyleVanillaKeyButtons()`、`SyncVanillaPendingFromManager()` 与 `ConflictAggregationView.RefreshConflicts()` 时，`ComboKeyStore.IsBypassed` 恒为 `false`；
  4. 此时玩家并没有物理按住 `Ctrl` 等修饰键，`Patch_KeyBindHelper_Get` Postfix 误将 UI 查询当成局内运行时按键消费，直接把返回值过滤成了 `KeyCode.None`，导致 UI 表现层将其格式化为「无」！
- **【解法 Remedy】**：实施分层铁壁防御：
  1. **模型读取层永久强制旁路 (`ModOptionScanner.cs`)**：`ModHotkeyEntry.GetKey` 与 `SetKey` 是配置定义与模型持久化入口，语义永远代表“该项配置保存的真实物理键是什么”，在 `GetVanillaKeybinds`、`CreateHotkeyFromConfigEntry`、`ScanWorkshopGameOptionSections` 及快照抓取中，全链路委托内部强制包裹 `using (ComboKeyStore.EnterBypass())`；
  2. **UI 全生命周期无死角旁路保障 (`UiSessionState.cs` + `UnifiedOptionWindow.cs`)**：在 `UiSessionState` 新增 `internal static bool IsBuildingOrOpeningUI`；在 `OpenEnhancedUI` 最外层使用 `try/finally` 与 `using (ComboKeyStore.EnterBypass())` 全量包裹；将 `ComboKeyStore.ExternalBypassPredicate` 统一更新为 `() => IsEnhancedWindowOpen || IsCapturingAnyKey || IsBuildingOrOpeningUI || IsApplyingSettings`；并在 `SyncVanillaKeyButtonColors`、`BuildVanillaSectionCard` 等操作内部均加上 `using (ComboKeyStore.EnterBypass())` 兜底。
- **【严禁反模式 Anti-pattern】**：严禁假定 `IsEnhancedWindowOpen` 能覆盖 UI 离线装配与构建阶段；严禁在配置定义读取委托中裸调 `rawGetKey()` 而不开启 `EnterBypass()`。

### 34. 改造件封面强制 `SetVisible(true)` 导致未配封面模组串图与网络超时卡死
- **【现象 Symptom】**：
  1. 在游戏「改造件 (Mods)」列表中切换模组时，没有封面的模组界面残留了上一个模组的封面（严重“串图”）；
  2. 点击工坊模组时发生长达数秒的界面冻结与卡顿，控制台日志频繁报错 `[ModUI] Could not download the Workshop preview ... Data Processing Error`。
- **【根因 Root Cause】**：
  1. 原版 `ModUIManager.GetPreview` 优先调用 `ModWorkshop.TryGetPreviewUrl` 尝试从 Steam CDN 异步网络下载预览大图，并立即返回 `null`。在国内网络环境下，CDN 下载极易超时或失败，失败后才回退到本地磁盘解码 `GetLocalPreview`（读取模组目录下的 `preview.png`），导致玩家每点击一个订阅模组都经历漫长阻塞与报错；且原版 `ModUIManager` 的 `previewCache` 字典跨场景销毁未持久化复用；
  2. 旧补丁在 `Patch_ModUIManager_ApplyPreview.Prefix` 与 `Patch_ModUIManager_ShowMod.Postfix` 中，切换至非本模组时无脑执行了 `previewPanel.SetVisible(true)`，强行覆盖了原版根据图片是否为空控制显隐的安全逻辑；此时 `Image.sprite` 依然残留着上一个模组的封面，直接导致无封面模组亮出上一模组的封面（串图）！
- **【解法 Remedy】**：
  1. 挂钩 `Patch_ModUIManager_GetPreview` 优先秒查跨场景静态字典 `PersistentPreviewCache`，未命中时直接通过委托调用游戏原生的 `GetLocalPreview(mod)` 读取本地已下载好的 `preview.png`（耗时仅 1~2ms），写入持久缓存并直接返回，彻底跳过容易超时的网络下载链路；
  2. 在 `ApplyPreview.Prefix` 切换模组时显式执行 `previewElem.SetSprite((Sprite?)null)` 清空残影；
  3. 彻底删除 `Prefix` 与 `ShowMod.Postfix` 中对其他模组强设 `previewPanel.SetVisible(true)` 的代码，完全交由原版逻辑根据图片是否有效自主控制显隐；
  4. 本模组专属去封面优化依然严密保留（`previewPanel.SetVisible(false)`，且按钮保持 42px 零拉伸）；
  5. `ModUIPrewarmRunner` 在主菜单闲置帧按每帧 2 个模组轻量分步预热本地未缓存封面。
- **【严禁反模式 Anti-pattern】**：严禁在 UI 切换控件状态时强行覆盖原版有效的空值防御显隐逻辑；严禁在主线程发起容易超时的网络下载或重复反序列化。

### 35. 原版退回主菜单 `MainMenuWithAnalytics` 强制 1.0 秒协程空等阻塞与切场卡顿
- **【现象 Symptom】**：玩家在局内点击“退回主菜单”按钮时，游戏明显定格并空等超过 1 秒钟才开始加载场景。
- **【根因 Root Cause】**：
  1. 反编译 `EscapeUIManager.MainMenuWithAnalytics` 揭示核心真相：原版代码在调用 `EmporiumAnalytics.TrackLeaveToMainMenu()` 后，若 `EmporiumOption.Instance.consentAnalytics` 为 `true`（绝大多数玩家的默认状态），无条件执行 `yield return new WaitForSecondsRealtime(1.0f);` 强制死等整整 1 秒钟；
  2. 且切回主菜单首帧若在 `Awake` / `Start` 中同步执行重型面板与对象池预热，极易造成场景切换瞬间的画面顿挫。
- **【解法 Remedy】**：
  1. 新增 `Patch_EscapeUIManager_OnMainMenu.Prefix`，在 `EnableFastQuit || EnablePerformancePatches` 开启时接管回调。安全上报遥测后，**100% 完整执行原版退出主菜单的全部状态重置逻辑**（`NewGameData.Instance.isLoadFromMainMenu = true; PlayerStore.Instance.Reset(); EmporiumEntry.ResetData(); PlayerStore.ResetData(); StoreStation.ResetData();`），零秒等待直接调起 `SceneManager.LoadScene("EmporiumMenu")`，物理消除 1,000ms 强制空等；
  2. 将此前在 `ModUIManager.Awake` 与 `CustomUIManager.Start` 中同步执行的预热彻底解耦，统一交由 `ModUIPrewarmRunner` 延后 5 帧在主菜单稳定渲染后的空闲帧分步执行，消除切场进主菜单时的瞬间定格。
- **【严禁反模式 Anti-pattern】**：严禁在优化切场与退回主菜单时跳过游戏自身的单例状态重置逻辑，严禁触碰或跳过任何存档保存逻辑。

### 36. 第三方模组延迟本地化委托主菜单空引用击穿扫描导致选项界面回退原版及四级故障隔离
- **【现象 Symptom】**：玩家订阅并启用创意工坊模组「物品堆叠增强（Item Stacking）」（ID: `3830647974`）等包含动态委托配置项的模组后，在游戏主菜单按 Esc 键或点击设置，选项界面回退到原版简陋外观，统一按键优化中心的折叠卡片与搜索栏完全失效。
- **【根因 Root Cause】**：
  1. 部分第三方模组（如 `item_stacking`）在 `ModConfig.Define` 中将配置项的 `Label` 与 `Tooltip` 绑定为动态本地化委托，底层依赖局内存档加载事件（`ModHook.OnGameLoadedLate`）置位本地化字典就绪标志；
  2. 玩家在游戏主菜单打开选项界面时，游戏尚未载入局内存档，第三方本地化字典尚未初始化，其内部字典检索逻辑在 `table == null` 时调用 `TryGetValue` 抛出 `System.NullReferenceException`；
  3. 旧版 `ModOptionScanner.ScanModConfigIntoGroup` 在调用 `entry.Label?.Invoke()` 与 `entry.Tooltip?.Invoke()` 时未设置局部异常捕获，空引用异常向上穿透至最外层 `Patch_GameOptionUIManager_OpenUI.Prefix` 的 catch 块；
  4. 该 catch 块记录日志后直接返回 `true` 放行原版 `GameOptionUIManager.OpenUI` 原生方法体，导致统一选项界面整体崩溃并降级回退到原版。
- **【解法 Remedy】**：建立四级故障隔离与自愈防御网：
  1. **第一级：单条目动态委托沙盒求值**：在 `ModOptionScanner` 中对所有第三方 `entry.Label`、`entry.Tooltip`、`entry.BoxedValue`、`entry.Choices` 实行受控读取，捕获异常后将 `ModCatalogCache.HasDegradedEntries` 标记为 `true`；若 `label` 或 `tooltip` 异常或为空，自动调用 `SectionLocalizer.TryResolveConfigLabelFromModLocalization` 与 `TryResolveConfigTooltipFromModLocalization` 从模组已解压的 `LoadedMod.Localization` 中提取中英双语规范译文，仍无则安全回退为 `entryKey`；
  2. **第二级：单模组/单卡片级构建隔离**：在 `ScanAll` 遍历各模组、`UnifiedOptionWindow` 遍历 `ModGroups` 构建卡片外壳、以及 `CollapsibleCardView` 恢复展开卡片内容时，施加独立的单模组/单卡片级异常隔离与 `CustomUIBuilder` 容器栈平衡保护（`try-finally { builder.End(); }`），单个第三方模组损坏绝不中断其他模组与原版选项的渲染；
  3. **第三级：会话状态读取安全防护**：在 `UiSessionState` 的 `CaptureOpenBaselineSnapshot`、`HasAnyPendingGeneralChanged` 与 `HasAnyHotkeyChanged` 中对条目取值委托全量包裹空值与异常保护，确保第三方异常 Getter 不向外抛出未捕获异常，且不误触发假脏保存确认弹窗；
  4. **第四级：最高优先级守护与运行时热自愈**：为 `Patch_GameOptionUIManager_OpenUI` 显式标注 `[HarmonyPriority(Priority.First)]` 确保接管权；在 `ModEntry.Init` 中确保 `OptionUiHost.Active` 初始化；并通过 `Patch_ModHook_FireOnGameLoadedLate` 在游戏载入存档进入运行时后自动对降级条目执行 `ModCatalogCache.Invalidate()` 热刷新，实现动态字典延迟就绪后的自动无感修复。
- **【严禁反模式 Anti-pattern】**：严禁在扫描与渲染外部模组配置时裸调任何第三方委托；严禁在最外层捕获到单个模组异常时粗暴放弃接管并直接放行原版界面；严禁在出现第三方异常时产生未修改假脏状态弹窗。

### 37. 卡片拖拽自由排序与状态机未保存防假脏闭环
- **【现象 Symptom】**：玩家在选项页通过拖拽卡片调整顺序后，若中途改回原顺序，仍会弹出“存在未保存修改”提示框；或者在单模组卡片内点击“恢复默认”时，导致玩家精心排列的卡片顺序被意外重置。
- **【根因 Root Cause】**：
  1. 拖拽卡片后如果仅仅通过布尔标志标记页面已修改，当卡片被重新拖回原位置时，无法识别当前顺序已与开窗初始顺序完全一致；
  2. 模组卡片内的“恢复默认”如果遍历重置了 `ModFeatureConfig.CardSortMode`，会导致卡片排序偏好被当作游戏快捷键设置一起被冲掉。
- **【解法 Remedy】**：
  1. 在 `UiSessionState` 中引入基准视觉顺序（`BaselineVisualModIdOrder`）与基准排序模式（`BaselineCardSortMode`）；
  2. 每次拖拽完成后调用 `CheckCardOrderDirtyState()`，若当前视觉顺序与基准顺序完全相同，自动将当前排序模式还原为基准模式，并撤销未保存标记；
  3. 在本模组卡片的一键恢复默认逻辑中，显式暂存并在重置后还原 `CardSortMode`，排序模式不随常规配置项重置。
- **【严禁反模式 Anti-pattern】**：严禁在拖动操作中脱离基准快照进行粗暴布尔判脏；严禁在重置模组业务设置时误伤玩家的界面排布偏好。

### 38. UGUI 拖动插入指示线零重算与视口平滑滚动
- **【现象 Symptom】**：拖动卡片时界面明显掉帧，拖拽目标在长模组列表两端时无法向外扩展，滚轮滚动卡顿。
- **【根因 Root Cause】**：
  1. 在拖拽过程中若通过往卡片容器内动态增删临时占位 GameObject 来展示插入位置，会导致父级 `VerticalLayoutGroup` 和 `ContentSizeFitter` 在每一帧触发强制排版计算（`LayoutRebuilder`）；
  2. 拖拽事件劫持了鼠标指针输入，若未实现 `IScrollHandler`，滚轮事件无法透传给外层 `ScrollRect`。
- **【解法 Remedy】**：
  1. 在 `CardDragReorderController` 中使用单一轻量指示线（Image），仅根据目标卡片的 `RectTransform` 局部高度动态更新其 Y 轴位置，不改变父容器内部的同级节点结构，单帧开销极低；
  2. 在指示线上实现 `IScrollHandler` 将滚轮增量直接转发给外层滚动视图，并在视口上下各设置 60px 缓冲区，接近边缘时平滑自动滚屏；
  3. 设置 8px 拖拽阈值，避免玩家单击标题折叠时误触发拖动。
- **【严禁反模式 Anti-pattern】**：严禁在拖拽高频帧中增删节点触发 UGUI 整体排版回流；严禁拦截滚动输入而不向下传递。

### 39. 零权限 `mod_enabled_order` 替代文件系统时间戳与 .NET 8 无头测试 Unity ECall 隔离
- **【现象 Symptom】**：
  1. 为了获取模组启用顺序而读取模组文件的修改时间，导致需要声明 `files` 权限，触发官方 2026-12-10 零权限安全拦截；
  2. 在脱机单元测试中调用放弃修改逻辑时，抛出 `SecurityException: ECall methods must be packaged into a system module` 异常。
- **【根因 Root Cause】**：
  1. 官方加载器自 2026-12-10 起对未获白名单认证且声明了文件权限的模组直接阻断加载；
  2. 在非 Unity 托管的 .NET 8 单元测试中，直接调用 `Transform.SetSiblingIndex` 会触发 Mono/Unity 内部调用（InternalCall），CLR 运行时检测到外部程序集直接中断报错。
- **【解法 Remedy】**：
  1. 在 `ComboKeyStore` 中通过 PlayerPrefs 保存 `probably_unified_hotkeys.mod_enabled_order.v1` 字符串列表，每次扫描时将新启用的模组追加至末尾，全程零外部文件操作，达成 0 权限纯净运行；
  2. 在 `UiSessionState.RestoreCardVisualOrder` 中，将调用 `transform.SetSiblingIndex` 的逻辑抽离到带有 `[MethodImpl(MethodImplOptions.NoInlining)]` 的独立私有方法中，并在进入前通过 `PrefsStorage.UseInMemoryPrefsForTests` 前置短路，保证脱机单测模式绝不触碰 Unity 内部调用。
- **【严禁反模式 Anti-pattern】**：严禁为了轻量功能声明文件或网络权限打破零权限标准；严禁在脱机测试可执行路径中直接调用未隔离的 Unity InternalCall。

### 40. `ModFeatureConfig` 多次注册重复累加 `OnChanged` 委托导致配置保存卡顿
- **【现象 Symptom】**：点击设置窗口底部的「应用」或修改配置项时，界面明显停顿数秒，日志中密集刷新扫描记录。
- **【根因 Root Cause】**：
  1. 在 `ModFeatureConfig.RegisterBoolEntry` 与 `RegisterStringChoiceEntry` 中，当 `config.Get(key)` 命中已有条目时，旧代码直接执行 `existing.OnChanged += _ => onChanged(existing);`；
  2. 随着进入选项页、重置配置或打开齿轮窗口，`EnsureRegistered()` 被多次调用，导致同一个条目上叠加注册了数十个相同的变更回调，一旦触发保存，所有委托成倍执行引发重扫风暴。
- **【解法 Remedy】**：
  1. 在 `ModFeatureConfig` 内部引入静态防重集合 `private static readonly HashSet<object> BoundEntries = new HashSet<object>();`；
  2. 无论是新创建的条目还是已有条目，仅在 `BoundEntries.Add(entry)` 返回 `true` 时才绑定 `OnChanged` 监听，保证无论调用多少次 `EnsureRegistered()`，每个配置项的委托仅绑定一次。
- **【严禁反模式 Anti-pattern】**：严禁在具有多次调用预期的初始化逻辑中向事件或委托重复挂载监听而不做幂等去重。

### 41. Unity `OnPointerUp` 偶发先于 `OnEndDrag` 触发导致拖拽手柄卡在半透明灰色
- **【现象 Symptom】**：拖动卡片调整顺序后松开鼠标，被拖动的卡片有时依然保持半透明淡灰色（透明度停留在 0.55f），必须再次点击才能恢复正常。
- **【根因 Root Cause】**：
  1. 在 Unity UGUI 的事件流中，拖拽结束时 `OnPointerUp` 有可能早于 `OnEndDrag` 分发，或在拖拽超出视口释放时丢失 `OnEndDrag` 回调；
  2. 若卡片恢复不透明度（`alpha = 1f`）仅写在 `OnEndDrag` 中，事件错序或丢失就会导致卡片透明度未被还原。
- **【解法 Remedy】**：
  1. 在 `CardDragReorderController` 中同时实现 `IPointerUpHandler`，在松开鼠标时与 `OnEndDrag` 共同执行清理重置；
  2. 在组件的 `OnDisable` 中增加兜底复位，确保组件失活或卡片被回收时透明度必定恢复至 1f。
- **【严禁反模式 Anti-pattern】**：严禁将 UI 视觉状态的清理逻辑仅寄托于单一且可能受输入环境影响而丢失的事件回调。

### 42. UGUI 拖拽指示线未配置 `ignoreLayout = true` 触发布局自激震荡
- **【现象 Symptom】**：拖动卡片时指示线剧烈上下跳动，鼠标悬停位置不断颤动。
- **【根因 Root Cause】**：
  1. 指示线游戏对象挂在包含 `VerticalLayoutGroup` 的卡片列表容器下，但没有在自身的 `LayoutElement` 上设置 `ignoreLayout = true`；
  2. 每次指示线显示并改变位置时，父级布局组件把指示线当作列表项重新计算排版，导致卡片位置移动，卡片移动又反过来改变了计算出的指示线坐标，形成高频自激循环。
- **【解法 Remedy】**：
  1. 指示线生成时在其 `LayoutElement` 组件上显式设置 `ignoreLayout = true`，使父级布局完全忽略指示线对象；
  2. 仅通过修改指示线的 `RectTransform.anchoredPosition` 进行局部悬浮定位，彻底切断与父级排版计算的循环依赖。
- **【严禁反模式 Anti-pattern】**：严禁在自动布局容器内放置未忽略布局的临时辅助高亮或测量节点。

### 43. 模组卡片根节点缺少 `childForceExpandWidth = true` 导致右侧控件参差不齐
- **【现象 Symptom】**：在不同模组卡片中，右侧的快捷键按钮、滑动条、输入框和复选框没有右对齐，左右宽度各不相同。
- **【根因 Root Cause】**：
  1. 卡片每行设置的父级 HorizontalLayoutGroup 若未统一开启 `childForceExpandWidth = true`，或者内部的标签与控件未合理配置 `FlexibleSpace`；
  2. 左侧标签文字长度不一时，右侧的固定宽度控件会被左侧内容不同程度地向右推移，无法整齐靠右对齐。
- **【解法 Remedy】**：
  1. 在卡片根节点与行构建中明确父容器与每行的水平布局规则，在左侧标题与右侧控件之间统一插入弹性间隔（`AddFlexibleSpace`）；
  2. 右侧各类控件（按键 160f、滑块 224f、输入框与下拉框 245f）统一设置固定宽度，保证所有卡片右侧控件在垂直方向严格对齐。
- **【严禁反模式 Anti-pattern】**：严禁在没有弹性间隔支撑的情况下混用不同长度的文本标签与右侧交互组件。

### 44. `CustomUIBuilder.AddSpace` 不更新 `builder.Last` 导致追加按钮误移原版控件
- **【现象 Symptom】**：在原版选项页底部行注入左下角「模组设置」按钮后，原版居中的 180f「应用」按钮被向右挤压甚至被移至行尾。
- **【根因 Root Cause】**：
  1. 调用 `builder.AddButton` 注入按钮后，若调用 `builder.AddSpace` 添加间隔，`CustomUIBuilder.AddSpace` 内部并不更新 `builder.Last` 属性；
  2. 随后若对 `builder.Last` 调用 `SetAsLastSibling()`，实际上操作的是先前的按钮而非间隔，破坏了整行的兄弟节点排列顺序，使居中对齐彻底失效。
- **【解法 Remedy】**：
  1. 在按钮的 `LayoutElement` 组件上显式配置 `ignoreLayout = true`，使横向布局组完全忽略该按钮的占位；
  2. 将其 RectTransform 锚点设置为绝对左中锚定（`anchorMin/Max = (0, 0.5f)`，偏移 `(10f, 0f)`），在完全脱离布局计算的前提下悬浮定位在左下角，对原版 180f 居中应用按钮实现零物理干扰与零像素偏移。
- **【严禁反模式 Anti-pattern】**：严禁在未核查 `CustomUIBuilder` 内部状态更新的前提下连续调用 `builder.Last` 进行层级操作；严禁在原版布局行内插入未忽略布局的自定义元素打破居中平衡。

### 45. `CaptureOpenBaselineSnapshot` 与保存期 `Invalidate` 误清空活动窗口引用导致卡片失灵
- **【现象 Symptom】**：在选项页点击「应用」保存后，模组卡片无法再次点击展开，折叠按钮无响应；或者在选项页关闭某顶栏开关后，对应按钮未能即时隐藏。
- **【根因 Root Cause】**：
  1. 保存配置后触发 `ModCatalogCache.Invalidate()`，其订阅的 `ClearModelUiBindings()` 在窗口依然处于活动状态时误清空了 `CardParts` 与 `HotkeyUiMap`，使卡片视图丢失了对应的 GameObject 句柄；
  2. `CaptureOpenBaselineSnapshot()` 内部错误将顶栏控件句柄（如 `TopHintLabelEl`）重置为 null，后续动态更新时判定为 null 而跳过执行。
- **【解法 Remedy】**：
  1. 在 `UiSessionState.ClearModelUiBindings()` 中加入前置守卫：当 `(CurrentWindow != null && CurrentWindow.IsAlive) || IsApplyingSettings || IsBuildingOrOpeningUI` 时直接返回，保护活动窗口的 UI 句柄不受破坏；
  2. 从 `CaptureOpenBaselineSnapshot()` 中移除所有顶栏与窗口对象的置空代码，快照逻辑仅负责比对数值与键位基准，绝不污染活动句柄。
- **【严禁反模式 Anti-pattern】**：严禁在 UI 会话存活期间销毁表现层持有的活动节点句柄；严禁将数据快照逻辑与 UI 生命周期清理逻辑耦合。

### 46. 独立模式下第三方模组通过默认优先级补丁及 `KeyBindHelper.Define` 混入原版界面
- **【现象 Symptom】**：开启「模组独立界面」模式后，打开原版选项页，某些第三方模组（如水质扫描仪）的快捷键依然混杂在原版快捷键区域中。
- **【根因 Root Cause】**：
  1. 旧拦截补丁以 `Priority.First` 执行后放行原版，第三方模组以默认 `Priority.Normal` 挂载的 Prefix 依然会在我们之后向 `GameOptionRegistry` 追加条目，或通过 Postfix 在原版窗口生成控件；
  2. 部分模组通过 `KeyBindHelper.Define` 注册按键，当原版遍历该列表时，生成的委托声明类属于原版内部闭包，绕过了常规外部程序集检查。
- **【解法 Remedy】**：
  1. 建立 `VanillaKeybindWhitelist` 硬核白名单（包含 `rotate_cw`、`flip_item` 等 17 个固定原版常量键名及 `toolbox_slot_` 前缀），非白名单按键一律判定为非原版项；
  2. 采用三明治补丁架构：在所有第三方 Prefix 执行完毕后（`[Priority.Last] Prefix`）备份并过滤 `GameOptionRegistry`；在原版构建完毕后（`[Priority.First] Postfix`）记录纯净原版节点；在所有第三方 Postfix 执行完毕后（`[Priority.Last] Postfix`）将所有非原版外来节点隐藏，并由 Finalizer 确保备份 100% 还原。
- **【严禁反模式 Anti-pattern】**：严禁仅依赖 `entry.source` 字符串判定原版归属性；严禁在多补丁竞争环境下使用单层前置拦截。

### 47. Harmony 补丁方法未按标准命名且未显式指定 `[HarmonyPostfix]` 导致补丁静默失效
- **【现象 Symptom】**：在独立模式下打开原版设置窗口，左下角的「模组设置」按钮始终没有注入显示，但编译与启动日志中均无报错。
- **【根因 Root Cause】**：
  1. 在定义补丁类时将后置方法命名为 `PostfixLast`，且仅标注了 `[HarmonyPriority(Priority.Last)]`，漏掉了 `[HarmonyPostfix]` 特性；
  2. Harmony 默认根据方法名是否精确等于 `Prefix`、`Postfix`、`Finalizer` 等关键词进行自动识别，非标准命名的公开静态方法若未显式标注特性，会被 Harmony 静默跳过而不执行挂载。
- **【解法 Remedy】**：
  1. 拆分为独立标准 Patch 类，方法名严格使用标准 `Postfix`，并显式标注 `[HarmonyPostfix]` 与优先级特性；
  2. 编写 `Harmony_AllPatchMethods_FollowStandardNamingOrAttributes_Verification` 全仓反射扫描单元测试，对程序集内所有 Patch 类的静态方法进行命名与特性审查，防止未来再次发生静默漏挂。
- **【严禁反模式 Anti-pattern】**：严禁在未显式标注 Harmony 特性的情况下使用自定义方法名作为钩子入口。

### 48. 工坊模组未打包本地 `preview.png` 导致封面永久缺失与异步缓存收编
- **【现象 Symptom】**：开启本地封面优化后，部分未在工坊根目录提供 `preview.png` 的模组（如仅依赖 Steam 详情页的 jpg 封面）在改造件页面长期显示空白或默认占位图。
- **【根因 Root Cause】**：
  1. 部分工坊作者上传时未随 DLL 打包 `preview.png`，游戏原版 `GetLocalPreview` 返回 null；
  2. 优化逻辑若将 null 占位直接当作已缓存处理，会阻断原版向 Steam 发起异步拉取，导致该模组封面永久无法展示。
- **【解法 Remedy】**：
  1. 空闲预热阶段调用 `ModWorkshop.RequestDetails` 提前向 Steam 批量请求未配本地封面模组的详情信息；
  2. 快速读取封面时，若原版缓存中存在 null 占位则主动移除，放行原版异步拉取；
  3. 在原版异步完成设置 Sprite 时，同步收编至跨会话持久缓存，使后续浏览与页面切换即时显示。
- **【严禁反模式 Anti-pattern】**：严禁将临时空值占位作为持久缓存命中；严禁对异步网络加载链路采取粗暴拦截。

### 49. 原版设置界面修改快捷键点击「应用」后退出仍误弹未保存确认窗之两端闭环
- **【现象 Symptom】**：在「模组独立界面」的原版设置页中，修改按键并点击「应用」保存后，尝试退出时仍会错误弹出「未保存的更改」确认窗。
- **【根因 Root Cause】**：
  1. 此前在原版 `apply` 按钮上挂载的保存监听是在原版 `OnApplySetting()` 执行之后触发；导致原版保存执行时未处于 `ComboKeyStore.EnterBypass()` 状态，部分组合键被过滤为 None，且提前清空了 pendingKeys 导致快照对比错位；
  2. 原版 `OnApplySetting` 执行后未彻底复位 `AllHotkeys` 中原版条目的 `HasPendingChange = false` 及 `PendingKey/Modifiers`，导致会话状态机依然判定存在未保存待定修改。
- **【解法 Remedy】**：
  1. 挂钩 `GameOptionUIManager.OnApplySetting`：在 Prefix 中开启 `EnterBypass()` 并调用 `OnBeforeVanillaStandaloneApply` 提前将待定键与组合键修饰位写入 `pendingKeys` 与 `ComboKeyStore`；
  2. 在 Postfix 中调用 `OnAfterVanillaStandaloneApply` 彻底清空 `pendingKeys`、复位 `HasPendingChange = false` 并重拍基准快照，由 Finalizer 确保安全释放 bypass；
  3. 增加单测覆盖单键、组合键、空键解绑、空键重绑及放弃修改 5 大场景，锁死防假脏状态机。
- **【严禁反模式 Anti-pattern】**：严禁在原版保存流程之外滞后执行配置落盘；严禁在完成保存后遗留脏标记或快照不同步。
