# .19 多人窗口当前测试记录

2026-10-12 更新：用户指定本次版本为 **v0.4.0** 并授权测试分支合入 main。v0.4.0 仅统一版本信息并完善说明/更新器/打包，游戏逻辑沿用最终 .19；双版本/Bootstrap 构建零警告错误，DLL `0F7F37F7346ACCF7CFE5A63BC5F6CE5BD4E7F6FF632DF372DD94300D09CCFD76`。以下游戏测试仍注明其实际 .19 候选；本轮没有重跑游戏测试。更新器新独立 `Test-Updater040.ps1` 9 项通过，网络响应模拟，真实发行包运行文件哈希一致。手动验收及长历史三秒限制保留，当前 Git/交付状态优先看 `docs/session-context.md`，下文“未提交/推送”为当时历史状态。

状态：`.19` 本次动画修复及功能候选交付、待用户手动验收，未提交/推送。DLL SHA-256：`B30389FF1B44782389500375317B8501F5976822F57D4C0613BEE965BC91C194`。长战斗历史严格三秒仍未全部通过，不称所有性能目标完成。

所有当前测试采用原版 `.111`、真实 localhost ENet 三人、铁甲战士/静默猎手/故障机器人和各自独立原生副本。2026-10-12 用户要求后台静默：隐藏宿主、静音、屏幕外原生副本、低优先级，复用 F 盘隔离资源，不操作当前游戏、真实存档或系统鼠标键盘。保存的实际 D3D12 渲染图片仅为证据，不是窗口的数据通路。

## 已完成的独立专项

证据目录前缀：`F:/projectFile/RMPStable-development/evidence/`。各项保留其实际测试候选，不能把旧候选证据虚称为对最新 DLL 的重新测试。

