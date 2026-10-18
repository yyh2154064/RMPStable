# v0.4.5 已发布 Latest，等待手动验收（2026-10-19）

基线 **v0.4.4 已由用户明确验收通过**，原附魔／选牌／模式偏好问题移出活动测试和待验收。docs/selections044.md 与原脚本仅保留为历史证据，不再默认执行。新会话先读本文件与 AGENTS.md，核对实际 Git 和资源；不要仅因接收上下文重建或启动游戏。

用户本轮明确授权切换 main 开发、测试后提交推送 main、按往期名称及 Markdown 发布 **RMP Stable v0.4.5**，并更新原桌面上下文。本轮授权不能泛化为未来版本开发。

## 本轮范围

- 占卜三次／六次灰雾预览，默认关闭，F4 切换，账号／档案偏好保存，SL 和重启保留。16% 不透明灰雾，95% 原版明亮揭示边界，保留原版隐藏状态、鼠标命中、次数、奖励和随机数。修复本体直接 MouseReleased 揭格漏记同步。
- 单人／多人控制实况时 F4/F5 操作当前来源，支持好友；远程 F4 只改变选中来源。F5 在实况内确认／取消，来源客户端按原版向房主申请，来源房主执行原版全队 SL；切换来源／代次后旧确认失效。F8 只管理本机窗口。
- 实况地图右键绘画、中键擦除，本地立即调用原版绘图方法及场景；来源权威回声到齐后移除临时展示。保留每个鼠标移动点，使用当前视口和地图变换，跟随原版笔尖热点；绘画流与普通操作等待编号分离，避免画后 F4/F5 等回复错误。

始终是独立进程原版界面，只传状态与操作，来源权威、原版房主判定；临时地图笔迹不进入存档或权威模型，不混合本地模拟、画面传输或手绘 UI。

## 独立专项与证据

只运行本轮新入口：

- tools/Test-CrystalFog.ps1 -Scenario both -CheckRestart、tests/CrystalFogProbe：单人三次／六次、本体与实况占卜交互、奖励、偏好重启、F5 取消与真实存档恢复。
- tools/Test-CrystalFogMulti.ps1、tests/CrystalFogMultiProbe：三来源／三原生副本，三次／六次、双工具、交替揭格和奖励、来源隔离、观战拒绝、远程 F4、F5 客户端请求／房主拒绝和远程房主确认后全队 SL，F8 不转发。
- tools/Test-MapDrawing045.ps1 -Mode single|multi、tests/MapDrawing045Probe：右键绘画／中键擦除、故意扣留网络提交、三种尺寸和地图滚动、同帧原生笔迹和坐标精度、来源回声收敛、观战拒绝、绘画后 F4/F5。

复用启动／隔离／原版 ENet／输入／日志工具，不调用历史 full／feedback／replay／all 或阶段验收入口。均为可见 Windows 渲染窗口，静音，F 盘独立档案；没有操作当前游戏或真实存档。v0.111.0 实际运行，v0.107.1 编译，三次构建零警告错误。不称完整通关、Steam 跨电脑、全屏高 DPI 或系统物理鼠标全部组合已验证。多人 SL 仅替换 Steam 发现／重连边界为 localhost ENet，原版存档、开始／广播、退出、loaded-run 加入与自动准备实际执行。

最终 DLL：42BD66421F740CCEF94043F4880A71A5457D37492560E461E1E6CE3A3092ED05。

