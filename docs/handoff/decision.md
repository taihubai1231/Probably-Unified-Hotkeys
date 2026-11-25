# 架构决策记录 (docs/handoff/decision.md)

> **版本**：`v1.5.1`  
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

---

## ADR-008: 三端文案极简说明书风格、内外分层与用户友好更新口径
- **决策状态**：已拍板并强制执行
- **决策内容**：
  1. **极简说明书文风**：彻底清除对外玩家文案中的 AI 罐头词（如“一站式原生中心”、“全链路流畅度重构”等）与底层代码黑话（如 `SetActive`、`FittedFontSizeCache`、`RowPool` 等）；
  2. **内外分层准则**：`README.md` 彻底移除关于 `AGENTS.md`、AI 协作宪章、`handoff/` 等内部施工痕迹，保持专业开源项目门面；
  3. **三端差异化定位**：`manifest.xml` 定位为游戏内「10 秒极简速查卡」；工坊 VDF 维护纯中文极简说明书（< 8,000 字节）；`README.md` 维护通用开源主页并补充面向模组作者的配置对接说明；
  4. **用户获益视角更新说明**：`v1.5.0` 发版日志统一从玩家切身获益视角进行陈述（代码重写与独立异常隔离、0 权限纯净运行无警告、空键判定与快照读写稳定性）。
- **决策理由**：杜绝对外文案的人机味与工程浮夸之风，向玩家提供最清爽易读的实际使用指南，提升模组专业度与用户信任感。

---

## ADR-009: 四大高频按钮极致瞬开军令状与性能分摊架构
- **决策状态**：已拍板并立项锁定（退出游戏与改造件优化已落地，选项与全部展开于第 3 批执行）
- **决策背景**：实机测试中老板下达核心指令：退出游戏实测卡顿达 6.4 秒不可接受，且所有高频核心按钮必须杜绝任何百毫秒级卡顿，必须做到单帧或亚帧级极致顺滑。
- **决策内容**：
  1. **「退出游戏 (`Exit`)」按钮**（已落地）：
     - **指标**：彻底根治 6.4 秒卡顿，达成 **< 0.05 秒真·秒退**，同时 100% 确保配置安全落盘与 ModScanner 0 权限纯净。
     - **机制**：退出期由 `PrepareFastQuit()` 执行 `PrefsStorage.Save()`，随后将 `ModLoader.Mods` 的 `HarmonyInstance` 置空，阻断 52 个模组各自在 `OnDisable` 中重复调用 `UnpatchAll` 的连环重编译，主菜单与局内退出直接调用 `Application.Quit()`。
  2. **「改造件 (`Mods`)」按钮**（已落地）：
     - **指标**：首次点击与二次点击达成**单帧瞬开无感响应**。
     - **机制**：通过 `Patch_ModUIManager_Awake` 在当前场景创建 `ModUIManager` 时离线预热（`PrewarmPanelInstance`），提前完成窗口骨架构建、52 行对象池复用（`RowPool`）与冲突缓存预热，避免开窗帧遍历销毁/生成 52 个 Prefab 的布局风暴。
  3. **「选项 (`OpenUI`)」按钮**（第 3 批军令状）：
     - **指标**：首次开窗帧预算严格控制在 **< 16 ms（单帧内完成）**。
     - **机制**：
       - 主菜单/场景初始化后执行离屏骨架装配；
       - **模组折叠卡片按需懒构建（Lazy Content Build）**：首次开窗仅渲染原版 5 张核心卡片 + 39 张第三方模组折叠卡片头部（Header），各卡片内部改键行与控件在此阶段**完全不实例化 GameObject**；
       - 彻底将数千个 UI 控件的实例化与排版开销均匀分摊至玩家后续点击展开的交互时刻。
  4. **「全部展开 (`Expand All`)」按钮**（第 3 批军令状）：
     - **指标**：点击全部展开响应无冻结感（杜绝百毫秒级卡顿）。
     - **机制**：
       - 批量展开前临时禁用根 Canvas 渲染或挂起 Content 的 `VerticalLayoutGroup` / `ContentSizeFitter`；
       - 批量按需实例化剩余卡片内容，全面复用 `FittedFontSizeCache` 单次字号拟合缓存；
       - 批量生成完成后一次性恢复 Canvas 并触发单次 `LayoutRebuilder.ForceRebuildLayoutImmediate`，杜绝逐张展开引发的数十次回流风暴。
- **决策理由**：通过时序分摊、按需懒构建与布局回流合并，在 40+ 工坊模组的高压负载下彻底攻克 Unity UGUI 的性能瓶颈，打造行云流水般的工业级交互体验。

---

