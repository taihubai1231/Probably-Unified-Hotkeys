# AI Agent 协作宪章与工程治理总纲 (AGENTS.md)

> **适用范围**：本协议为《深空当铺》（*Probably Stolen*，Steam AppID: `4348910`）原生核心模组 **「可能是按键统一优化 / Probably Unified Hotkeys」**（模组 ID: `probably_unified_hotkeys`，创意工坊 ID: `3837828444`，目标版本 `v1.5.0`）工程协作的最高行为宪章。所有进入本工作区的智能代理（AI Agents）必须无条件服从并严格执行本文件包含的所有组织拓扑、红线铁律与流程门禁。

---

## 第一章 组织拓扑与协作宪章

本项目采用 **“老板（Human User）— 幕僚长（Carbon Coordinator）— 四大扁平化职能部门（3.8Flash Specialists）”** 的扁平化协作拓扑。

```mermaid
flowchart TD
    Boss["👑 业务决策人 / 老板 (Human User)"]
    Carbon["🧠 幕僚长 / 总协调员 (Carbon Coordinator)"]
    
    subgraph Specialists["四大扁平化职能部门 (3.8Flash Specialists)"]
        direction LR
        Docs["📐 基建与规范部<br/>(infra-docs)"]
        Core["⚙️ 核心架构部<br/>(arch-core)"]
        UI["🎨 交互表现部<br/>(client-ui)"]
        QA["🛡️ 质检与发布部<br/>(qa-release)"]
    end

    Boss <-->|"战略决策 / 目标验收"| Carbon
    Carbon <-->|"自包含派发 / 聚合汇报"| Docs
    Carbon <-->|"自包含派发 / 聚合汇报"| Core
    Carbon <-->|"自包含派发 / 聚合汇报"| UI
    Carbon <-->|"自包含派发 / 聚合汇报"| QA
```

### 1.1 拓扑铁律与职权边界
1. **单层星型扁平拓扑（绝对禁止套娃派生）**：
   - 所有的职能代理（Specialists）均直接由幕僚长（Carbon）单层派发与回收。
   - **严禁子代理再派生子代理**：各职能专家严禁在自身任务中调用 `swarm add` 或类似工具衍生孙代代理；若任务过载，必须由幕僚长按阶段拆分为离散批次调度。
2. **零历史自包含派发（Self-Contained Task Brief）**：
   - 职能代理启动时默认处于无历史上下文（Clean-slate）状态。幕僚长向职能代理下发的 Brief 必须自包含完成任务所需的全部输入：明确的工作区路径、管辖文件清单、依赖前置、技术规范参考源以及验收完成标准。
3. **上下文防污染与汇报机制**：
   - 职能代理向留言板或幕僚长呈报进展时，执行**只读结构化摘要汇报**（禁止冗余复述完整文件、禁止闲聊打招呼、禁止无效确认回执）。
   - 跨阶段状态流转必须沉淀于 `docs/handoff/` 交接三件套（`map.md`、`risk.md`、`decision.md`），确保上下文干净、可追溯且零丢失。

---

## 第二章 工程红线与安全铁律（最高准则）

### 2.1 工作区管辖与防碰撞红线
1. **唯一合法写入工作区**：`D:\workspace\probably_unified_hotkeys_re` 为当前版本重构的唯一合法写入区。
2. **永久绝对禁写区（最高物理红线）**：
   - `D:\workspace\probably_unified_hotkeys`（v1 原版仓）
   - `D:\workspace\probably_unified_hotkeys_v2`（v2 规划仓）
   - **上述两个历史目录永久处于只读模式**：严禁执行任何写入、修改、重命名、移动或删除命令！任何工具调用若命中上述两路径的写操作，直接判定为严重工程事故并立即阻断。
3. **模块目录管辖边界（防并发冲突）**：
   - 各职能部门代理严格在分配的文件管辖范围内作业，严禁擅自修改属于其他部门的文件。
   - 任何代理不得触碰未经授权的基建配置文件（如 `.editorconfig`、`Directory.Build.*`、`.sln`、`.github/workflows/` 等）。

### 2.2 反垃圾化与高质量交付铁律
1. **严禁生成一次性临时垃圾脚本**：
   - 严禁手写生成 `temp.ps1`、`test.py`、`gen_doc.sh` 等一次性脚本来生成或修改代码及文档；所有文件必须通过标准文件读写工具（`write_file` / `edit_file`）直接生成与落盘。
