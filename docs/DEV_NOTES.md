# 可能是按键统一优化 / Probably Unified Hotkeys — 开发与归档手册

本手册沉淀《深空当铺 (Probably Stolen)》（AppID: `4348910`）模组 `probably_unified_hotkeys`（工坊 ID: `3837828444`）的工程结构、安全边界、UI 排坑记录，以及 GitHub 与 Steam 创意工坊发布 SOP，供后续版本维护与协同开发参考。

---

## 一、仓库与发布目录结构

- **源码仓库**：`D:\Workspace\probably_unified_hotkeys`
  - GitHub 远端：`git@github.com:taihubai1231/Probably-Unified-Hotkeys.git`
- **游戏部署目录**：`<GameDir>\Mods\probably_unified_hotkeys\`（默认 `<GameDir> = E:\Program Files (x86)\steam\steamapps\common\Probably Stolen`）

### 严格发布清单（游戏 `Mods\` 目录禁止混入任何多余文件）

`<GameDir>\Mods\probably_unified_hotkeys\` 内**只能**存在以下 4 个官方模组文件，任何源码（`.cs`）、脚本（`.ps1`）、文档（`docs/`）或临时产物均不得放入，否则会被 `ModScanner` 检出或污染工坊上传包：

1. `ProbablyUnifiedHotkeys.dll`
2. `manifest.xml`
3. `README.md`
4. `preview.png`

> `ProbablyUnifiedHotkeys.csproj` 已通过 `<DefaultItemExcludes>$(DefaultItemExcludes);tools\**;docs\**</DefaultItemExcludes>` 将 `tools/` 与 `docs/` 排除在主模组编译之外。

---

## 二、代码模块与 Harmony 补丁清单

### 1. 核心源码模块（14 个 `.cs` + 嵌入资源 `pinyin.bin`）

| 文件 | 行数 | 职责说明 |
|---|---:|---|
| `UnifiedOptionUI.cs` | 5,137 | 「选项」整页 UI 重构：顶部统一操作行（展示/排查筛选选项、仅看冲突、模组排查、全部展开）、折叠卡、拼音 A-Z 排序、只读探测行、美化置顶概况栏的「模组排查」与双语渲染 |
| `ModOptionScanner.cs` | 1,749 | 扫描原版 `GameOptionRegistry`、各模组 `ModConfig`（含非标准命名实例）与回调归属，并按模块开关受控归并 `DllKeyProbe` 只读探测结果 |
| `ModEntry.cs` | 1,313 | `IMod` 入口（`Init` / `OnEnable` / `OnDisable`）、退出钩子、核心 Harmony 补丁、`WorkshopModPatcher` 运行时适配 |
| `PerformancePatches.cs` | 1,166 | 4 项流畅度优化补丁（选项开窗异步/离线装配、字号缓存、改造件列表行复用、退出流程精简）与模块化动态开关受控逻辑 |
| `DllKeyProbe.cs` | 914 | 基于官方 `DseCecil.dll` 与 `ModData`/模组配置文件的只读热键探测器与增量指纹缓存（`dll_probe_cache_v1.txt`） |
| `ComboKeyStore.cs` | 697 | 组合键（Shift/Ctrl/Alt + 主键/鼠标、Mouse3~Mouse6）、单键修饰门控（受控于防误触开关）、「无」空键、强制强转与忽略非按键列表 |
| `KnownModAdapters.cs` | 688 | 第三方模组专项适配与强转桥接（受控于修改器桥接开关） |
| `ModModel.cs` | 417 | 统一数据模型、视图筛选枚举（`NormalDisplayMode` / `DiagnosticFilterMode`）与快照/默认值/只读探测状态管理 |
| `I18n.cs` | 292 | 中英双语词典、字体回退应用（`ApplyFontIfRuntime` / `ApplyFontToDropdown`）与语言选项接口 |
| `PinyinEngine.cs` | 282 | 基于嵌入资源 `pinyin.bin` 的离线汉字转拼音/首字母检索引擎（受控于拼音检索开关） |
| `ModFeatureConfig.cs` | 193 | 模组功能模块化配置中心：通过官方 `ModConfig` 注册 6 大功能开关（静态探测、流畅度优化、秒退、拼音、防误触、修改器桥接） |
| `ModCatalogCache.cs` | 159 | 主菜单闲置预热与配置目录缓存 |
| `ConflictDetector.cs` | 101 | 精准按键冲突分组（跳过「无」空键，可改键与只读探测键联动冲突排查） |
| `TradSimpTable.cs` | 73 | 繁简中文互转搜索对照表 |

### 2. Harmony 补丁点位索引

- **`ModEntry.cs`**：
  - `GameOptionUIManager`：选项界面接管与按键录入捕获
  - `ModConfigWindow`：`BuildEntry`、构造 / `Open`、`Apply`（配合 `ModConfigUiHelper`）
  - `KeyBindHelper`：单键与组合键适配
  - `WorkshopModPatcher`：启动阶段合规运行时适配（`Apply(workshopHarmony, log)`）
  - Unity 退出钩子：`Application.wantsToQuit` / `Application.quitting`
- **`PerformancePatches.cs`**：
  - `MainMenuUIController.OnExitClick`、`EscapeUIManager.OnExit`、`MainMenuUIController.Start`
  - `ModUIManager.PopulateLayoutList`、`RefreshRowIssues`、`GetModIssue`、`ApplyFilterToRows`、`ShowMod`、`OpenPanel`（`ConflictScope`）
  - `CustomUIWindow.Get(string)`

### 3. 数据持久化 Key 与缓存文件

- `PlayerPrefs`：
  - `probably_unified_hotkeys.combos.v1`：组合键、鼠标侧键与单键修饰状态
  - `probably_unified_hotkeys.forced_hotkeys.v1`：「模组排查」中显式强转为按键绑定的原始项
  - `probably_unified_hotkeys.ignored_candidates.v1`：「模组排查」中标记忽略的非按键候选项
- `ModData`：
  - `probably_unified_hotkeys/dll_probe_cache_v1.txt`：各模组 DLL / 配置文件大小与时间戳指纹缓存
- `ModConfig`（模组 ID `probably_unified_hotkeys`）：
  - `ui_language`：`auto`（跟随游戏语言）/ `zh`（简体中文）/ `en`（English）

---

## 三、官方红线与安全扫描边界（v1.4.4 路线 B）

综合官方 `ModdingDocs`（`CodeOfConduct.zh-CN.md`、`ModdingGuide.zh-CN.md`、`APIChanges.zh-CN.md`、`trust-list.json`）及工坊经验库 `3834708453`（`docs/06-发布与工坊.md`、`docs/10-官方红线清单.md`）：

1. **合规声明 `<Capability>files</Capability>` 与开发者认证机制**：
   - `manifest.xml` 严格声明 `<Capabilities><Capability>files</Capability></Capabilities>`（`<Prerequisites />` 为空），专用于 `DllKeyProbe.cs` 只读扫描其他已启用模组目录下的 DLL（交由游戏内置 `DseCecil.dll` 静态 IL 解析）与小型本地配置文件；**绝不写入或修改任何外部目录文件**。
   - **发版硬性门禁**：每次 `ModScanner.Scan` 必须满足：检测 `Capabilities` 与 `manifest.xml` 声明严格一致（仅 `[files]`），且 **`HardFlags = 0 / Pending = 0 / DeprecatedCapabilities = 0 / UsesDeprecatedCapability = False / UsesDeprecatedReflection = False / Refused = False`**。
   - 自 `2026-12-10` 起，创意工坊对声明 `<Capability>files</Capability>` 的新版本强制要求进入官方开发者认证白名单（`trust-list.json`），每次发版（或申请作者永久信任 `"*"`）前需按官方指引提交工坊认证申请。
2. **严禁触碰废弃能力与红线 API**：
   - **零外部反射**：严禁 `reflection-external`（已标记废弃并将自 `2026-12-10` 起直接 `Refused=True`），不得对无命名空间第三方类型（如 `probablystolen.trainer`、`RealTimeClock`）使用 `AccessTools` / `Type.GetType` 反射打补丁；这类模组继续通过只读官方数据通道与 `DseCecil` 静态 IL 探测接入。
   - 不得触碰 `[ModRestricted]` API：`ModLoader.ApproveAndEnable`、`SetModEnabled`、`ReorderMods`、`SetDeveloperMode`、`ForgetMod`、`ModTrust`、`ModWorkshop.Unsubscribe`、`Process.Start`、`Application.OpenURL`、`SaveFiles.*`、`Patch DirectoryMaster.Item`。
   - **禁止打包 `DseCecil.dll`**：`.csproj` 对 `DseCecil.dll` 固定设 `<Private>false</Private>`，运行时复用游戏 `Managed\DseCecil.dll`。

---

## 四、UI 排版排坑备忘（UGUI / TextMeshPro）

1. **方形折叠按钮被压扁**：
   - 游戏 `Meta_Button` 预制体自带 `HorizontalLayoutGroup.padding = (16, 16, 4, 4)`；制作 `36x36` 折叠箭头按钮时必须在 `StyleFoldButton` 中显式清零 `padding`。
2. **换行文本左侧缩进参差不齐**：
   - 官方 `CustomUIBuilder.AddSpace(pixels)` 仅设置 `preferredWidth` 而未锁死 `minWidth`（保持 `-1`），当同行的诊断文本触发自动换行并挤压宽度时，`HorizontalLayoutGroup` 会按比例压缩左侧占位 `LayoutElement`，导致首字左边距时宽时窄。
   - **固定写法**：使用 `AddFixedHorizontalSpace`（同时设 `minWidth = preferredWidth` 且 `flexibleWidth = 0`）+ `ConfigureWrappedDiagnosticLabel`（设 `minWidth = 0`、`preferredWidth = 0`、`flexibleWidth = 1`、`TextAlignmentOptions.MidlineLeft`）+ 行布局 `childForceExpandWidth = true`。
3. **非 16:9 显示器重置分辨率锁死**：
   - 恢复默认「显示」设置时，需根据当前显示器重算合法分辨率列表。

---

## 五、版本号规范与四端文案对齐清单

### 1. 三段式版本号命名规则（`Major.Minor.Patch`）

本模组统一采用三段式语义化版本号 **`主版本.次版本.修订号`**（例如 `1.4.3`、`1.4.4`）：
- `manifest.xml` 的 `<ModVersion>` 写纯数字格式（如 `1.4.4`）；
- `README.md` 更新日志与 Git Tag 加 `v` 前缀（如 `v1.4.4`）。

| 版本段 | 触发条件（何时递增） | 归零规则 | 本模组示例 |
|---|---|---|---|
| **主版本（Major / 大版本）**<br>`X.0.0` | **不向下兼容的破坏性变更或推翻级重构**：<br>1. 变更持久化数据键或数据结构且不再兼容旧配置（如调整 `combos.v1` / `forced_hotkeys.v1` 存储结构导致旧配置失效）；<br>2. 底层架构或核心交互模式推翻重做、跨世代重构。 | 次版本、修订号归零 | `1.4.4` → `2.0.0` |
| **次版本（Minor / 中/小版本）**<br>`X.Y.0` | **向下兼容地新增重要功能或特性模块**：<br>1. 新增独立核心功能或主要子视图（如新增整合中心、组合键体系、全链路性能重构等）；<br>2. 对现有体验做较大幅度扩展，但不破坏已有玩家配置。 | 修订号归零 | `1.4.4` → `1.5.0` |
| **修订号（Patch / 补丁版本）**<br>`X.Y.Z` | **向下兼容的 Bug 修复、局部完善与适配微调**：<br>1. 修复已知崩溃、UI 排版、搜索误命中或按键冲突判重问题；<br>2. 局部功能完善、性能优化微调、新增少量第三方模组适配；<br>3. 仅适配新游戏版本（更新 `<GameVersions>`）或补充双语文案。 | 不影响前两位 | `1.4.3` → `1.4.4` |

> **发布提示**：每次发版必须确保 `<ModVersion>` 严格递增；若本次更新引入了新的 `<Capabilities>` 声明，玩家客户端会触发重新「审核并启用」，建议在四端说明中写明权限用途。

### 2. 四端文案对齐清单

每次发布新版本前，确保以下四处版本号与功能边界描述一致：

1. `manifest.xml`：`<ModVersion>`、`<Version>`（当前 `0.46` / `0.46D`）与双语 `<Description>`
2. `README.md`：GitHub 技术说明与版本更新日志（`v1.1.0` ~ 当前版本）
3. `tools/steamcmd/probably_unified_hotkeys.vdf`：Steam 创意工坊 BBCode 简介与 `changenote`（UTF-8 编码后 **必须小于 8,000 字节**）
4. 游戏内 `I18n.cs` / `UnifiedOptionUI.cs`：双语 UI 文案与默认行为说明

---

## 六、发版与工坊同步 SOP

### 1. 本地预检（每次构建必跑）

```powershell
.\tools\check_mod.ps1
```

脚本依次执行：
1. `dotnet build ProbablyUnifiedHotkeys.csproj -c Release`（同步部署到 `<GameDir>\Mods\probably_unified_hotkeys\`）；
2. 校验 `<GameDir>\Mods\probably_unified_hotkeys\` 严格只包含 4 个官方文件、无多余子目录；
3. 校验 `tools/steamcmd/probably_unified_hotkeys.vdf` 的 `description` 小于 8,000 UTF-8 字节；
4. 构建并运行 `tools/ScannerCheck`，验证 `ModScanner.Scan` 零告警通过。

### 2. Steam 创意工坊发布流程（AppID `4348910` / Item `3837828444`）

> **关键坑点**：
> - `steamcmd` 会强制踢下线本地桌面的 Steam 客户端（`BLoggedOn=False`）；
> - `steamcmd` 读取 VDF 时**会忽略 `"language"` 字段**，只更新默认语言槽位（导致简体中文客户端看到的介绍仍是旧版）。
> - 多次连续提交元数据时，建议每次间隔约 **1 分钟**，避免触发 Steam Web / UGC 限流。

标准四步发布（见 `tools/publish_workshop.ps1`）：

1. **上传内容包 + 更新默认语言简介（`steamcmd`）**
   ```powershell
   .\tools\publish_workshop.ps1 -Step default
   ```
   （把仓库 `tools/steamcmd/probably_unified_hotkeys.vdf` 同步到 `E:\Program Files\steamcmd\probably_unified_hotkeys.vdf` 并交互登录 `steamcmd` 上传；提示需输入 Steam Guard 令牌，密码不落盘。）
2. **重新登录桌面端 Steam 客户端**（确认在线后再继续）。
3. **同步简体中文（`schinese`）标题与简介**
   ```powershell
   .\tools\publish_workshop.ps1 -Step schinese
   ```
   底层通过 `tools/SteamTagger` 调用 `SteamUGC.SetItemUpdateLanguage("schinese")` 写入简体中文槽位；若 Steam 后端偶发 `k_EResultFileNotFound`，会自动补带 `SetItemContent` + `SetItemPreview` 重试。
4. **（按需）恢复 11 个工坊分类标签 & 在线查验**
   ```powershell
   .\tools\publish_workshop.ps1 -Step tags
   .\tools\publish_workshop.ps1 -Step query
   ```
   固定保留的 11 个官方标签（大小写严格匹配工坊侧边栏）：
   `Mod`、`Quality of Life`、`QoL`、`UI`、`Utilities`、`Simplified Chinese`、`Compatibility`、`Utility`、`Utilities for Players`、`Mono`、`English`。

### 3. GitHub 归档发布

```powershell
git add -A
git status -s
git commit -m "chore: archive dev docs, publish scripts, and ModScanner tool"
git tag v1.x.x   # 如发正式新版本
git push origin main --tags
```
