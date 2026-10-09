# 2026-10-09 开发资源清理与后续入口

本机与 GitHub 仅保留 `main`、`liveSharingTest`、`multiInstanceTest`。主仓库 `C:/Project_file/RMPStable-Github` 当前在既有 `multiInstanceTest` 开发；`main` 仍为 `ef59867edb864cb77bf66fd98f1fd4fe496afa5e`，本地模拟方案 `liveSharingTest` 仍为 `7227412795362e90d835549057cf86274dc07c9e`。不创建其他分支或 detached HEAD 开发，不混合两个方案。

## 已完成 C 盘清理

- 删除 `native-feedback` 开发工作树及注册。主仓库是本项目唯一剩余工作目录。
- 删除 408d、4971、796b、a3a5、d772 的空目录残余。
- 删除旧工作树内的重复构建、九份历史隔离测试档案、旧包备份与旧阶段脚本。
- 删除主仓库及子项目的 `.compat-work`、旧 bin/obj、Packager 下载工具/重复引用以及 output 内的历史 ZIP 包，保留 `.gitkeep`。
- 将必要引用、原版源码、已有隔离 runtime、最小测试 seed、.13 可用包和少量验收证据移到 `F:/projectFile/RMPStable-development`。删除桌面重复开发归档及其已重复的引用 ZIP、旧 .6 包、Git bundle。
- 未修改桌面可用 mod、正式游戏安装、真实存档、其他项目工作目录；`Timp` 的工作树不属于本项目。

C 盘可用空间从初查的 34,430,046,208 字节增加到清理完成时的 36,348,166,144 字节，约增加 1.8 GiB；该值是可用空间差，可能包含系统同期写入变化。逐项目录记录在 `F:/projectFile/RMPStable-development/cleanup-report.json`。

## 资源位置

| 内容 | 位置 |
| --- | --- |
| 唯一源码工作目录 | `C:/Project_file/RMPStable-Github` |
| 双版本引用 | `F:/projectFile/RMPStable-development/references` |
| 已有原版源码 | `F:/projectFile/RMPStable-development/original` |
| 已有独立运行环境 | `F:/projectFile/RMPStable-development/runtime` |
| 最小隔离档案种子 | `F:/projectFile/RMPStable-development/profile-seed` |
| 构建与唯一候选包 | `F:/projectFile/RMPStable-development/build/package/RMPStable` |
| 验收与故障证据 | `F:/projectFile/RMPStable-development/evidence` |
| .13 原方案可用包 | `F:/projectFile/RMPStable-development/package-liveSharing13` |
| 桌面交付文件夹 | `C:/Users/山岚/Desktop/RMPStable` |

旧桌面上下文里 `native-feedback/.tools/feedback`、桌面开发归档和旧 package-v7 路径已失效。不要重新创建那些 C 盘目录或执行旧清理脚本。源文件和历史提交由现有三个 Git 分支/GitHub 恢复；引用已迁移到 F 盘，不重复下载/解压。

`tools/Build-MultiInstance.ps1` 默认将输出写到上述 F 盘 build。传入既有 references 和桌面 `RMPStable.pck`，不会启动游戏或部署正式安装。`tools/Test-MultiInstance.ps1 -Mode feedback` / `-Mode full` / `-Mode replay` 复用 F 盘 runtime，独立 profile、静音、屏幕外运行、禁止 Steam，日志放 evidence。运行目录只能串行使用；脚本拒绝覆盖已有测试档案。测试完成后保留日志及必要截图，清理测试 profile，不持续积累档案。
