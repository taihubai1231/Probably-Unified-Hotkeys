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

### 1. 核心源码模块（12 个 `.cs` + 嵌入资源 `pinyin.bin`）

| 文件 | 行数 | 职责说明 |
|---|---:|---|
| `UnifiedOptionUI.cs` | 4,445 | 「选项」整页 UI 重构：搜索栏/作用域、折叠卡、拼音 A-Z 排序、「仅看冲突」、置顶反馈栏的「模组排查」、像素级对齐辅助与双语渲染 |
| `ModOptionScanner.cs` | 1,612 | 扫描原版 `GameOptionRegistry`、各模组 `ModConfig`（含非标准命名实例）与回调归属，推断快捷键/常规/候选原始项 |
| `ModEntry.cs` | 1,312 | `IMod` 入口（`Init` / `OnEnable` / `OnDisable`）、退出钩子、核心 Harmony 补丁、`WorkshopModPatcher` 运行时适配 |
| `PerformancePatches.cs` | 1,148 | 4 项流畅度优化补丁（选项开窗异步/离线装配、字号缓存、改造件列表行复用、退出流程精简） |
| `KnownModAdapters.cs` | 688 | 第三方模组专项适配与强转桥接 |
| `ComboKeyStore.cs` | 651 | 组合键（Shift/Ctrl/Alt + 主键/鼠标、Mouse3~Mouse6）、单键修饰门控、「无」空键、强制强转热键清单 |
| `ModModel.cs` | 396 | 统一数据模型与快照/默认值状态管理 |
| `I18n.cs` | 292 | 中英双语词典、字体回退应用（`ApplyFontIfRuntime` / `ApplyFontToDropdown`）与语言选项接口 |
| `PinyinEngine.cs` | 275 | 基于嵌入资源 `pinyin.bin` 的离线汉字转拼音/首字母检索引擎 |
| `ModCatalogCache.cs` | 159 | 主菜单闲置预热与配置目录缓存 |
| `ConflictDetector.cs` | 89 | 精准按键冲突分组（跳过「无」空键） |
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

### 3. 数据持久化 Key

- `PlayerPrefs`：
  - `probably_unified_hotkeys.combos.v1`：组合键、鼠标侧键与单键修饰状态
  - `probably_unified_hotkeys.forced_hotkeys.v1`：「模组排查」中显式强转为按键绑定的原始项
- `ModConfig`（模组 ID `probably_unified_hotkeys`）：
  - `ui_language`：`auto`（跟随游戏语言）/ `zh`（简体中文）/ `en`（English）

---

## 三、官方红线与安全扫描边界

综合官方 `ModdingDocs`（`CodeOfConduct.zh-CN.md`、`ModdingGuide.zh-CN.md`、`APIChanges.zh-CN.md`、`trust-list.json`）及工坊经验库 `3834708453`（`docs/06-发布与工坊.md`、`docs/10-官方红线清单.md`）：

1. **零权限声明与零告警通过**：
   - `manifest.xml` 保持 `<Capabilities />` 为空、`<Prerequisites />` 为空；
   - 发版前必须保证官方 `ModScanner.Scan` 输出 **`0 Capabilities / 0 Findings / 0 HardFlags / Refused=False`**。
2. **严禁越界 API**：
   - 不得直接调用 `System.IO` 读写磁盘文件（只读模组附带数据时统一使用官方 `ModHelper.ReadModDataFileText(...)`，配置保存统一走 `PlayerPrefs` / `ModConfig`）。
   - 不得触碰 `[ModRestricted]` API：`ModLoader.ApproveAndEnable`、`SetModEnabled`、`ReorderMods`、`SetDeveloperMode`、`ForgetMod`、`ModTrust`、`ModWorkshop.Unsubscribe`、`Process.Start`、`Application.OpenURL`、`SaveFiles.*`。
   - 不得 Patch `DirectoryMaster.Item`。
3. **无命名空间外挂的沙盒避让**：
   - `ModScanner` 会拦截对无命名空间第三方类型（如 `probablystolen.trainer`、`RealTimeClock`）的外部反射；
   - 这类模组不得添加基于反射的修饰键前置门控补丁，改以只读/官方数据通道（如 `ModHelper.ReadModDataFileText("hotkey-v1.txt")`、`"ProbablyStolenTrainerHost"`）接入，保持单键、鼠标侧键 `Mouse3~6` 与 `Esc` 清空可用。

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

## 五、四端文案对齐清单

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
