# RMPStable 新会话上下文：.12 已验收基线

更新：2026-10-11。用户已手动验收通过 `0.3.9-multiInstance.12` 及此前单人功能，单人开发基本完成。此前功能清单、旧 bug、逐版修复和回归记录已从本交接文档移除；没有未完成的单人修复或待验收项。后续只开展用户新提出的工作。

## 必须持续遵守

- 本机和 GitHub 只能有 `main`、`liveSharingTest`、`multiInstanceTest`，同名分支提交一致。不得另建分支/工作树，不以 detached HEAD 开发；`main` 不改名、不开发、不合并、不发布，除非用户特别授权。
- `liveSharingTest` 保留本地模拟方案 `0.3.9-liveSharing.13`；`multiInstanceTest` 是当前原生多实例方案 `.12`。后续多实例 `.13` 与旧本地模拟 `.13` 是不同方案，不能混合或自行互相合并。
- 在现有测试分支正常提交和推送，不强推、不改写历史。开始先核对分支、工作树、工作区和资源，保留用户未提交改动。
- 不额外占用 C 盘开发空间；复用唯一主仓库及 F 盘资源，不新增克隆、运行副本、重复引用、缓存和包备份。构建、测试档案及大文件输出放 F 盘。清理先核对精确目标和证据，不触及真实游戏、真实存档、主仓库或其他项目。
- 用户允许在既定 F 盘范围内后台构建、运行、隔离测试和必要清理，不逐次询问。保持长时间静默开发、尽量少打断；已审核事项不重复审核。只在修改既定范围外、从未批准的内容时请求审批；系统权限边界仍须遵守。后台启动隐藏窗口。

## 测试与用户验收规则（优先于旧说明）

1. `.12` 及此前功能已验收，默认正常；不再复跑累积断言。没有新问题时不运行测试。
2. 每轮针对用户新问题重新编写独立专项脚本，只覆盖当轮修复点。例如下一版修复两个点，就只测试两个点，不向旧脚本追加并跑全量。可以复用构建、启动、隔离和日志工具。
3. 不默认运行历史 `full`、`feedback`、`replay`、`all` 套件，也不将“三套全部通过”作为后续交付门槛。不自行扩展测试范围；确需扩展时先由用户提出或同意。
4. 专项自动化通过后交付直接可用的 mod 文件夹，说明本轮修复和实际验证范围。自动化通过不等于用户最终验收。
5. 交付后记录版本、专项脚本及“等待用户手动验收”。用户明确通过：从活动脚本/待验收清单移除这些项目，下一轮默认其正常。未通过：针对反馈继续修复；未反馈：保留专项脚本和待验收状态，不自动反复重测。
6. 历史脚本和证据仅供查阅，不参与默认执行；验收后不必删除 Git 历史。当前活动测试和待验收清单均为空。

## 当前版本、仓库与资源

GitHub：<https://github.com/yyh2154064/RMPStable>。唯一工作目录 `C:/Project_file/RMPStable-Github`，当前检出 `multiInstanceTest`；旧 `native-feedback` 工作树、桌面开发归档和 C 盘重复构建/测试资源已清理，不重建旧环境或执行旧阶段清理/分支脚本。

代码基线：`main` 为 `ef59867edb864cb77bf66fd98f1fd4fe496afa5e`；本地模拟 `.13` 为 `7227412795362e90d835549057cf86274dc07c9e`；原生多实例 `.12` 为 `a9ac81e2cf96e5b8a45a5ee5a5125c4ef32dc3c0`。这些是功能基线，后续规则/文档提交会改变测试分支 HEAD；实际 HEAD 以 Git 查询为准。

| 资源 | 实际位置 |
| --- | --- |
| 已交付四文件 mod | `C:/Users/山岚/Desktop/RMPStable` |
| F 盘开发根目录 | `F:/projectFile/RMPStable-development` |
| 双版本引用 / 原版反编译源码 | 根目录下 `references` / `original` |
| 已有隔离游戏 / 最小档案种子 | `runtime` / `profile-seed` |
| 构建和候选包 | `build` / `build/package/RMPStable` |
| 历史证据 / 旧本地模拟包 | `evidence` / `package-liveSharing13` |
| 原版 PCK（只读） | `F:/SteamLibrary/steamapps/common/Slay the Spire 2/SlayTheSpire2.pck` |

桌面 `.12` DLL SHA-256：`F57AF11C85632C53F3843D0A4D217BEB7B837CF7F55BA6915E25DB4FB187B488`。文件夹仅含 DLL、PCK、JSON、README，不含测试 harness；替换游戏 mods 后需重启加载。`.12` 隔离测试日志/截图留在 F 盘 evidence，三个测试 profile 已清理。v0.111.0 运行验收、v0.107.1 编译记录属于已完成历史，不需要重跑。

## 后续可能用到的架构与入口

多实例当前仅支持本机单人：本体是唯一权威，副本为独立 OS 进程中的原版界面；只传检查点、状态和原生操作。不要回退像素传输或手绘界面，也不能在同一进程建立第二个依赖原版单例的 `NRun`。操作保留权限、generation/epoch、事件数、hash、身份及合法性校验，副本不预测对局结果。独立 APPDATA/LOCALAPPDATA，测试只操作认证自有进程和隔离档案。

源码主要位于 `RMPStable/src/RemoveMultiplayerPlayerLimit/Features/LiveSharing`：

- `MirrorHost` / `MirrorJournal` / `MirrorState`：权威、检查点、操作与校验；`MirrorEnding`：原版最终结算。
- `MirrorRenderer` / `MirrorCardInput` / `MirrorKeyboard` / `MirrorPresentation` / `MirrorNativeUi`：输入和本地展示；`SpectatorCommands` / `SpectatorNativeUi`：本体执行与原版选择器。
- `MirrorProcess` / `MirrorProtocol` / `MirrorNativeWindow`：进程、通信、窗口；`LiveSharingController` / `SpectatorView`：生命周期及外壳；`Features/QuickSl`：SL。

`tools/Build-MultiInstance.ps1` 复用 F 盘 references/build，生成内嵌双版本 payload 的 bootstrap。后续专项脚本复用已有 runtime，串行运行、屏幕外、静音、禁用 Steam，不移动系统鼠标；输出证据到 F 盘，结束后清理专项档案。旧 `tests/MultiInstanceSmoke`、`tools/Test-MultiInstance.ps1` 和 `.tools/maintenance` 的全量入口属于 `.12` 历史，不直接作为下一轮验收入口，阶段交付脚本亦有固定版本/哈希假设。

多人、好友发现/远端传输、来源切换、断线恢复、完整本体呈现跟随尚未实现；仅在用户新提出时开展。呈现跟随讨论可查 `docs/native-mirror-presentation.md`，历史证据可查 `docs/multi-instance-test.md`。这些不是活动任务，也不恢复已验收需求清单。
