# 可能是按键统一优化 / Probably Unified Hotkeys

> **作者**：太湖白1231 ｜ **当前版本**：`v1.4.4` ｜ **适用游戏**：*深空当铺 (Probably Stolen)* `0.46` / `0.46D` ｜ **语言**：简体中文 / English ｜ **安全扫描**：官方 `ModScanner` 零高危告警（仅声明 `<Capability>files</Capability>`，0 HardFlags / 0 Deprecated / `Refused=False`）  
> **Steam 创意工坊**：[物品 ID `3837828444`](https://steamcommunity.com/sharedfiles/filedetails/?id=3837828444) ｜ **QQ 交流群**：`1127147529`

![可能是按键统一优化 v1.4.4 界面预览](preview.png)

> 面向 **深空当铺 (*Probably Stolen*)** 的一站式原生统一按键与模组配置中心 —— 支持 `Shift / Ctrl / Alt` 组合键与鼠标侧键（`Mouse3 ~ Mouse6`）、原版与模组双区折叠及「恢复默认」、全范围繁简中文 / 英文 / 拼音搜索、智能防误触隔离、静态 `DseCecil` IL 与本地配置只读热键探测（自动覆盖未公开官方配置的硬编码模组并参与冲突检测）、交互式「模组排查」与疑难热键强转/忽略、中英双语实时切换、后台静默预建 + 无过渡单帧原子切换，严格遵循官方 `<Capability>files</Capability>` 合规权限通过 `ModScanner` 安全扫描。

---

## 🌟 核心特性概览

### 1. 深度原生融合与全局防重复隔离
- **100% 嵌入官方选项页**：直接接管游戏原生 `ESC -> 选项` 窗口，完全沿用官方 `160px` 宽/原生高度的按键按钮与交互手感，无需记忆额外弹窗快捷键，也不占用额外悬浮窗位置。
- **上方纯原版、下方模组中心**：游戏、音频、画面、对话与原版快捷键以统一折叠卡片保留在上方 **「游戏原生设置与快捷键」**；所有第三方模组统一收纳至下方 **「创意工坊模组设置与快捷键」** 区域，并按中文拼音首字母（A-Z）自动排序展示。
- **来源委托级重复拦截**：不仅检查配置段与条目的 `source` 标识，还会追溯其底层读写回调委托（`Delegate`）所属的程序集来源；即便第三方模组直接向原版分区注入设置项，也会自动从上方原版区剔除并收编到下方专属卡片，彻底避免上下重复。

### 2. 统一折叠卡片、原版/模组「恢复默认」与原生视觉一致性
- **原版与模组统一折叠体系**：无论是上方 **原版官方分区** 还是下方 **创意工坊模组卡片**，头部均配备统一的 `36×36` 方形折叠按钮（零内外边距压缩、居中 `▶` / `▼` 图标）以及条目计数徽章 `(按键: X | 常规: Y)`；顶部 **`全部展开` / `全部收起`** 可单帧无过渡动画一键同步切换全页（含原版与模组）。
- **原生按键按钮质感对齐**：卡片内部、`[!] 仅看冲突` 视图与上方原版设置共用统一 `StyleHotkeyButton` 规范（对齐官方原生高度、原生字号及左右 `160px` 宽度），既保留原版精致外观，又能在录入超长组合键（如 `Ctrl+Shift+Alt+鼠标侧键1`）时自适应微缩防裁切。
- **垂直双区分段**：模组卡片展开后内部采用「上半截快捷键 + 下半截常规设置」垂直分隔结构（开关、滑动条、下拉框），原版区块同样保持 `24px / 16px` 对称内缩进对齐。
- **全卡片统一按需「恢复默认」**：原版官方分区（`游戏` / `音频` / `显示` / `对话` / `按键绑定`）与创意工坊模组卡片均在展开后于卡片右上角提供统一的 **`恢复默认`** 按钮（`112×36`，折叠时自动隐藏）；原版区块按游戏官方出厂默认值还原（含 `display` 非 16:9 显示器 `fullscreen -> resolution` 规则重算、`dialogue_bubble_scale = 10` 及原版按键 `KeyBindHelper.GetDefault` 与组合键修饰重置，点击底部「应用」统一生效），未提供显式默认值的第三方模组则通过**会话启动快照**兜底恢复。

### 3. `Shift / Ctrl / Alt` 全覆盖组合键与鼠标组合
- **原版与模组统一支持**：上方原版快捷键、官方 `ModConfig`（`ConfigEntry<KeyCode>`）、`GameOptionRegistry` 注册项及主流创意工坊模组均可绑定单修饰或多重修饰组合键（如 `Ctrl + C`、`Ctrl + 0`、`Ctrl+Shift+Alt+E`）。
- **原生手感鼠标组合键**：录入期间按住 `Shift / Ctrl / Alt` 再点击鼠标左键、右键、中键或侧键，即可绑定 `Alt + 左键`、`Ctrl + 右键`、`Ctrl + 中键` 等组合操作。
- **单点修饰键保留兼容**：点按单个修饰键并直接松开（期间未按主键或其他修饰键），会自动回落为绑定该修饰键自身（保留如 `LeftAlt` / `LeftShift` 单独使用的习惯）。
- **本地持久化与即时热通知**：组合键修饰位独立持久化保存于玩家配置键 `probably_unified_hotkeys.combos.v1`，点击底部 **「应用」** 时同步更新原版 `PendingKeys`、写入模组设置并触发官方 `ModHook` 配置生效事件，修改无需重启即刻生效。

### 4. 鼠标侧键完整支持 (`Mouse3 ~ Mouse6`)
- **绕过 Unity 漏检限制**：对 `KeyCode.Mouse3 ~ Mouse6` 进行独立轮询，在按键捕获、冲突判断、UI 显示与游戏运行时触发全流程打通。
- **友好中文显示**：卡片按钮、冲突列表与悬浮提示中均统一显示为 **`鼠标侧键1` ~ `鼠标侧键4`**，同样支持单键绑定与 `Shift / Ctrl / Alt + 鼠标侧键1~4` 组合绑定。

### 5. `Esc` 顺手解绑与三态冲突提示体系
- **直观解绑不误关窗**：按键录入状态下按 **`Esc`** 立即解绑为 **`无`**（`KeyCode.None`），并自动吸收当帧窗口关闭信号，不会误关闭整个设置面板；**`Backspace`（退格键）** 与 **`Delete` 键** 均保留为正常可绑定快捷键；不按修饰键直接单击鼠标**左键 / 右键**可取消本次修改并保持原按键。
- **消除空键假冲突**：修复官方逻辑将多个 `KeyCode.None` 互判冲突导致无法保存的漏洞，任意数量功能均可同时保持 `无` 状态。
- **精确组合判重与三色状态**：只有修饰键与主键**完全一致**的非空绑定才判定为冲突（`Ctrl + C` 与单键 `C` 互不干扰）：
  - **常规默认色**：正常已绑定；
  - **柔和灰 (`无`)**：未绑定空键；
  - **淡橙色 (`#ffb347`) + 悬浮 `[!] 冲突提醒`**：存在重叠冲突的按键，鼠标悬停即列出全部冲突模组及功能名。
- **修复原生悬停变蓝 & 冲突聚合视图**：同步更新游戏原生 `ButtonTextColor` 组件的 `normal / highlight / pressed` 状态，冲突按键与顶部 **`[!] 仅看冲突 (N 组)`** 按钮在鼠标扫过后不再褪回蓝色；点击进入冲突聚合视图后，也可直接在冲突清单内就地重绑或按 `Esc` 清除，随时点 **`返回完整设置`** 切回全量页面。

### 6. 智能防误触隔离与长按动作适配
- **组合键与裸键精准隔离**：当某动作设为 `Ctrl + C` 时，单独按 `C` 不会触发它；触发 `Ctrl + C` 的瞬间，同主键的独立裸键 `C` 动作也会被精确抑制防误触，而其余不按多余修饰键的单键功能不受影响。
- **界面打开 / 录入期间自动静默**：停留在增强设置窗口或正在录入快捷键时暂停游戏内全局热键响应，避免设置时误触背景功能。
- **完整覆盖点按与长按动作**：同时接管 `KeyBindHelper.GetKeyDown`（单次触发）与 `KeyBindHelper.GetKey`（持续按住触发），包括原版柜台摇铃呼叫下一位顾客（`call_next_customer`）、按住快移双击转移（`quick_move_inventory / showcase / storage`）、精准拖拽分组快移与 `Pen便利性优化` 的长按修饰功能。

### 7. 全页面四模式繁简中文/英文/拼音搜索与自动定位
- **原版 + 模组全范围四档搜索**：搜索栏左侧可随时切换 **`整体搜索`**（匹配分类/模组名 / ID / 按键 / 常规；若标题命中则整卡展示）、**`按键+常规`**（仅过滤具体设置项，不按标题整卡放行）、**`仅搜按键`** 与 **`仅搜常规`**；全面覆盖上方 **原版官方设定** 与下方 **创意工坊模组卡片**。
- **多维度智能匹配**：
  - 内置 **20,902 字** CJK 全字符集拼音引擎与 **2,473 组**纯代码繁简汉字对照表；
  - 简体输入（如 `现实时钟`、`店铺升级`、`主音量`、`全屏`）可直接命中原版或繁/简体模组设置；
  - 同时支持英文原名、全拼、拼音首字母缩写（如 `sp` 搜 `商品`、`zyl` 搜 `主音量`、`xssz` 搜 `現實時鐘`），按键检索还会额外匹配组合键全称（如 `ctrl`、`侧键`、`无`）与键码词元前缀（如 `keypad6`、`kp6`、`numpad 6`，避免词中子串误匹配）。
- **自动跳转视窗焦点与友好空状态**：输入关键字后自动退出冲突视图、隐藏未匹配区块标题、在挂起根容器布局下单帧同步展开命中卡片并滚动视窗定位至首个匹配项；清空搜索后自动恢复原始折叠态并回到顶部。

### 8. 全链路流畅度重构（选项 / 全部展开 / 改造件 / 退出游戏）与官方安全规范
- **闲置分帧预热与离线装配（「选项」开窗）**：主菜单启动后的空闲帧内单线程静默完成 `pinyin.bin` 词库（~20,902 字）、`ModCatalogCache` 配置目录、`ModUIManager` 界面骨架与 `ConflictScanner` 冲突缓存预热；开启「选项」时先隐藏窗口根节点完成全量原版/模组卡片离线装配与原版规则同步，并在 `Show()` 激活后做轻量按钮排版校准，配合 `ButtonTextColor` / `FittedFontSizeCache` 组件与字号缓存、适配器 `epoch` 反射缓存与零分配委托归属检测，大幅削减首次与重复打开耗时，并抑制第三方模组未展开区段的 `[CustomUI]` 缺失备注告警。
- **单次宽高双重字体测量 + 缓存挂起布局（「全部展开 / 收起」）**：全面移除 50~100+ 颗按键与 `恢复默认` 按钮上的 `TMP_Text.enableAutoSizing` 多次二分测量循环，改为基于 `GetPreferredValues` 宽高双约束结果缓存与 `HorizontalLayoutGroup` 内边距精确控制的单次字号自适应；闲置帧预建以约 `7.5ms/帧` 预算连续完成多张折叠卡片构建；一键展开/收起直接复用已缓存的各级容器/标题行/内容容器 `LayoutGroup` / `ContentSizeFitter` 列表进行单帧无过渡瞬间切换，零每点击 `GetComponents` 扫描。
- **列表对象池化、O(1) 依赖索引与冲突引擎去重（「改造件」面板）**：在 `ModUIManager.PopulateLayoutList` 引入按模组 ID 复用的 `Row` 对象池，避免每次开关面板销毁并重建 40+ 个模组条目（120+ 个子对象）；在 `RefreshRowIssues` / `GetModIssue` 期间建立前置依赖 O(1) 查找字典以消除 `ModLoader.Mods.Find` 带来的 O(N²) 列表拷贝分配；对同时装有 `ConflictScanner` 的环境消除 `OpenPanel` 期间两次重复 `ConflictEngine.Invalidate + EnsureBuilt(true)`（全量 40+ DLL 的 `DseCecil` 同步解析）并以内存模组状态签名跨次开窗缓存。
- **秒级退出流程（「退出游戏」）**：主菜单退出、暂停菜单退出与窗口关闭前统一安全触发各模组 `OnDisable()` 以保存配置，随后清空已卸载模组持有的一次性 `HarmonyInstance` 引用以跳过 `ModLoader.OnApplicationQuit()` 在进程销毁前的 40+ 次同步 `UnpatchAll` 动态方法重编译；同时在游戏禁用遥测（`OVERWRITE_DISABLE_ANALYTICS == true`）时跳过暂停菜单 `QuitWithAnalytics()` 无意义的 1 秒固定空等，实现流畅秒退。
- **全面委托化热点钩子 & `ModScanner` 全绿**：全代码库零 `FindObjectsOfType` / `GameObject.Find`，所有 `AccessTools.TypeByName` 与热点 `MethodInfo` 均在启动/epoch 初始化阶段一次性绑定为静态 `Delegate.CreateDelegate` 强类型委托，消除每帧与热路径反射查找；工坊程序集监听通过 `O(1)` 名称白名单过滤，严格遵守官方 `ModScanner` 静态扫描（仅声明合法的 `<Capability>files</Capability>` 用于静态只读热键探测，保持 `0 HardFlags / 0 Deprecated / UsesDeprecatedReflection: False / Refused: False`）。

### 9. 模组排查、只读热键探测与疑难设置接管（v1.4.3 ~ v1.4.4）
- **官方渠道全量恢复**：在遵循官方 `ModScanner` 安全边界前提下，深入官方 `ModConfig.configs` 注册字典与 `GameOptionRegistry` 条目回调归属，自动找回作者命名不规范（“野 `ModConfig.For(...)` ID”或错配 `source`）导致的漏检设定，并在应用/还原时联动通知全部关联配置 ID 生效。
- **全自动只读热键探测（Route B · `<Capability>files</Capability>`）**：使用游戏内置静态 IL 解析库 `DseCecil` 与官方 `ModData` / 模组配置读取接口，自动探测未公开官方热键配置的代码模组所占用的硬编码按键 / 本地配置文件按键，按模组卡片与文件指纹缓存；在模组卡片与 `[!] 仅看冲突` 视图中以 `[探测·只读]` 紫调标注展示并参与冲突检测，同时严守零外部反射红线。
- **疑难候选管理升级**：在「模组排查」中支持一键 `设为按键绑定`、`忽略(非按键)`（永久免打扰误报并支持 `恢复提醒`）与 `复制排查汇总`。
- **独立「模组排查」子视图**：点击顶部操作栏 **`模组排查 (N)`** 按钮即可按需排查所有已启用模组：
  - 将模组按四类归并为可折叠分类卡片（`[有疑难/待确认项]` 默认展开，其余 `[未检测到官方配置]`、`[已正常纳管]`、`[纯资源/无代码]` 默认折叠并保留会话折叠状态，支持顶部 **`全部展开 / 全部收起`** 一键切换及搜索自动展开），同时将 **`已启用模组排查`** 概览与绿字操作反馈条置顶固定在滚动区上方，上下浏览与点击操作时随时清晰可见；支持顶部搜索框实时过滤、**`强制全部重扫`**、单模组 **`复查此模组`**、**`查看设置`** 与 **`复制信息`** 反馈文本导出（原地无闪刷新且自动保留尚未应用的待定修改）；
  - 针对通过 `ConfigEntry<string>`（非明确单键名）或未设范围的 `ConfigEntry<int>` 编写的非常规按键，支持在 **`查看原始项`** 中一键 **`设为按键绑定`** 强制纳管或 **`还原`**，强转及自动识别的 string/int 热键同样享有完整的 `Shift/Ctrl/Alt` 组合键、防误触拦截、`Mouse3~6` 侧键、`Esc` 清空与 **`前去改键`** 自动定位功能（配置持久化于 `probably_unified_hotkeys.forced_hotkeys.v1`）。

### 10. 中英双语本地化与双入口实时切换（v1.4.3 新增）
- **三档精准语言策略 (`I18n.cs`)**：提供 **`跟随游戏语言`（默认 `"auto"`）**、**`简体中文`（`"zh"`）** 与 **`English`（`"en"`）** 三项切换（刻意不包含“跟随系统”，确保始终与游戏语境一致；非中文游戏环境自动回退为英文）。
- **全界面与双入口同步生效**：语言切换覆盖顶部搜索与作用域下拉、操作行按钮、卡片计数/恢复默认、冲突分组页、模组排查与确认弹窗；配置通过官方 `ModConfig`（`ID: probably_unified_hotkeys`，键：`ui_language`）保存，并同时暴露在 **「改造件」页面右侧本模组齿轮设置** 与 **「ESC → 选项」内本模组独立卡片** 中；借助 `I18n.LanguageChanged` 事件与 `OnChanged` 回调，切换后无需重启游戏即可原地热刷新 UI。
- **双语清单与源码说明**：`manifest.xml` 中的 `<Name>` 与 `<Description>` 同步采用「中文在前、1:1 英文在后」格式，作者统一署名为 **`太湖白1231`**。

---

## 🧩 模组兼容与已知边界

- **通用自动纳管**：所有使用官方 `ModConfig` 或标准 `GameOptionRegistry / ModHelper` 注册设置与热键的创意工坊模组均可自动收纳与管理。
- **专属适配与运行时增强的工坊模组**：
  - `ClientDemandHighlight`（用户需求高亮 —— 兼容冒号选项 ID `hollow.clientdemandhighlight:toggleKey` 组合键识别与隔离）
  - `Pen_mod_QoL_Tweaks`（Pen 的便利性优化 —— 覆盖快捷操作与长按修饰键、`key_` 前缀候选兼容）
  - `PreciseGroupDrag`（精准拖拽分组 —— 补全群组快移长按热键对组合键与侧键的支持）
  - `ShowItemNames`（物品名称常显 —— 接管 `Mouse3~Mouse6`、组合键过滤与 `Esc` 设空持久化）
  - `ConflictScanner`（模组冲突检测器 —— 兼容 `Esc` 设空、侧键显示、组合键及开启「改造件」双扫去重优化）
  - `FactionReputationExpansion`（势力声望扩展 —— 放行无绑定与 `Mouse3~Mouse6` 侧键持久化规则、手帐热键组合）
  - `SmartInventoryArrange`（智能物品整理 —— 捕获面板放行 `Esc` 空键、修复开关误判并完整双向同步 `OpenSettingsHotkey` 等原生 `"Ctrl+X"` 组合热键串与默认值重置）
  - `SpeedUp`（游戏加速 —— 完整解析并双向读写 `"Alt+Equals"`、`"Ctrl+Home"`、`"Ctrl+F8"` 等 9 组原生组合热键串，支持统一改键、冲突检测与恢复默认）
  - `DuojiHotkeyManager`（多吉改键器 —— 兼容空键显示保存、`Mouse3~Mouse6` 与 `Esc` 清空）
  - `probablystolen.trainer`（Probably Stolen 修改器 —— 通过 `ModHelper.ReadModDataFileText("hotkey-v1.txt")` 与 `"ProbablyStolenTrainerHost"` 运行时消息通道适配修改器开关热键，支持单键、`Mouse3~Mouse6` 侧键与 `Esc` 清空）
- **已知官方扫描限制边界**：
  - 对于 `RealTimeClock`（现实时钟）、`probablystolen.trainer` 等将辅助/宿主类声明在全局无命名空间中的第三方模组，游戏官方 `ModScanner` 静态策略会拒绝针对外部无点号类型名的反射补丁。为保证本模组严格遵守官方安全规范通过加载扫描，不对其挂载外部反射门控补丁——这些模组仍可在统一中心内管理、绑定单键、`Mouse3~Mouse6` 侧键与 `Esc` 解绑，仅暂不叠加修饰组合键前提门控。
- **内置数据持久化键表**：
  - 组合键修饰位：`PlayerPrefs` 键 `probably_unified_hotkeys.combos.v1`
  - 强制纳管热键表：`PlayerPrefs` 键 `probably_unified_hotkeys.forced_hotkeys.v1`
  - 忽略的非按键候选项：`PlayerPrefs` 键 `probably_unified_hotkeys.ignored_candidates.v1`
  - 只读热键探测指纹缓存：`ModData` 文件 `dll_probe_cache_v1.txt`
  - 界面语言选择：官方 `ModConfig`（模组 ID `probably_unified_hotkeys`，配置键 `ui_language`）

---

## 📝 更新日志

### v1.4.4
1. **自动只读热键探测（Route B · `<Capability>files</Capability>`，零外部反射）**：
   - 新增 `DllKeyProbe.cs`，基于游戏自带静态 IL 解析器 `DseCecil.dll` 与 `ModData` / 本地小型 `.json/.cfg/.ini` 配置扫描，为未通过官方 `ModConfig` / `GameOptionRegistry` 公开热键的第三方代码模组自动提取硬编码或自定义保存的按键占用（覆盖 `Input.GetKey*` 字面量、静态字段、`PlayerPrefs.GetInt` 默认键以及 `KeyBindHelper.GetKeyDown("...")`）。
   - 按各模组 `manifest.ModVersion` + DLL/配置大小与修改时间建立增量指纹缓存并持久化至 `ModData/probably_unified_hotkeys/dll_probe_cache_v1.txt`，主设置开窗零重复解析开销，并支持在「模组排查」中点击 **`强制全部重扫`** / **`复查此模组`** 按需失效重探。
   - 在模组卡片头部显示 `探测: N` 徽章，卡片内新增 **「自动探测热键 (只读 · 仅供冲突排查)」** 分区与 **`[探测·只读]`** 不可改键行；已探测按键实时并入全局 **`[!] 仅看冲突`** 检测（提示标注来源，避免“硬编码+已配模组”静默撞键），并保持仅探测-vs-可改键的实用冲突降噪。
2. **「模组排查」疑难收口、分类筛选与诊断效率提升**：
   - 扩充非按键整数关键词黑名单并收紧键码整数启发式（自动跳过 `50/60/100/110/120` 常见数值滑块与 ASCII 数字范围），显著减少常规数值设置误入 `[有疑难/待确认项]`。
   - 修复忽略误报时的计数混淆：清晰区分 **`待确认: N (原始: M)`**（需人工确认的 `[疑难待确认]` / `[原UI未显示·待确认]` 条目）与已通过启发式筛选的 **`[常规选项·可强转]`** 行；**`忽略(非按键)`** 与 **`恢复提醒`** 按钮仅出现在真正需要确认的疑难行上，避免“忽略 1 项待确认后卡片仍显示未归零候选数却已变绿”的困惑。
   - 候选项忽略状态持久化于 `probably_unified_hotkeys.ignored_candidates.v1`，待确认项全部处理完毕的模组自动收口至 `[已正常纳管]`。
   - 「模组排查」置顶汇总栏新增 **探测热键总数**、**`复制排查汇总`** 一键复制按钮，以及一行 **快速分类筛选栏**（`全部`、`探测热键`、`疑难待确认`、`未配官方`、`正常纳管`、`纯资源`）；切换到 `探测热键` 时自动展开明细并支持 **`定位`** 跳转至对应模组卡片。
3. **完整设置展示筛选与原版冲突徽章同步修复**：
   - 完整设置顶部操作行新增 **`显示: 全部` / `仅展示按键` / `仅展示常规`** 下拉筛选器（位于 **`模组排查`** 右侧），可一键将当前全部原版/模组卡片聚焦为纯按键视图或纯常规视图；空组自动折叠隐藏，搜索、展开/收起与冲突跳转无缝联动。
   - 修复原版官方快捷键冲突时仅按键变橙但前方缺少 **`[!]` 叹号徽章** 与鼠标悬浮冲突详情提示的漏绑问题（同步更新原版行 `"label_" + entry.id` 文本并保留原版 `info` 图标）。
4. **合规声明与扫描工具同步**：
   - `manifest.xml` 显式声明 `<Capabilities><Capability>files</Capability></Capabilities>` 并附中英双语权限说明；全程不触碰任何废弃能力（`0 HardFlags / 0 Deprecated / UsesDeprecatedReflection=False / Refused=False`）。

### v1.4.3
1. **官方渠道深度防漏检（野 `ModConfig` / 错配 `source` 自动收编）**：
   - 新增从官方 `ModConfig.configs` 全量字典反查逻辑：针对作者把 `ModConfig.For("...")` ID 写成非标准 `manifest.ID`（或大小写/前缀/后缀变体）的“野配置”，自动按前缀/名称安全映射归并至所属模组卡片（若无法唯一归属，仍保留独立卡片展示绝不丢失），并在保存与恢复默认时通过 `ExtraNotifyModIds` 一并通知对应配置 ID 生效。
   - 强化 `GameOptionRegistry` 归属回收：当作者注册原版区或错误 `source` ID 的条目时，追溯其 `get/set` 回调程序集归属自动归还到对应模组卡片，消除模组选项遗漏。
2. **新增交互式「模组排查与疑难设置接管」视图**：
   - 在顶部操作栏第二行新增 **`模组排查`** / **`模组排查 (N)`** 按钮，点击后切换至置顶汇总区 + 滚动列表布局：**`已启用模组排查`** 摘要栏与绿字反馈提示常驻滚动区上方，下方按 **`[有疑难/待确认项]`**（默认展开）、**`[未检测到官方配置]`**、**`[已正常纳管]`**、**`[纯资源/无代码]`** 四组可折叠卡片收纳（支持会话折叠记忆、顶部 **`全部展开 / 全部收起`** 联动、搜索框实时过滤与自动展开），并提供 **`强制全部重扫`** 原地无闪热更新（保留未保存修改）、`[查看原始项]`、`[复查此模组]`、`[查看设置]` 与 `[复制信息]`（仅更新置顶提示不打断滚动位置）。
   - 针对 AI/野生模组常见的非规范按键写法，开放 **`ConfigEntry<string>` / 无范围 `ConfigEntry<int>`** 一键 **`设为按键绑定`**（及 **`还原`**）人工纠偏入口，持久化于 `probably_unified_hotkeys.forced_hotkeys.v1`，强转后可直接点击 **`前去改键`** 自动跳转并展开对应卡片。
3. **非规范按键扩展与运行时组合键拦截全通**：
   - 扩展按键关键词库（新增 `TriggerBtn`、`FastMapping`、`HotKey`/`Shortcut`/`BindKey` 等精确词元），加入 `MaxKeyCount` / `KeyCount` / `KeySize` 等非键码整数保护过滤以防常规滑块被误抓为按键；同时自动纳管无歧义单键码字符串配置（如 `"F8"`、`"Mouse4"`）并保持普通文本词条原样。
   - 为 `ConfigEntry<string>.Value` 与 `ConfigEntry<int>.Value` 读取路径补全 Harmony 后缀防误触门控，确保强转与自动识别的 string/int 热键同样支持 `Shift / Ctrl / Alt` 组合键与组合防误触隔离，且全程保持 `ModScanner` **0 警告**。
4. **全界面中英双语本地化与双入口实时语言切换（`I18n.cs`）**：
   - 新增 `I18n` 本地化中心，支持 `跟随游戏语言`（默认，非中文环境自动回退英语）、`简体中文` 与 `English` 三选一切换（刻意移除“跟随系统”）。
   - 同时接入「改造件」页面右侧本模组齿轮设置与「ESC → 选项」内本模组独立卡片双入口，持久化于官方 `ModConfig`（`ui_language`），切换后无需重启即时热刷新全部 UI 文本。
5. **新增 `probablystolen.trainer` 适配与安全边界收敛**：
   - 通过只读 mod 数据通道与运行时消息适配 `probablystolen.trainer`（Probably Stolen 修改器）开关热键，支持单键、`Mouse3~Mouse6` 侧键与 `Esc` 清空，同时严格遵守官方 `ModScanner` 对无命名空间外部类的扫描边界，保持 0 Findings / 0 Capabilities。
6. **清单规范化、按钮对齐与实机封面更新**：
   - `manifest.xml` 更新为 `太湖白1231` 署名及中英双语标题/介绍，统一「选项」底部保存按钮指引文案为 **`应用`**，并更新 `1920×1080`（`< 1 MB`）`v1.4.3` 实机 `preview.png` 封面图。

### v1.4.2
1. **彻底修复四大核心按钮文字空白（`[!] 仅看冲突`、`全部展开`、`▶/▼` 折叠按钮、`恢复默认`）**：
   - 定位 `Meta_Button` 预制体自带 `HorizontalLayoutGroup.padding = (16, 16, 4, 4)` 与 `childForceExpand = false` 在 `34×34` 折叠按钮上将内部 `Text (TMP)` 矩形压缩至仅 `2px` 宽的根因，并在紧凑型动作按钮（`228×40` / `146×40` / `112×36`）上改由 `HorizontalLayoutGroup` 接管唯一内边距（消除 `TMP.margin` 二次累加导致的垂直高度溢出与 `Ellipsis` 零字符裁切）。
   - 修复离线装配阶段（`windowRoot.SetActive(false)` 后）因 `TMPLocalizer` 尚未运行导致测量与 `Lubrifont` 运行时字体不一致、以及 `builder.Show()` / `SetVisible(true)` 首次激活时可能触发组件重置的问题：捕获活跃层 `_activeButtonFont` 预先参与测量、`ApplySinglePassFontSize` 同时校验安全宽高与二次微调，并在窗口 `Show()` 及卡片 `SetExpanded(true)` 后同步刷新按钮字体、主题色与排布。
2. **修复“整体搜索 `yp` 误匹配 `ConflictScanner` 冲突报告窗口快捷键”的根源逻辑**：
   - 因 `conflict_scanner.ReportKey`（当前绑定显示为 `Numpad 6`）对应的 Unity 枚举名 `KeyCode.Keypad6` 中恰包含词中子串 `"yp"`（`Ke[yp]ad6`），原 `PinyinEngine.Matches(hotkey.EffectiveKey.ToString(), query)` 会产生反直觉误命中。
   - 新增 `MatchesKeyCodeSearchToken` 与格式化键名边界匹配 `MatchesFormattedKeyText`（支持 `keypad6`、`kp6`、`numpad 6`、`num6`、`小键盘6`、`xjp6`、`ctrl+c` 等词元起点与别名匹配，同时禁止英文按键文本内部的 `"yp"` / `"ow"` / `"on"` / `"ne"` / `"mp"` 词中子串误触发）。
3. **重度解决「改造件」与「全部展开」卡顿并消除 `[CustomUI]` 警告**：
   - 在 `ModUIManager.OpenPanel` 期间注入 `ConflictScannerPerfHelper` 作用域去重与环境指纹缓存：消除每次点开「改造件」触发 `ConflictScanner.PatchPopulateLayoutList` + `PatchOpenPanel` 连续执行 **两次全量 40+ DLL 的 `DseCecil` 同步解析**，并在 `IdleWarmupRunner` 主菜单闲置阶段静默预热，同时完整编码 `LoadedMod.IsEnabled` 与 `ModLoader.IsModEnabled` 双状态位及模组列表中增删改动以按需失效重建，零 `System.IO` 越界保持 `ModScanner` 全绿。
   - 缓存 `WithSuspendedRootLayout`（含各卡片 `ContentContainerObject`）与 `ModUIManager` 容器 `LayoutGroup` / `ContentSizeFitter` 引用列表，消除「全部展开 / 收起」及列表筛选期间反复调用 `GetComponents` 与堆列表分配，并将折叠卡片后台预建预算提升至 `7.5ms/帧`（仍保持零过渡动画瞬间切换）。
   - 通过 `Patch_GameOptionUIManager_RefreshRules` + `Patch_CustomUIWindow_Get` 在增强版选项规则刷新期间优雅静默未实例化/无独立面板注记的模组段（如 `note_user.realtimeclock:clock`），清除 `Player.log` 冗余警告日志。
4. **全代码库反射与遍历规范审计优化**：
   - 清理 `ModEntry` 中此前在每帧钩子（`Duoji_ToggleAnyDown_Prefix`、`ConflictScanner_Poll_Prefix`、`ShowItemNames_PollCapture_Prefix`、`FactionReputation_Tick_Prefix`）及捕获保存路径中反复执行的 `AccessTools.TypeByName` 与 `MethodInfo.Invoke`，全面升级为补丁安装/类型加载时一次性绑定的 `Delegate.CreateDelegate` 强类型委托与静态字段句柄。
   - 将 `UnifiedOptionUI`（`BuildEntry` / `NoteId` / `OnApplySetting` / `RefreshRules` / `SetKeyBindStatus` / `FireOnModConfigApplied`）、`ModOptionScanner.ApplyBoxed`、`KnownModAdapters` 及 `PerformancePatches.ModUIManagerPerfHelper` 中可委托化成员全部迁移为强类型委托调用，全库零 `FindObjectsOfType` / `GameObject.Find`。

### v1.4.1
1. **深度扫描审计与第三方模组兼容性完善**：
   - 修复 `ModOptionScanner.IsHotkeyEntry` 缺乏值类型约束导致布尔开关（如 `SmartInventoryArrange.ArrangeHotkeyEnabled`、`BetterBottlePrinter.show_keycard`、`Pen_mod_QoL_Tweaks.tab_switch_keys`）与逗号关键词字符串（如 `JacksonShopPages.machine_keywords / module_keywords`）被误判为按键按钮的问题。
   - 新增原生 `"Ctrl/Shift/Alt+Key"` 组合热键串解析与双向写回（打通 `SpeedUp` 全部 9 组加速热键与 `SmartInventoryArrange` 3 组整理热键的读取、保存、恢复默认与冲突排查），同时保持单键词模组（如 `FactionReputationExpansion`）原有机制不变。
   - 强化 `ModCatalogCache` 指纹维度，将各模组启用状态（`ModLoader.IsModEnabled`）与 `ModConfig` 条目数纳入缓存指纹，防止开关模组或延迟注册后命中过期目录缓存。
2. **流畅度与生命周期边界再加固**：
   - 将按键录入轮询 `TickCapture` 中的 `Enum.GetValues(typeof(KeyCode))` 提取为静态去重键值表 `BindableKeyboardKeys`，消除按帧录入期间的内存分配与重复枚举。
   - 优化 `PinyinEngine` 全拼小写归一化与搜索词分词去分隔符匹配（修正中英混排全拼命中并加入有界归一化缓存，连续键入匹配耗时降至原先约 1/10），同步修复 `KnownModAdapters` 首轮反射引发程序集延迟加载时的 `epoch` 级联失效（二次开窗零重复全程序集扫描）。
   - 为 `RefreshConflicts` 增加排序归一化冲突签名比对，并在未发生真实冲突增减时跳过 `[!] 仅看冲突` 视图冗余重建，连续切换视图响应零卡顿。
   - 为 `FittedFontSizeCache` 增加上限保护（超过 `2,048` 条时自动清理），同时在 `ModEntry` 补挂 `Application.wantsToQuit` 钩子，确保通过 `Alt+F4` 或窗口右上角关闭时也能抢在 `ModLoader.OnApplicationQuit` 前安全保存并跳过冗余 `UnpatchAll`。

### v1.4.0
1. **彻底优化「退出游戏」最大卡顿源**：在游戏退出前安全执行各模组 `OnDisable()` 落盘后跳过 `ModLoader.OnApplicationQuit` 触发的 40+ 次同步 `Harmony.UnpatchAll` 动态方法重编译卡顿，并跳过游戏暂停菜单在遥测关闭（`OVERWRITE_DISABLE_ANALYTICS == true`）状态下无意义空等 1 秒的 `QuitWithAnalytics` 协程，点击退出立即收尾。
2. **深度优化「改造件 / 模组」面板开启响应（「改造件」卡顿）**：新增 `PerformancePatches.cs`，在 `ModUIManager` 中实施 `Row` 行对象池化复用与变更差分更新，消除每次打开销毁重造 40+ 个条目（120+ 子对象）开销；将前置依赖检查升级为 O(1) 哈希索引以消除 `ModLoader.Mods.Find` O(N²) 列表复制与卡顿；并在主菜单空闲帧静默预建未激活面板骨架与行池（完全兼容第三方冲突检测插件 Postfix）。
3. **重做「全部展开 / 全部收起」与按键排版耗时源**：移除全页 50~100+ 颗按键及 `恢复默认` 按钮的 `TMP_Text.enableAutoSizing` 多轮二分排版循环，替换为带全局结果缓存的 `GetPreferredValues` 单次拟合算法；将后台折叠卡片预建升级为 `3.5ms/帧` 预算调度；并将卡片标题行 `HorizontalLayoutGroup`、`Viewport` 与外层滚动容器一并纳入单帧挂起重排，视觉无过渡动画且瞬时齐位。
4. **优化「选项」开窗构建与反射/查表热路径**：主菜单空闲分帧预先暖机 `PinyinEngine` 与 `ModCatalogCache`；开窗时按视图层级（`menu / meta`）仅首开抓取一次按钮主题后立即隐藏根窗口完成离线控件装配；引入 `ButtonTextColor` / `FittedFontSizeCache` 缓存、差异渲染守卫、`KnownModAdapters` 按程序集加载 epoch 缓存以及零数组分配的 `ModOptionScanner.GetEntryExternalAssembly`。

### v1.3.3
1. **消除「全部展开 / 全部收起」滚动过渡动画并大幅降低卡顿**：移除多帧逐卡弹出的 `PendingCardTasks` 动画队列，改为“闲置帧静默预建（在 `inactive` 折叠容器内逐卡构建，零画面变动）+ 批处理挂起根布局（`WithSuspendedRootLayout`）+ 卡片状态幂等守卫”；点击 `全部展开` / `全部收起` 立即单帧整齐到位，既无多余过渡动画也显著减轻卡顿。
2. **为原版官方设置与按键按钮补全「恢复默认」并统一文案**：上方 **游戏、音频、显示、对话、按键绑定** 全部 5 个原版折叠卡片与下方模组卡片统一在展开后显示 **`恢复默认`**（`112×34`）；原版常规项严格对齐 `EmporiumOption.SetDefault()` 与 `GameOptionRegistry.RegisterVanilla()` 出厂默认值（含 `dialogue_bubble_scale = 10` 及非 16:9 显示器 `fullscreen -> RefreshRules -> resolution` 联动重算），原版快捷键按 `KeyBindHelper.GetDefault` 重置并清除旧 `ComboKeyStore` 组合键修饰、同步刷新冲突警示，最后点底部「应用设置」保存生效。

### v1.3.2
1. **上方原版设置区块正式支持折叠与全范围搜索**：将官方「游戏」「音频」「显示」「对话」「按键绑定」等分区升级为统一可折叠卡片（默认展开并按会话记忆折叠状态），并彻底接入顶部四档搜索栏；现可直接使用简体/繁体/英文/拼音（如 `主音量`、`zyl`、`全屏`、`ctrl`、`侧键`）搜索定位原版设置及按键，空结果时显示友好提示。
2. **彻底消除「全部展开 / 全部收起」与搜索卡顿**：引入分帧时间片增量调度队列（`PendingCardTasks`），点击 `全部展开` / `全部收起`、搜索触发大量匹配卡片或重开窗体恢复展开状态时均按帧预算平滑执行，消除几十张卡片同步实例化导致的帧率骤降。
3. **卡片快捷键按钮与官方原生视觉完全统一**：新增共享样式工具 `StyleHotkeyButton` / `StyleNativeWidthButton` 统一接管上方原版、下方模组卡片与 `[!] 仅看冲突` 三处的按键按钮；还原官方原生按钮高度、原生标准字号与 `160px` 标准宽度，同步统一原版卡片内嵌列缩进（`24px / 16px`），彻底告别压扁缩字割裂感（仅超长组合键自动收缩防溢出）。
4. **全面系统性防复发修复**：修正带有记忆展开态卡片重开窗口时因构建时序导致的空内容异常；确保折叠或隐藏状态下原版动态规则联动（`RefreshRules`）与底部 `应用设置` 保存 100% 正常工作。

### v1.3.1
1. **修复工坊组合键与长按热键失效**：修正 `ClientDemandHighlight`（用户需求高亮）因冒号 ID（`hollow.clientdemandhighlight:toggleKey`）未匹配导致的 `Ctrl+Z / Ctrl+C / Ctrl+0` 自我抑制问题；同步补齐 `Pen_mod_QoL_Tweaks`（`key_` / `Pen_mod_QoL_Tweaks:` 前缀）、`PreciseGroupDrag`（群组快移长按热键）及 `FactionReputationExpansion`（侧键规则）。
2. **根治按钮悬停变蓝 Bug**：直接接入游戏公开类型 `ButtonTextColor` 并同步修改 `normal / highlight / pressed` 字段与 `isHovered` 状态，修复冲突按键和 `[!] 仅看冲突` 在鼠标扫过后退回蓝色的问题。
3. **优化折叠三角比例与按钮内边距**：将折叠三角缩放至 `0.50f` 居中对齐；加宽工具栏与按键按钮尺寸并配置内边距与超宽自动缩字号，彻底解决字体贴边粘连。
4. **繁简中文互通搜索与自动滚动定位**：新增纯 C# `2,473` 组繁简对照表（`TradSimpTable`），支持简体直接搜繁体模组及配置并双向互通；搜索命中时自动退出冲突视图、展开匹配卡片并滚动视窗定位到首个命中卡片。
5. **消除选项页打开与游戏退出卡顿**：改用卡片外壳与冲突视图双懒加载机制消除打开卡顿；通过 `Application.quitting` 在进程退出时跳过冗余 `UnpatchAll` 与注册表清理降低退出卡顿；为 `WorkshopModPatcher` 加入程序集白名单 `O(1)` 过滤，并通过官方 `ModScanner` 零告警扫描。

### v1.3.0
1. **统一规范命名与工程目录**：正式更名为 **「可能是按键统一优化」**（`probably_unified_hotkeys` / `ProbablyUnifiedHotkeys.dll` / `ProbablyUnifiedHotkeys`）。
2. **全面新增 `Shift / Ctrl / Alt` 组合键支持**：同时支持原版、官方框架与主流工坊模组；支持多修饰键组合、`Alt + 左键` 等鼠标组合以及单独点按修饰键绑定；升级全局组合冲突判定与运行时防裸键误触隔离。
3. **新增鼠标侧键（`Mouse3 ~ Mouse6`）支持**：全流程适配单键与组合绑定，统一显示为 `鼠标侧键1 ~ 4`。
4. **统一 `Esc` 单键解绑**：解绑改为更顺手的 **`Esc`**（自动吞掉当帧关窗事件），彻底释放 **`Backspace`（退格）** 和 **`Delete`** 作为正常可绑定快捷键。

### v1.2.1
1. **彻底清除上方原版区重复项**：结合委托来源程序集识别，将混入原版分区的第三方模组设置彻底过滤并收编至下方卡片区。
2. **重构两行工具栏与四档搜索范围**：新增 `整体搜索`、`按键+常规`、`仅搜按键`、`仅搜常规` 四种过滤方式，解决仅搜模组名时显示空标题框的问题。
3. **优化卡片细节与配色**：统一 `34×34` 方形折叠按钮、分区分割线、卡片改为「上半截快捷键 + 下半截常规」，并将“恢复默认”精简为展开按需显示的 **`恢复`** 按钮（附首次启动快照兜底）；统一空键柔和灰与橙色 `[!]` 冲突提示样式。

### v1.2.0
1. **重构为「统一按键管理与模组配置中心」**：告别单一改空键补丁形态，将全部已加载模组的快捷键与常规参数直接整合进游戏原生 ESC「选项」界面。
2. **内置 20,902 字 CJK 纯内存拼音引擎与指纹缓存**：支持中英文、全拼与拼音首字母实时搜索，并通过环境指纹缓存保持开启选项页流畅。
3. **上线全局冲突分析与 `[!] 仅看冲突` 聚合视图**：淡橙色预警、悬浮冲突详单与集中式改键排查，允许自由保存空键冲突状态。

### v1.1.0
1. **初版核心发布（原名 `AllowUnbindKey / 允许绑定空按键`）**：打通游戏原生设定、多吉改键器及多款主流工坊模组的空键绑定与持久保存。
2. **修复原版 `KeyCode.None` 冲突死锁 Bug**：拦截 `FindPendingKeyOwner` 对空键的误判，使未绑定动作之间不再报错阻断保存，并通过官方 `ModScanner` 静态安全规范认证。

---

## 🎮 使用方法

### 安装与启用
1. 将模组目录 `probably_unified_hotkeys`（内含 `ProbablyUnifiedHotkeys.dll`、`manifest.xml`、`README.md`、`preview.png`）放入游戏根目录下的 `Mods\` 文件夹中（或通过创意工坊订阅启用）。
2. 启动游戏，在「改造件 / 模组」列表中确认勾选启用 **「可能是按键统一优化」**，无需任何前置依赖库。

### 日常操作步骤
1. 进入主菜单或游戏局内后按 **ESC**，打开 **「选项」**。
2. **快速查找设置（原版 + 模组全覆盖）**：
   - 在顶部左上角选择搜索模式（推荐默认 **`整体搜索`**；只想搜某类条目时可切为 **`仅搜按键`** 或 **`仅搜常规`**）。
   - 在搜索框输入**简体/繁体中文、英文、全拼、拼音缩写**（如 `sp`、`zyl`、`xssz`）或**按键关键字**（如 `ctrl`、`侧键`、`无`），原版分区与模组卡片将同步自动过滤并展开命中项、滚动定位至首个匹配卡片；清空输入即恢复折叠态与全览并回到顶部。
3. **一键排查冲突**：
   - 若顶部按钮显示 **`[!] 仅看冲突 (N 组)`** 且字体呈淡橙色，说明当前有 `N` 个按键存在重复绑定。
   - 点击该按钮即可只查看发生冲突的按键分组，鼠标悬停或直接点击其中的按键按钮即可就地修改；排查完毕点击 **`返回完整设置`**。
4. **折叠浏览与恢复默认设置**：
   - 上方官方分区与下方模组卡片均支持点击左侧 **`▶` / `▼`** 独立展开/收起，或使用顶部 **`全部展开` / `全部收起`** 单帧瞬时无过渡切换。
   - 展开任意**原版分区**或**模组卡片**后，如需将当前分区的设置与按键恢复出厂默认值（无显式默认值的第三方模组回退至本局初始快照），直接点击卡片右上角 **`恢复默认`**；原版更改记得点击底部 **「应用」** 生效。
5. **绑定与解绑快捷键**：
   - 点击任意按键按钮，按钮文字变为 **`请按键... (Esc=无)`** 后即可开始录入：
     - **绑定普通单键 / 鼠标中键 / 侧键**：直接按目标键盘键、`Backspace`、`Delete`、鼠标中键或侧键 `1~4`。
     - **绑定组合键**：先按住 **`Shift` / `Ctrl` / `Alt`**（支持单按或多键齐按），按钮会实时显示当前修饰前缀（如 `Ctrl+...`），接着再按主按键或点击鼠标左键 / 右键 / 中键 / 侧键即可完成绑定。
     - **绑定独立修饰键**：轻点一下单个 `Shift` / `Ctrl` / `Alt` 后直接松手，即可绑定该单键自身。
     - **解除绑定（设为空）**：按下 **`Esc`**，按键立即变为灰色 **`无`**（不会关闭设置窗口）。
     - **取消本次录入**：不按任何修饰键直接单击鼠标**左键**或**右键**，立即放弃本次录入并还原原按键。
6. **保存生效**：
   - 所有热键与常规参数调整妥当后，务必点击页面最底部的 **「应用」** 按钮完成持久化保存并实时生效。
7. **模组排查与疑难强转（如发现隐藏热键）**：
   - 若怀疑某个 Mod 的按键未出现在主列表中，点击顶部 **`模组排查 (N)`** 查看分类统计；可展开对应模组的 **`查看原始项`**，将文字/整数形式的候选热键一键点击 **`设为按键绑定`**（可随时 **`还原`**），再点击 **`前去改键`** 自动跳回对应卡片。
8. **切换模组界面语言**：
   - 可在游戏主界面 **「改造件 → 本模组右侧齿轮」** 或 **「ESC → 选项 → 可能是按键统一优化」** 卡片中修改 **`界面语言 / UI Language`**（`跟随游戏语言` / `简体中文` / `English`），无需重启即时生效。

---

## 📦 兼容性与源码结构

- **适用游戏版本**：*深空当铺 (Probably Stolen)* `0.46` / `0.46D`
- **前置依赖 / 权限声明**：无前置模组 ｜ 仅声明官方 `<Capability>files</Capability>`（用于静态只读读取各模组 DLL 与本地配置探测未知热键，不修改任何外部文件；通过官方 `ModScanner` 零高危扫描：`0 HardFlags / 0 Deprecated / Refused=False`）
- **核心代码模块**：
  - `ModEntry.cs`：模组入口、生命周期/快速退出状态管理、`KeyBindHelper` 与工坊程序集 Harmony 补丁中枢，以及「改造件」齿轮与 `I18n` 入口注册（`ModConfigUiHelper`）。
  - `I18n.cs`：中英双语本地化系统（`跟随游戏语言` / `简体中文` / `English`）、`ModConfig` 语言持久化（`ui_language`）与热刷新事件通知。
  - `PerformancePatches.cs`：v1.4.0+ 退出秒退、主菜单分帧闲置预热、`ModUIManager` 行池化、O(1) 依赖索引及 `ConflictScanner` 双扫去重优化模块。
  - `UnifiedOptionUI.cs` & `ModModel.cs`：原生选项页接管、离线构建、单次字体尺寸适配、按帧预算静默预建、挂起根布局瞬时切换、全范围四档搜索、只读探测热键行、「模组排查」子视图与 `ButtonTextColor` 色彩缓存同步。
  - `ComboKeyStore.cs`：组合键持久化（`combos.v1`）、修饰键掩码校验、侧键命名格式化、强转/忽略列表持久化与运行时隔离抑制引擎。
  - `DllKeyProbe.cs`：`DseCecil` 静态 IL、`ModData` 与模组小型配置文件全自动只读热键探测器及指纹增量缓存。
  - `ModOptionScanner.cs` & `KnownModAdapters.cs` & `ModCatalogCache.cs`：全渠道设置收集（含野 `ModConfig` 收编与只读探测并入）、委托来源防重识别、专项适配（含修改器）、会话启动快照与指纹缓存。
  - `ConflictDetector.cs`：修饰键+主键精确判重（含可改键与只读探测键联动检测）与冲突提示生成。
  - `PinyinEngine.cs` & `TradSimpTable.cs`：CJK 拼音与 `2,473` 组繁简互通归一化搜索引擎。

---

## 💬 交流与 Bug 反馈

- **QQ 交流群**：`1127147529`
- **提交 Issue / 反馈模板**：反馈问题时请尽量附上以下信息以便定位：
  1. 游戏版本与本模组版本号（例如 `0.46 / v1.4.4`）；
  2. 问题现象与复现步骤（所在界面、操作步骤、预期与实际表现）；
  3. 若涉及特定第三方模组，请在 **`模组排查`** 中点击该模组的 **`复制信息`**（或顶部的 **`复制排查汇总`**）并粘贴至反馈中；
  4. 游戏日志 `Player.log`（位于游戏存档数据目录，若有报错段落请优先截取）。
