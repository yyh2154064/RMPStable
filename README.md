# RMP Stable source repository

## v0.4.1

修复单人和多人窗口的地图浏览：捏奥及其他先古之民尚未选择时，可滚轮/拖动查看路线和 Boss，交互右侧原版图例，并用返回或 Esc 回到选择。观战模式无需操控授权，浏览不改变来源选择和地图旅行。独立地图专项见 [v0.4.1 测试记录](docs/map-browse041.md)。

沿用 v0.4.0 原生多实例窗口方案。默认 F8 打开单人或多人窗口，按真实队伍切换观战来源；游戏本体保留本人视角，窗口将所选角色放在全队立绘最前。经来源玩家授权后，可通过窗口操作战斗、队友药水、地图投票、个人/合作事件、商店、火堆、宝箱和奖励，并协助好友点击胜败结算的继续按钮。

切换来源、快速 SL、重置及同一游戏进程内再开一局均复用原生副本。修复地图按钮、玩家血条信息入口、遗物放大与放大镜缩放、设置及其他返回按钮、药水取消、商店金币不足反馈，以及战斗死亡和结算倒地绘图；补齐占卜、灯火钥匙及洁净回放同步。单人、多人 SL 后的新开局注能及连锁出牌恢复原版动画速度，隐藏历史追赶才使用加速。

沿用原有 16 人扩容、快速 SL 和 v0.107.1 / v0.111.0 双版本加载。此次多人窗口运行测试覆盖三名不同角色的原版本机 ENet；v0.107.1 为编译验证，Steam 跨电脑联机和更多人数窗口仍需验证。普通多人 SL 从进入游戏到窗口完整就绪实测 0.54～1.51 秒；72 条战斗历史的单次切换为 3.06～3.68 秒，三秒目标仍未全部达到。注能三张开局窗口先显示，再按原版速度播放约 3.5 秒。自动化结果保留实际候选，仍待用户手动验收。

使用方法见 [窗口说明](docs/README-multiInstance.md)，专项范围与性能限制见 [测试记录](docs/multiplayer-window19-validation.md)。

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