## ADR-010: 录键状态机鼠标按键分配与第 3 批交互契约
- **决策状态**：已拍板并强制锁定（作为第 3 批 `KeyCaptureController.cs` 的唯一执行标准）
- **决策背景**：实机测试中发现旧版将鼠标左/右键均作为“取消录入”，导致玩家无法将游戏官方默认且常用的鼠标右键（`Mouse1`）或组合键绑定至功能项。
- **决策内容**：
  在第 3 批 `Client/UI/KeyCaptureController.cs` 中严格执行以下按键录入状态机判定法则：
  1. **无修饰鼠标左键（`Mouse0`）**：作为唯一鼠标取消键，单击立即取消本次录入并还原原键位；
  2. **无修饰鼠标右键（`Mouse1`）**：**直接完成录入绑定为 `KeyCode.Mouse1`（鼠标右键）**，并置位鼠标释放等待标志（`_waitingMouseRelease = true`），严禁再将右键当作取消键；
  3. **修饰键 + 鼠标按键（如 `Alt + Mouse0` / `Ctrl + Mouse1` 等）**：正常绑定为鼠标组合键；
  4. **鼠标中键与侧键（`Mouse2 ~ Mouse6`）**：支持单键或组合键直接绑定（显示为“中键 / Middle Click”及“鼠标侧键 / Mouse4”等）；
  5. **`Esc` 键**：清空当前按键为「无」（`KeyCode.None`），并触发 Esc 三重吸收防止误关窗口。
- **决策理由**：与《深空当铺》游戏官方原生支持鼠标右键改键的交互直觉完全一致，同时释放右键作为核心功能热键的潜力，彻底纠正“右键误当取消键”的历史缺陷。

---

