# 搜刮悬停专项

仅检查当轮反馈：金币领取后药水上移、药水领取后卡牌上移，鼠标不动时原版黄色选中框是否刷新；再检查移出奖励区域会取消悬停。使用原版 `RewardsCmd.OfferCustom`、`NRewardsScreen` 和权威操作回放，固定金币、药水、卡牌三项。初始地图先正常进入火堆，避免地图遮挡奖励层。

`tools/Test-RewardHover.ps1` 默认观察桌面包；候选包使用 `-PackageRoot ... -Label ... -ExpectFixed`，此时四项悬停和权威校验必须通过。无历史套件引用。`MultiInstanceSmoke` 仅为既有诊断许可槽名称，本项目是独立测试 DLL。运行目录、输出和档案在已有 F 盘，游戏可见，脚本不移动系统鼠标，结束后清理临时档案并保留证据。

当前状态与证据见 `docs/session-context.md`。用户验收后退出活动测试，不自动复跑。
