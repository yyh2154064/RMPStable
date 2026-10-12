# RMPStable 新会话上下文：v0.4.0；用户授权测试分支合入 main；历史切换三秒待改进

2026-10-12 最新交接（优先于下方所有历史）：用户明确授权更新桌面上下文、提交并推送 `multiInstanceTest`、合并到 `main`；并指定本次模组版本为 **v0.4.0**，模组介绍必须包含本次更新，更新脚本必须能在用户手动发布 v0.4.0 Release 后升级。未授权代为创建 Release/标签、替换当前游戏或桌面模组。Git 最终 HEAD 以实际查询和桌面上下文为准；后续开发仍回到现有 `multiInstanceTest`，不得自行在 main 开发或建立其他分支。

版本清单、双 payload/Bootstrap 的程序集版本及启动日志已统一为 0.4.0，模组菜单中文介绍、仓库首页、模组 README、安装说明及窗口 README 都已补充更新内容。复用 F 盘重新编译双版本和 Bootstrap，均零警告/错误。本次只有版本信息、说明、打包和更新器调整，没有继续修改 .19 最终游戏逻辑；未启动游戏或重跑旧累积套件，不能把 .19 实际候选的证据改写为新 DLL 的运行结果。

直接交付目录 `F:/projectFile/RMPStable-development/build/package/RMPStable` 现在为 **五文件**：DLL、PCK、JSON、README-multiInstance.md、update.cmd。v0.4.0 DLL SHA-256：**0F7F37F7346ACCF7CFE5A63BC5F6CE5BD4E7F6FF632DF372DD94300D09CCFD76**。Release 附件已在同级生成 `RMPStable-v0.4.0.zip`，不新增 C 盘构建资源。窗口专项脚本保留原名并将候选保护更新到该哈希，以便收到新反馈后再按授权运行；不默认启动测试。

更新器专项 `tools/Test-Updater040.ps1` 共 **9 项通过**，证据 `F:/projectFile/RMPStable-development/evidence/updater-v040/results.json`：实际旧 v0.3.9 脚本及新脚本从 0.3.9/0.3.9-multiInstance.19 升至真实 v0.4.0 附件的三个运行文件哈希一致；同版/本地较新/Latest较旧不覆盖，未发布附件、包版本与标签不符、缺文件失败不改动原文件。版本查找及下载网络被模拟，不宣称已验证尚未发布的 Release。新脚本校验包版本等于标签；新旧脚本均使用 `releases/latest` 和 `RMPStable-v0.4.0.zip`，main 清单仅为页面失败时备用。用户须自行创建非草稿、非预发布 v0.4.0 Release、设为 Latest、上传上述附件；不要使用 Source code ZIP。

自动化与性能状态维持：最终 .19 第四阶段 360 通过，前三阶段 480 功能/普通性能检查通过但 6 次长历史 strict3 失败，单次 3.06～3.68 秒（并发更高），不称所有四阶段性能目标通过。普通多人 Mod SL 在全员连接、进入游戏后完整就绪 0.54～1.51 秒；注能三张窗口显示 0.90～1.77 秒，再正常播放约 3.5 秒，稳定后验证完整状态。单人/多人动画修复实际候选与数量见下方和 `docs/opening-playback19.md`。原生全队角色、所选角色置前、来源授权、常驻副本、药水/导航/结算/事件专项均按真实候选保留。

用户本次版本及合并指示不自动等于所有功能手动验收通过。v0.4.0（原 .19）及此前 `.13～.18` 未明确验收项仍待用户反馈；`.12` 和此前已验收，不重跑。后台静默测试要求继续有效，不操作当前游戏、真实存档或系统鼠标。当前无需再运行任何游戏套件。只保留 main/liveSharingTest/multiInstanceTest 三分支，复用唯一仓库及批准 F 盘资源，架构仍只传状态/操作、原版权威与原版副本界面。

## 以下为 .19 开发过程与历史交接记录

2026-10-12 17:44 最终：本次注能SL动画修复已完成，单人2/3专项027635共32、多人2张53AC05共99、最终B30389多人强化3张 `...multiplayer-openingMulti3-20261012-174021` 共102通过（此前53AC05的3张99保留历史），均原版正常速度、场景先就绪、完整状态与同PID。最终3张基准3502～3525ms；SL本体3502～3531/窗口3479～3535；窗口显示1771/900/1126ms后再正常播放，原生绘制恢复/fastReplica=false。全部测试正常退出、隔离profile清理。本次单人实际专项候选与最终不同但单人修复代码未再修改；不虚称所有旧专项在最终DLL上复测。

交付复用既有四文件文件夹 `F:/projectFile/RMPStable-development/build/package/RMPStable`，版本0.3.9-multiInstance.19，DLL **B30389FF1B44782389500375317B8501F5976822F57D4C0613BEE965BC91C194**，双版本/bootstrap零警告错误。未安装到当前游戏/桌面、未碰真实存档、未提交/推送；本机三分支与本地origin引用仍分别main ef59867 / liveSharingTest 6e3b367 / multiInstanceTest 4498ee7，一个现有checkout。自动化通过仅可交付，`.19`仍待用户手动验收，`.13～.18`此前待验收不自动变通过，`.12`及此前已验收不重跑。

四阶段功能当前：B30389第四阶段独立360通过；前三阶段逐端 `...battle-switch19-20261012-173600` 480通过/6性能失败，functionalPassed=true、performancePassed/passed=false、exit1。所有6次历史切换完整同步、身份/权限/原生绘制/exact PID正确，耗时3062～3683ms，**strict3未全部达标、并发整轮未全通过**，保留独立性能脚本为待改进/未验收，不能声称全部目标完成。普通SL进入游戏后完整状态535～1514ms，强化3张SL显示900～1771ms后正常播放。无收益CustomStep及64条batch均撤回；最终隐藏MP重建使用原版Instant/FPS120/省去中间GPU绘制，结束或异常restore，NormalOpening不加速、不省绘制。所有当前测试后台静默。后续以本段优先于下方历史“运行中”。

2026-10-12 17:42：B30389 / `...multiplayer-battle-switch19-20261012-173600` **三端functionalPassed=true，performancePassed/passed=false，exit1**。各162检查中各160功能/普通性能断言通过、各2长历史strict3失败，共480通过/6失败。全部6次72条历史来源切换保持完整hash/三原版gameplay、实际身份/3槽位、原生绘制与正常动画恢复、旧权限撤销、自身授权、exact PID：me3683/3171ms，A3280/3062，B3203/3627。没有把性能失败当整体通过。SL按用户口径完整状态就绪612/535/1514ms；NativeBegin4345/4199/5130ms单独记录不计入3s。第四阶段同B30389已360通过，前四阶段功能可独立核对，原并发整轮未通过/历史strict3仍待改进，不称所有目标完成。

最终候选强化3張动画专项 `tools/Test-OpeningPlayback19.ps1 -Mode multi` / `...multiplayer-openingMulti3-20261012-174021` 正在后台静默运行，新增rendering恢复/fastReplica=false检查，尚未结果。候选仍B30389，构建已零警告错误。README写明当前历史3.06~3.68s与SL0.54~1.51s实测限制；最终包README待同步。未交付/提交/推送，注能单人32/多人2張99/旧3張99等实际候选记录保留，未重新跑旧全量。

2026-10-12 17:37：0CDC54 / `...battle-switch19-20261012-172643` 首端152后历史切换3891ms，64条batch没有实测收益，已撤回。双版本/bootstrap零警告错误重建，恢复与第四阶段360通过相同的 **B30389FF1B44782389500375317B8501F5976822F57D4C0613BEE965BC91C194**。最终新入口 `...multiplayer-battle-switch19-20261012-173600` 正在后台静默运行：逐端完成六次历史切换，仍以3000ms记录passed:false的性能失败，但不提前终止其余功能检查；若有性能失败，最终passed/performancePassed=false及exit1，functionalPassed只有全部身份、完整状态、权限、同PID和正常绘制恢复通过才true。原fourStages仍是原并发strict3失败即停，没有伪装全通过。当前还没有该结果，未交付/提交/推送。

注能SL本次需求独立说明见 `docs/opening-playback19.md`（32单人、99多人2张、99多人3张，实际候选分别027635/53AC05）；核心修复是Source标记不再加速本体，NormalOpening不加速新开局。新B30389只有隐藏MP加载drawLoop减少中间帧，显示前及finally恢复，NormalOpening保留绘制。需最后强化3张专项补确认该恢复设置（增加原生rendering/fastReplica检查），不默认复跑历史已通过套件。

