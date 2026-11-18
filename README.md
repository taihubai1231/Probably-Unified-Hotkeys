# 《深空当铺》模组 - 可能是按键统一优化 / Probably Unified Hotkeys

> 面向《深空当铺》（*Probably Stolen*，Steam AppID: `4348910`）的一站式原生统一快捷键管理与模组配置中心 —— 将原版与所有创意工坊模组的快捷键及常规设置无缝整合进官方 `ESC → 选项` 页面，全面重构「选项开窗 / 全部展开 / 改造件面板 / 退出游戏」四大核心交互流畅度，并提供多场景预设快照与未保存退出防丢保护。

- **模组作者**：太湖白1231
- **模组 ID**：`probably_unified_hotkeys`
- **当前版本**：`v1.5.0`
- **适用游戏版本**：《深空当铺》(*Probably Stolen*) `0.46` / `0.46D`
- **Steam 创意工坊**：[物品 ID `3837828444`](https://steamcommunity.com/sharedfiles/filedetails/?id=3837828444) ｜ **QQ 交流群**：`1127147529`
- **安全合规状态**：默认 **0 权限纯净模式**通过官方 `ModScanner` 静态字节码安全门禁（`0 Capabilities / 0 HardFlags / 0 Deprecated / Refused=False`，零外部反射，面向 2026-12-10 官方白名单准入标准）

---

## 核心特性

- **100% 原生嵌入与未保存退出防丢保护（v1.4.6 ~ v1.5.0）**：
  - 直接接管官方 `ESC → 选项` 窗口，上方保留 **「游戏原生设置与快捷键」**，下方按中文拼音首字母（A-Z）清晰收纳所有 **「创意工坊模组设置与快捷键」**；底栏保持官方原生绝对居中的单个 `180px` 「应用」按钮。
  - 修改任何原版或模组设置但未保存时，按 `Esc` 或点右上角 `X` 将弹出原生模态确认框（**保存并退出 / 放弃修改 / 取消**）；在按键录制状态（提示 `请按键... (Esc=无)`）下按 `Esc` 仅用于解绑为「无」，绝不误触发退出弹窗；解绑为「无」点击应用后刷新基准快照彻底复位脏标记。
- **`Shift / Ctrl / Alt` 组合键与鼠标侧键 (`Mouse3 ~ Mouse6`)**：
  - 支持多修饰组合键、鼠标左右中键组合及 `鼠标侧键1 ~ 4`。
  - 配备**智能防误触隔离（`enable_key_suppression`）**：按下组合键（如 `Ctrl + C`）时精确抑制原生单键 `C` 的响应；单独按单键 `C` 时绝不误触发组合键功能。
- **方案快照与剪贴板一键分享（Schema: `puh_profile_v1`）**：
  - 在「统一快捷键管理」独立折叠卡片中内置 **3 套本地场景预设槽位**（日常 / 搜刮 / 备用）与 **剪贴板紧凑 JSON 导出/导入**，支持一键备份、切换或跨设备分享全套原版/模组快捷键、常规设置及强转/忽略表。
- **三层根治式模组分区标题本地化引擎**：
  - 自动通过原生 `LoadedMod.Localization` 直查/反向对齐、工坊全生态词典与智能 Key 净化，彻底消除 `speed.config.*` 等代码键与未翻译英文标题。
- **四模式繁简/英文/拼音搜索与精确冲突排查**：
  - 内置 20,902 字离线拼音词库与 2,473 组繁简对照表，支持全拼、首字母缩写（如 `sp` 搜 `商品`）与键码词元边界检索（搜 `yp` 不误命中 `Keypad6`）。
  - 内置 **`[!] 仅看冲突`** 聚合视图，自动跳过 `无` 空键，精确标记冲突并支持在冲突视图内就地改键。
- **交互式「模组排查」与疑难设置接管**：
  - 自动找回非标准命名的“野 `ModConfig`”设置；支持对疑难 `string`/`int` 键位一键 **`设为按键绑定`** 或 **`忽略(非按键)`**；在改造件面板选中本模组时自动剔除大块封面以置顶展示说明。
- **全链路流畅度与极速秒退优化**：
  - 离线批量装配（`SetActive(false)`）、单次字号拟合缓存（`FittedFontSizeCache`）、改造件列表对象池复用（`RowPool`）、`ConflictScanner` 开窗双扫去重，以及安全落盘后的**极速秒退游戏**。
- **中英双语实时热切换**：
  - 支持 `跟随游戏语言`（默认）、`简体中文` 与 `English` 三档切换，无需重启即时刷新全部 UI。

---

## 前置依赖与安装

1. **游戏本体**：确保《深空当铺》（*Probably Stolen*）已更新至支持版本（`v0.46` / `v0.46D`）。
2. **前置加载器**：**无需安装任何外部加载器（如 BepInEx）**。本模组基于游戏**官方原生模组系统（`IMod`）**与内置的 `0Harmony 2.4.2` 构建，零外部依赖。
3. **安装方式**：
   - **方式 A（推荐 · Steam 创意工坊）**：在 Steam 创意工坊订阅 [可能是按键统一优化 (ID: 3837828444)](https://steamcommunity.com/sharedfiles/filedetails/?id=3837828444)，启动游戏后在主菜单 **「改造件 (Mods)」** 中勾选启用并重启游戏。
   - **方式 B（手动安装 / 开发者部署）**：下载最新 Release 压缩包（或在源码仓库运行 `.\tools\check_mod.ps1` 编译），将解压出的标准 4 个文件放入游戏目录 `Mods/probably_unified_hotkeys/` 文件夹中：
     - `ProbablyUnifiedHotkeys.dll`
     - `manifest.xml`
     - `README.md`
     - `preview.png`

---

## 配置文件与功能开关说明

本模组通过官方 `ModConfig`（模组 ID：`probably_unified_hotkeys`）管理配置，玩家可在 **「主菜单 → 改造件 → 本模组右侧齿轮」** 或 **「ESC → 选项 → 可能是按键统一优化卡片」** 双入口随时调整，修改后即时生效：

| 配置键 (Key) | 数据类型 | 默认值 | 说明 |
|---|---|---|---|
| `ui_language` | `string` | `"auto"` | 界面语言：`"auto"`（跟随游戏语言，非中文回退英文）/ `"zh"` / `"en"` |
| `enable_performance_patches` | `bool` | `true` | 是否开启界面流畅度深度优化补丁（离线装配、字号拟合缓存、改造件行池化、去重） |
| `enable_fast_quit` | `bool` | `true` | 是否开启退出游戏秒退流程（配置安全落盘后旁路冗余反注册与遥测超时） |
| `enable_pinyin_search` | `bool` | `true` | 是否开启 20,902 字离线拼音与繁简中文智能检索 |
| `enable_key_suppression` | `bool` | `true` | 是否开启组合键触发时的同主键裸键防误触隔离 |
| `enable_dll_probe` | `bool` | `false` | 静态热键探测开关（默认 0 权限纯净包中该项被编译宏剔除隐藏） |
| `enable_debug_logging` | `bool` | `false` | 是否开启详细调试诊断日志（在 Player.log 中输出详细追踪） |

> **持久化存储位置**：组合键修饰位（`combos.v1`）、强转表（`forced_hotkeys.v1`）、忽略表（`ignored_candidates.v1`）与 3 套预设方案快照（`profile_slot_json.v1.*`）保存在 Unity `PlayerPrefs` 命名空间下，详细 Schema 契约请查阅 [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)。

---

## 存档兼容性与卸载说明

- **中途加载**：**100% 兼容现有存档**。本模组仅接管设置 UI、按键映射与界面流畅度，绝不向《深空当铺》原版局内存档文件（`SaveFiles`）写入任何自定义数据，支持在任何新档或中后期存档中随时启用。
- **卸载提示**：随时在「改造件」菜单中停用或直接移除本模组文件夹，**对原版存档零破坏、零残留风险**。移除后，游戏将自动恢复使用原版默认的按键绑定逻辑。

---

## 开发者与工程治理指南

本项目采用标准化工程治理体系，参与开发或重构前请按顺序阅读：

1. [`AGENTS.md`](AGENTS.md)：AI Agent 协作宪章与红线铁律（星型扁平拓扑、零历史自包含派发、防碰撞与门禁）。
2. [`CONTRIBUTING.md`](CONTRIBUTING.md)：协作者开发环境、分支命名、约定式提交与四端文案对齐规范。
3. [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)：7 层架构拓扑蓝图、纯领域模型解耦（`UiSessionState`）与持久化契约。
4. [`docs/PITFALLS.md`](docs/PITFALLS.md)：18 大深坑全景账本（【现象】->【根因】->【解法】->【反模式】）。
5. [`docs/HARMONY_SPEC.md`](docs/HARMONY_SPEC.md)：内置 Harmony 2.4.2 + ModScanner 薄钩子解耦与反射安全规范。
6. [`docs/REGRESSION_CHECKLIST.md`](docs/REGRESSION_CHECKLIST.md)：9 大阶段实机全量回归验收清单。
7. [`docs/RELEASE_SOP.md`](docs/RELEASE_SOP.md)：语义化版本、静态门禁、打包契约与 SteamTagger 工坊发布 SOP。
8. [`docs/handoff/`](docs/handoff/)：跨阶段交接三件套（`map.md` 模块图谱、`risk.md` 拆解风险、`decision.md` 架构决策）。
9. [`CHANGELOG.md`](CHANGELOG.md)：语义化版本变更历史。

---

## 开源协议与致谢

- 本项目代码基于 **MIT License** 协议开源。
- 感谢《深空当铺》（*Probably Stolen*）官方开发团队提供的原生 Modding API、`DseCecil` 静态分析支持与详尽的 `ModdingDocs` 文档。
- 感谢创意工坊各位模组作者与交流群（QQ 群：`1127147529`）玩家的测试反馈。
