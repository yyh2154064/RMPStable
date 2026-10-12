# v0.4.1：先古之民选择前的地图浏览

用户反馈：捏奥尚未选择时，窗口地图不能正常滚动、交互右侧图例或点击返回；要求排查所有先古之民，并同时修复单人和多人。

根因位于窗口共用输入处理。顶部地图按钮已经是本地浏览，但地图滚轮/拖动仍走授权操作队列；多人操作协议没有这条滚动入口。地图返回和图例也没有纳入窗口本地按钮，返回后还可能被来源的地图开关状态覆盖。另外，原版 FromSerializable 不恢复 ActFloor，多人直接恢复非战斗房间绕过 EnterMapPointInternal 的 `row + 1` 初始化，导致地图把当前层数视作 0，禁用原版返回按钮。与具体先古之民的奖励实现无关。

修复统一在原版副本界面处理地图浏览：原版 ProcessScrollEvent 实现滚轮和左键拖动，原版图例保留悬停提示与节点高亮，原版返回和 Esc 更新窗口自身地图开关状态。多人进入非战斗房间前按原版地图坐标还原 ActFloor，不修改权威存档。无需来源操控授权；地图旅行/投票和绘图工具仍按原有权限与权威流程执行。窗口仍是独立进程原版场景，不传输画面，不绘制替代地图。

独立入口 `tools/Test-MapBrowse.ps1 -Mode single` / `-Mode multi`，探针 `tests/MapBrowseProbe`。复用既有原版启动、隔离档案、输入队列、日志工具；仅运行本轮地图检查，没有运行历史累积套件。单人由原版地图实际进入每位先古之民；多人通过三位不同角色的原版 localhost ENet，进入捏奥并逐一建立其他先古之民的原版事件房间。所有奖励保持未选择。六类图例逐一验证原版悬停、提示和类型高亮；同时检查查看 Boss、上下滚动、左键拖动、返回和 Esc、未选择时不能旅行、浏览不发权威请求且不改变完整对局状态。捏奥额外验证来源授权控制模式。

最终候选版本 **0.4.1**，双 payload 和 Bootstrap 均零警告/错误。DLL SHA-256：`2E7574FA2163BB464A0BA2AB7367AD5A8551B68E6B47F5879202024FD1CA570F`。

单人全部 8 位先古之民专项 **394 项通过**，证据 `F:/projectFile/RMPStable-development/evidence/multiplayer-map-browse-single-20261012-191630`，实际候选 `0289EB60E4B14FB2494E5C6DCC51691F7B98909BDF83459365DABB7212983E1B`。覆盖 Neow、Darv、Nonupeipe、Orobas、Pael、Tanx、Tezcatara、Vakuu；捏奥额外验证授权控制模式。之后只新增多人 ActFloor 恢复，单人修复逻辑未修改。

最终候选多人专项 **三端各 435，共 1305 项通过**，证据 `F:/projectFile/RMPStable-development/evidence/multiplayer-map-browse-multi-20261012-193310`，8 位先古之民全部覆盖，原版 ActFloor 与来源一致。最终候选单人 `-Mode single -NeowOnly` 补验 **88 项通过**，证据 `F:/projectFile/RMPStable-development/evidence/multiplayer-map-browse-single-20261012-193733`，覆盖捏奥观战及授权模式。两次最终运行均为 `2E7574FA...`，源进程全部正常 exit 0，未被清理强制终止；全部专项进程退出、隔离档案清理。

直接交付五文件目录 `F:/projectFile/RMPStable-development/build/package/RMPStable`，不含探针；ZIP `F:/projectFile/RMPStable-development/build/package/RMPStable-v0.4.1.zip` 的每个文件均与候选目录逐一 SHA-256 校验一致。用户授权推送测试分支并合入 main；未代为创建 Release/标签或替换当前游戏、桌面模组。

早期 352 项单人通过的运行使用 `53B600F586EFE2AA4CCADDB33891C6F15182EEAA717196C743B5B8F4E317012C`；其后整理版本文件并加强类型高亮及授权模式验证。初始化中的教程档案未生成捏奥、地图输入观察入口漏接、直接跳转未执行原版旅行关闭开关均属于 fixture 失败；修正 fixture 后，多人仍不能返回，进一步确认并修复上述 ActFloor 产品缺陷。没有把失败运行计入通过。

自动化通过可交付，仍等待用户手动验收。运行验证为 v0.111.0；v0.107.1 本轮仅编译验证。测试使用 F 盘既有隔离运行目录、隐藏父窗口和原版渲染；无系统鼠标/键盘操作，不替换当前游戏或真实存档。