2026-10-12 17:26：**第四阶段独立专项通过**，`tools/Test-MultiplayerRooms19.ps1` / `...multiplayer-rooms19-20261012-172235`，实际B30389候选三端各120=360，exit0，隔离profile已清理。商店真实个人库存/购买卡、遗物、药水/重复请求/删牌预览取消与确认；旧代次和旧权限拒绝；火堆全队forefront/升级/治疗；宝箱；个人及共享事件；地图1/2票等待3票推进且撤权；原版起手稳定、实际战斗结束与奖励Continue；事件卡奖励领取/跳过；终局战斗金币与Continue地图；每步完整来源hash及三原版gameplay。上一171209因fixture提前离开起手中的实际地图战斗而原版checksum3分歧，现等原版稳定及结束/奖励Continue后才进入下一测试事件，没有关闭原版校验。

B30389逐端历史切换 `...battle-switch19-20261012-171724` 首端3807ms仍未3s，其他端未执行最后历史切换，不能宣称通过。新候选 `0CDC5457F43E420562B588797D4E2E9525401B1D4E6E59D7C593C2B18AE460D5` 双版本/bootstrap零警告错误：隐藏历史native队列由每8条yield改64条，原版PlayCardAction在Execute时才解析Card，Hook/Choice/Resume原生缓冲未来记录；保持源顺序/跨阶段等待及最后原生执行器完整完成，不改普通live/newopening节奏。当前逐端strict3专项待跑；此前注能230、第四阶段360等保留实际候选。未交付/提交/推送。

2026-10-12 17:14：当前候选 `B30389FF1B44782389500375317B8501F5976822F57D4C0613BEE965BC91C194`，双版本/bootstrap零警告错误。仅多人隐藏历史加载暂时关闭RenderLoopEnabled（原生_scene树/动画/动作/选择/Hook照常处理）；FadeIn前及finally恢复之前绘制设置，正常SL新开局不关闭、不加速。性能待测，未称3s达标。

第四阶段 `...rooms19-20261012-170856` 旧商店消息计数仍false，进一步确认并非产品漏拒：ForwardControlIntent在本地就丢弃旧LocalGeneration，来源本来不会收到、不会增加拒绝计数。fixture现分别核对旧本地代次不执行，以及使用当前传输代次但保留原商店Epoch/Context/Action送到来源后明确拒绝；未改产品权限规则。`...multiplayer-rooms19-20261012-171209` 在B303上商店/删牌/休息点front/旧上下文拒绝已通过，原版升级测试中，尚未整段结果。新的候选NormalOpening路径与已通过230注能需求代码一致，但不虚称旧测试为B303重测；未交付/提交/推送。

2026-10-12 17:06：056004 / `...multiplayer-battle-switch19-20261012-165927` 三端各152后，72条历史三端同时切换4784/4690/4618ms，仍未严格3s。Tween CustomStep未见收益已撤回，双版本/bootstrap重建零警告错误，候选恢复为与注能2/3专项已通过完全相同的 `53AC05E49649F1C13C2AE58C4B79F809D6E54BD5F6CBA9C42330542F141B6508`，没有保留该实验。当前battleSwitch19入口改为逐端完成最后两次历史来源切换，每端仍严格<=3000/完整源hash/exact PID，区分常规单次切换与三副本同时重建的资源竞争，不称并发3s已达成。四Stages入口仍保留原并发行为/严格断言。

056004独立第四阶段 `...165701` 商店等通过后休息点一端false：保存的三端最终原生restFront均true，测试读取到旧商店快照；只改fixture等待当前RestSite实际forefront采样。隐藏加载期间观察器不执行昂贵UI/完整快照采样，生产同步/计时不暂停，待sceneReady再核对全部状态。后续rooms19及逐端battleSwitch待测试；未交付/提交/推送。

2026-10-12 16:58：53AC05四阶段 `...163952` 三端各152后，战斗72条历史来源切换4265/4813/5012ms，仍未严格3s通过，第四阶段未执行。独立第四阶段 `tools/Test-MultiplayerRooms19.ps1` / `...multiplayer-rooms19-20261012-165037` 三端各19后在药水购买Sync失败；来源日志显示 fixture以silent:true清除旧药水，却留下原生药水栏旧holder，导致原版NPotionContainer.AnimatePotion报Sequence contains no matching element。已改正常DiscardPotionInternal事件通知，不修改产品采购/同步规则。

候选 `0560043F3E9AC146707782AE875FCC2E9B799493D89486A58BECC9FF6D80FAAD` 双版本/bootstrap零警告错误：仅隐藏副本历史重建逐帧CustomStep原生运行Tween，经原生回调推进，不改引擎时钟/后续速度，正常开局及来源本体均不进入该路径。性能尚未复测，不称3s达标。此前注能SL 230检查通过保留；未交付/提交/推送。此段优先于下方旧记录。

2026-10-12 16:40 新增注能SL动画需求当前已通过专项：单人027635候选32；多人53AC05强化3张 `...163429`99，未强化2张 `...multiplayer-openingMulti2-20261012-163657`99，合计230检查。multi2基准2489~2507ms，SL本体2527~2587/窗口2536~2572；窗口显示1611/849/850ms，exact PID与完整状态保持。原版首回合自动效果正常可见，耗时包含Catastrophe自身及连锁2/3张，未压成Instant；隐藏历史追赶仍单独加速。自动化通过不等于用户验收，当前未交付/提交/推送。

正在后台静默运行53AC05 `tools/Test-MultiplayerFourStages19.ps1` / `...multiplayer-four-stages19-20261012-163952`，补完此前battle历史3s与第四阶段。此前候选战斗来源切换均未严格3s通过；当前新候选尚待该结果，不能宣称四阶段完成。README已补新开局正常动画说明，包README待最终交付前同步。此段优先于旧记录。

2026-10-12 16:37 当前53AC05候选多人强化三张专项 `...multiplayer-openingMulti3-20261012-163429` **三端各33=99通过、exit0**：原版实际房主检查点，经原生loaded lobby分发到三名原版角色；本体及所选角色副本自动播放Catastrophe+3卡，正常速度、开始前scene可见、完整来源hash/三原版gameplay一致，SL exact PID复用。进入原版游戏后窗口显示1087/1742/1717ms，原版BeginRun另记5193/5830/5794ms，未把播放完毕/settled hash伪装成动画前就绪。窗口显示后按正常速度播放完毕，再Sync完整状态。上一 `...163209` 的client失败是fixture误以为原版每客户端都写多人存档；只改为房主实际保存数据供原版loaded lobby使用，未改产品存档或同步规则。

当前 `tools/Test-OpeningPlayback19.ps1 -Mode multi -Unupgraded`（openingMulti2）后台静默运行，尚未结果；单人32已在027635上通过、未虚称53AC重测。仍需完成此前battle来源3s与第四阶段，未交付/提交/推送。

2026-10-12 16:30 新需求单人 `...multiplayer-openingSingle-20261012-162150` / 027635候选 **32检查通过、exit0**：真实mod私有RunSingleplayerSlAsync，原版Catastrophe注能2/3自动播放，source/replica可见、正常动画、完整状态Verified、exact PID；基准2张2338ms，SL本体2433/窗口2537；3张基准3364，本体3350/窗口3470。trace尾部两条fast隐藏记录发生专项结束清理时的副本重建，非可见SL段；新fixture结束前标记cleanup，避免把关闭进程的尾部归为播放段。未虚称最新DLL重新测试。

多人 `...multiplayer-openingMulti3-20261012-162338` **未通过**：正常3张已执行但副本sceneReady仍false，窗口启动16956/18232ms超3s。定位LoadMultiplayer的无条件while(ActionExecutor.IsPaused)会等待所有玩家注能开局完成，现NormalOpening跳过该等待、直接显示原生房间后继续正常流。fixture也从源ToSave(after battle)改为原版F隔离盘实际LoadAndCanonicalizeMultiplayerRunSave(Id)，原始Save应是开战前checkpoint，不用战斗后快照伪装SL；启用该专项原版shouldSave:true，其他专项仍false。最新候选 `53AC05E49649F1C13C2AE58C4B79F809D6E54BD5F6CBA9C42330542F141B6508` 双版本/bootstrap零警告错误。新multi3后台测试正在进行（证据以工具返回路径为准），编译遗漏SaveManager namespace已补后重跑。单人/多人新需求未整体交付，此前battle历史3s/第四阶段仍待完成；未交付/提交/推送。

