# 更新日志 (Changelog)

本项目（**可能是按键统一优化 / Probably Unified Hotkeys**）的所有显著变更均记录在此文件中。  
格式遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.0.0/)，版本号严格遵循 [语义化版本 (SemVer 2.0.0)](https://semver.org/lang/zh-CN/)。

---

## [1.5.0] - 2026-11-20

### Added
- **7 层单向解耦架构落地**：
  - 确立 `Diagnostics` -> `Config` -> `Core` -> `Persistence` -> `Adapter` -> `Patches` -> `Client` 单向分层拓扑，彻底杜绝逆向跨层调用。
- **UI 运行时状态独立管理 (`UiSessionState.cs`)**：
  - 彻底修复历史版本与 `v2` 遗留的设计缺陷：将 `Core/Model/ModModel.cs` 净化为 100% 纯业务领域模型（零 `GameObject` / `CustomUIElement` 控件依赖）；
  - 新增 `src/Client/State/UiSessionState.cs` 独立集中维护所有运行时 UI 控件句柄、卡片展开状态、`BaselineSnapshot` 基准快照与未保存脏状态机；
  - 订阅 `ModCatalogCache.SessionResetRequested` 自动清理控件句柄，杜绝跨会话句柄内存泄露。
- **5,137 行巨石 UI (`UnifiedOptionUI.cs`) 正交拆解为 10 个高内聚子组件**：
  - `UnifiedOptionWindow.cs`：主窗口生命周期、`IOptionUiBridge` 18 项契约兑现与离线批量装配；
  - `UiLayoutAndFontHelper.cs`：`FittedFontSizeCache` 单次字号拟合缓存与 36px 方形折叠按钮防挤压内边距清零；
  - `UnsavedConfirmDialog.cs`：原生模态居中防丢确认弹窗与按键录入状态豁免；
  - `TopSearchAndActionBar.cs`：四模式纯内核检索引擎复用与全部展开/收起瞬切调度；
  - `CollapsibleCardView.cs`：原版分区与模组折叠卡片，落地 ADR-009 离线按需懒构建（Lazy Content Build）；
  - `KeyCaptureController.cs`：按键/组合键/侧键录入控制器，支持鼠标右键（`Mouse1`）与中键（`Mouse2`）绑定增强（ADR-010）；
  - `ConflictAggregationView.cs`：冲突专属聚合视图与就地实时联动改键；
  - `ModDiagnosticView.cs`：疑难接管排查视图、原地热重扫与剪贴板合规汇总复制；
  - `ProfilePresetCardView.cs`：3 套场景预设快照与剪贴板 JSON 一键导出/导入；
  - `SectionLocalizer.cs`：零 UI 依赖三层分区标题本地化与结构化键名智能净化。
- **四大高频核心按钮极致顺滑优化 (ADR-009)**：
  - 「退出游戏」：通过 `PrepareFastQuit` 消除 52 个模组 `UnpatchAll` 同步反编译与网络等待，退出时间降至 `< 0.05s`；
  - 「改造件」面板：建立全量模组环境指纹签名，彻底斩断 5 次重复重扫（265 次反编译），实现单帧瞬开；
  - 「选项」面板：模组折叠卡片按需懒构建，开窗仅加载外壳，消除逐控件回流风暴；
  - 「全部展开」：在离线非激活容器下批量实例化，结合 `WithSuspendedRootLayout` 挂起顶层重排，达成丝滑瞬切。
- **释放鼠标右键与中键改键能力 (ADR-010)**：
  - 修复原版右键被当作取消键的缺陷，全面支持将 `Mouse1`（右键）与 `Mouse2`（中键）直接录制或组合绑定为游戏操作键；
  - 仅无修饰键单击鼠标左键（`Mouse0`）执行取消录入。
- **39 项全套离线单元测试 100% 全绿覆盖**：
  - 新增 `Batch3ClientArchitectureAndStateTests.cs`，反射扫描全程序集架构纯度、独立子组件隔离、`OptionUiHost` 桥梁挂载、模型字典自清理与 `KeyCode.None` 防假脏契约。
- **完备的工程治理与标准化技术基建**：
  - 新增《AI Agent 协作宪章》(AGENTS.md)，落地单层星型扁平拓扑、零历史自包含派发与防并发红线；
  - 新增《踩坑全景账本》(docs/PITFALLS.md)，按四元组收录 18 大历史深坑；
  - 新增《Harmony 补丁与 ModScanner 解耦规范》(docs/HARMONY_SPEC.md)；
  - 新增《9 阶段实机回归验收清单》(docs/REGRESSION_CHECKLIST.md)；
  - 新增《标准化发布作业程序》(docs/RELEASE_SOP.md) 与《跨阶段交接三件套》(docs/handoff/)。
- **终极收官 4 项顽疾清零与全链路防退化闭环**：
  - **改造件详情页零错位重叠与按钮 42px 零拉伸（Bug #1）**：移除父 Row 的 `minHeight/preferredHeight` 强锁与左侧 `column` 的 `ContentSizeFitter`，保持父 Row `childControlHeight = true && childForceExpandHeight = false` 让 7 行元数据高度自然展开；仅在右侧按钮自身注入 `LayoutElement`（`preferredHeight = 42f, minHeight = 42f, flexibleHeight = 0f`），配合 `preview_panel` 对本模组专属隐藏与描述 `ScrollRect`（`flexibleHeight = 1f`），达成文字零错位、按钮 42px 零拉伸；
  - **中文多音字检索引擎全面升级（Bug #2）**：突破 `pinyin.bin` 单一读音局限，新增 `PolyphoneTable.cs` 覆盖 215+ 个高频多音字（如「重」覆盖 `zhong` 与 `chong`，「调」覆盖 `tiao` 与 `diao`，「行」覆盖 `hang` 与 `xing` 等）；实现零堆分配位掩码剪枝检索 `MatchesPolyphoneTokens`，全面支持全拼、声母缩写、混合拼打与半截前缀容错（如「多重密码解序器」对 `chong`、`zhong`、`duochong`、`duozhong`、`dcmm`、`dcmmjxq`、`dzmm`、`duoch` 均 100% 命中，且无关词零误判）；
  - **彻底破案并根治组合键底层失效与同主键效果抵消（Bug #3）**：DseCecil 逆向揭示游戏物品翻转/旋转（`ItemMouseDragHandler`）未走 `KeyBindHelper.GetKeyDown`，而是由 `InputActionManager` 收集并通过 `KeyBindHelper.Get("flip_item")` 直接读取 `KeyCode`；且当顺逆时针旋转同主键（如 `Ctrl + F` 与 `Ctrl + Alt + F`）时，未过滤的物理按键同帧产生 `+90°` 与 `-90°` 毫秒级物理抵消。通过在 `OptionAndKeyBindPatches.cs` 中挂载底层 `Patch_KeyBindHelper_Get` Postfix，按当前修饰键掩码精确放行物理键，同主键不同修饰键精准解耦，单按物理键零误触，保存期与 ES3 读写全链路 bypass 保护；
  - **按键按钮恢复原版 160f 尺寸与长组合键自适应零截断（Bug #4）**：严格坚守 `KeybindButtonWidth = 160f` 原生设计尺寸（杜绝改大外部框体）；通过将按钮水平 padding 收窄至 4px、文本 margin 收窄至 2px，在外部框体尺寸一分不差的前提下直接释放 24px+ 有效宽度（达 148px）；配合 `FittedFontSizeCache` 单次字号自适应微缩（`[8.5f, 15f]`），确保 `Ctrl + Space`、`Ctrl+Alt+F`、`Ctrl+Shift+F12` 等长组合键完整清晰显示，绝不触发截断省略号；
  - **彻底修复原版键重绑时 ExplicitUnboundHotkeys 残留导致退回「无」隐患**：在 `UnifiedOptionWindow.ApplyAllSettings` 同步原版及模组键保存时显式清除解绑记录，并在底层 `SetSavedCombo` 增加非空键自动释放守卫；
- **56 项全套离线单元测试 100% 全绿覆盖**：
  - 新增 `FinalShipVerificationTests.cs`，100% 针对上述 4 项问题及重绑恢复编写代码级与沙盘级自动化单测（场景 A 翻转、场景 B 同主键不同修饰键顺逆时针零抵消、场景 C 单组合键防误触抑制、按钮 160f 原尺寸不变、多音字全覆盖、解绑重绑恢复），测试总数增至 56 项，100% 全绿通过。

### Changed
- **异常防崩网加固**：全库消灭空 `catch {}`，统一通过 `SafeExecution.SafeExecute` / `SafeFunc` 执行并记录结构化日志（`[ProbablyUnifiedHotkeys][LEVEL]`）。
- **0 权限默认纯净契约**：默认构建注入 `DisableDllKeyProbe=true` 编译宏，`manifest.xml` 保持空 `<Capabilities />`，确保静态扫描达到 `0 Capabilities / 0 HardFlags / 0 Deprecated / Refused=False`（1,411 个扫描方法全部通过），面向官方 2026-12-10 安全白名单门禁。

### Fixed
- **彻底根除原版卡片标题栏被方案预设提示污染**：切断预设逻辑对 `SetKeyBindStatusDelegate` 的误调，反馈严格隔离在卡片内部状态栏与底栏。
- **彻底根除扫描与开窗未开启 `EnterBypass` 导致的组合键退化单键与假脏弹窗**：在配置扫描与开窗最外层强制包裹 `using (ComboKeyStore.EnterBypass())`，重构 `SyncVanillaPendingFromManager` 修饰键同步算法，收窄 `ExplicitUnbound` 作用域至全量规范 ID。
- **彻底根除搜索框打字时按 Esc 误关闭窗口与误弹未保存弹窗**：扩展 `IOptionUiBridge.IsSearchInputFocused` 独立释放聚焦。

---

## [1.4.7] - 2026-11-18

### Fixed
- **彻底修复解绑按键（设为「无」）点击「应用」后退出仍误触发「未保存更改」弹窗的 Bug**：
  - 修复 `ComboKeyStore.GetSavedModifiers` 在查询 `KeyCode.None` 目标键时未严格阻断而意外回落返回历史残留修饰位（如 `Ctrl / Shift`）的问题。
  - 在 `HasUnsavedChanges()` 中加入显式解绑容错豁免，当按键处于两端解绑（`None`）状态时不再因第三方模组 Getter 滞后而误报脏标记。
  - 在 `ApplyAllSettings()` 执行完毕后强制刷新全量基准快照（Baseline Snapshot）并彻底复位脏标记与临时变动。

### Changed
- **0 权限纯净构建默认强制化**：`src/ProbablyUnifiedHotkeys/ProbablyUnifiedHotkeys.csproj` 默认启用 `DisableDllKeyProbe=true`（`DISABLE_DLL_KEY_PROBE`），`manifest.xml` 保持空 `<Capabilities />`，全面通过官方 `ModScanner` 0 权限安全扫描（`0 Capabilities / 0 HardFlags / 0 Deprecated / Refused=False`）。

---

## [1.4.6] - 2026-11-17

### Added
- **退出设置未保存防丢确认弹窗（Unsaved Changes Confirmation Dialog）**：
  - 覆盖全部原版设置与第三方模组快捷键、组合键修饰及常规配置项；在顶层 `CustomUIManager.HandleEscape` 拦截，解决主菜单修改后按 Esc 无反应的死锁问题，全面覆盖主菜单、局内暂停及商店 Esc 菜单。
  - 提供数学级居中对齐的「保存并退出」、「放弃修改」与「取消」三项原生样式按钮；严格保障快捷键录制状态（`请按键... (Esc=无)`）及缓冲帧内按 `Esc` 仅清空绑定为「无」，绝不误触发退出弹窗。

### Changed
- **选项页底部「应用」按钮纯净居中复原**：移除底部栏拥挤的 5 个方案辅助按钮，恢复官方原生绝对居中的单个 180px「应用」按钮；3 套预设快照与剪贴板 JSON 导入/导出完整收纳于「统一快捷键管理」独立折叠卡片内。

---

## [1.4.5] - 2026-11-15

### Added
- **键位与设置方案快照管理（`HotkeyProfileManager.cs`）**：
  - 新增 3 套本地场景预设槽位（`方案一 (日常)`、`方案二 (搜刮)`、`方案三 (备用)`）与剪贴板紧凑 JSON 一键导出/导入（Schema: `puh_profile_v1`），全量备份与恢复快捷键、适配器自定义热键、常规模组设置、强转表与忽略表。
- **三层根治式模组分区标题本地化引擎**：
  - 第 1 层通过零反射、零权限读取原生 `LoadedMod.Localization` 多语言表并支持英文值反向对齐中文译文；第 2 层内置工坊全生态分区词典；第 3 层对未知 `xxx.config.yyy` 键名自动剥离前缀净化排版。
- **改造件面板本模组封面独立剔除**：选中本模组时隐藏大块封面，使介绍、指南与依赖信息置顶完整展示。

### Fixed
- 修复超长文本输入框（如需求高亮模组「忽略指定物品 ID」）被挤压至单字符垂直换行的排版缺陷。

---

## [1.4.4] - 2026-11-13

### Added
- **可选静态只读热键探测（`DllKeyProbe.cs`，Route B · `<Capability>files</Capability>`）**与 **模组功能模块化体系（`ModFeatureConfig.cs`）**。
- **「模组排查」疑难收口与顶部操作栏像素级对齐（`158×36`）**，支持 `忽略(非按键)` 与 `恢复提醒`。

### Fixed
- 修复原版官方快捷键发生冲突时仅按钮变橙但左侧缺少 `[!]` 叹号徽章与悬浮冲突详情提示的问题。

---

## [1.4.3] - 2026-11-12

### Added
- 官方渠道深度防漏检（野 `ModConfig` 与 `GameOptionRegistry` 委托归属回收）、交互式「模组排查」子视图、全界面中英双语本地化（`I18n.cs`）及 `probablystolen.trainer` 合规适配。

---

## [1.4.2] - 2026-11-12

### Fixed
- 修复 `Meta_Button` 内边距吞字导致的四大核心按钮文字空白问题；修复整体搜索 `"yp"` 误命中 `KeyCode.Keypad6` 问题；消除「改造件」面板 `ConflictScanner` 双扫卡顿并完成全库热路径委托化。

---

## [1.4.1] - 2026-11-11

### Added / Fixed
- 打通 `SpeedUp` 与 `SmartInventoryArrange` 原生 `"Ctrl/Shift/Alt+Key"` 组合热键串双向读写；提取静态 `BindableKeyboardKeys` 表并加固 `Application.wantsToQuit` 生命周期边界。

---

## [1.4.0] - 2026-11-11

### Added
- **四大核心交互全链路流畅度重构（`PerformancePatches.cs`）**：实现「退出游戏秒退」、「改造件面板行对象池与 O(1) 依赖索引」、「全部展开单次字号拟合与帧预算预建」及「选项开窗空闲预热与离线装配」。