## ADR-011: 吸收维基百科 WP:AISIGNS 建立对外文案去 AI 化（去人机味）治理标准
- **决策状态**：已拍板并强制执行
- **决策背景**：智能代理在生成对外文案时，极易陷入大语言模型天然的统计缺陷与模板八股习气：堆砌浮夸程度副词（“彻底”、“深度”、“丝滑”）、公文与互联网黑话（“赋能”、“一站式”）、否定平行与虚假定义句（“不是…而是…”）、机械对称标题（“XX与YY”）、正文与列表中频繁塞入举例括号（“（如…）”）、千篇一律加粗列表模板，甚至将底层 Steam BBCode 代码直接倾倒给人类审阅者。老板亲自批改指出：对外文案必须干净平实、直入痛点、消除人机味。
- **决策内容**：
  1. **深度吸收维基规范**：全面拆解中文维基百科《Wikipedia:AI生成文的特征》（`WP:AISIGNS`），剖析均值回归、论证坍缩与过度防御等机理，建立全维度工程参考手册 [docs/ANTI_AI_WRITING_GUIDE.md](file:///d:/Workspace/probably_unified_hotkeys_re/docs/ANTI_AI_WRITING_GUIDE.md)；
  2. **固化工作区技能**：创建标准 Antigravity Skill [.agents/skills/anti-ai-writing/SKILL.md](file:///d:/Workspace/probably_unified_hotkeys_re/.agents/skills/anti-ai-writing/SKILL.md)，为后续所有 Agent 提供 7 步去人机味自动化执行清单；
  3. **宪章最高准则植入**：在 [AGENTS.md](file:///d:/Workspace/probably_unified_hotkeys_re/AGENTS.md) 第二章正式植入第 2.3 节「对外文案去 AI 化（去人机味）铁律」，确立零禁词、零伪深刻对比句、零机械加粗模板与交付分层门禁；
  4. **沉淀老板批改黄金案例文库**：收录拼音多音字去括号、组合键平实陈述、以玩家真实痛点开门见山、破除对称标题、审阅净版与 `.vdf` 底层标记分离等 5 大实战案例；
  5. **违规阻断门禁**：对外文案初审命中明显 AI 腔者一律直接退回重写，严禁合入版本库或提交人类审阅。
- **决策理由**：从根本上铲除工程协作中的人机味与虚浮之风，让所有面向玩家与社区的文字回归真人作者的真诚、简洁、准确与专业，保障用户信任感。

---

## ADR-012: v1.5.1 第三方配置委托异常隔离与 LoadedMod.Localization 主菜单兜底机制
- **决策状态**：已拍板并强制执行
- **决策背景**：实机测试发现工坊模组「物品堆叠增强」（Workshop ID: `3830647974`）等模组在 `ModConfig.Define` 中将配置项 `Label` 与 `Tooltip` 绑定为动态本地化委托，底层依赖 `OnGameLoadedLate`（存档载入）初始化语言表。当玩家在主菜单打开选项界面时，本地化未就绪导致 `table == null` 并在执行 `table.TryGetValue` 时抛出 `NullReferenceException`。旧版 `ModOptionScanner` 裸调委托导致异常穿透至最外层 `Patch_GameOptionUIManager_OpenUI.Prefix` 的 catch 块，直接返回 `true` 放行原版界面，造成「选项界面回退原版」故障。
- **决策内容**：
  1. **委托求值沙盒化与标记**：在 `ModOptionScanner` 中对所有第三方 `entry.Label`、`entry.Tooltip`、`entry.BoxedValue`、`entry.Choices` 实行受控调用，捕获异常后将 `ModCatalogCache.HasDegradedEntries` 标记为 `true` 并按模组限流记录日志；
  2. **多语言表静态回退**：当配置项标题或提示委托抛出异常或为空时，自动调用 `SectionLocalizer.TryResolveConfigLabelFromModLocalization` 与 `TryResolveConfigTooltipFromModLocalization` 从模组已解压的 `LoadedMod.Localization` 中提取中英文译文，仍未命中则安全回退为配置原始键名（`entryKey`）；
  3. **会话状态防假脏保护**：在 `UiSessionState` 的快照与脏状态检查中对第三方 Getter 施加确定性默认值防护，异常 Getter 不向外抛出异常且不误触发未修改假脏保存弹窗；
  4. **全生命周期自愈与最高优先级**：为 `Patch_GameOptionUIManager_OpenUI` 显式标注 `[HarmonyPriority(Priority.First)]` 确保接管权；新增 `Patch_ModHook_FireOnGameLoadedLate` 在局内存档加载完成后自动对降级条目触发 `ModCatalogCache.Invalidate()` 热刷新；
  5. **脱机回归测试锁定**：建立 `FaultIsolationAndItemStackingTests.cs`，对主菜单空引用零崩溃、双语解析、多模组隔离与缓存失效重置进行脱机回归验证。
- **决策理由**：第三方工坊模组的代码质量参差不齐且执行时序不可控。本模组作为基础优化中心，必须具备工业级的容错与自愈能力，绝不能因单个第三方模组的空引用异常而放弃自身界面的接管。

---

## ADR-013: v1.5.1 双端 7 组自由配置体系、三态显示位置与零权限卡片拖拽排序架构
- **决策状态**：已拍板并强制执行
- **决策背景**：随着模组功能增多，玩家对界面呈现形态与管理方式提出了更细致的要求：部分玩家希望将模组设置与原版选项合并，部分玩家希望在原版左下角设立独立按钮以免主界面冗长，部分玩家希望仅通过改造件右侧齿轮打开；同时，固定拼音首字母排序无法满足经常调整重点模组玩家的需求，亟需提供自由排序能力；且此前较为粗放的打包开关需要进一步解耦细化，使玩家能够独立掌控各项按键规则与加载优化。
- **决策内容**：
  1. **双端 7 大分区细粒度配置体系**：
     - 在官方 `ModConfig` 中划分 7 大分区（界面语言、选项页面布局、选项页顶部工具栏、快捷键绑定规则、改造件页面设置、预载及退出加速、调试日志）；
     - 将此前捆绑的优化与规则全面拆分为独立配置项（如允许绑定组合键、允许绑定鼠标右键中键侧键、允许绑定空键、冲突按键同时生效、改造件面板行对象池、本地封面缓存、退出加速等），所有选项均即时生效；
     - 齿轮页面与选项卡片双端同步，采用符合规范现代汉语书面语的自然文案与中英双语提示。
  2. **三态显示位置模式（`OptionDisplayLocation`）**：
     - `MergedInOptions`（模组界面合并至原版界面）：默认推荐模式，合并展示原版设置与创意工坊模组卡片；
     - `StandaloneBottomButton`（模组独立界面）：放行原版设置页面，在底部左侧插入「模组设置」按钮（原版「应用」按钮 180f 尺寸分毫不改），点击后打开纯模组设置中心，关闭时平稳切回原版选项页；
     - `GearOnly`（原版设置界面）：完全保持原版选项界面不变，仅在主菜单「改造件」齿轮中配置本模组。
  3. **零权限卡片多维排序与拖拽自由排布**：
     - 提供 6 种排序策略：按名称升序、按名称降序、自由拖动排列、最近启用优先、按键数量优先、游戏加载顺序；
     - 实现卡片右侧 `≡` 拖动手柄与动态插入线，支持上下平滑滚屏与 8px 防误触阈值；
     - 从 `ModConfig` 中彻底移除 `pin_own_card_to_top` 与 `mod_card_sort_mode` 两项冗余配置，卡片排序仅由列表标题栏右侧的排序下拉框、卡片拖拽与预设方案统一控制，并通过 PlayerPrefs（`custom_card_order.v1` 与 `card_sort_mode.v1`）安全持久化，零文件系统权限消耗；
     - 本模组卡片全面参与自由排序与拖拽，赋予玩家完全自主权；
  4. **卡片拖动 5 大防假脏状态机保护**：
     - 记录开窗基准排序与卡片顺序快照；
     - 拖动位置实际改变且未保存退出时弹出防丢确认框；
     - 拖动回原位置与基准完全一致时，自动还原排序模式并撤销脏标记；
     - 放弃修改时完整还原卡片排布；保存生效后即时刷新基准快照；
     - 单模组一键恢复默认时严格豁免卡片排序设置，避免误重置玩家整理好的顺序；
  5. **方案预设快照协议无损扩充**：
     - 在 `puh_profile_v1` 顶层增加可选 `sortMode` 与 `cardOrder` 字段，导入旧版预设时自动优雅缺省，保持无缝向下兼容。
- **决策理由**：在坚守官方零权限与原版 UI 尺寸保护的前提下，将界面布局、按键规则与排序选择权交由玩家自主决定，并建立严格的事务与脏状态状态机，避免误触未保存提示。