2026-10-12 16:21 用户新增明确需求：修复开窗后 mod SL 导致本体/副本注能自动出牌过快，单人和多人均测试。原版对应 `Catastrophe`（随机打出抽牌堆2张、强化3张）+`Imbued`（首回合自动打出技能）。确认旧单人 RunSingleplayerSlAsync 从FadeOut至玩家阶段持有 MirrorFastRestore.Source，Active=Source||Replica使本体原版等待/动效也被跳过，且最后才FadeIn隐藏了开局自动出牌。新候选 `02763599FB2D02D32A390F12DC5B79A5122FD6B2B13B335DE6DA2E2E5141F6F0`：Active仅renderer && Replica，Source仍是checkpoint priming标记；本体房间建立即淡入；新增NormalOpening协议，单人恢复checkpoint、多人SL保留副本后的首个load按原版速度播放新开局，不套隐藏历史Instant/追赶等待。历史切换/重置仍隐藏加速，并延伸至IsEnemyTurnStarted结束；隐藏重建临时FPS120恢复后还原，fixture VisibleStatus不再每500ms覆盖该产品临时FPS。双版本/bootstrap构建零警告错误。

当前专项入口 `tools/Test-OpeningPlayback19.ps1 -Mode single`，`...multiplayer-openingSingle-20261012-162150`后台静默进行，尚未结果；新OpeningPlayback.cs观察原版CardModel.OnPlayWrapper实际自动播放任务耗时/张数/原生scene显示，以正常开战为基准，再执行真实私有mod单人SL，覆盖2/3；多人2/3入口已编译但尚未运行。初次fixture编译命名空间/私有RemovePotion API错误已改公开DiscardPotionInternal后零警告错误。未交付/提交/推送。

前一候选F54FAD四阶段 `...155817`仍战斗来源切换约7.2秒失败，第四阶段未执行；采样已保留evidence：隐藏动作完成后fast已关，原版敌人段仍继续，当前新候选修复该余段及隐藏FPS，**尚未复测三秒，不能声称四阶段完成**。死亡/结算等此前独立专项保持通过，未虚称对新候选重测。当前新需求优先，后续仍须完成此前性能与第四阶段。

2026-10-12 15:58 A67DFA / `...155027` battle来源切换A7160ms、B5743ms仍失败，房主被其他端失败结束前尚未写最终计时；不能声称三端三秒达标。临时采样jsonl旧启动脚本仅复制*.json导致最后采样随隔离profile清理，未虚称保留；现在采样直接写evidence，snapshot复制也支持*.json*。候选 `F54FAD1A9B14E8997E67383D02990EF93C9D0C9830C5075DC6AAB66B7D836361` 隐藏多人重建临时使用原版 PrefsSave.FastMode.Instant（原版牌堆动效也直接读该值），finally恢复副本此前设置；来源设置不改，不保存。双版本/bootstrap与新fixture零警告错误；`...multiplayer-four-stages19-20261012-155817` 后台静默进行，尚未结果；未交付/提交/推送。

2026-10-12 15:50 C4DD4F四阶段 `...154109` 三端各152检查后战斗历史切换仍10839~11095ms，不能称三秒达标。日志显示尾部enemy turn还在场景显示后推进；原版 AfterAllPlayersReadyToBeginEnemyTurn 的异步 phase2 在 ActionExecutor之外。新候选 `A67DFAC2A2DAEE7CBF75ED1690949C12A2CA0CEBC7828AA46B552B2D0FA0043C` 将隐藏追赶延伸到 IsStarting/EndingPlayerTurnPhaseOne/Two 完成，遇到来源当前真实未完成选择则允许原生选择页显示。新增仅四阶段fixture的100ms执行器/phase/fast采样，不修改原版行为。双版本/bootstrap零警告错误；采样脚本一处 Environment命名冲突已修正后零警告错误编译。新 `...multiplayer-four-stages19-20261012-155027` 后台静默进行，尚未得到结果，未交付/提交/推送。

2026-10-12 15:41 用户“继续”：四阶段 `...103842` 三端各152项后确认为**产品性能失败**：战斗已有72条记录时切换本人来源需12175/12247/12302ms，exact PID/身份正确但超过严格3s，第四阶段未执行。定位隐藏加载 ApplyMultiplayer 每条等待一帧，且动作入队后提前撤销 presentation-only speedup，尾部原版执行器仍播常速动画。当前候选 `C4DD4F6D05F71ED0B00187DC207384434A25311CD7FD076EF04AB7599AA106C6`：隐藏历史每8条yield，保留原版队列顺序/选择/Hook/Resume及原有阶段等待，追赶原版执行器完成后才结束加速；普通live deltas不变。双版本/bootstrap零警告错误构建。新四阶段 `...multiplayer-four-stages19-20261012-154109` 正在后台静默验证，**尚未得到结果、未交付/提交/推送**；已有独立通过项保持，不虚称最新候选已重测。此段优先于下方历史记录。

2026-10-12 10:38 四阶段 `...103215` SL/前12切换已严格3s通过，权限伪造/旧上下文/撤权和药水实际取消亦通过；第三阶段71/72/72处失败为fixture拒绝计数基线竞争：三个角色环发错误目标到下一来源，但各自记录的是自己的接收计数，最快的队友已发来拒绝后最慢端才读基线，于是最慢端等待不存在的“再增一次”。现记录基线后新增全员barrier再发伪造目标，不改任何产品目标/权限规则。新同候选四阶段正在后台复测，尚未整轮通过；第四阶段及后两次battle切换未完成。未交付/提交/推送。最新候选仍C631C2。

2026-10-12 10:35 最新候选 `C631C26EB377460963EE95EA58B21BA88343E6940B70608DAE0C04A363B704E9`，修复SL恢复字段顺序，双版本/bootstrap零警告/错误构建。当前四阶段 `...multiplayer-four-stages19-20261012-103215` **前两阶段/SL与12次来源切换局部通过，第三阶段运行中，尚未整轮通过**。SL exact PID保留，进入原版游戏后到完整校验就绪：房主576ms、A1549ms、B1578ms；原版BeginRun到就绪另记4423/5044/5109ms，按用户口径不计入3s。12次来源切换1750~2160ms，各严格<=3000，身份、全队槽位、waiting Grid原版候选/约束一致，source old permission撤销，同PID；F8与waiting Grid重置复用，房主61检查处继续战斗权限/药水。**尚未交付/提交/推送**，后续两次战斗来源切换与第四阶段待最终结果。

2026-10-12 10:31 四阶段 `...multiplayer-four-stages19-20261012-102258` 原版启动期退出、未执行检查，日志无fixture结果；新启动设施补充退出码/原始stdout/stderr。重试 `...102730` 初始三人窗口/来源完整hash通过5项后，SL **实际产品失败**：“多人来源已离线”。定位新增普通 Stop 清理restore字段，与 BeginQuickSlRestore 先赋值再Reset冲突，Reset→Stop把新_restoreState/_restorePlayers清空，下一帧误取消恢复。现把旧state/队伍/身份暂存局部变量，先Reset，再发布新的恢复字段与60s期限，保留原认证PID。正在双版本构建，尚未复测。Purity102/灯火129等已通过专项保持，不默认重跑；四阶段尚未通过，未交付/提交/推送。README更新为.19描述但未交付，包以构建及最终记录为准。

2026-10-12 10:22 **洁净独立专项已通过**：`tools/Test-MultiplayerPurity19.ps1` / `...multiplayer-purity19-20261012-101925`，当前FBEAE9候选三端各34=102，exit0、隔离profile清理。真实原版Purity出牌，0~3手牌约束/候选跟随来源，等待选牌重置不完成来源choice、无授权原生点击不修改来源；选三张+自身恰四张消耗，完成后clean replay完整hash/gameplay和exact PID一致；第二次选零张仅消耗自身、不重复上次所选三张。

当前开始 `tools/Test-MultiplayerFourStages19.ps1`（复用启动设施 Case=fourStages，用户本轮明确要求的三人四阶段整体验证，并非历史全量套件）。严格来源切换3s从点击到正确身份/选牌契约或settled完整hash就绪；选牌等待时原版无 settled hash，用真实来源候选/约束验证，不能要求本来等待输入的动作变成settled。SL严格3s从全员已重连、原版进入游戏到窗口完整校验，另記BeginRun耗时。当前尚未得到整轮结果；未交付/提交/推送，所有测试后台静默。

2026-10-12 10:19 当前候选 `FBEAE968DD03D5B1620ED9E0A0514DB8B6CA9D0A219D87F1027A5BA3278795B7`，只较493FC源码注释更新，语义相同。**灯火两个完整分支已通过**：仅灯火 `tools/Test-MultiplayerEvents19.ps1 -LanternOnly` / `...multiplayer-events19-20261012-101610` 三端各43=129，exit0/隔离profile清理；全队1/2票等待与3票归还各100，保留/战斗原版出牌、完整sourcehash与三原版gameplay、战斗重置exact PID、实际领取SpecialCardReward每人恰一LanternKey及原版Continue地图。`...101147` 中出牌/重置通过，奖励失败是夹具在新奖励检查点/原生奖励页尚未就绪时先授权被边界撤权；已先等待双方NRewardsScreen+Verified并Sync，再授权，精准点SpecialCardReward。没有改变产品权限规则来绕过失败。