2. **零占位代码铁律（Zero-TODO Policy）**：
   - 代码与文档中严禁遗留 `TODO`、`FIXME`、`// 待实现`、`/* placeholder */` 等任何敷衍性占位符；交付物必须是功能完备、逻辑闭环且经得起静态检查的成品。

---

## 第三章 游戏底层约束与零权限门禁

### 3.1 官方原生模组 SDK 契约
1. **非 BepInEx 环境**：
   - 《深空当铺》内置官方原生模组系统（`IMod`、`LoadedMod`、`ModLoader`，运行于 Unity 2021.3.45f2 Mono BleedingEdge 环境）。
   - 严禁假定或引入 BepInEx 依赖链，所有生命周期挂载与选项扩展均基于游戏官方提供的 `IMod` 接口与 Harmony 2.4.2。
2. **官方 2026-12-10 零权限生死线（Zero-Permission Standard）**：
   - 自 2026-12-10 起，未获官方白名单（`trust-list.json`）认证而声明 `files` 或 `network` 能力的模组将被官方加载器强行阻断（`Refused=True`）。
   - **默认发布包必须保持 0 权限纯净模式**：`src/ProbablyUnifiedHotkeys/ProbablyUnifiedHotkeys.csproj` 默认注入 `DisableDllKeyProbe=true`（定义 `DISABLE_DLL_KEY_PROBE`），`manifest.xml` 声明 `<Capabilities />` 为空，确保官方静态审计达成 **`0 Capabilities / 0 HardFlags / 0 Deprecated / Refused=False`**。
3. **零外部无命名空间反射红线**：
   - 严禁对无命名空间的外部第三方类（如 `probablystolen.trainer`、`RealTimeClock` 等）使用 `AccessTools` / `Type.GetType` 动态打补丁，否则会被官方扫描器识别为 `reflection-external` 并触发废弃拦截。此类模组统一通过官方只读数据通道接入。

---

## 第四章 提交与验证门禁（Definition of Done）

任何涉及代码修改或重大文档重构的分支/提交，在交付前必须按顺序通过以下流水线门禁：

1. **工程编译验证（亦可通过 `.\tools\check_mod.ps1` 一键验证）**：
   ```powershell
   dotnet build src/ProbablyUnifiedHotkeys/ProbablyUnifiedHotkeys.csproj -c Release
   ```
   要求：**Zero Errors, Zero Warnings**（开启 `<Nullable>enable</Nullable>` 与 `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` 严格模式）。
2. **.NET 8 官方静态合规扫描（禁止 PowerShell 直接加载）**：
   - **红线警告**：严禁在 PowerShell 5.1 进程中通过 `AssemblyResolve` 加载游戏 `Managed\` 目录，否则 Mono `mscorlib.dll` 进入 .NET Framework 4.8 会引发死递归 `StackOverflowException`（“无法创建新的堆栈防护页面”）。
   - **合规操作**：必须统一通过独立 .NET 8 工具 `tools/ScannerCheck` 进行扫描：
   ```powershell
   dotnet run --project .\tools\ScannerCheck -c Release
   ```
   扫描结果必须严格满足：
   - `Detected Capabilities`: `0`
   - `HardFlags`: `0`
   - `DeprecatedCapabilities`: `0`
   - `UsesDeprecatedReflection`: `False`
   - `Refused`: `False`
3. **部署目录 4 文件纯净度审计**：
   游戏部署目录 `Mods\probably_unified_hotkeys\` 内严格仅允许存在以下 4 个官方文件，禁止混入任何多余文件或目录：
   - `ProbablyUnifiedHotkeys.dll`
   - `manifest.xml`
   - `README.md`
   - `preview.png`
4. **VDF 工坊描述字节限制**：
   - `tools/steamcmd/probably_unified_hotkeys.vdf` 的 `description` 字段 UTF-8 编码体积必须严格控制在 **8,000 字节** 以内。

---

## 第五章 状态流转与交接规范

每次完成阶段性工作或发生核心技术决断变更时，必须同步更新以下治理资产：
1. `docs/handoff/map.md`：更新模块层级分布与代码归属图谱。
2. `docs/handoff/risk.md`：记录已知边界风险与防退化应对预案。
3. `docs/handoff/decision.md`：记录经幕僚长与老板批准的架构决策记录（ADR）。
4. `CHANGELOG.md`：遵循 Keep a Changelog 格式记录可观察变更。
