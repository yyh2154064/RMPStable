# RMPStable 项目架构

每次按目标分支和实际游戏版本核对实现。[会话上下文](session-context.md)记录当前版本与验收，[组织与部署](project-deployment.md)记录构建产物和发布约定。

## 技术栈与加载

- Slay the Spire 2 游戏内 Mod。逻辑使用 C# 11、MSBuild/.NET SDK、Godot 4.5 C# API、Harmony 和反射。项目标记 `net7.0`，关闭隐式框架引用，使用 `GameManagedDir` 中对应游戏版本的托管程序集。分别编译 v0.107.1、v0.111.0、v0.113.0。后两者定义 `STS2_0111`，v0.113.0 同时定义 `STS2_0113`。入口见 [RMPStable.csproj](../RMPStable/src/RMPStable.csproj)。
- 游戏加载 Mod 清单、DLL 和 PCK。发行 DLL 是 [Bootstrap](../RMPStable-Bootstrap/ModEntry.cs)。它读游戏 `release_info.json`，加载匹配的嵌入程序集，注册 Godot 脚本并调用功能入口。未知版本由引导层拒绝。PCK 由 [Godot 资源工程](../RMPStable/doc/project.godot)导出，包含本地化、图标等。
- [功能入口](../RMPStable/src/RemoveMultiplayerPlayerLimit/Core/ModEntry.cs)初始化配置和模块，挂载 `RMPController` 与场景监听。模块通过 `IRMPModule` 接入。`Infrastructure` 提供场景注册、游戏状态访问、反射缓存与本地化。

## 模块与数据流

| 路径 | 职责 |
| --- | --- |
| [Core](../RMPStable/src/RemoveMultiplayerPlayerLimit/Core) | 初始化、配置、多人协议容量。当前大厅上限配置为 16。 |
| [Network](../RMPStable/src/RemoveMultiplayerPlayerLimit/Network) | Steam 大厅发现与联机引导、扩展大厅成员和准备状态、配置同步、快 SL 消息与实况数据包。改变消息布局时同步评估全队版本兼容。 |
| [Features](../RMPStable/src/RemoveMultiplayerPlayerLimit/Features) | `CampfireLayout`、`ShopLayout`、`TreasureRoom`、`VictoryFlow` 处理房间和结算，`QuickSl` 协调原版存档恢复和房主判定，`LiveSharing` 实现原生实况。 |
| [Platform](../RMPStable/src/RemoveMultiplayerPlayerLimit/Platform) | 平台检测和 macOS TLS 处理。原生窗口和本轮验证以 Windows 为基线。 |

实况由玩家本体启动同一游戏的独立副本进程，副本使用独立档案和原版场景。`MirrorProcess`、`MirrorResident`、`MirrorNativeWindow` 负责进程生命周期、复用与窗口挂接。`MirrorHost`、`MirrorProtocol` 和 `MirrorRenderer` 经同用户命名管道通信。`MirrorState`、`MirrorJournal` 与恢复逻辑用来源状态、检查点和语义操作重建对局。窗口浏览和临时呈现走原版控件，权威动作从副本回到来源本体执行，再同步结果。

多人由 `LiveSharingMultiplayer`、`MultiplayerMirrorSession`、`MirrorMultiplayerControl` 等将数据和操作路由到当前所选来源，经 `RmpMirrorPacket` 走原版联机通道。来源校验当前房间、权限及操作状态，涉及共享投票或快 SL 等房主决策时仍走原版房主流程。`MirrorNativeUi`、`Spectator*` 和 `Mirror*Input` 负责原版控件接入、窗口壳与输入转发。F8 管理本机窗口，F4/F5 按当前输入位置和模式处理。`liveSharingTest` 是另一个本地模拟方案，独立维护。

v0.4.6 从对局内打开实况时才创建副本。档案文件准备在后台执行，Win32 宿主和 Godot 窗口仍由游戏线程管理。已经创建的副本可在关闭窗口后继续同步，隐藏空闲时限帧。副本专用的动画、光标与输入挂钩只在副本初始化。v0.113.0 在 Mod 初始化期间登记嵌入程序集，参与原版消息映射。扩容协议携带 Standard、Blitz 难度和 Dread 轮换字段，原版大厅玩家包的席位字段扩为 4 位。开始对局后，房主忽略原版缓冲区迟到的大厅配置同步请求。商店回放使用原版购买 ID。

## 构建、运行与验证

v0.4.7 在离线或换局时清空实况订阅和控制状态。实况路由跳过离线对端，并处理 Steam 传输先于游戏名单移除连接的情况。健康副本仍可跨局复用。重新打开实况时若副本已退出或本机管道已失效，则回收原进程，按需启动并认证新副本，再以新订阅同步当前来源。

- 优先使用 [Build-MultiInstance.ps1](../tools/Build-MultiInstance.ps1)，传入已有 `-GameReferences`、`-ModPck`，新版引用可用 `-V0113References` 指向本机程序集目录。它编译三个功能程序集和引导 DLL，再组装文件夹。资源变更时用现有 Godot 工具按 [Build-Mod.ps1](../RMPStable-Packager/Build-Mod.ps1)的导入和导出步骤生成 PCK，工作和缓存路径按 F 盘约束安排。通用打包器仍使用仓库旁的工作路径，开发机用 F 盘构建入口。
- 手动运行将完整 `RMPStable` 文件夹放入游戏 `mods`，关闭游戏后替换，联机队员保持相同 Mod 和游戏版本。操作说明见 [README-multiInstance.md](README-multiInstance.md)。
- 隔离运行复用既有 runtime、profile seed、PowerShell 启动器与 `tests/` 中的 C# probe 结构。按本轮目标新增独立专项，常见入口为 `tools/Test-<场景>.ps1` 和 `tests/<场景>Probe/`。验收证据记录实际游戏版本、运行方式、结果和未覆盖条件。[开发资源](development-resources.md)中的旧测试命令供定位历史实现，日常入口以当前上下文和 `AGENTS.md` 为准。
- v0.4.5 专项实际运行 v0.111.0，v0.107.1 完成编译。多人专项使用原版 localhost ENet，Steam 跨电脑发现与重连、全屏和不同 DPI 的覆盖情况以实际证据为准。