当前正运行独立 `tools/Test-MultiplayerPurity19.ps1`（洁净原版0~3消耗及waiting/completed重放/重置），尚未得结果；四阶段整轮严格三秒与最终常规房间验证待运行，未交付/提交/推送。全部后台静默。此段与下方不一致时以此段为准。

2026-10-12 10:12 当前构建候选 `493FC23AC0C083D038B4FA3C9FDAE91272BE460EAB9BEA6972AEDEB25259E081`。`...100843` 正确进入嵌套 Monster 场景，但完整状态未一致；日志定位 renderer 原版 CombatReplayWriter 默认 enabled 且直接进入Event时未 RecordInitialState，后续战斗 checksum回调抛异常打断初始化。已在 multiplayer renderer SetUpReplay 后禁用**副本重复录制器**，来源原版 writer和ChecksumTracker、生产完整hash均未关闭/削弱。双版本构建零警告/错误，新仅灯火 `...multiplayer-events19-20261012-101147` 后台静默运行，尚未得结果。源注释一处随后改为不声称未经验证的“original replay runner”行为，无语义变化。Purity/四阶段依旧待运行；未交付/提交/推送。

2026-10-12 10:10 最新候选 `94E465BDF1E69322A427B45BD09C702CED24468BD7F69F1EDEA7B01A4AD0F153`，双版本/bootstrap零警告/错误构建。灯火 `...100521` 归还路线（无Ectoplasm）通过全队投票、各+100、完整来源hash；保留→战斗**实际产品失败**：来源进入带 ParentEventId 的嵌套 CombatRoom 后，旧检查仅按 room引用匹配，错误切回原版普通 replay 初始Map检查点，再在Map上重放eventVote导致越界。修复 `MultiplayerNoncombat.IsCurrent`：保留原事件检查点，并按父事件身份连续记录/replay原版投票、战斗 actions/hook/resume/choices；权限判定同样识别当前事件战斗，不序列化原版不允许保存的未完成 ParentEvent combat。最新仅灯火 `...multiplayer-events19-20261012-100843` 正静默运行，包含真实 Defend、战斗重置、奖励，尚未通过。Purity独立待运行；四阶段严格3秒及完整整轮待运行。未交付/提交/推送。

2026-10-12 10:06 当前候选仍 `0CB61F...`。`...multiplayer-events19-20261012-100153` **六次占卜完整路线已通过**：实际揭格/工具、三次卡牌选择、奖励行移除/排布、原生奖励 Continue 返回占卜屏幕，再点占卜的 NProceedButton 打开地图（不是 NEventOptionButton）。随后灯火归还两次投票等待/第三次完成及完整来源hash已一致，但测试断言固定+100失败：fixture原自带 Ectoplasm 阻止金币获取，三原版/副本都按规则未加钱。只修测试fixture移除该遗物；新 `tools/Test-MultiplayerEvents19.ps1 -LanternOnly` 证据 `...20261012-100521` 后台运行，仅未通过灯火两分支，不重测占卜。

新增独立 `tools/Test-MultiplayerPurity19.ps1` / `Purity.cs`：**“洁净”明确为原版 Purity 卡**，不是泛称clean reset。测试零/三张消耗，原版选牌0~3契约、等待选牌重置及完成后重放，完整来源hash/三原版gameplay/exact PID，代码已编译但尚未运行。`docs/multiplayer-window19-validation.md` 记录各专项真实候选/范围，尚未交付或整轮通过。Lantern脚本还补充嵌套战斗原版Defend出牌和clean reset；当前已启动DLL未含该后续fixture段，下一次未完成分支需使用新fixture。

2026-10-12 09:57 当前候选 `0CB61F79F0BB8C34A9115A0F12444DFC6AD87680B4FBAAB666888BF5D127E90D`：修复来源本人的原版奖励领取未执行 NRewardButton.GetReward，导致牌组已更新但副本奖励行不移除/排布的问题；现按来源奖励对象调用原版按钮，保留远端同步规则。双版本/bootstrap构建零警告/错误。独立其他返回 `tools/Test-MultiplayerReturnExtras19.ps1` 证据 `...multiplayer-return-extras19-20261012-094644` **三端各60、共180通过**：三个非空战斗牌堆返回、牌组卡牌详情/返回、设置四标签返回/暂停继续，完整来源hash、三原版gameplay与 exact PID。隔离profile已清理。

事件剩余入口 `tools/Test-MultiplayerEvents19.ps1 -Remaining`：`...093023` 确認6次占卜来源已完成三次选牌，副本却残留卡牌行，是上述产品问题；`...095255` 当前修复版三次领取均移除/排布一致，后续失败是测试误以为非终局奖励 Continue 直接开地图，原版应先返回已完成事件、再点击事件 Continue；现按原版流程修正。最新 `...multiplayer-events19-20261012-095724` 正后台静默运行，**尚未整体通过**，灯火钥匙仍待实际结果。四阶段脚本补充3秒严格断言，末尾战斗fixture改走真实 Monster 地图坐标，避免 debug Monster 与保存地图坐标不一致；尚未执行本次四阶段整轮。死亡585/完整结算153/药水225/窗口123/光标42/商店102等已通过保持，不重复历史范围。未交付/提交/推送，后台静默指令继续优先。

2026-10-12 09:29 最新进展（候选仍 F47DCF...）：完整胜利 ending `...multiplayer-ending19-victory-20261012-091626` 房主29+客户端26+26=81通过，支持授权 Continue/总结/完成三人整局与同进程新局 exact PID复用。独立商店反馈 `tools/Test-MultiplayerShopFeedback19.ps1` / `ShopFeedback.cs`，证据 `...multiplayer-shop-feedback19-20261012-092745` **三端各34、共102通过**：卡/遗物/药水/删牌四种没钱左键请求真正触发来源和副本原版 FailureGold tween、实际物品横向抖动与老板对白/tween；库存/金币/牌组/药水/遗物不变，完整来源hash和三原版gameplay一致，PID不变。首次 `...092339` 失败是 fixture 给后3类商品传 -1 索引，已修正成0，未改产品。两轮exit0、隔离profile清理。

特殊事件首次 `...multiplayer-events19-20261012-091849`：三次占卜路线原版揭格、切换大小工具、真实奖励选牌与完成/来源完整hash均通过；六次揭格也均一致，但测试后续不断点击第一行满槽药水奖励，未进入卡牌选项，picked0断言失败（房主102、客户端100），不能称整轮通过。现新增原生奖励类型/NodeKey快照先点 CardReward，余药水可原版跳过；正在仅复测6次和未执行的灯火钥匙，用 `tools/Test-MultiplayerEvents19.ps1 -Remaining`，不复跑3次。未交付/提交/推送，四阶段整体验证及其他返回仍待完成。所有后续默认后台静默。

2026-10-12 09:15 最新候选 `F47DCFB386BC97F5A6C3E5E380FC3AE515BFE66D256678D75B400D338DE6D7DF` 死亡专项已完成：自然战斗出牌 defeat `...091126` 三端96=288；victory `...091348` 三端99=297，共585，exit0、隔离profile清理。修复单名死亡/失败/胜利的原生动画/界面、reset/full来源hash与exact PID复用，并防止 replay自己提前创建第二个结算及最后动作覆盖死亡姿态。稳定帧图片已实际查看，不仅看hash。完整 victory ending 独立入口现在运行；完整 defeat 72项上一个同功能候选已通过，特殊事件/无钱商店/四阶段整体验证仍待完成，未交付/提交/推送。所有后续测试继续后台静默。

2026-10-12 09:11：完整原版 defeat ending 独立入口 `tools/Test-MultiplayerEnding19.ps1 -Outcome defeat` 已通过，证据 `...multiplayer-ending19-defeat-20261012-090832`：房主26+客户端23+23=72，来源拒绝/授权窗口原版 Continue、summary/原版最终菜单、房主依次协助两位好友及本人结束整局、三原版进程都清理 NRun/RunState、原进程再开三人新局复用各自 exact PID。候选仍 `86953D...`。测试针对正常结算离线放行仅该已知预期错误，并对切换期间 local=null 做就绪等待，未修改产品网络判定来掩盖失败。胜利稳定帧补拍最终证据 `...multiplayer-death-visual19-victory-20261012-090659`，各99检查，通过；暂隐外壳后的 source 与 replica 逐图核对确认 TheArchitect 背景及三个角色倒地一致。

