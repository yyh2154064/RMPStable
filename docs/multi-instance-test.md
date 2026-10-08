# Native multi-instance exploration (.1)

本分支：`codex/multi-instance-spectator`。版本：`0.3.9-multiInstance.1`。
起点为主分支 `ef59867edb864cb77bf66fd98f1fd4fe496afa5e`，没有合并 `.13` 的提交。
`.13` 已单独提交并推送到 `codex/live-sharing-control`：`550fe00e55f40ea4ac5c7a0d71994c5b2a7b00d0`。

## 范围

这是单人、本机、原版战斗回放的探索版本，还不是 `.13` 的完整替代品。
保留 `.13` 的面板、标题栏、取消按钮、观战/控制切换、固定、拖动、缩放、四边收纳、F8 原版设置项和同一份配置文件。
仅复制上述外层及主进程的命令验证代码；未复制 `.13` 的卡牌、敌人、地图和介绍框重绘器。
面板内是一张独立游戏进程通过 Godot 原版 viewport 生成的纹理。第二个进程中使用原版 `NRun`、`NCombatRoom`、角色模型、敌人模型、卡牌、hover 和 `NTargetingArrow`。

首轮接入战斗初始状态、已发生事件、结束回合、普通指向攻击牌、战斗中途重新打开面板。
商店、奖励、事件、地图、地图画笔、弹出选牌和朋友远程传输尚未接入完整状态同步。
这些页面会显示暂停提示并拒绝控制。原版存档通常不能恢复当前弹窗或完整商店库存，不能依靠相同种子假装它们已同步。
当前只启用副本中的 RMP；其他 gameplay mod 的一致性尚未验证，不适合作为其通用复用方案。

## 状态和数据归属

主进程是唯一权威。战斗锚点和有序事件单向发往副本：原版 `CombatReplay` / `PacketWriter` / `PacketReader`。
副本通过原版 replay service 重建模型并执行 `GameAction`、hook、resume、player choice 事件。
后续批次必须保留已执行事件前缀。重新进入战斗使用新的 generation；重新打开面板使用新的随机 session 和密钥。

本机命名管道仅允许当前 Windows 用户连接。消息校验协议版本、随机 session、密钥、玩家 ID、连续序号；握手还校验子进程 PID、游戏及 payload MVID、原版模型序列化 hash。
上一代画面和状态不会进入当前对局。副本只返回画面、实际 UI 命中位置和校验状态，不能把自己的游戏模型写回主进程。

允许控制必须同时满足：副本与源均空闲、事件数量一致、原版 `NetFullCombatState` 的 SHA-256 一致；提交瞬间再计算主进程 hash。
画面控制还要求该画面的事件数量和 hash 与确认状态一致。点击命中使用副本实际卡牌/敌人/结束按钮位置，按手牌及 creature 的原版索引映射到主进程的临时标识。
最后由 `.13` 的源端验证 session、玩家、页面 context、权限 epoch、请求编号和目标合法性，再调用源端原版操作。
过期状态、不同 hash、断线和版本差异会阻止控制。鼠标 motion 和箭头提示只改变副本展示；不向副本发送原版点击/键盘操作。

`NetFullCombatState` 覆盖原版校验的生命值、能量、牌堆、力量、RNG、选择和奖励计数等；它不证明所有 UI 时序、动画、mod 自定义字段都一致。
实时回放批次尚未包含未来 enemy checksum 时，停用离线回放的提前报错比较，改用上述空闲点完整原版状态 hash 门禁；不修改权威进程的 checksum 行为。

## 进程隔离

每次创建全新的 `user://rmp-multi-instance/<random session>/Roaming` 和 `Local`。
子进程替换 `APPDATA` / `LOCALAPPDATA`，使用 `--force-steam off`，从不使用玩家账号的运行存档。
只读取源的非运行 `progress.save` / `prefs.save` 作为解锁和启动资料；不复制当前局、历史、Quick SL 或观战自动打开文件。
专用设置：跳过片头、启用 RMP、窗口模式、屏幕外位置、静音、30 FPS、BelowNormal 优先级。
副本角色禁止递归创建新观战进程；父进程消失或管道断开时退出。
关闭面板只终止该 session 创建的 Process 对象，不按游戏名字查找或结束其他进程。
会话目录保留用于诊断，本阶段尚未做自动保留数量限制。

PCK 启动参数不能从 `OS.GetCmdlineArgs()` 重建：Godot 已移除引擎参数。
正式目录从 executable 同目录定位 `.pck`；隔离测试通过 `RMP_MULTI_MAIN_PACK` 提供明确路径。
资源不存在时在启动前抛错，不创建子进程。第一次测试遗漏该路径产生了用户看到的弹窗，已经修正。

## 测试和限制

构建支持 `v0.107.1` 和 `v0.111.0`，两份 payload 和 bootstrap 均须通过编译。
`tests/MultiInstanceSmoke` 为仅开发测试使用的模组，绝不放进用户发布包。
测试入口同时要求 `RMP_MULTI_TEST=1` 和 `--force-steam off`，副本不会运行此入口。
隔离父/子进程使用 `--headless`，不启动影响当前联机的可见窗口、不覆盖安装中的模组、不写真实存档。

无界面测试能验证独立进程、原版模型和 UI 节点创建、状态及事件同步、命令门禁、原版命中位置和进程回收。
它不能验证 GPU 画面读取/传输/显示质量、原版 tooltip 的鼠标体验或帧率。
画面传输目前为 PNG / 命名管道，最多 15 FPS；游戏副本 30 FPS。此实现用于打通链路，不代表最终性能方案，后续应评估共享纹理。
独立游戏进程减少界面重绘适配，但仍需要针对原版 replay API、状态序列化和输入语义适配游戏更新。

本机测试记录保存在 `.tools/multi-instance/`，不上传游戏二进制或个人存档。
后续验证先维持隔离、静音和无界面约束；在 GPU 与非战斗页面验证完成前，不替换玩家正在使用的 `.13` 安装。

### 2026-10-08 已验证结果

- v0.107.1 / v0.111.0 两份 payload、bootstrap 和测试 harness：0 warnings / 0 errors。
- v0.111.0 隔离无界面父/子进程：`test-reconnect-v1.log`，27 项 PASS，`ALL PASSED`，退出码 0。
- 初始战斗、结束回合、敌方回合、下一回合手牌、指向攻击后的能量/伤害/手牌，以及战斗中途重开后的事件前缀均通过原版状态比较。
- 不同 session、玩家、消息序号、checkpoint generation 和状态 hash 的拒绝，以及关闭后主进程战斗继续，均通过验证。
- 两个会话的子进程日志没有 `Replay state diverged`。
- 测试和构建 DLL SHA-256 均为 `5BB24BB6ABC2FBBE3E26FF76A7CCC0B39DD0C02D74F5057DC96AFE7E6A4BC35C`。
- 模型与发牌动画的就绪时刻不同；测试等待实际命中位置及提交瞬间原版 hash 同时就绪，未通过修改数据或绕过门禁使其通过。
- GPU 帧读取、显示、tooltip/拖牌体感、非战斗页面及 v0.107.1 实际运行仍未验证。
