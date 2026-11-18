# 贡献与协同开发指南 (CONTRIBUTING.md)

> 感谢参与 **可能是按键统一优化 / Probably Unified Hotkeys**（模组 ID: `probably_unified_hotkeys`，创意工坊 ID: `3837828444`）的开发与维护！无论你是人类开发者还是智能代理（AI Agent），在提交代码或发起 Pull Request 前请严格遵循以下工程约定。

---

## 1. 本地开发环境准备

1. **.NET SDK**：
   - 推荐安装 **.NET 8.0 SDK 及以上**，用于编译主模组（目标框架 `netstandard2.1`）以及运行 `tools/ScannerCheck` 与 `tools/SteamTagger`（目标框架 `net8.0`）。
2. **游戏本体与原生 SDK**：
   - 确保本地已安装《深空当铺》（*Probably Stolen*，Steam AppID: `4348910`，版本 `0.46` / `0.46D`）。
   - **非 BepInEx 环境**：游戏自带官方原生 Mod 加载器（`Assembly-CSharp.dll` 中的 `IMod`、`LoadedMod`），并集成了 `0Harmony.dll` (v2.4.2)。严禁引入任何 BepInEx 外部框架。
   - 若游戏未安装在默认路径 `E:\Program Files (x86)\steam\steamapps\common\Probably Stolen`，请设置环境变量：
     ```powershell
     $env:PROBABLY_STOLEN_DIR = "<你的游戏安装根目录>"
     ```
3. **工具链安全最高红线**：
   - 查看游戏类库签名或批量扫描工坊模组时，统一通过独立 .NET 8 工具 `tools/ScannerCheck` 执行；**绝对禁止在 Windows PowerShell 5.1 进程中通过 `AssemblyResolve` 反射加载游戏 `Managed\` 目录**（会因 Mono `mscorlib.dll` 引发死递归 `StackOverflowException`「无法创建新的堆栈防护页面」崩溃）。

---

## 2. 分支模型与提交规范 (Git Flow & Commits)

### 2.1 分支规范
- 严禁直接向 `main` 主分支提交未验证代码。
- 请基于最新 `main` 拉取语义化特性分支：
  - `feat/<short-name>`：新特性或第三方模组专项适配
  - `fix/<short-name>`：Bug 修复（需附带防退化说明）
  - `refactor/<short-name>`：分层重构与代码解耦（保持行为 100% 零退化）
  - `perf/<short-name>`：流畅度优化与帧预算节流
  - `docs/<short-name>`：文档、避坑指南与治理规范更新

### 2.2 约定式提交规范 (Conventional Commits)
提交信息必须遵循标准格式：`type(scope): subject`
- `type` 枚举：`feat` / `fix` / `refactor` / `perf` / `docs` / `test` / `chore` / `release`
- `scope` 示例：`core`, `client`, `adapter`, `patches`, `persistence`, `i18n`, `ui`
- 示例：
  - `refactor(client): extract UiSessionState from ModModel`
  - `fix(persistence): prevent KeyCode.None from returning residual modifiers`
  - `feat(adapter): add support for new workshop mod hotkeys`
  - `docs(pitfalls): record tmp input field layout priority pitfall`

---

## 3. 本地预检与发版对齐门禁

每次提交前，必须在本地运行四合一自动化预检脚本：

```powershell
.\tools\check_mod.ps1
```

预检脚本将严格校验以下 4 项指标：
1. `dotnet build src/ProbablyUnifiedHotkeys/ProbablyUnifiedHotkeys.csproj -c Release` 编译无 Error、无 Warning；
2. 游戏部署目录 `Mods\probably_unified_hotkeys\` 严格仅含 4 个合法文件（`ProbablyUnifiedHotkeys.dll`、`manifest.xml`、`README.md`、`preview.png`），零多余子目录或 `.pdb`；
3. `tools/steamcmd/probably_unified_hotkeys.vdf` 的 `description` 字段小于 8,000 UTF-8 字节；
4. 运行 `tools/ScannerCheck` 验证官方 `ModScanner.Scan` 达成：
   - `0 Capabilities`（默认 0 权限纯净模式）
   - `HardFlags = 0`
   - `DeprecatedCapabilities = 0`
   - `UsesDeprecatedReflection = False`
   - `Refused = False`

---

## 4. 架构铁律与编码规范

1. **单向分层拓扑**：严格遵循 `Diagnostics` -> `Config` -> `Core` -> `Persistence` -> `Adapter` -> `Patches` -> `Client`，严禁逆向调用。
2. **纯领域数据模型**：`src/Core/` 内的代码绝对禁止引用 `UnityEngine.UI`、`TMPro` 或任何 UI 控件句柄；所有运行时 UI 句柄集中归入 `src/Client/State/UiSessionState.cs`。
3. **Thin Hook 薄钩子与 SafeExecute**：Harmony 补丁仅做事件转发；所有跨程序集与高危调用强制使用 `Diagnostics.SafeExecution` 捕获隔离，彻底消灭空 `catch {}`。
4. **18 大避坑法则**：编写 UI 布局、改键录制与 Harmony 补丁前，必须通读并严格遵循 [`docs/PITFALLS.md`](docs/PITFALLS.md)。

---

## 5. 正式发布流程与四端文案对齐

若本次变更为正式版本发布（`release(vX.Y.Z)`），必须同步完成：
1. **四端文案对齐**：
   - `manifest.xml`（`<ModVersion>`、`<GameVersions>`、`<Description>`）
   - `README.md`（版本更新日志与特性矩阵）
   - `tools/steamcmd/probably_unified_hotkeys.vdf`（工坊 BBCode 说明与更新说明）
   - `src/Client/I18n/I18n.cs` / UI 内置双语文案
2. **Steam 创意工坊闭环发布**：
   严格按照 [`docs/RELEASE_SOP.md`](docs/RELEASE_SOP.md) 执行三步闭环发布（`default` 上传 -> 恢复桌面端 Steam 登录并 `query` 验真 -> `schinese -SkipCheck` 覆写不可注销的中文槽位）。