随后检查自然致命动作路径发现 native replay 也会自行调用 ShowGameOverScreen，可能先建一个结算、再由来源记录建第二个结算而销毁已 reparent 的角色；最后动作异步未完成也可能再播 cast/idle。新增只允许来源最终记录在 renderer 创建结算、先等待原版 ActionExecutor 完成的保护。**当前候选 `F47DCFB386BC97F5A6C3E5E380FC3AE515BFE66D256678D75B400D338DE6D7DF`** 双版本/bootstrap 零警告/错误构建；最新独立 death defeat `...multiplayer-death-visual19-defeat-20261012-091126` 正在运行，此分支改为玩家2、1、3分别正常使用原版 Bloodletting 依次死亡，未用 Kill fixture 代替最后全队失败。尚未得结果，victory 最新候选待测。详细当前范围见 `docs/multiplayer-death-visual19.md`。未交付/提交/推送。

2026-10-12 08:57 最新死亡专项已通过：`tools/Test-MultiplayerDeathVisual19.ps1` defeat `...multiplayer-death-visual19-defeat-20261012-085139` **三端各96、共288**；victory `...multiplayer-death-visual19-victory-20261012-085535` **三端各99、共297**；合计585。候选 `86953D8735FE9C7AED0AD9A977AF99A6FAE729CF1FAEE9494E27C6D41EC2CE7B`。真实三角色原版 ENet、原版 Bloodletting 动作致仅角色2死亡，未以改HP/播放动画伪造中途死亡；source/replay 完整hash一致；逐角色HP、死活、实际Spine动画、交互/焦点、血条UI透明度、visuals所属、来源手牌UI对齐；clean重置后仍一致、同PID；全队失败与原版 WinRun 胜利结算均核对三角色实际 die/visible/alpha、结果/角色/分数并保存真实D3D12渲染图，exit0、隔离profile已清理。初始化测试 CharacterStats 修正 shouldSave:false 缺统计，未改原版游戏规则。首轮 `...084708` 到90 checks失败为测试误把存活敌人的Visuals算进“三玩家倒地”，已按原版CreatureNodes玩家引用过滤；`...085002` 在静默宿主可见性校验失败提前停止，不是产品失败。隐藏宿主继承/样式已强制校验，成功运行的副本 Win32实际 screenBounds 约(-29658,-29936)，不可见sourceparent，静音/NoFocus，无OS输入，全部后续入口默认静默。当前独立完整 ending defeat `...multiplayer-ending19-defeat-20261012-085713` 运行中，完整结束/再开局尚未证明。死亡专项自动化通过≠用户手动验收，未交付/提交/推送，其他当前活动专项仍待完成。

2026-10-12 08:47 最新用户指令优先：**此后所有测试后台静默，不再显示测试窗口**。新增专项为全队失败倒地、胜利结算绘图以及战斗中单名玩家死亡后的角色/界面与来源原版一致。定位原版 combat game-over 会直接重用旧角色 Visuals，不自动触发死亡动画；副本旧 ending 仅将另建 presentationState HP 置零，未触发实际战斗节点倒地。现已在原版 overlay 接管前对 live 原生节点应用终局 HP/原版 StartDeathAnim，不执行游戏死亡 Hook 或另生成结算。候选 DLL `86953D8735FE9C7AED0AD9A977AF99A6FAE729CF1FAEE9494E27C6D41EC2CE7B` 已双版本及 bootstrap 零警告/错误构建；新增独立 `tools/Test-MultiplayerDeathVisual19.ps1`，三人不同角色/真实 ENet/实际 D3D12 渲染，隐藏宿主 `--wid` 阻止原版启动时移回屏幕，静音，不操作系统鼠标/键盘。当前 defeat 正在运行，证据 `...multiplayer-death-visual19-defeat-20261012-084708`，**尚未通过**，victory 待运行。未交付/提交/推送。

上一轮完整 defeat `...multiplayer-ending19-defeat-20261012-083516` 到 19 checks 后失败：source 原版结算 summary 报 CharacterStats 缺角色（测试 SetUpTest shouldSave:false 跳过原版 OnEnded 的统计初始化），已在新 fixture 用原版 GetOrCreateCharacterStats 初始化，未为此修改真实游戏行为。授权 Continue/分数与结果是局部通过，不代表整局完成或绘图一致。新增 renderer 原版 NRun 拆除后清理 stale scene/权限但保留子进程保护已编入上述候选，完整结束/再开局尚未证明。光标真实缩放专项 `...032243` 三端各 14，共 42 项通过：原生 resize GuiInput/Layout 改变 viewport，Help 图10/17像素随实际倍率变化；之前 `...032041` 只改 panel.Size 未改变 content viewport 不能当实际缩放证据。特殊占卜/灯火钥匙独立 events 代码及脚本已编译，尚未运行。下方可见测试/旧候选描述仅历史。

2026-10-12 03:20 最新进度（下方候选 hash/未测说明均为历史）：多人药水独立入口 `tools/Test-MultiplayerPotion19.ps1` **三端各 75，共 225 项通过**；证据 `F:/projectFile/RMPStable-development/evidence/multiplayer-multiplayer-potions19-20261012-031632`，实测候选 DLL `F976076EBE8DD58BD79AF928586CE3F8B0BA77F5BFBD41E6807F10CAEF977840`。三不同角色真实原版 ENet + 原生副本输入队列：Block 战斗中逐个为队友 +12 格挡；Blood 战斗中逐个治疗队友；Fire 逐个攻击单个敌人；左键弹窗取消、非法目标清除旧悬停、右键/Escape 取消；来源拒绝敌人作为队友目标；战斗外 CombatOnly 不开放，Blood **只给自己喝**且暴露 owner 唯一允许目标，伪造队友目标被拒绝，连续两次自身治疗。每步三原版 gameplay 一致、各副本与所选来源生产完整 hash 一致；主菜单预热副本到多人入局保持 PID。全部退出/隔离 profile 已清理。自动化通过，不是用户手动验收；仍未交付/提交/推送。

规则纠正：原版 `.107` / `.111` `CanThrowAtAlly` 仅战斗内为 true，战斗外血药走 native self drink，不得据旧段落“战斗外队友”执行。生产补齐 null target→owner，与原版 EnqueueManualUse 一致。失败 `...031305` 的伪造目标测试把 viewer/source 身份混用，有时选成 source 自身；测试药水静默添加未通知原版栏，导致 source RemoveUsed 找不到控件；已改 source 身份与正常获取通知，未为 fixture 错误修改原版。`...030957` 揭示正常 prewarm reset 调用了会直接访问 NRun 的 `NTargetManager.Instance`，主菜单 NRE 后子进程重建；现已改安全 NRun/GlobalUi.TargetManager 访问，并在成功药水测试核对 exact PID。

当前 F 盘候选 DLL `A9B831BD9EAE7471846651FE0FEC3A7DFAAAEBB720E300B95F309BA273A304CC`，新增完整多人 final journal/原版结算及授权按钮路径，native host 最后菜单按钮延后六帧执行以先回传可靠 ack；显式 Stop 清理 SL restore 标记；**这两点尚未验证**。双版本/bootstrap 编译零错误/警告。新入口 `tools/Test-MultiplayerEnding19.ps1` 已编写 defeat/victory、Continue、whole-run 完成及同进程新局 PID 检查，未运行。光标缩放可见专项 `tools/Test-CursorResize19.ps1` 当前运行（证据 `...multiplayer-cursor-resize19-20261012-032041`），未得结果。全面四阶段、商店无钱反馈、特殊占卜/灯火钥匙、clean replay 及所有返回仍未完成；不得把局部通过称整轮通过。桌面 `.14` 未动。

2026-10-12 最新要求与实际状态（优先于下方旧开发记录）：主游戏进程仍活着时，认证原版副本只创建一次；正常切换来源、多人 mod SL、状态重置、退出后开始新局均需复用同一 PID。resident 代码与窗口地图/玩家信息/光标缩放的首轮修改已编译，尚未测试通过，候选 DLL `0E67B96CBEE8BDBEFFE189CB808B1723B1AB2D2AC291A64046DE2C5FE658F45C`。异常副本崩溃/认证管道不可恢复仍是单独异常，不能承诺维持已退出进程的 PID。

最新局部通过：独立入口 `tools/Test-WindowInteraction19.ps1`（仅窗口专项，共享三人启动设施），证据 `F:/projectFile/RMPStable-development/evidence/multiplayer-window-interactions19-20261012-024834`，三端各 41、共 **123 项通过**。真实可见三人原版联机，原生窗口输入队列，顶部地图开/关稳定、三玩家血条打开正确遗物/药水/卡组及返回、牌组返回、暂停设置/返回/继续、顶部遗物原版放大/关闭、浏览不改来源完整 hash、PID 保持。未覆盖缩放后光标或牌堆所有返回/设置所有标签，未作物理 OS 鼠标验收。已结束并清理隔离 profile。原先 `...024457` map 点击坐标使用 Godot WindowGetSize 缓存而非嵌入窗口 Win32 客户区，已修正测试坐标，并在真实非战斗房间测地图开关；不是通过直接 ForceClick 代替窗口点击。