| 入口 | 实际候选 / 证据目录 | 通过范围与数量 |
| --- | --- | --- |
| Test-WindowInteraction19.ps1 | A8BCAE / multiplayer-window-interactions19-20261012-024834 | 123：顶部地图开关、三玩家信息/牌组及返回、暂停设置返回、原生遗物放大 |
| Test-MultiplayerPotion19.ps1 | F97607 / multiplayer-multiplayer-potions19-20261012-031632 | 225：战斗队友格挡/治疗、单敌火药、自身战斗外血药、左键/右键/Esc取消、非法目标拒绝、效果和消耗归属 |
| Test-CursorResize19.ps1 | A9B831 / multiplayer-cursor-resize19-20261012-032243 | 42：实际原生窗口 resize/Layout，Help 放大镜随 viewport 倍率缩小/放大 |
| Test-MultiplayerDeathVisual19.ps1 -Outcome defeat | F47DCF / multiplayer-death-visual19-defeat-20261012-091126 | 288：真实出牌致单名/全队死亡，逐角色动画、交互、血条和手牌 UI，等待动作后只建一次结算，重置/PID/完整来源状态 |
| Test-MultiplayerDeathVisual19.ps1 -Outcome victory | F47DCF / multiplayer-death-visual19-victory-20261012-091348 | 297：真实单名死亡与重置，原版 WinRun 流程、胜利结算角色动画/场景/结果/分数；实际稳定帧核对 |
| Test-MultiplayerEnding19.ps1 -Outcome defeat | 86953D / multiplayer-ending19-defeat-20261012-090832 | 72：拒绝/授权原版 Continue、总结、房主协助两位好友及本人完成整局、同主进程再开三人新局 exact PID |
| Test-MultiplayerEnding19.ps1 -Outcome victory | F47DCF / multiplayer-ending19-victory-20261012-091626 | 81：胜利完整结算及授权/整局结束/同进程新局 PID |
| Test-MultiplayerShopFeedback19.ps1 | F47DCF / multiplayer-shop-feedback19-20261012-092745 | 102：没钱购买卡牌/遗物/药水/删牌，来源和副本真实 FailureGold 动画位移/商人对白，库存和状态不变 |
| Test-MultiplayerReturnExtras19.ps1 | 0CB61F / multiplayer-return-extras19-20261012-094644 | 180：三个非空战斗牌堆返回、牌组卡牌详情/返回、设置四标签返回和继续；原版状态不变、同 PID |
| Test-MultiplayerEvents19.ps1 -LanternOnly | FBEAE9 / multiplayer-events19-20261012-101610 | 129：归还全队投票各100金币；保留后嵌套战斗原版出牌、重置、完整状态和PID；每人恰一钥匙及地图继续 |
| Test-MultiplayerPurity19.ps1 | FBEAE9 / multiplayer-purity19-20261012-101925 | 102：洁净原版0~3手卡选择、三张/零张消耗，等待和完成后clean replay、完整状态/同 PID |
| Test-OpeningPlayback19.ps1 -Mode single | 027635 / multiplayer-openingSingle-20261012-162150 | 32：用户新增的注能Catastrophe 2/强化3张，原版正常开战基准与真正mod单人SL，本体/副本正常可见动画、完整状态和同PID |
| Test-OpeningPlayback19.ps1 -Mode multi | 53AC05 / multiplayer-openingMulti3-20261012-163429 | 99：三名不同原版角色、实际房主多人检查点/原生loaded lobby、注能强化3张、本体/窗口正常可见动画、完整来源hash/三原版gameplay、同PID；显示1087/1742/1717ms |
| Test-OpeningPlayback19.ps1 -Mode multi -Unupgraded | 53AC05 / multiplayer-openingMulti2-20261012-163657 | 99：多人未强化2张，同样完整原版SL流程、正常可见动画与状态/PID |
| Test-MultiplayerRooms19.ps1 | B30389 / multiplayer-rooms19-20261012-172235 | 360：三原版玩家个人商店/购买/重复拒绝/删牌预览取消确认、旧代次及权限拒绝、火堆立绘/升级治疗、宝箱、个人/共享事件、地图1/2票等待3票推进撤权、原版战斗结束Continue、事件卡牌领取/跳过、战斗金币与Continue地图，完整源hash/三原版gameplay |
| Test-OpeningPlayback19.ps1 -Mode multi 最终候选补测 | B30389 / multiplayer-openingMulti3-20261012-174021 | 102：正常可见强化3张开局、完整状态/同PID、原生绘制恢复/fastReplica=false，本体3502～3531ms、窗口3479～3535ms，显示900/1126/1771ms |

来源与副本比较生产完整 hash，包括所选来源的历史。三个原版客户端比较现态 run/combat，只剔除原版各客户端自行记账的地图历史和呈现发现记录。没有修改原版多人状态同步规则，也没有为通过测试而降低来源与其副本的完整一致性要求。

## 当前未完成

- 最终前三阶段逐端入口 B30389 / `multiplayer-battle-switch19-20261012-173600` 全部功能检查完成：三端各162，其中各160通过/2历史性能失败，共480通过/6失败；最终functionalPassed=true、performancePassed/passed=false、exit1。六次72条历史切换3062～3683ms，完整来源hash、三原版gameplay、全3角色/身份/权限/正常绘制恢复/exact PID均正确。第四阶段360独立通过，功能可分别核对；并发整轮未全通过、strict3性能仍是待改进项。两个无收益实验CustomStep/64条batch均已撤回。下方“正在运行”叙述为保留的历史，以上最终结果优先。

