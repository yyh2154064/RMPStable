# v0.4.4 修复与专项完成，准备发布（2026-10-17）

本轮以 **v0.4.3** 为基线。用户明确确认 v0.4.3 以前的测试验证通过，移出活动待验收，不再默认重跑；历史细节留在 Git 和各版本文档。本轮明确授权修复后提交推送 GitHub main、仿照往期 Release 标题和 Markdown 发布 Latest，并更新原桌面上下文；发布完成后追加实际记录。本轮 main 授权不泛化到未来开发。

## 修复

- 遗忘之墓等 9 类原版附魔问号全部附魔选项，接入原版附魔选择、单张／多张、预览、取消和确认。
- 补齐原版变形和卡包 UI，保留实际过滤、选择数量、牌组顺序及专用变形映射；完整网格候选由来源校验执行，不依赖来源滚动或持牌控件。原版记录结果完成副本任务，观战不能选择。
- 覆盖储君 Begone（下去）、Charge（抽牌堆两张变牌）、Wish、Prepared、融合打击／防御、先古附魔、Claws→Maul（撕咬）、潘多拉结果确认及 ScrollBoxes 卡包。
- 单人／多人分别保存观战／控制偏好；收纳展开、临时权限和房间刷新不改写玩家选择。新代次清除旧就绪标记，场景就绪后重新发送来源有效租约，避免重建吃掉早到的授权。
- 修复随机变形预览模板被当作对局卡牌序列化、阻断后续同步的问题；只采集真实选择合同。潘多拉结果确认交回来源，再同步关闭；卡牌浏览本地执行。

仍只传状态和操作，来源权威，原版房主判定，独立进程原版界面；不混合本地模拟、画面传输或手绘 UI。

## 最终专项与活动验收

独立入口 **tools/Test-Selection044.ps1**、**tests/Selection044Probe**，只运行新 TestSelections，`-Part all|combat|events|ancients|extras|cardpack`；未调用历史 full / feedback / replay / all 或阶段验收入口。最终完整整轮证据：

`F:/projectFile/RMPStable-development/evidence/multiplayer-selection044-20261017-201858`

三端各 **438 项、共 1314 项通过**，最终 DLL 上全自动完成，无调试补发操作；三个来源退出 0、未被清理终止，全部隔离副本退出，专项档案清理。三来源／三副本原版 localhost ENet、实际 Windows 渲染、后台隐藏、静音，不操作系统鼠标、当前游戏或真实存档。v0.111.0 为运行验证，v0.107.1 为编译验证；不称完整通关、Steam 跨电脑或物理鼠标／DPI 已验证。真实失败调试记录见 **docs/selections044.md**，不混计最终通过数量。

实际程序集只读核对入口 **tools/Inspect-Selection044.ps1**。当前 v0.111.0 美丽手镯自动随机附魔 4 张×2，不弹选择器；旧 original 目录仅作参考。非战斗卡包眼睛原版禁用／隐藏。美丽手镯及潘多拉消耗共享 RunRng.Niche，专项顺序触发原版选择，不宣称并发共享随机验证。其余附魔／变形覆盖观战拒绝、预览、取消重选、确认及完整状态对照。

**唯一活动待验收：v0.4.4 本轮问题。** 自动化通过表示可交付，仍等待用户手动验收；明确通过后移出活动清单，未通过按新反馈继续修复，未反馈保留脚本及状态。新会话不能仅因接收上下文重新构建或启动游戏。

## 交付

- 直接可用五文件目录：`F:/projectFile/RMPStable-development/build/package/RMPStable`。
- 附件：同级 `RMPStable-v0.4.4.zip`，顶层 RMPStable/，内含 DLL、PCK、JSON、README-multiInstance.md、update.cmd，逐文件哈希一致。
- DLL SHA-256：`4FD6037637FDF5E41BDD6082AC7C77A25FB9441E0FF9B13CF46EB2C3D9C0E1F2`。
- ZIP SHA-256：`92B9CD147C2F804DBA000A7F74BA98CAF24EDF73F8E8B06899AF67D2A968C710`，2,598,049 字节。
- 双版本 payload / Bootstrap 为 0.4.4.0，构建零警告错误；清单、启动日志、更新器标识为 0.4.4。最终测试后只更新了包内 README 版本／安装说明和通过数量，DLL、PCK、JSON、更新器未改。
- 发布文案 `docs/release-v0.4.4.md`，沿用 v0.4.3 格式。未替换当前游戏、真实存档或桌面旧模组目录。

## 仓库与资源

唯一工作目录 `C:/Project_file/RMPStable-Github`，开发使用已有 multiInstanceTest；只保留 main / liveSharingTest / multiInstanceTest，发布后实际 SHA 以 Git 查询和桌面交接为准。不建立分支、工作树、克隆、C 盘游戏副本或重复资源。main 与 multiInstanceTest 发布前基线均为 848e5e267009198615f9727fff60fb4497d1fd80；liveSharingTest 保持 6e3b367ee7e5391c83479a68905ead88f91abc0c。

已批准的 F 盘开发根目录 `F:/projectFile/RMPStable-development`，复用 references（双版本引用）、original（旧参考源码）、runtime（隔离游戏）、profile-seed、build、evidence。原版 PCK 只读 `F:/SteamLibrary/steamapps/common/Slay the Spire 2/SlayTheSpire2.pck`。隐藏后台构建／隔离测试已授权，不逐次询问，仍遵守系统权限边界。