当前构建候选 `A8BCAE566B5C885564F5699F46F6647DD57572B31BC26DEF78446CA30779DF1A`：补齐原版顶部遗物只读入口、战斗外 AnyTime 药水的队友目标控制、血条目标命中与旧 hovered 目标清除，双版本编译无警告/错误。新独立药水入口 `tools/Test-MultiplayerPotion19.ps1` / `PotionTargets.cs` / `PotionRooms.cs` 正在验证 Focus（自身）、Block（队友）、Fire（敌人）、Blood（战斗外队友/自己）、取消/非法目标与效果归属，不能据编译或部分取消通过称全部完成。`...multiplayer-visible-three-v1-20261012-024025` 是失败证据：药水原版非法目标点击会结束瞄准，测试最初错误要求保持瞄准；实测没有使用请求/消耗，已纠正。正常 Grid 状态重置复用 PID 在该轮通过。活动三人四阶段全面测试仍待完成，新增六类特殊案例/结算亦待完成；未交付/提交/推送。

用户追加专项：遗物放大镜随窗口缩放；顶部地图按钮开/关；左侧三玩家血条打开对应遗物/药水/卡组；设置和其他返回按钮；药水左键取消与多人队友目标使用/取消/非法目标/效果归属；遗物原版放大；商店无钱动画与对话；胜败完整结算及经来源授权代点继续结束整局；重放洁净的状态一致性；占卜与灯火钥匙合作事件。均在真实三人原版联机与原生副本中测试，失败需修复，不据单人历史结果推断多人通过。仍未完成交付、提交、推送或最终验收。

可见失败证据 `...multiplayer-visible-three-v1-20261012-021020`、`...021452`：快速隐藏恢复实测常规来源切换 1.920～2.372 秒，SL 从原版进入房间到已校验窗口约 0.47～1.53 秒；BeginRun 到窗口约 4.2～5.1 秒，按用户澄清不属于 SL 的 3 秒口径。这是旧 fast restore 候选的局部结果，不代表新增 resident/UI 候选通过。第二轮战斗阶段通过 130 余项后，三原版商店购买的全量 hash 比较失败：现态金币/卡牌一致，但原版各端历史统计 gold_spent/gold_lost/floor_added_to_deck 记录不同。测试已改为原版三端比较现态 run/combat（仅剔除原版历史元数据）；每个副本与其所选来源仍必须匹配未经削弱的生产完整 hash，包括来源历史。未修改原版历史规则。

2026-10-12 活动任务：用户已明确授权三人、四阶段全面可见测试，覆盖范围与显示方式以本次请求为准，不再使用静默测试。三角色为铁甲战士、静默猎手、故障机器人；我/测试员 A/测试员 B 的观战来源环为 1→2→3→1。用户已确认可以看到观战界面，此前“空白”是每次切换来源约十秒冷启动的误判。用户追加：缩短来源切换启动时间，多人快速 SL 尽量保留副本。

用户最新计时口径：来源切换目标 3 秒内；多人快速 SL 的 3 秒从全员重新连接后进入游戏开始计算，**不包含快速 SL 的退出/重连过程**。同时记录原版 loaded-run BeginRun 到窗口就绪、原版进入房间到窗口就绪两段耗时；不把重连总时长混入目标。当前尚未证明达标。

最近失败证据 `F:/projectFile/RMPStable-development/evidence/multiplayer-visible-three-v1-20261012-015532`：原版 load lobby 按真实 join response/reconnected 消息补齐三人后，副本保留及三人 roster/hash 恢复通过；来源切换保持 PID，但实测 5.735～6.056 秒。后续 Grid 控制被测试同时断开旧 ENet 房主造成的断线弹窗阻塞，整轮仍失败（39/39/40 项），不能声称四阶段通过。已改测试先退出客户端后关闭旧房主，并使用 mod SL 后半段相同的原版 loaded-run screen/ready/BeginRun/LoadRun，而非测试专用读档路径。副本隐藏重建的动画缩短和同队伍/章节资产复用已写入，尚待重新构建实测。当前包 SHA 为 A036B4E57E7016EC7EAF0DC4847A2D6580FD7358DCC217D3C1B5B614B9761A86（此次优化前）。

正在 `multiInstanceTest` 开发 `.19`：正常切换复用唯一认证副本进程，重新订阅来源、撤销旧权限并从原版检查点加载新观察身份；本地单调 generation 与来源 generation 分开，拒绝旧来源输入。原版快速 SL 的房主开始/客户端接受处保留副本，服务断开期间隐藏并清空权限，恢复同一原队伍的新状态后重新订阅；失败/超时或副本异常时释放，显式重置仍重建。原版游戏和真实存档未修改。双版本已编译；**尚未完成本轮自动化和交付，不得视为通过或用户验收**。

新独立测试入口 `tools/Test-MultiplayerVisible.ps1` / `tests/MultiplayerVisibleProbe`，最新可见运行证据 `F:/projectFile/RMPStable-development/evidence/multiplayer-visible-three-v1-20261012-014859`。前一轮 `...20261012-013153` 在 Grid 结果同步处超时：测试采样器写入被读取中的记录文件时遭遇 UnauthorizedAccessException 并停止，已修改工具按帧重试文件替换；该轮为失败证据，不能称四阶段通过。另两轮为可见图形启动握手超时与测试初始身份 null 竞争，均保留为失败参考。

当前 F 盘 package 已更新为 `.19` **开发候选**，DLL SHA-256 `EFEBD16DCFE7350452721790F52C90F65BC8D2EC6069D72EEDB5D6A72B525AF8`；下文 `.18` hash/包说明是上一轮历史。桌面仍为 `.14`。本轮所有修改尚未提交/推送；完成后需更新实际结果与活动测试状态。三人 SL 专项通过 localhost ENet 断开/重连和原版 SetUpSavedMultiplayer 加载真实序列化存档来测副本保留，未验证实际 Steam 好友发现/跨电脑自动重连。

更新：2026-10-11。用户指定 `.14` 为多人开发基线，已授权第四阶段开发与测试；当前候选版本 `0.3.9-multiInstance.18`，新增常规多人非战斗房间的原版观战与来源授权操作：地图投票、个人/共享事件、奖励、商店、火堆和宝箱。沿用前三阶段的数据路由、原版战斗窗口和控制。用户要求测试保持静默，不影响其游戏和电脑使用。`.12` 及此前功能已验收；`.13`、占卜、`.14`、`.15`、`.16` 和 `.17` 原待验收状态继续保留，不据继续开发推断最终验收、不重复旧测试。此前不升 `.15` 的要求属于上一轮 `.14` UI 修复。

## 必须持续遵守

- 本机和 GitHub 只能有 `main`、`liveSharingTest`、`multiInstanceTest`，同名分支提交一致。不得另建分支/工作树，不以 detached HEAD 开发；`main` 不改名、不开发、不合并、不发布，除非用户特别授权。
- `liveSharingTest` 保留本地模拟方案 `0.3.9-liveSharing.13`；`multiInstanceTest` 是当前原生多实例方案 `.18`，以 `.14` 开发。两个方案不能混合或自行互相合并。
- 在现有测试分支正常提交和推送，不强推、不改写历史。开始先核对分支、工作树、工作区和资源，保留用户未提交改动。
- 不额外占用 C 盘开发空间；复用唯一主仓库及 F 盘资源，不新增克隆、运行副本、重复引用、缓存和包备份。构建、测试档案及大文件输出放 F 盘。清理先核对精确目标和证据，不触及真实游戏、真实存档、主仓库或其他项目。
- 用户允许在既定 F 盘范围内后台构建、运行、隔离测试和必要清理，不逐次询问。保持长时间静默开发、尽量少打断；已审核事项不重复审核。只在修改既定范围外、从未批准的内容时请求审批；系统权限边界仍须遵守。后台启动隐藏窗口。

## 测试与用户验收规则（优先于旧说明）

1. `.12` 及此前功能已验收，默认正常；不再复跑累积断言。没有新问题时不运行测试。
2. 每轮针对用户新问题重新编写独立专项脚本，只覆盖当轮修复点。例如下一版修复两个点，就只测试两个点，不向旧脚本追加并跑全量。可以复用构建、启动、隔离和日志工具。
3. 不默认运行历史 `full`、`feedback`、`replay`、`all` 套件，也不将“三套全部通过”作为后续交付门槛。不自行扩展测试范围；确需扩展时先由用户提出或同意。
4. 专项自动化通过后交付直接可用的 mod 文件夹，说明本轮修复和实际验证范围。自动化通过不等于用户最终验收。
5. 交付后记录版本、专项脚本及“等待用户手动验收”。用户明确通过：从活动脚本/待验收清单移除这些项目，下一轮默认其正常。未通过：针对反馈继续修复；未反馈：保留专项脚本和待验收状态，不自动反复重测。
6. 历史脚本和证据仅供查阅，不参与默认执行；验收后不必删除 Git 历史。当前活动项见下节，不自动重复测试。

