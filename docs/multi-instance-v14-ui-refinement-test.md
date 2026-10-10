# .14 UI 细化与商店交互专项

仍计入 `.14`。本轮独立入口 `tools/Test-Ui14Refinement.ps1` / `tests/Ui14RefinementProbe`，不运行历史累积套件。

原版商店流程审阅范围：`NMerchantRoom`、`NMerchantInventory`、`NMerchantSlot`、卡牌/遗物/药水/删牌控件，`MerchantInventory` 及四类 `MerchantEntry`，`NMerchantDialogue`/台词集合、`NInspectCardScreen`、`NTickbox`。同时核对 v0.111.0 实际程序集的预览及键盘入口；stable 的 accept 在 beta 已改为 confirm，按版本使用原版动作。

- 原因：`EnoughGold` 被窗口提前当作禁用条件，挡住原版 `FailureGold`、摇动、音效与台词；商店使用 `NMerchantSlot.OnPreview`，并不使用奖励卡牌 holder 的 `AltPressed`。
- 修复：允许有效、在售商店控件提交购买尝试，由本体原版检查钱、库存、容量及禁购规则；失败尝试同样进入操作回放。卡牌/遗物预览调用原版虚方法，仅在副本展示，升级预览沿用原版 tickbox。详情开启时拦住购买穿透。
- 专项：窗口一半高度、512 图标缩小显示和悬停；Steam 账号路由、8 中文宽度、圆角、生产路径 7 个本机来源逐一切换及身份保留；商店四类无钱点击原版失败/台词回调并保持 hash 一致；卡牌右键、升级切换、关闭、遗物右键和关闭。
- Steam 在隔离运行中禁用，昵称测试短暂替换平台策略；只验证账号读取路由，真实 Steam 昵称仍由用户手动验收。商人装饰台词使用原版 Chaotic RNG，两边可能选到同类的不同句子，不参与对局 hash。

最终专项 23 项通过，v0.107.1 / v0.111.0 双版本构建无警告/错误。证据：`F:/projectFile/RMPStable-development/evidence/ui14-refinement-v14-20261011-032332`。原版商店卡牌升级与遗物详情截图已核对。专项记录器使用允许替换的共享读取处理 Windows 并发文件替换，避免旧诊断文件造成假超时。

测试证据、DLL 校验及待验收状态以 `docs/session-context.md` 为准。源码和包不包含测试 harness。结束清理专项档案，仅留 F 盘日志和截图。