- 占卜三次路线早期局部通过；0CB61F / multiplayer-events19-20261012-100153 六次路线实际奖励、行移除、事件完成及占卜页第二个Continue地图通过（该轮后续灯火夹具固定100金币与Ectoplasm冲突，整轮不是全通过）。灯火两完整分支现129项通过。
- 当前第四阶段房间功能独立360项通过；前三阶段功能与严格历史切换性能仍在最终验证，未将失败的并发四阶段整轮称为通过。SL 从原版进入游戏开始计时，另记 BeginRun 耗时，不含退出重连。等待选牌状态核对来源原版候选/约束；其他来源切换核对settled完整状态。
- C631C2 / multiplayer-four-stages19-20261012-103842 前三阶段三端各152项后发现战斗历史来源切换12175/12247/12302ms，超过3s；第四阶段未执行。C4DD4F修复隐藏历史逐条等待显示帧及提前结束动画加速，新 multiplayer-four-stages19-20261012-154109 正在验证，不能把此前局部通过记成整轮通过。
- 后续C4DD4F、A67DFA、F54FAD的战斗历史切换仍分别约11、5.7~7.2、7.2秒失败。53AC05 / multiplayer-four-stages19-20261012-163952 三端各152后，72条战斗历史切换4265/4813/5012ms，仍未达到严格3s。056004添加仅隐藏副本原生Tween逐帧推进，`Test-MultiplayerBattleSwitch19.ps1` / multiplayer-battle-switch19-20261012-165927 后台验证当前前三阶段与性能，尚未结果。
- 第四阶段单独入口 `Test-MultiplayerRooms19.ps1`：53AC05 / ...165037 采购药水时失败为fixture silent清空药水却未通知原生holder，现已修正；056004 / ...165701 商店购买、重复请求、删牌预览取消/确认通过，随后一端休息点forefront断言读到旧快照失败。保存的新休息点快照三端restFront最终均true，fixture现等待当前RestSite实际采样。旧本地Generation请求在viewer已被丢弃，来源不应增加拒绝计数；现分别验证viewer不执行和保留旧权限/上下文送到来源后拒绝。B30389 / ...171209 到95项，包括商店、升级、治疗、宝箱、个人/合作事件、地图1/2票等待及3票推进、领取卡牌奖励；随后原版checksum3分歧断线。日志显示fixture从地图投票进入的战斗尚未完成原生起手异步阶段就debug跳入下一事件。现在等原版起手稳定、完整Sync，并用原生战斗结束/奖励Continue流程完成后再进入下一事件，未关闭原版校验。尚未整段通过。
- 056004 CustomStep实验三端并发历史切换4784/4690/4618ms未达标，无收益已撤回。当前B30389只在隐藏历史重建期间关闭中间帧GPU绘制，原生树/模型/动画继续处理，显示前和finally恢复；NormalOpening不关闭绘制、不加速。逐端72条历史严格3s测试 `...multiplayer-battle-switch19-20261012-171724` 运行中，不能宣称通过；前三轮并发失败证据保留。
- B30389 / ...171724 逐端首个72条历史切换3807ms，仍未strict3。0CDC54将仅隐藏native历史入队每8条yield改为64条，保留原版事件次序、跨阶段等待和队列完整完成；普通live/newopening逐帧不变。当前逐端 strict3 入口 / multiplayer-battle-switch19-20261012-172643 后台运行，尚未结果。原并发fourStages入口仍保留原strict3判定，没有降低阈值或隐去失败。
- 2026-10-12新增注能SL动画需求：本体加速已限制为仅隐藏副本；单人32项通过（2张基准2338ms，本体2433/副本2537；3张基准3364，本体3350/副本3470）。多人027635的自动效果虽正常速度，但其sceneReady直到全队开局完成，启动16.9~18.2秒失败；53AC05按NormalOpening跳过全队暂停等待，并用原版房主实际隔离多人存档/loaded lobby复测，2张/3张各99通过。新开局先显示正常动画、播放结束再核对settled完整状态；显示时间849~1742ms，未把正常播放时长算作启动延迟。
- 所有版本自动化通过均不等于用户手动验收；未逐一枚举全卡牌/遗物/事件组合，`.107` 本轮仅编译，真实 Steam 跨电脑及物理鼠标尚未验证。

## 本轮已定位的产品修复

详见实际源码和 session-context：认证副本常驻与来源绑定、正常切换/SL/重置保留进程、隐藏恢复提速、原版地图/玩家信息/返回与光标、多人药水目标规则、最终状态及原生死亡绘图、完整授权结算、占卜语义记录，以及本地原版奖励按钮领取后移除/重新排列奖励行。构建/测试工具不默认编译或运行历史累积套件。