- 地图单人最终：F:/projectFile/RMPStable-development/evidence/multiplayer-map-drawing045-single-20261019-014555，18 项通过。
- 地图多人最终：F:/projectFile/RMPStable-development/evidence/multiplayer-map-drawing045-multi-20261019-014337，81 项通过，18 条轨迹／738 个指针位置，坐标最大误差 0.00003412 像素，处理输入到笔迹更新相差 0 帧；权威接替最大差 0.00001526 像素。扣留提交 8 帧仍已显示笔迹；来源回声 176～343 毫秒不阻塞本地落笔，不声称物理网络零延迟。
- 占卜单人最终：F:/projectFile/RMPStable-development/evidence/crystal-fog-20261019-015251，三次新档／同档重启／六次新档共 98 项通过；每次均含实际 F5 恢复。
- 占卜多人最终：F:/projectFile/RMPStable-development/evidence/multiplayer-crystal-fog-20261019-015750，三端 128／115／118，共 361 项通过，远程房主 F5 后原版三人存档恢复及全队状态／历史对照通过。
- 四组共 **558 项通过**，使用同一最终 DLL，三个来源退出 0、未被清理终止，副本全部退出，隔离档案清理。开发中真实失败与修正详见 docs/crystal-fog045.md、docs/map-drawing045.md，不混计为通过。

## 活动验收与交付

**唯一活动待手动验收：v0.4.5 本轮占卜／实况快捷键／地图绘画。** 自动化通过表示可交付，发布不等于用户手动验收。明确通过后移出活动清单，未通过按新反馈修复，未反馈保留专项脚本；已验收 v0.4.4 不再参与默认执行。

- 五文件直接可用目录：F:/projectFile/RMPStable-development/build/package/RMPStable。
- 附件：同级 RMPStable-v0.4.5.zip，顶层 RMPStable/，DLL、PCK、JSON、README-multiInstance.md、update.cmd，五文件哈希全部一致；2,607,539 字节，ZIP SHA-256：8E75240B1D656CD7C3F1401DEE2B94962EAEF63BAFA964863EE3905B2BBA275D。
- 版本统一为 0.4.5；发布文案 docs/release-v0.4.5.md 沿用 v0.4.4 标题／列表／安装段落／测试结尾格式。
- 已正式发布，详见下方发布核验；自动化通过和发布均不替代用户手动验收。

## 仓库与资源

唯一 checkout C:/Project_file/RMPStable-Github，当前明确授权的 main；仅 main／liveSharingTest／multiInstanceTest，无新分支、工作树、克隆或 detached HEAD。本轮不改其他测试分支。提交前基线 main 和 multiInstanceTest 为 659ea990748d351f74edf83d62c4179f9aad9d6f，liveSharingTest 为 6e3b367ee7e5391c83479a68905ead88f91abc0c；发布后查询实际本地／远端 SHA。

既有资源根 F:/projectFile/RMPStable-development：references/v0.107.1、v0.111.0、runtime、profile-seed、build、evidence。大输出均用 F 盘，不新增 C 盘工作树或运行目录，不替换当前游戏、真实存档或桌面旧模组文件夹。

## 发布核验

- 正式 Latest：[RMP Stable v0.4.5](https://github.com/yyh2154064/RMPStable/releases/tag/v0.4.5)，非草稿、非预发布，按 v0.4.4 标题和 Markdown 格式发布。
- 源码提交及 v0.4.5 标签：d048e62fe9942e8685226d8dff8f2bf3b4eb561e，已正常推送 main 和标签，无强推；随后文档提交只记录交付，不改变 DLL／附件／标签。
- 通过用户已登录的内置浏览器正常上传与发布；公开 GitHub API 已核对 Latest、标题、正文与 docs/release-v0.4.5.md 一致，附件名称／大小／SHA-256 与本地完全一致。
- 附件下载：https://github.com/yyh2154064/RMPStable/releases/download/v0.4.5/RMPStable-v0.4.5.zip。
- 核验记录 F:/projectFile/RMPStable-development/evidence/release-v045-published.json；发布截图同目录 release-v045-published.jpg。桌面原上下文随交付更新；未改桌面旧模组目录。
- multiInstanceTest 与 liveSharingTest 未改；各分支本地／远端以最终 Git 核对和桌面上下文为准。未来开发仍遵守 AGENTS.md，不继承本轮 main 授权。
