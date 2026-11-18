## 变更摘要 (Summary)

<!-- 请简述本 PR 解决的问题、重构的模块或新增的特性，并说明其设计思路 -->

- **关联任务 / Issue**：
- **涉及模块**：<!-- 如 Core / Client / Adapter / Patches / Persistence / Config / Diagnostics -->

---

## 变更类型 (Type of Change)

- [ ] `feat`: 新增功能 / 新工坊模组专项适配
- [ ] `fix`: Bug 修复（请在下方说明防退化措施）
- [ ] `refactor`: 架构拆分 / 代码重构（纯结构调整，行为 100% 保持一致）
- [ ] `perf`: 性能优化 / 帧预算节流 / 内存 GC 优化
- [ ] `docs`: 文档 / 避坑指南 / 治理规范更新
- [ ] `release`: 正式发版构建（包含四端文案对齐）

---

## 架构与安全强制自检清单 (Mandatory Checklist)

在发起 PR 前，请确保已对照以下工程红线逐项核验：

### 1. 架构与分层边界
- [ ] **纯领域模型铁律**：`src/Core/` 目录下绝对没有引用 `UnityEngine.UI`、`TMPro`、`GameObject` 或任何 UI 句柄；运行时 UI 控件已统一交由 `src/Client/State/UiSessionState.cs` 维护。
- [ ] **单向依赖拓扑**：本 PR 的改动严格遵守 `Diagnostics` -> `Config` -> `Core` -> `Persistence` -> `Adapter` -> `Patches` -> `Client` 单向分层，不存在逆向跨层引用。
- [ ] **Thin Hook 薄钩子**：若涉及 Harmony 补丁，补丁内部仅做事件转发与管道适配，高危调用全部由 `SafeExecution.SafeExecute` 隔离网保护，消灭了所有空 `catch {}`。

### 2. 避坑账本自检 (`docs/PITFALLS.md`)
- [ ] **重载显式匹配**：Harmony 拦截存在重载的方法时，已显式声明 `new Type[] { ... }` 签名。
- [ ] **UI 排版排坑**：折叠按钮已清零内边距；同行换行缩进使用 `AddFixedHorizontalSpace`；长文本 `TMP_InputField` 已设置 `layoutPriority = 100`。
- [ ] **未保存脏状态安全**：`KeyCode.None` 已阻断残留修饰位；按键解绑为「无」后点击应用已刷新 `BaselineSnapshot`，退出绝不误报未保存更改。
- [ ] **搜索边界安全**：快捷键搜索已使用 `MatchesKeyCodeSearchToken` 边界匹配，杜绝 `yp` 误命中 `Keypad6`。

### 3. 本地构建与预检门禁
- [ ] **本地 check_mod.ps1 验证通过**：
  - `dotnet build -c Release` 编译无 Error、无 Warning；
  - 游戏部署目录严格仅含 4 个官方文件（`ProbablyUnifiedHotkeys.dll`、`manifest.xml`、`README.md`、`preview.png`）；
  - `tools/steamcmd/probably_unified_hotkeys.vdf` 的 UTF-8 字节数严格小于 8,000 字节；
  - 运行 `tools/ScannerCheck` 确认 `0 Capabilities / 0 HardFlags / 0 Deprecated / Refused=False`。
- [ ] **默认 0 权限纯净模式**：`src/ProbablyUnifiedHotkeys/ProbablyUnifiedHotkeys.csproj` 默认启用 `DisableDllKeyProbe=true` 编译宏，`manifest.xml` 中 `<Capabilities />` 保持为空。

### 4. 文档与规范同步
- [ ] 若涉及对外功能、模块边界或持久化契约变动，已同步更新 `docs/ARCHITECTURE.md`、`docs/handoff/` 及 `CHANGELOG.md`。
- [ ] 本次 PR 的 `git diff` 高度聚焦单一任务，无无关代码格式化或跨界修改（No Scope Creep）。