## 本轮结果与待验收

本节专项脚本和详细结果文档位于 `multiInstanceTest`；`liveSharingTest` 只同步交接规则与状态，不合并另一方案的代码或测试。

- `.18` 多人第四阶段：使用独立来源日志补齐原版 CombatReplay 的非战斗/战后记录缺口；非战斗进入原版初始化前以及战后奖励生成前建立原版检查点，副本仍为独立原版 Replay 进程，只传状态、固定语义决策及来源校验的操作。常规地图投票、个人/共享事件、商店卡牌/遗物/药水及删牌、火堆休息/升级、宝箱与遗物投票、奖励选牌/跳过及战后继续已接通。原版房主仍排序和判定全队旅行/共享结果；个人完成状态不替代其他队员完成。来源本体自己的火堆立绘在前，副本所选角色在前，保留原队伍槽位。地图自己的投票也通过原版方法呈现。
- `.18` 控制沿用来源授权，按钮改为“允许本房间”；房间/检查点变化及战后奖励的新检查点撤权并要求重新申请。私人上下文包含来源玩家、当前原版页面、选择和库存，不以全队空闲/hash 相等作为输入前提。新增牌组删牌/升级的原版候选、预览取消/确认和奖励选牌等待；只有来源原版结果完成真实选择。非战斗记录仅在与当前来源房间匹配的有效检查点内生效，战斗初始化期间不重复录制原版 Hook。
- `.18` 独立入口 `tools/Test-MultiplayerPhase4.ps1` / `tests/MultiplayerPhase4Probe`，最终 **176 项通过（房主 88、客户端 88）**；证据 `F:/projectFile/RMPStable-development/evidence/multiplayer-noncombat-v2-20261011-224732`。`.111` 两玩家原版 localhost ENet、双来源原版副本，覆盖上述常规房间、私人库存/候选与真实结果、删牌取消重选、可靠购买去重、旧请求拒绝、个人事件独立完成、共享/地图单票等待、全队地图标记/旅行、新检查点撤权、战后金币/继续，以及跳过奖励并关闭原版页面。双版本 payload、bootstrap、探针均零警告/错误。全部 headless、静音、低优先级；只走副本内部输入队列，无 Windows 鼠标/键盘/焦点操作。专项进程全部退出、档案已清理；未运行第一至第三阶段或任何历史累积套件。
- `.18` 待手动验收四文件包在 F 盘 `build/package/RMPStable`，不含探针；桌面 `.14` 和当前游戏未替换。特殊自定义事件、变形/附魔/卡包等其他选择器、完整结算与断线恢复仍需后续完成；Steam 跨电脑、客户端之间路由、三人以上、可见窗口全屏/DPI、物理鼠标和键盘/手柄组合未验证，`.107` 本轮仅编译验证。详细范围见 `docs/multiplayer-phase4.md`。交付后等待反馈，不自动复测，也不因新会话自动构建/启动游戏。

- `.17` 多人第三阶段：点击窗口控制按钮申请，来源玩家本体明确允许本场战斗后开放出牌、原版目标、药水、结束回合及五类原版个人选择操作；自己角色的窗口直接授权。来源本体仍可操作或随时撤权，每个来源同时只允许一个控制者。隐藏/关闭、切回观战、来源切换、重建、房间/检查点变化、断线和约 15 秒心跳失联均释放权限，下一场重新申请。
- `.17` 操作只传来源生成的临时操作/目标 ID 和私人上下文，来源验证后调用原版流程，原版房主继续排序。保留全队槽位和立绘，不改变本体身份。用玩家自身手牌/药水/能量/回合/个人选择/页面及本体操作修订拒绝旧请求，不要求其他队员空闲；全队 hash 用于结果核对。可靠重复请求去重，拒绝未来事件进度、过期轮次和非法操作。原版异步药水及选择确认遇到其他玩家动作回放时短暂保留具体语义，绑定权限、检查点、回合/choice ID，最长八秒；不缓存跨页面操作结果。
- `.17` 本轮独立入口 `tools/Test-MultiplayerPhase3.ps1` / `tests/MultiplayerPhase3Probe`，仅战斗控制专项；最终 **104 项通过（房主 52、客户端 52）**，证据 `F:/projectFile/RMPStable-development/evidence/multiplayer-battle-control-v5-20261011-200812`。`.111` 两玩家原版 localhost ENet，双来源原版副本；检查权限、双方本体并发出牌、旧上下文/未来事件/重复请求、三类药水目标、五类选择、结束回合、真实心跳超时撤权、来源切换及战斗退出。原生鼠标队列检查选牌/确认，快捷键队列检查出牌及结束回合，药水使用原版控件/目标管理器。双版本 payload、bootstrap、探针编译均零警告/错误。未执行第一、第二阶段和任何历史累积套件。测试复用 F 盘已有资源，headless、静音、低优先级，无 Windows 鼠标/键盘/焦点操作；专项进程全部退出，专项档案已清理。桌面 `.14`、当前游戏和真实存档未替换。测试细节和限制见 `docs/multiplayer-phase3.md`。
- `.17` 仍待用户手动验收，F 盘候选位置已由 `.18` 更新，不额外复制历史包；第三阶段脚本/证据保留待验收，本轮未执行。`.17` 当时未开放多人非战斗页面；常规非战斗页面现由 `.18` 接通。完整呈现/断线恢复/房间外好友连接仍未完成。真实 Steam 跨电脑、客户端之间路由、三人以上、可见窗口全屏/DPI/物理拖牌和全部键盘/手柄组合未验证。不自动复测。

- `.16` 多人第二阶段：开放原观战快捷键（默认 F8，保留重映射），真实连接队员名单/昵称及独立多人来源偏好；选择其他队员或自己，切换仅替换对应自有副本，原版全队立绘保留且所选角色位于最前，本体身份不变。隐藏重开复用副本，标题栏重置只重建所选来源。多人控制按钮禁用，单人控制偏好保留。
- `.16` 个人 UI：原版 Replay 等待选择时，按玩家/choice ID 捕获原版命令参数，创建原版手牌单选/多选、升级、网格、战斗牌堆及三选一界面；候选、条件、已选项及已选牌行顺序跟随来源，只有原版记录的选择结果才能结束等待并继续动作。元数据绑定 generation 和事件进度；牌堆候选采集原版实时网格，避免读取永远为空的基类字段。离开战斗立即隐藏旧画面并提示当前房间，不把非战斗页面伪装为已同步。
- 第二阶段专项入口 `tools/Test-MultiplayerPhase2.ps1` / `tests/MultiplayerPhase2Probe` 保留待验收，本轮未执行；历史最终 88 项通过（房主 44、客户端 44），证据 `F:/projectFile/RMPStable-development/evidence/multiplayer-native-ui-v1-20261011-150326`。`.111` 两玩家 localhost 原版 ENet，双来源原版副本；通过游戏本地输入事件检查 F8，真实卡牌/遗物检查六个选择实例（含两张牌选择和快速撤销重选）、只读点击/确认/跳过、原版结果恢复、来源切换及立绘、等待升级时重建、进入宝箱房间时隐藏旧战斗。候选列表均检查非空及与对应来源一致；双版本编译零警告/错误。当时未执行 `.15` 或任何旧套件。
- `.16` 仍等待用户手动验收；F 盘候选位置已由 `.18` 更新，不额外复制历史包，`.16` 已测 hash 和证据保留。桌面 `.14` 和当前游戏未替换。第二阶段测试 headless、静音、低优先级、独立 F 盘档案，无 Windows 输入/焦点操作；全部专项进程退出、专项档案已清理，真实游戏未作为测试或终止目标。可见窗口、Steam 跨电脑、三人以上、客户端间路由、不同语言排序及其他特殊选择器未验证；其历史范围见 `docs/multiplayer-phase2.md`。后续不自动复跑第二阶段测试。

