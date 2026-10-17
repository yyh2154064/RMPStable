# RMP Stable source repository

## v0.4.4

修复附魔、变形等原版选牌界面缺失及交互中断；完整同步预览、取消和确认，并记住观战／控制选择。专项覆盖储君变牌、融合打击／防御、先古附魔、撕咬及潘多拉结果页，记录见 [v0.4.4 专项](docs/selections044.md)。以 v0.4.3 为基线；更早版本已由用户确认通过，不再默认重测。

默认 F8 打开单人或多人原生观战／控制窗口，按真实队伍切换来源；副本使用独立进程和原版界面，操作交回来源，经原版多人流程处理。保留 16 人扩容、快速 SL、窗口布局和来源选择，以及 v0.107.1 / v0.111.0 双版本加载。

使用方法见 [窗口说明](docs/README-multiInstance.md)。本轮只验证 v0.4.4 的选牌及模式记忆问题；v0.4.3 以前已验收，不默认重跑。Steam 跨电脑、更多人数和不同 DPI 等实际范围仍以各专项记录为准。

This repository stores the editable source code for RMP Stable. Compiled mod
artifacts are deliberately not kept under `RMPStable`.

- `RMPStable/src`: C# source and project file.
- `RMPStable-Bootstrap`: version-detecting loader that embeds both compiled implementations.
- `RMPStable/doc`: Godot source project for the icon and localization.
- `RMPStable/RMPStable.json`: source manifest used by the packager.
- `RMPStable-Packager`: double-clickable Windows release packager.
- `RMPStable-Packager/output`: generated release ZIP files.

On Windows, double-click `RMPStable-Packager/一键打包.cmd`. The packager builds
the version-aware DLL and PCK from source and creates a versioned ZIP
containing the `RMPStable` mod directory. The packager needs local game assembly
snapshots for both v0.107.1 and v0.111.0 (saved under its ignored `tools`
directory when each version is active). If Godot 4.5 is not installed, the first run
downloads and verifies the official portable Godot 4.5.1 build automatically.