- `.15` 多人第一阶段：真正的原版房主/客户端同房间可靠路由、订阅身份和版本校验、32 KiB 分片、不可变战斗检查点和四类原生事件增量；本机认证管道接入独立原版 Replay 副本，不加入房间、不保存、不操控。副本身份为所选角色，本体身份不变，保留原队伍槽位及同步编号；原版立绘位置与绘制顺序已检查。
- `.15` 专项入口 `tools/Test-MultiplayerPhase1.ps1` / `tests/MultiplayerPhase1Probe` 保留待验收，本轮未执行；最终 41 项通过（房主 21、客户端 20），证据 `F:/projectFile/RMPStable-development/evidence/multiplayer-enet-v1-20261011-140837`。`.111` 两玩家 localhost 原版 ENet，双来源各带原版副本；测试 Gambling Chip 的 Hook、双方出牌/选择/恢复、敌方与下一回合、状态 hash、身份、立绘及消息拒绝。双版本编译零警告/错误。所有进程 headless、静音、低优先级，无系统鼠标/焦点操作；专项进程退出、档案清理。后续不自动复测。
- `.15` 尚待用户确认；该版本未开放多人 F8 入口、个人选择器、非战斗多人场景和远程操控。第一阶段数据可用性与缺口见 `docs/multiplayer-phase1.md`。F 盘候选包已依次被 `.16`、`.17`、`.18` 更新，不额外复制 `.15` 包备份；历史 DLL hash 与测试证据保留。桌面 `.14` 及当前游戏未替换。

- `.14` 本轮四项 UI：不可用翻页箭头隐藏且位置固定；顺序观战/控制 → 重置 → 图钉 → 关闭，三个图标按钮统一 36×36 逻辑尺寸和反馈，模式按钮高 28、在高 40 的导航栏居中；默认整个窗口高为游戏视口 1/2、贴右、位于可见状态栏和遗物行下方，观战并固定展开。
- `.14` 用 `LayoutRevision=1402` 一次性迁移旧布局，随后保存的拖动、缩放、模式和图钉选择继续保留。顶部位置按实际可见状态栏控件与遗物计算；原版 TopBar 外层为全屏容器，不能用其底边。
- `.14` 追加 UI：重置采用 512 像素 SVG 栅格纹理缩小至 24 逻辑像素，圆环加粗，36×36 按钮与图钉、关闭同中心；用户名框容纳 8 个中文字符并加圆角。单人包默认 7 个本机测试来源，使用 Steam 昵称加序号，全部复用同一个认证副本；切换只改变导航选择，保持权限、真实 SourceId 和进程身份。离线 Steam 未初始化时显示“本机玩家”，不显示会话 ID “1”。
- `.14` 商店：无钱点击不再提前禁用，交由原版购买失败/商人台词/摇动处理；商店卡牌和遗物使用各自原版右键预览入口，卡牌升级浏览仅在副本展示，不修改真实卡牌。核对原版房间、货架、四类商品、购买/库存/容量/删牌、台词、详情、升级及版本间按键差异，细节见 `docs/multi-instance-v14-ui-refinement-test.md`。
- 当前新独立入口 `tools/Test-Ui14Refinement.ps1` / `tests/Ui14RefinementProbe`，只验证本轮 UI、昵称和商店反馈/预览。Steam 禁用隔离环境用短暂平台策略替身验证账号路由，真实昵称待用户手动确认。首次 `.14` 的 1/3 布局和 18 项测试已成为历史参考，不再执行。本轮 23 项检查通过；证据 `F:/projectFile/RMPStable-development/evidence/ui14-refinement-v14-20261011-032332`。双版本构建无警告/错误；未运行旧套件。已更新桌面四文件包和上下文，仍等待用户手动验收。

- 占卜事件：按用户要求可见测试，原版三次/六次路线通过，未发现所测多次揭格、选牌不同步。六次最终独立自动化 26 项通过；初轮用户也手动点击，不作为纯自动化证据。细节见 `docs/crystal-sphere-focused-test.md`，独立入口 `tools/Test-CrystalSphere.ps1`。
- `.13` 搜刮：奖励移除后，剩余行移到静止鼠标下，重新触发原版悬停；独立入口 `tools/Test-RewardHover.ps1`。
- `.13` 首次显示：纠正真实原生窗口低于本体的层级，无输入也可见。用户已明确确认“已经能看到地图”；该确认不代表整个 `.13` 已验收。独立入口 `tools/Test-StartupDisplay.ps1`。
- `.13` 计时：原版副本从检查点累计时长开始自己的计时，加载耗时/暂停可能造成偏差，时间字段原本不参与对局 hash。现在只同步本体原版计时标签，副本不独立刷新，不改权威运行时长或存档。独立 `tools/Test-TimerSync.ps1` 验证 12 次秒跳、副本故意偏移 120 秒、本体回退 20 秒，显示跟随且对局校验保持通过。通信存在短暂传递时间，不承诺跨进程同一瞬间刷新。
- 上述结果与证据汇总见 `docs/multi-instance-v13-focused-test.md`。交付后等待用户对占卜结果及 `.13` 三项明确验收；通过后移出活动清单，独立脚本保留只读历史，不再默认执行。`.12` 以前的套件未运行。

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

当前 `.18` 已测候选 DLL SHA-256：`1615418B7875FEF8C94A17F7756455DC52E7BE4B76825494D6E0BB48264E9DBD`。四文件包位于 F 盘 `build/package/RMPStable`，不含测试 harness。`.17` 历史已测 DLL SHA-256：`1F6DEA1755F7039E20DC9182ACE6CC88A0A6BD8AEE46580ABFEB7B565797B423`；`.16` 历史已测 DLL SHA-256：`F69C36ADC50D44D7B7EFFE376A7435F9C3D743412B710C1E3470390FC69A3A74`；`.15` 历史已测 DLL hash：`68AE147A39315C7AEF1AC71BAC216889BDDF7077BACC3A261C332DA67BA3E758`。桌面 `RMPStable` 仍是 `.14`，DLL SHA-256：`AEE5C5217562315C20F51016B3F1E2E8198BB65DF883C907DA54FBE349178992`；桌面历史交接文档本轮未覆盖，当前状态以仓库本文为准。当前游戏未替换，替换 mods 后需重启加载。`.12` 已验收 DLL 历史 SHA：`F57AF11C85632C53F3843D0A4D217BEB7B837CF7F55BA6915E25DB4FB187B488`，不重新验收。专项日志和状态留在 F 盘 evidence，专项 profile 已清理。

## 后续可能用到的架构与入口

公开窗口支持单人、多人战斗及常规非战斗房间观战与来源授权控制，以及上述原版个人选择器。多人对局仍由原版房主排序并判定，所选来源验证控制操作并提供原版状态和结果；副本为独立 OS 进程中的原版界面。不要回退像素传输或手绘界面，也不能在同一进程建立第二个依赖原版单例的 `NRun`。多人操作校验权限、generation/epoch、事件进度、自身上下文、身份和原版合法性；不要重新引入全队空闲/hash 相等作为多人输入前提。副本不预测对局结果。独立 APPDATA/LOCALAPPDATA，测试只操作认证自有进程和隔离档案。

源码主要位于 `RMPStable/src/RemoveMultiplayerPlayerLimit/Features/LiveSharing`：

- `MirrorHost` / `MirrorJournal` / `MirrorState`：权威、检查点、操作与校验；`MirrorEnding`：原版最终结算。
- `MultiplayerMirrorSession` / `Network/RmpMirrorPacket`：多人订阅、房主路由、分片与来源元数据；`MultiplayerBattleControl` / `MirrorMultiplayerControl`：来源授权、合法操作、去重/心跳和副本输入；`MirrorMultiplayerReplay`：多人原版 Replay 身份和事件消费；`MultiplayerNoncombat` / `MultiplayerRoomControl`：非战斗/战后检查点、决策与原版房间操作；`LiveSharingMultiplayer`：公开窗口及来源生命周期；`MultiplayerPersonalUi`：原版个人选择器与呈现。
- `MirrorRenderer` / `MirrorCardInput` / `MirrorKeyboard` / `MirrorPresentation` / `MirrorNativeUi`：输入和本地展示；`SpectatorCommands` / `SpectatorNativeUi`：本体执行与原版选择器。
- `MirrorProcess` / `MirrorProtocol` / `MirrorNativeWindow`：进程、通信、窗口；`LiveSharingController` / `SpectatorView`：生命周期及外壳；`Features/QuickSl`：SL。

`tools/Build-MultiInstance.ps1` 复用 F 盘 references/build，生成内嵌双版本 payload 的 bootstrap。后续专项脚本复用已有 runtime，串行运行、屏幕外、静音、禁用 Steam，不移动系统鼠标；输出证据到 F 盘，结束后清理专项档案。旧 `tests/MultiInstanceSmoke`、`tools/Test-MultiInstance.ps1` 和 `.tools/maintenance` 的全量入口属于 `.12` 历史，不直接作为下一轮验收入口，阶段交付脚本亦有固定版本/哈希假设。

多人房间内路由、真实队员来源切换、来源授权的战斗控制及常规非战斗房间已实现，但未验证真实 Steam 跨电脑。房间外好友发现/连接、完整断线恢复、完整本体呈现跟随及特殊非战斗选择器尚未完成；仅在用户授权下一阶段时开展。呈现跟随讨论可查 `docs/native-mirror-presentation.md`，历史证据可查 `docs/multi-instance-test.md`。不恢复已验收需求清单。
