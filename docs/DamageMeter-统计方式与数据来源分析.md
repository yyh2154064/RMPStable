# DamageMeter 1.14.7：统计方式与数据来源分析

分析日期：2026-09-19。分析对象：`F:/SteamLibrary/steamapps/common/Slay the Spire 2/mods/DamageMeter/` 中的已编译成品。

## 1. 结论与计数口径

**此版本实际注册了 18 个局内统计分类，而不是简介中的 17 个。** 依据是 `DamageMeter.Scripts.DamageMeterUI.Initialize()` 中的 18 次 `Categories.Add(...)`，不是根据宣传文案推测。

本文按以下口径组织，避免把同一数据的图表、明细、上传副本重复算成新统计方式：

| 层次 | 数量 | 本文位置 | 如何理解 |
| --- | ---: | --- | --- |
| A：局内独立统计分类 | **18** | A01—A18，每类一张字段表 | 回答“游戏内有多少种统计方式”的主要口径；包括历史最佳 |
| B：额外采集维度 | **4** | B01—B04，每维度一张表 | 卡牌类型、充能球、星星、召唤量；没有各自独立的分类入口 |
| C：复盘、归档数据组 | **7** | C01—C07，每组一张表 | 为说明全部数据范围而作的文档分组，不是游戏宣称的 7 个新统计模式 |
| 公共元数据、归因规则、图表和持久化 | 不另计分类 | 第 2、6、7、8 节 | 支撑上面的统计；不会把个人伤害、伤害柱状图、上传伤害再计三遍 |

因此：**按独立菜单分类计为 18 类；若在此基础上把 4 个额外采集维度分别计入，则为 22 个统计条目。** C 组属于额外的数据采集/输出范围，不应简单叠加成“29 种战斗统计”。

### 样本与证据边界

| 项目 | 核对结果 |
| --- | --- |
| Mod 名称 | `皮皮统计: Skada`，ID 为 `DamageMeter` |
| 声明版本 | `DamageMeter.json`：`1.14.7` |
| 成品内容 | `DamageMeter.dll`、`DamageMeter.json`、`DamageMeter.pck` |
| DLL 大小 | 501,760 字节 |
| DLL SHA-256 | `52F4050A83D7BACDC6B19E7AF28B3A2288706B6F5F5993EC028BF4802255140E` |
| 分析方法 | 使用 ILSpy CLI 8.2.0.7535 静态反编译 DLL，交叉核对分类注册、数据结构、事件分发、计算、界面引用和 JSON 写出 |
| 中文名称依据 | DLL 内嵌资源 `DamageMeter.localization.zhs.json` |
| 结论范围 | 已核对该 DLL 客户端显式实现的统计。没有运行游戏逐场验证；不把反编译代码当成作者原始源码；未分析远端网站服务端的二次统计 |
| 原始历史档案的边界 | Mod 会原样传送游戏 `.run` JSON，因此这部分字段随游戏版本变化；C07 明确标出这种开放范围，不虚构固定字段清单 |

下文的类名、方法名和字段名均可在该 DLL 中定位。简写：`CDC` = `DamageMeter.Scripts.CombatDataCollector`；`PS` = 其嵌套类 `PlayerStats`；分类类均位于 `DamageMeter.Scripts.Categories`；分析导出类均位于 `DamageMeter.Scripts.Analytics`。方法名比反编译行号更适合跨工具复核。

## 2. Mod 主要采用的方法

### 2.1 数据流

```text
RunManager / CombatManager 公共事件
  └─ PublicApiLifecycle：开始/结束战斗，绑定回合事件
       └─ HistoryTailer：订阅 CombatHistory.Changed
            └─ 按 _lastSeenIndex 增量读取 History.Entries
                 └─ CDC.Record*：分玩家计数、累计、归因与推算
                      ├─ PlayerStats → 18 个分类、浮窗、仪表盘
                      ├─ CombatEvent → 战斗日志、死亡前快照
                      ├─ CombatSegment → 历史战斗、整体汇总
                      └─ CombatSummary → 整局分析 JSON

SerializableRun / RunHistory / MapPointHistory
  └─ AnalyticsWriter：整局、逐层、地图、环境标记
       └─ 本地 JSON → AnalyticsSyncer（分享设置允许时）

游戏已有 .run 文件
  └─ ArchiveSyncer：原始 JSON 加元数据后批量同步
```

核心战斗采集采用**公开战斗历史 + 事件订阅**，并非逐帧读取画面，也不是为每张卡分别打补丁。`DamageMeter.MainFile.Initialize()` 确实调用了 Harmony 的 `PatchAll`，但本样本找到的 Harmony 补丁是 `GameOverScreenPatch` 对 `NGameOverScreen._Ready` 的后置补丁，用于结算界面分析链接。不能根据旧更新日志把当前版本描述为“完全没有 Harmony”。

### 2.2 原始入口与字段

| 数据入口 | 原始对象/字段 | Mod 接收方法 | 用途 |
| --- | --- | --- | --- |
| `CombatHistory.Changed` | `History.Entries`，增量索引 | `HistoryTailer.Drain/DispatchEntry` | 统一分发下列 11 种历史记录；历史长度缩短时重置索引 |
| `DamageReceivedEntry` | `Dealer`、`Receiver`、`Result`、`CardSource` | `CDC.RecordDamage` | 输出、承伤、格挡吸收、过量、命中、逐回合、助攻、减伤、死亡检测 |
| `BlockGainedEntry` | `Receiver`、`Amount`、`Props`、`CardPlay` | `CDC.RecordBlockGained` | 玩家获得格挡、给队友格挡、敌人脆弱导致的格挡减少估算 |
| `CardPlayFinishedEntry` | `CardPlay.Card`、`CardPlay.Resources.EnergySpent` | `CDC.RecordCardPlay` | 实际出牌次数、类型、能耗、卡牌归属 |
| `CardDrawnEntry` | `Card` | `CDC.RecordCardDrawn` | 抽牌 |
| `CardDiscardedEntry` | `Card` | `CDC.RecordCardDiscarded` | 弃牌 |
| `CardExhaustedEntry` | `Card` | `CDC.RecordCardExhausted` | 消耗牌 |
| `OrbChanneledEntry` | `Actor` | `CDC.RecordOrbChanneled` | 每条历史记录计一次生成充能球 |
| `PotionUsedEntry` | `Potion`、`Target` | `CDC.RecordPotionUsed` | 药水次数；`Target` 传入但未用于细分统计 |
| `PowerReceivedEntry` | `Power`、`Amount`、`Applier`、`Actor` | `HistoryTailer.DispatchPowerReceived` → `CDC.RecordPowerReceived` | 减益层数及增益/减益来源追踪；目标暂不可用时延后处理 |
| `StarsModifiedEntry` | `Actor`、`Amount` | `CDC.RecordStarsModified` | 正数星星获取量 |
| `SummonedEntry` | `Actor`、`Amount` | `CDC.RecordSummoned` | 正数召唤量 |
| `CombatManager.TurnStarted` | `CombatState.RoundNumber` | `CDC.OnTurnStarted` | 当前回合编号 |
| `CombatManager.TurnEnded` | `CurrentSide`、各玩家 `PlayerCombatState.Energy` | `CDC.OnTurnEnded` | 玩家阶段结束时未用能量 |
| 敌人 `Creature.Died` | `DoomPower`、缓存 HP、普通伤害击杀标记 | `CDC.OnCreatureDied` | Doom 直接击杀的等效伤害补记 |
| `ModManager.OnMetricsUpload` | `SerializableRun`、`isVictory`、`localPlayerId` | `RunDataCollector.OnMetricsUpload` | 整局复盘数据生成入口 |

### 2.3 公共身份、分组与时间范围

| 数据/维度 | 内部字段 | 来源及规则 |
| --- | --- | --- |
| 玩家键 | `PS.Key` | `Creature.Player.NetId.ToString()`；不存在则回退 `Creature.Name` |
| 玩家名 | `PS.Name` | `PlatformUtil.GetPlayerName`；失败时回退生物名称/角色 ID。单人或伪多人优先本地平台 ID |
| 角色 | `PS.CharacterId` | `Player.Character.Id.Entry` |
| 显示颜色 | `PS.CharacterColor` | `Player.Character.NameColor`；只是展示元数据 |
| 卡牌键 | 各 `*ByCard`、`CardPlayCount` | 大多数伤害、格挡、出牌、能耗使用 `Id.Entry + (IsUpgraded ? "+" : "")`；不区分更高升级次数 |
| 未细分升级的卡牌键 | `DrawCount`、`DiscardCount`、`ExhaustCount`、`AssistBlockByCard` | 直接用 `Id.Entry`，与上行口径不同 |
| 当前/历史战斗 | `_players`、`CombatSegment.Players/TurnCount/EncounterKey` | 开始下一战时克隆上一战数据归档；遭遇 ID 来自 `CombatState.Encounter.Id.Entry` |
| 整体统计 | `BuildOverallView`、`PlayerStats.MergeFrom` | 合并所有已归档战斗和当前战斗；大多数字段相加，最高单击取最大 |
| 总览逐回合分布 | `DamagePerTurn`、`EnergyWastedPerTurn` | 不同战斗相同回合编号直接相加；表示“各战的 T1 合计”等，不是整局连续时间轴 |
| 总览的回合分母 | `CDC.CurrentTurn` | 有归档时取归档 `TurnCount` 的最大值；无归档时取当前回合。不是所有战斗回合之和，且已有归档时不把当前战斗回合加入最大值计算 |

## 3. A 组：18 个局内独立统计分类

以下顺序与 `DamageMeterUI.Initialize()` 的注册顺序一致。表中“字段”属于 `PS`，除非另有注明。各表把主榜、明细和悬浮提示一并列出。

### A01. 真实伤害（rDPS）

实现：`RdpsCategory.GetPlayerBars/GetDetailBars`。

| 统计数据 | 字段或计算方式 | 数据来源 | 口径说明 |
| --- | --- | --- | --- |
| rDPS 总贡献 | `max(0, DamageDealt - DamageBoostedByOthers + AssistDamage)` | CDC 输出及助攻归因，见 A02/A04 | 是重分配后的总伤害贡献；中文“真实伤害”不代表无视格挡，也不是每秒伤害 |
| rDPS 每回合 | rDPS 总贡献 ÷ `CurrentTurn`，整数除法 | 上项 + 回合事件/所选视图 | 界面标为 `rDPS/t` |
| 全队占比 | 个人 rDPS ÷ 各玩家 rDPS 之和 × 100% | 本分类即时求和 | 各玩家先执行非负截断 |
| 原始个人伤害 | `DamageDealt`、`DamageByCard` | `RecordDamage` 及特殊状态补记 | 明细显示原始值，提示显示主要卡牌/来源 |
| 受他人增伤总量与构成 | `DamageBoostedByOthers`、`DamageBoostedByPower` | `ComputeAndRecordAssists` → `AddDamageBoostedByOthers` | 从输出者的贡献中扣除；按 Power/遗物键拆分 |
| 为他人增伤总量与构成 | `AssistDamage`、`AssistDamageByPower` | 同一助攻计算链 | 加回提供帮助的人 |

### A02. 个人伤害

实现：`DamageDealtCategory`；核心来源：`CDC.RecordDamage`、`RecordDamageForPlayer`、`RecordDoomDamageForPlayer`。

| 统计数据 | 字段或公式 | 原始数据来源 | 口径说明 |
| --- | --- | --- | --- |
| 输出总伤害 | `DamageDealt` | `DamageResult.UnblockedDamage + BlockedDamage + OverkillDamage` | **包含敌人格挡吸收、过量伤害**；普通路径要求玩家/宠物攻击敌人 |
| 全队伤害占比 | 本人输出 ÷ 全队输出和 | `Players.Values` | 不是有效扣血占比 |
| 平均每回合伤害 | 输出 ÷ `CurrentTurn` | 输出累计 + 回合 | 整数除法 |
| 命中/伤害记录数 | `HitCount` | 每次直接伤害记录；特殊状态按分配到玩家的记录计数 | 不等于出牌数；多段/AOE 可多次计数；普通路径没有正伤害过滤 |
| 平均单次伤害 | 输出 ÷ `HitCount` | 上述两项 | 展示 1 位小数 |
| 最高单次伤害及来源 | `MaxSingleHit`、`MaxSingleHitCard` | 每次入账伤害超过旧最大值时更新 | 对按份额拆分的状态伤害，记录的是分给该玩家的份额 |
| 按卡牌/来源的伤害 | `DamageByCard` | `CardSource.Id.Entry`；宠物用 `Monster.Id.Entry`；特殊状态用 Power 键 | 名称虽然叫 ByCard，实际也含宠物、毒、Doom、Haunt、Strangle 和 `Other` |
| 每张牌出牌数、均次伤害 | `CardPlayCount[key]`、`DamageByCard[key] / CardPlayCount[key]` | 伤害记录 + `CardPlayFinishedEntry` | 仅在存在出牌次数时显示均次值 |
| 每张牌累计能耗 | `EnergySpentByCard[key]` | `CardPlay.Resources.EnergySpent` | 不是卡面费用；见 A10 |

召唤物直接伤害归属 `dealer.PetOwner.Creature`。无施伤者的状态伤害不是全量自动识别，具体支持和推断条件见第 6 节。

### A03. 伤害承受

实现：`DamageTakenCategory`、`CDC.RecordDamage`。

| 统计数据 | 字段或公式 | 数据来源 | 口径说明 |
| --- | --- | --- | --- |
| 承受伤害 | `DamageTaken` | 目标 `Receiver.IsPlayer` 时累计 `Result.UnblockedDamage` | 不加被格挡部分，也不加 `OverkillDamage`；不要求攻击者一定是敌人 |
| 每回合承伤 | 承伤 ÷ `CurrentTurn` | 上项 + 回合 | 整数除法 |
| 按来源承伤 | `DamageBySource` | 若施伤者 `IsMonster`，用 `Dealer.Monster.Id.Entry`；否则 `Unknown` | 不是按敌人实例或敌方招式细分；同种怪物会合并 |
| 主要承伤来源 | 上述字典降序 | 分类提示/明细 | 无来源、自伤等可能进入 `Unknown` |

### A04. 助攻伤害

实现：`AssistDamageCategory`、`CDC.ComputeAndRecordAssists`、`SplitAssistCredit`。

| 统计数据 | 字段或计算方式 | 数据来源 | 口径说明 |
| --- | --- | --- | --- |
| 帮助他人增加的伤害 | `AssistDamage` | 每次玩家/宠物伤害后的状态检查与反推 | 仅处理带 `ValueProp.Move` 且不带 `Unpowered` 的正总伤害 |
| 按增伤来源拆分 | `AssistDamageByPower` | Power 的 `Applier`、动态倍率；施加来源账本；遗物倍率方法 | 来源全集见第 6.2 节 |
| 全队助攻占比 | 本人助攻 ÷ 全队助攻和 | 本分类求和 | 不包含格挡助攻 |
| 外部增伤接收账 | `DamageBoostedByOthers`、`DamageBoostedByPower` | 同一次分配给助攻者后，在输出者侧同步累计 | 供 rDPS 扣除，不能再作为额外实际伤害加到队伍总输出 |

乘法增伤不是游戏提供的独立“助攻数值”，而是 Mod 按倍率反推并分摊；因此是估算归因。自身施加的增伤份额不算“帮助他人”。

### A05. 助攻格挡

实现：`AssistBlockCategory`、`CDC.RecordBlockGained`。

| 统计数据 | 字段或公式 | 数据来源 | 口径说明 |
| --- | --- | --- | --- |
| 给队友的格挡总量 | `AssistBlockGiven` | `BlockGainedEntry.Amount` | 收到格挡者和 `CardPlay.Card.Owner.Creature` 都是玩家，且不是同一人 |
| 按受益玩家拆分 | `AssistBlockByRecipient` | 接收者的玩家键 | 明细主列表展示这一维度 |
| 按提供格挡的牌拆分 | `AssistBlockByCard` | `CardPlay.Card.Id.Entry` | 提示展示主要牌；这里不添加升级后缀 |
| 全队占比 | 本人助攻格挡 ÷ 全队助攻格挡和 | 上述总量 | 只有能定位到出牌者的跨玩家格挡才进入此路径 |

此数是**给出的格挡量**，不是最后实际帮队友挡住了多少伤害。同一事件还会计入接收者 A09 的格挡总量。

### A06. 伤害减免

实现：`DamagePreventedCategory`；`ComputeWeakPrevention`、`ComputeGuardedPrevention`、`ComputeFrailBlockPrevention`。

| 统计数据 | 字段或公式 | 数据来源 | 口径说明 |
| --- | --- | --- | --- |
| 减免贡献总量 | `DamagePrevented` | 以下各分支累加 | **并非实际格挡吸收量**；还混入“敌人少获得的格挡” |
| 按效果拆分 | `DamagePreventedByPower` | `WEAK_POWER`、`PAPER_KRANE`、`DEBILITATE_POWER`、`SHRINK_POWER`、`GUARDED_POWER`、`FRAIL_POWER` | 根据效果施加者或来源份额归属 |
| 全队减免占比 | 本人减免 ÷ 全队减免和 | 本分类求和 | 是上述混合口径的比例 |
| Weak 系估计减少伤害 | `int(D × (1/m - 1))` | 伤害结果总量 D；敌方 Weak 的倍率，叠加 PaperKrane/Debilitate 修改 | `m` 为最终 Weak 倍率；多项效果再按对数权重分配 |
| Shrink 估计减少伤害 | `int(D × (1/m - 1))` | `m=(100-DamageDecrease)/100`，动态变量缺失时用 30% | 单独计算后累加；不与 Weak 联合做完整反事实模拟 |
| Guarded 估计减少伤害 | 每个符合条件的 Guarded 实例记 D | 接收玩家当前 `GuardedPower` 实例及其玩家施加者 | 代码直接按本次总伤害记账，没有读取独立的“实际减免结果” |
| Frail 减少敌人格挡 | `int(blockGained × 0.33333333333333326)` | 敌人 `BlockGainedEntry` + `FrailPower.Applier` | 按约 25% 减格挡反推；不是玩家 HP 伤害 |

上述分支均要求 `Move` 且非 `Unpowered`；伤害分支跳过 D≤0。因此零伤害、额外 Mod 效果或复合倍率不能据此认为已完整覆盖。

### A07. 每回合伤害

实现：`DptCategory`。

| 统计数据 | 字段或公式 | 数据来源 | 口径说明 |
| --- | --- | --- | --- |
| 平均每回合输出 | `DamageDealt / CurrentTurn` | A02 输出 + 当前视图回合数 | 整数除法；非每秒 DPS |
| 各回合输出 | `DamagePerTurn[turn]` | 各伤害记账函数，在 `_currentTurn` 下累加 | 包含本回合敌方阶段中归给玩家的状态伤害 |
| 最佳回合及伤害 | 字典中最大值及键 | `DamagePerTurn.MaxBy` | 仅查询已有键 |
| 最差回合及伤害 | 字典中最小值及键 | `DamagePerTurn.MinBy` | 没写入字典的零输出回合不会自动参与；多于一个键时显示 |
| 输出总量 | `DamageDealt` | A02 | 提示中的基础量 |

总体视图的分母和回合合并方式见第 2.3 节，不能直接当成“整局总伤害 ÷ 整局总回合”。

### A08. 卡牌使用

实现：`CardUsageCategory`、`CDC.RecordCardPlay`。

| 统计数据 | 字段 | 数据来源 | 口径说明 |
| --- | --- | --- | --- |
| 总出牌次数 | `CardsPlayed` | 每条 `CardPlayFinishedEntry`，定位 `Card.Owner.Creature` | 按游戏记录的播放完成事件计数，不是点击次数或不同卡牌张数 |
| 每种卡牌出牌次数 | `CardPlayCount` | 卡牌 ID，含是否升级的 `+` 后缀 | 重复打出同种牌累计；不按卡牌实例区分 |
| 最常使用牌 | 字典降序及 Top 提示 | 上项 | 只是相同数据的排名 |

同一事件还采集 `Card.Type`，在仪表盘类型分布中使用，单列为 B01。

### A09. 格挡

实现：`BlockCategory`、`CDC.RecordBlockGained`。

| 统计数据 | 字段或公式 | 数据来源 | 口径说明 |
| --- | --- | --- | --- |
| 获得格挡总量 | `TotalBlockGained` | 玩家接收的正数 `BlockGainedEntry.Amount` | 归属于接收者；不是当前剩余格挡、实际吸收伤害或浪费格挡 |
| 按牌/来源获得格挡 | `BlockByCard` | `CardPlay.Card` 的 ID 和升级后缀；无法定位则 `Other` | 非卡牌的格挡来源不会自动细分为每个遗物/Power |
| 每回合平均格挡 | 总格挡 ÷ `CurrentTurn` | 上项 + 回合 | 整数除法 |
| 主要格挡来源 | 字典降序 | `BlockByCard` | 提示、明细及仪表盘复用 |

### A10. 能量

实现：`EnergyCategory`；采集来自 `RecordCardPlay`、`OnTurnEnded`。

| 统计数据 | 字段或公式 | 数据来源 | 口径说明 |
| --- | --- | --- | --- |
| 出牌实际消耗能量 | `TotalEnergySpent` | `CardPlay.Resources.EnergySpent` | 不是印刷费用，也不是回合内所有可能形式的能量流出 |
| 按卡牌消耗能量 | `EnergySpentByCard` | 上项，按卡牌键累计正值 | 供个人伤害提示和效率分类使用 |
| 未用能量累计 | `TotalEnergyWasted` | 玩家阶段结束时每个玩家的 `PlayerCombatState.Energy` | 只累计正值；直接当作浪费，不检查下一回合是否保留 |
| 每回合未用能量 | `EnergyWastedPerTurn` | 同上，按 `_currentTurn` 累加 | 明细仅列正值；非能量总供应量 |
| 浪费率 | `wasted / (spent + wasted) × 100%` | 上述两项 | 此公式的分母是已花费加结束剩余，不是读取“总生成能量” |
| 浪费最多的回合 | `EnergyWastedPerTurn.MaxBy` | 逐回合剩余能量 | 提示附回合号和数值 |

该分类主榜跳过 `TotalEnergySpent <= 0` 的玩家，即使其有未用能量。仪表盘能量条的显示条件更宽，二者并不完全一致。

### A11. 伤害/能量（卡牌效率）

实现：`CardEfficiencyCategory`。

| 统计数据 | 字段或公式 | 数据来源 | 口径说明 |
| --- | --- | --- | --- |
| 玩家总体效率 | `DamageDealt / TotalEnergySpent` | A02 + A10 | 单位 `DMG/E`；分子含宠物/状态等归属伤害，不仅是花费能量的牌造成的伤害 |
| 各牌效率 | `DamageByCard[key] / EnergySpentByCard[key]` | 同键的伤害和能耗字典 | 只列该键能耗 >0 的项目，零费牌不显示为无穷大 |
| 效率前三牌 | 上项降序取 3 | 同上 | 提示信息 |
| 单牌出牌数、伤害和能耗 | `CardPlayCount`、`DamageByCard`、`EnergySpentByCard` | 出牌完成与伤害历史 | 支持复核效率 |
| 排序/条形图值 | `max(1, int(效率 × 10))` | 分类内部派生 | 实际显示效率保留 1 位小数，条形图不是原始整数伤害 |

不统计格挡/能量效率，也未实现对卡牌全部辅助价值的统一评分。

### A12. 过度杀伤

实现：`OverkillCategory`。

| 统计数据 | 字段或公式 | 数据来源 | 口径说明 |
| --- | --- | --- | --- |
| 过量伤害 | `OverkillDealt` | `DamageResult.OverkillDamage` | 普通路径直接累计；特殊按份额归因路径有例外，见第 6.1 节 |
| 过量比例 | `OverkillDealt / DamageDealt × 100%` | A02 总输出 | 分母包含格挡和过量 |
| 被目标格挡的伤害 | `BlockedByTarget` | `DamageResult.BlockedDamage` | 是玩家输出被敌人挡住，并非玩家自己挡住伤害 |
| 计算出的有效部分 | `DamageDealt - OverkillDealt - BlockedByTarget` | 上述三项 | 大于 0 才列出；对未细分过量/格挡的状态份额不能保证等于实际扣血 |
| 原始伤害总量 | `DamageDealt` | A02 | 提示中供比较 |

主榜只显示过量伤害 >0 的玩家。没有保存“按每张卡牌拆分的过量伤害”字典。

### A13. 药水使用

实现：`PotionCategory`、`CDC.RecordPotionUsed`。

| 统计数据 | 字段 | 数据来源 | 口径说明 |
| --- | --- | --- | --- |
| 使用次数 | `PotionsUsed` | `PotionUsedEntry`，`Potion.Owner.Creature` | 每条有效记录加 1，归药水所有者 |
| 各药水使用次数 | `PotionUseCount` | `Potion.Id.Entry` | 按药水种类统计 |
| 主要使用药水 | 字典降序 | 上项 | 提示及明细 |

此分类不计算各药水治疗量、伤害、格挡效率，也不保存药水目标明细。药水造成的其他事件若进入通用采集路径，会按通用规则处理。

### A14. 减益施加

实现：`DebuffsCategory`、`CDC.RecordPowerReceived`。

| 统计数据 | 字段或公式 | 数据来源 | 口径说明 |
| --- | --- | --- | --- |
| 各减益累计施加量 | `DebuffsApplied[Power.Id.Entry] += (int)max(1, amount)` | `PowerReceivedEntry` | 要求 `Power.Type == Debuff`、目标是敌人、施加者是玩家 |
| 减益总量 | `DebuffsApplied.Values.Sum()` | 上项 | 将不同减益的层数/计量单位相加，不是去重种数 |
| 按减益类型排名 | 字典降序 | `Power.Id.Entry` | 名称用 Power 本地化解析 |
| 战斗日志中的施加事件 | `DebuffApplied`、回合、标签、计量值 | 同一入口 | 记录的是本次计入量 |

该分支没有先要求 `amount > 0`，所以若游戏历史提供零或负数事件，代码仍至少记 1。后续用于归因的来源追踪分支则明确要求正数。`Misery` 的无玩家施加者场景有补推逻辑，见第 6 节。

### A15. 卡牌流转

实现：`CardFlowCategory`、`CDC.RecordCardDrawn/Discarded/Exhausted`。

| 统计数据 | 字段或公式 | 数据来源 | 口径说明 |
| --- | --- | --- | --- |
| 抽牌数 | `CardsDrawn` | `CardDrawnEntry.Card.Owner` | 每条历史加 1 |
| 弃牌数 | `CardsDiscarded` | `CardDiscardedEntry.Card.Owner` | 不区分主动弃牌与回合结束弃牌 |
| 消耗牌数 | `CardsExhausted` | `CardExhaustedEntry.Card.Owner` | 每条历史加 1 |
| 按卡牌抽牌次数 | `DrawCount` | `Card.Id.Entry` | 已采集和持久化；该分类明细不逐牌展示抽牌字典 |
| 按卡牌弃牌次数 | `DiscardCount` | `Card.Id.Entry` | 已采集和持久化；该分类明细不逐牌展示弃牌字典 |
| 按卡牌消耗次数 | `ExhaustCount` | `Card.Id.Entry` | 明细列消耗次数前 5 的牌，提示也显示主要消耗牌 |
| 主条形值 | 抽牌 + 弃牌 + 消耗 | 上述三个总数 | 标签分别显示三项，玩家顺序按抽牌数降序；条长不代表抽牌数单项 |
| 仪表盘流转中的出牌数 | `CardsPlayed` | A08 | 仪表盘额外加入出牌一栏，不是重新采集 |

### A16. 死亡回放

实现：`DeathLogCategory`、`CDC.SnapshotDeathLog`。

| 统计数据 | 字段或结构 | 数据来源 | 口径说明 |
| --- | --- | --- | --- |
| 死亡/存活显示 | `_deathLogs` 是否含玩家键 | 玩家受到伤害且 `Result.WasTargetKilled` | 根据是否捕获过死亡快照，不是持续查询当前 HP |
| 死前事件 | 该玩家最近 8 条 `CombatEvent` | 从全局 `_eventLog` 筛玩家后 `TakeLast(8)` | 全局日志最多 200 条；只在该玩家第一次建立死亡快照时记录 |
| 事件类型 | `CombatEvent.Type` | 输出、承伤、格挡、出牌、药水、减益六类 | 不包含抽弃消耗的完整流水 |
| 回合、玩家、说明、数值 | `Turn`、`PlayerKey`、`Label`、`Value` | 创建事件时填入 | 没有逐事件 HP、剩余格挡或时间戳快照，不是动画重放 |

开始新战斗会清空死亡日志；`CombatSegment` 不保存它，切到旧战斗不会自动得到那个战斗的死亡回放。

### A17. 战斗日志

实现：`CombatLogCategory`、`CDC.LogEvent/GetPlayerEvents/GetPlayerEventCount`。

| 统计数据 | 字段或结构 | 数据来源 | 口径说明 |
| --- | --- | --- | --- |
| 玩家事件数 | 当前 `_eventLog` 中该玩家记录数 | 按 `PlayerKey` 筛选 | 受全局 200 条上限影响，不是从开战起无限累计 |
| 输出事件 | `DamageDealt`，伤害值 | 直接伤害和特殊归因；入账值 >0 时写日志 | 标签包含来源名称 |
| 承伤事件 | `DamageTaken`，未格挡伤害 | `RecordDamage`，正数时写日志 | 标签包含怪物来源名或未知 |
| 格挡事件 | `BlockGained`，格挡量 | `RecordBlockGained` | 归接收格挡的玩家 |
| 出牌事件 | `CardPlayed`，实际消耗能量 | `RecordCardPlay` | 数值不是出牌次数；零能量仍可有事件 |
| 药水事件 | `PotionUsed`，值为 1 | `RecordPotionUsed` | 药水名 |
| 减益事件 | `DebuffApplied`，本次计入量 | `RecordPowerReceived` | 减益名与数量 |
| 事件共同字段 | `Type/Turn/PlayerKey/Label/Value` | `CombatEvent` 记录结构 | 明细按倒序显示该玩家最近最多 50 条 |

事件条形图使用 `max(1, Value)` 保证可见，不代表零值事件实际数值变为 1。日志不随整体/旧战斗视图重新聚合，而是使用当前保留的事件缓冲。

### A18. 历史最佳

实现：`RecordsCategory`、`DamageMeterSettings.PersonalRecords/UpdateRecords`。

| 统计数据 | 字段 | 原始来源及更新规则 | 实际统计主体 |
| --- | --- | --- | --- |
| 最高单次伤害 | `HighestHit`、`HighestHitCard` | 遍历本场所有玩家的 `MaxSingleHit`，超过纪录时替换 | 所有记录到的玩家中最高，不限本机玩家 |
| 单场最高伤害 | `MostFightDamage` | 本场所有玩家 `DamageDealt` **求和**，再与历史最大比较 | 全队单场总伤害 |
| 最高回合伤害 | `BestTurnDamage` | 每位玩家 `DamagePerTurn` 最大值，再取历史最大 | 单个玩家某回合，不是全队该回合总和 |
| 单场最多出牌 | `MostCardsPlayed` | 各玩家 `CardsPlayed` 与旧纪录比较 | 单个玩家单场最大 |
| 单场最多格挡 | `MostBlockGained` | 各玩家 `TotalBlockGained` 与旧纪录比较 | 单个玩家单场最大 |
| 累计总伤害 | `TotalDamage`（long） | 每次归档把本场全队输出和累加 | 跨归档全队累计；显示可缩写 K/M |
| 完成战斗数 | `TotalFights` | 每次 `UpdateRecords` 加 1 | 统计归档次数；并未在此验证战斗胜利 |

`UpdateRecords` 的调用点在 `CDC.ArchiveAndStartNew` 归档上一战时。不能把 UI 名称理解为“每次战斗结束立即更新本机个人纪录”；最后一战若没有进入下一战，可能尚未触发该归档更新。纪录存于 `user://DamageMeter_settings.json`，不是普通战斗视图的即时合计。

## 4. B 组：4 个额外采集维度

### B01. 卡牌类型分布（仪表盘统计）

| 统计数据 | 字段或公式 | 数据来源 | 展示/用途 |
| --- | --- | --- | --- |
| 各类型出牌次数 | `PS.CardTypeCount[CardType]` | `CardPlayFinishedEntry.CardPlay.Card.Type` → `RecordCardPlay` | 攻击、技能、能力等，以实际枚举值为准 |
| 类型占比与总出牌量 | 各类型次数 ÷ 图中总次数 | `DamageMeterUI.BuildCenterChart` → `DashboardCharts.DonutChart` | 仪表盘圆环图；复用出牌事件，没有独立分类 |

### B02. 充能球生成

| 统计数据 | 字段 | 数据来源 | 展示/用途 |
| --- | --- | --- | --- |
| 生成充能球次数 | `PS.OrbsChanneled` | `OrbChanneledEntry.Actor` → `RecordOrbChanneled(actor)`，默认 amount=1 | 已采集；本地状态和分析 JSON `orbs` |
| 统计条件 | 玩家 Actor、正数量、正在追踪 | 同上 | 没有按球类型、激发次数、被动触发或各球伤害拆分的专门字典 |

### B03. 星星获取

| 统计数据 | 字段 | 数据来源 | 展示/用途 |
| --- | --- | --- | --- |
| 累计获得星星 | `PS.StarsGained` | `StarsModifiedEntry.Actor/Amount` → `RecordStarsModified` | 只累加正数；本地状态和分析 JSON `stars` |
| 统计条件 | 玩家 Actor、`Amount > 0` | 同上 | 不是当前持有量；没有累计星星花费和逐牌星星效率 |

### B04. 召唤量

| 统计数据 | 字段 | 数据来源 | 展示/用途 |
| --- | --- | --- | --- |
| 累计召唤量 | `PS.SummonsCreated` | `SummonedEntry.Actor/Amount` → `RecordSummoned` | 累加事件 `Amount`，不是无条件每次 +1；本地状态和分析 JSON `summons` |
| 统计条件 | 玩家 Actor、`Amount > 0` | 同上 | 不能仅凭字段名解释成“生成的独立宠物实体数”；没有召唤类型字典 |

以上 B02—B04 未注册为独立 `IStatCategory`，也未在当前局内仪表盘引用为独立 KPI。宠物造成的伤害仍走 A02，与 B04 是不同统计。

## 5. C 组：复盘与归档的数据范围

这部分说明 Mod 除局内浮窗外还记录什么。主链路是 `RunDataCollector` → `CombatSummary.SnapshotCurrent` / `AnalyticsWriter.WriteRunData`。实时分析数据的落盘、上传受分享同意设置控制：未询问时可先收集战斗快照；明确禁用后跳过分析快照/上传；首次同意后才写出待处理整局数据。局内计数不等于已上传。

### C01. 单场战斗复盘快照

| 输出字段 | 统计数据 | 数据来源/转换 | 限制 |
| --- | --- | --- | --- |
| `f` | 所在楼层 | 开战时 `CombatState.RunState.TotalFloor`，必要时结束时回退读取 | 不是遭遇索引 |
| `enc` | 遭遇 ID | 开战 `Encounter.Id.Entry`，结束时可回退 | 名称需另做本地化 |
| `etype` | 战斗类型 | 结束房间 `RoomType` → Boss=B、Elite=E、其他=N | 不同于逐层地图类型中的普通战斗 M |
| `turns` | 战斗回合 | 结束时 `room.CombatState.RoundNumber`，或 CDC 回合 | 不是出牌次数 |
| `won` | 该战胜利标记 | `!RunManager.Instance.IsGameOver` | 客户端采用的判断式，不是独立胜利事件值 |
| `enemies` | 敌人 `[Id, MaxHp]` 数组 | **开战时**遍历 `CombatState.Enemies`，取 `ModelId.Entry`、`MaxHp` | 没有在此持续加入战斗中新召唤的敌人 |
| `p[].char` | 玩家角色 ID | `PS.CharacterId` | 快照遍历当前统计到的全部玩家；没有逐项写出玩家 NetId/姓名 |
| `p[].dmg / taken / blk` | 输出、承伤、格挡 | `DamageDealt / DamageTaken / TotalBlockGained` | 沿用 A 组口径 |
| `p[].cards / nrg_s / nrg_w` | 出牌、花费能量、未用能量 | `CardsPlayed / TotalEnergySpent / TotalEnergyWasted` | 同上 |
| `p[].draw / disc / exh` | 抽牌、弃牌、消耗 | 三项流转总数 | 不输出按牌抽弃消耗字典 |
| `p[].orbs / stars / summons` | 充能球、星星、召唤量 | B02—B04 | 没有细分类型 |
| `p[].over / pot / maxhit` | 过量、药水次数、最大单击 | `OverkillDealt / PotionsUsed / MaxSingleHit` | 不输出最大单击来源键 |
| `p[].assist / prevent` | 助攻伤害、减免 | `AssistDamage / DamagePrevented` | 估算累计值 |
| `p[].dbc` | 按卡牌/来源输出 | `DamageByCard` 字典副本 | 非空才输出 |
| `p[].bbc` | 按卡牌格挡 | `BlockByCard` 字典副本 | 非空才输出 |
| `p[].pbc` | 按卡牌出牌次数 | `CardPlayCount` 字典副本 | 非空才输出 |
| `p[].ebc` | 按卡牌花费能量 | `EnergySpentByCard` 字典副本 | 非空才输出 |
| `p[].debuf` | 按减益施加量 | `DebuffsApplied` 字典副本 | 非空才输出 |
| `p[].dpt` | 逐回合输出数组 | `DamagePerTurn` 从 T1 到最大已有键填充，缺失回合填 0 | 不自动延长到整场最后一回合 |

**上传快照不是完整 PS 的序列化。** 此结构未写出 `AssistBlockGiven`、`DamageBoostedByOthers`、rDPS、`HitCount`、`BlockedByTarget`、承伤来源、各类助攻/减伤来源字典、卡牌类型分布、逐回合未用能量、战斗日志和死亡回放。因此不能假定远端凭此快照可以复现所有局内分类。

### C02. 整局结果与最终配置

| 输出字段（`run.*`） | 数据 | 来源/公式 |
| --- | --- | --- |
| `char` | 本地玩家角色 | `run.Players` 中 NetId 匹配 `localPlayerId` 的玩家；无匹配回退首位 |
| `asc` | 进阶等级 | `SerializableRun.Ascension` |
| `seed` | 随机种子 | `SerializableRng.Seed` |
| `mode` | STANDARD/DAILY/CUSTOM | 优先反射读取 `SerializableRun.GameMode`，再查 `RunHistory.GameMode`；有 modifiers 强制 CUSTOM；失败回退 STANDARD |
| `modifiers` | 自定义规则 ID 列表 | `SerializableRun.Modifiers[].Id.Entry`；空时 null |
| `win` | 整局胜利 | `OnMetricsUpload` 参数 `isVictory` |
| `abandoned` | 是否放弃 | `RunHistory.WasAbandoned`，或 `RunManager.IsAbandoned` |
| `death` | 致死遭遇/事件 ID | `RunHistory.KilledByEncounter`，其次 `KilledByEvent`；胜利、放弃或不可用时 null |
| `floor` | 经过楼层数 | 扁平化 `run.MapPointHistory` 后的条目数 |
| `time` | 游戏记录的运行时长 | `run.RunTime` 原值；该写出代码未转换单位 |
| `players` | 玩家数 | `run.Players.Count` |
| `deck` | 最终卡组 | 本地玩家 `Deck`；卡 ID + 是否升级的 `+` 后缀 |
| `relics` | 最终遗物 | 本地玩家 `Relics[].Id.Entry` |
| `potions` | 最终药水槽内容 | 本地玩家 `Potions[].Id.Entry`；无 ID 可写 null |

这些是整局快照，不是 Mod 自己从每次拾取、购买事件重建的全套结果。除战斗快照 `p[]` 外，本节和下节主要针对选择出的本地玩家。

### C03. 逐层成长、选择与资源变化

数据入口：`AnalyticsWriter.WriteRunData` 遍历 `run.MapPointHistory`，对每层调用 `GetEntry(本地玩家NetId)`。

| 输出字段（`run.floors[]`） | 数据 | 原始来源与转换 | 注意事项 |
| --- | --- | --- | --- |
| `f`、`type` | 从 1 计的楼层序号、地图房间类型 | 遍历索引；`MapPointType` | M普通战、E精英、B首领、T宝箱、S商店、V未知、R休息、A远古、其他 `?` |
| `hp` | `[推算进入HP, 离开HP]` | `[CurrentHp + DamageTaken - HpHealed, CurrentHp]` | 进入值由历史增减量倒推，不是进入时实时快照；原始 DamageTaken/HpHealed 未单独输出 |
| `gold` | `[推算进入金币, 离开金币]` | `[CurrentGold - GoldGained + GoldSpent, CurrentGold]` | 原始获得/花费量参与推算，未单独输出 |
| `pick` | 卡牌候选列表和选中下标 | `CardChoices[].Card/wasPicked` | 未选为 -1；写为一组候选加最终选中索引，多个选中项仅留下最后匹配索引 |
| `relic_pick` | 遗物候选与选中下标 | `RelicChoices[].choice/wasPicked` | 同样使用末尾索引表示选择 |
| `event` | 事件选择说明 | `EventChoices.Last().Title.GetFormattedText()` | 只保存最后选择文本，不是整段事件交互过程 |
| `event_id` | 事件 ID | 该地图点首个 Event 房间的 `ModelId.Entry` | 与事件选项文本分开 |
| `camp` | 休息点选择 | `RestSiteChoices.Last()` | 只保存最后选择 |
| `shop.relics` | 购买遗物列表 | `BoughtRelics` | 没有写逐件价格 |
| `shop.potions` | 购买药水列表 | `BoughtPotions` | 同上 |
| `shop.cards` | 购买牌列表 | **`BoughtColorless`** | 代码读取的是该字段，不可自行声称覆盖全部商店卡牌 |
| `shop.removed` | 移除卡牌列表 | `CardsRemoved` | 只要有移除牌就满足创建 shop 对象的条件，不代表一定发生在商店 |
| `upgrade` | 升级卡牌 ID 列表 | `UpgradedCards` | 单独输出升级事件涉及的卡 |
| `remove` | 非 shop 对象下的移除列表 | `CardsRemoved` 且未创建 `shop` | 按当前逻辑，CardsRemoved 非空已会创建 shop，故此后置分支通常不可达 |
| `anc_pick` | 远古候选项与选中下标 | `AncientChoices[].TextKey/WasChosen` | 保留文本键及最后匹配索引 |
| `gained` | 获得卡牌 | `CardsGained` | 可含升级后缀；不等价于只记录奖励选择 |
| `transform` | 卡牌变化前后配对 | `CardsTransformed[].OriginalCard/FinalCard` | 原始牌仅 ID，最终牌通过 `GetCardId` 可含 `+` |
| `pot_used` | 本层使用药水 ID 列表 | `PotionUsed` | 直接读逐层历史，可与 A13 战斗期次数覆盖范围不同 |

### C04. 地图结构与行进路线

实现：`AnalyticsWriter.ExtractMapActs`，地图优先来自 `RunDataCollector` 缓存，其次 `run.Acts[i].SavedMap`。

| 输出字段（`map_acts[]`） | 数据 | 数据来源 |
| --- | --- | --- |
| `act` | 幕索引 | `run.Acts` 的零起始索引 |
| `width / height` | 网格宽高 | `SerializableActMap.GridWidth/GridHeight` |
| `nodes[].coord` | 节点 `[col,row]` | `Points[].Coord`；还追加首领、第二首领、起点特殊节点 |
| `nodes[].type` | 节点类型 | `PointType` → C03 相同的类型编码 |
| `nodes[].children` | 出边目标坐标 | `ChildCoords`，非空才写出 |
| `start_coords` | 可用起始路径坐标 | `StartMapPointCoords` |
| `boss` | 首领节点坐标 | `BossPoint.Coord` |
| `second_boss` | 第二首领坐标 | `SecondBossPoint.Coord`，存在才写出 |
| `visited_coords` | 已走过的坐标序列 | 当前幕 `run.VisitedMapCoords`，旧幕用缓存 |

这是地图和路线原始结构，不是客户端已经算好的路线胜率或最优路径评分。

### C05. 重载、撤销、控制台和 Mod 环境标记

| 输出字段/内部数据 | 数据 | 计算来源 | 解释边界 |
| --- | --- | --- | --- |
| `flags.console_mod` | 是否安装控制台相关 Mod | `RunDataCollector.DetectConsoleModInstalled` 检查已加载 Mod | 不等于实际使用过命令 |
| `flags.console_used` | 是否检测到控制台使用 | `NDevConsole.Visible` 为真，或命令历史数量比开局基线增加，或最新命令变化 | 仅打开控制台也可能标记；启发式检测，非完整行为审计 |
| `flags.bug_exploit` | 特定异常标记 | 任意层最后营地选择为 SMITH 且 `UpgradedCards.Count >= 3` | 只是此一条规则，不是通用作弊检测 |
| `integrity.continue_reload` | 继续/重载标记 | `IntegrityDetector.OnRunStarted`：已有 token 的 StartTime、CharacterId 与当前存档一致 | 正常继续游戏也可能触发 |
| `integrity.hard_reload` | 进度回退标记 | 同 token 下当前幕更低，或同幕已访问节点数小于历史最大 | 由保存 token 与当前存档比较推断 |
| `integrity.sl` | SL 汇总标记 | `ContinueReloadDetected OR HardReloadDetected` | 不能直接当作弊结论 |
| `integrity.undo_used` | 撤销/重载综合标记 | SL，或反射读取 `Rewind.Scripts.RunStatsTracker.UndoCountThisRun > 0` | 是布尔量，不输出具体撤销次数；实现可空 |
| `mods` | 已加载 Mod 列表 | `EnumerateLoadedMods` → `id@version` | 若缺本 Mod 条目则补 `DamageMeter@1.14.7` |
| 重载检测本地 token | `StartTime/CharacterId/Ascension/PlayerCount/MaxActIndex/MaxVisitedMapCoords` | `RunManager.ToSave(null)` 与旧 token | 存在 `%APPDATA%/SlayTheSpire2/skada_run_token.json`，整局结束删除 |

### C06. 分析记录元数据与可选玩家展示

| 输出字段 | 数据 | 数据来源/条件 |
| --- | --- | --- |
| `v` | 分析格式版本 | 固定 2 |
| `cid` | 平台玩家 ID 的稳定摘要 | 玩家 ID 十进制字符串的 SHA-256，小写十六进制取前 12 位；优先当前本地平台玩家 ID，失败回退事件参数 ID |
| `mv` | Mod 版本 | 固定 `1.14.7` |
| `gv` | 游戏版本标识 | `RunHistory.BuildId`，无则 `schema-{run.SchemaVersion}` |
| `ts` | 写出时间 | UTC Unix 秒；不是逐次战斗事件时间 |
| `player_name` | 平台显示名称 | `ShowNameEnabled` 或 `LeaderboardEnabled` 时查询 `PlatformUtil.GetPlayerName`，非空才输出 |
| `leaderboard` | 参加排行榜 | 启用排行榜时写 true |

### C07. 原始历史档案同步

实现：`ArchiveSyncer.ResolveHistoryDirectories/SyncArchives/InjectMetadata`。这是与 C01—C06 的定制 JSON **不同**的一条输出链路。

| 数据 | 数据来源 | Mod 对内容的处理 |
| --- | --- | --- |
| 原始 `.run` JSON 的全部已有字段 | 游戏账户目录下 `profile*/saves/history/*.run` 和 `modded/profile*/saves/history/*.run` | `File.ReadAllTextAsync` 后整体发送，没有按字段白名单重建或删除字段；字段全集由游戏保存格式决定 |
| `_filename` | 文件名 | 注入原始 JSON 顶层 |
| `_source` | 固定 `archive` | 标识原始档案来源 |
| `_mod_version` | 固定 `1.14.7` | 注入顶层 |
| `_cid` | C06 的摘要 ID | 可用才注入 |
| 同步进度 | `last_synced_file` | 本地 `archive_sync_state.json` 保存文件名水位，不是战斗统计值 |

每批最多 10 个档案，跳过大于 524,288 字节的文件。接口路径为 `/api/runs/submit-archive-batch`。**由于它透传整个游戏档案，不能用前面列出的定制分析字段反推其全部传输内容。** 本文不读取用户真实战绩档案；此表精确描述该 Mod 的字段选择策略和新增字段。远端如何解析档案、生成胜率等不在本地 DLL 的可证实范围内。

## 6. 特殊伤害与辅助价值如何归因

这部分不是新增分类，而是 A02/A04/A06 的关键数据来源。

### 6.1 特殊状态伤害

| 情况 | 判断/来源 | 如何计入玩家 | 已见限制 |
| --- | --- | --- | --- |
| 玩家直接伤害 | Dealer 是玩家、Receiver 是敌人 | 按卡牌来源入账 | 无卡牌为 `Other`，不自动追溯具体遗物 |
| 宠物伤害 | Dealer.IsPet | 归 PetOwner；来源标签为怪物模型 ID 或 Pet | 不按宠物实例建立独立玩家 |
| Poison | Dealer 为空、目标敌人，历史事件侧为 Enemy 且有 PoisonPower | `_poisonSources` 保存玩家累计施加量；本次伤害按份额分配 | 是事件上下文推断；不是游戏历史直接标出“此伤害来自毒” |
| Strangle | Dealer 为空、目标敌人，历史侧为 Player 且有 StranglePower | `_strangleSources` 按累计施加量分配 | 同样是上下文推断 |
| Haunt | 无施伤者；上次已完成卡牌为 Soul，遍历玩家找到 HauntPower | 归第一个匹配的 Power.Owner，以 `HAUNT_POWER` 为来源 | 多人同时存在时不能当成精确全量来源解析 |
| Doom 击杀 | 敌人 Died；未标记普通伤害击杀；有 DoomPower；有先前 HP | 以缓存剩余 HP 为等效伤害，按 `_doomSources` 分；无账本则回退 Power.Applier，再回退单人唯一玩家 | 是“击杀等效伤害”，非 DamageResult；HP 快照跳过无限 HP 敌人 |
| Misery 施加者修正 | 先见 Misery 伤害且 Dealer 为玩家，随后敌方减益没有有效玩家 Applier | 临时把该次减益来源修正为 Misery 玩家 | 依赖事件次序，不是通用施加来源追溯 |

`TrackDebuffSource` 对宠物施加的状态可归宠物主人。Poison/Strangle/Doom 按各玩家累计施加量比例分摊，前面的份额整数截断，最后一份接余数。它不追踪每一层状态的精确消耗寿命。

Poison/Strangle 调用 `RecordDamageForPlayer(..., overrideDmg)` 时，分配后的伤害进入 `DamageDealt`，但该分支把过量和被格挡增量设为 0。因此这两类状态虽然参与总输出，其过量/格挡拆分会丢失；不能把 A12 的差值一概解释成精确实际扣血。

### 6.2 助攻增伤来源全集

令 D 为本次 `Unblocked + Blocked + Overkill`。代码先读取下列乘法修饰项，求乘积 M；Leadership 和外部 Strength 的加法贡献按 M 放大后先行记账为 A。剩余伤害 `R=D-A` 的乘法增量为 `R-R/M`，各项按照 `log(m_i)/log(M)` 分摊并取整数。无足够倍率或剩余伤害时跳过对应分支。

| 来源键 | 原始对象/字段 | 归属方法 |
| --- | --- | --- |
| `VULNERABLE_POWER` | 目标 Vulnerable 的 `DynamicVars["DamageIncrease"].BaseValue`，失败默认 1.5 | 优先用 `_vulnerableSources` 分摊，排除输出者自身份额；否则用 Applier |
| `PAPER_PHROG` | 输出者 `PaperPhrog.ModifyVulnerableMultiplier` | 取修改前后相对倍率；归输出者，因自助条件通常不会形成给别人的助攻 |
| `CRUELTY_POWER` | 输出者 Cruelty 的 `ModifyVulnerableMultiplier` | 相对倍率；优先 Power.Applier，否则输出者 |
| `DEBILITATE_POWER` | 目标 Debilitate 的 `ModifyVulnerableMultiplier` | 相对倍率；优先 `_debilitateSources`，否则 Applier |
| `FLANKING_POWER` | 目标各 Flanking 实例的 `Amount`，>1 才入项 | 各实例 Applier |
| `KNOCKDOWN_POWER` | 目标各 Knockdown 实例的 `Amount`，>1 才入项 | 各实例 Applier |
| `SLOW_POWER` | 目标 Slow 的 `SlowAmount`，倍率为 `1+0.1×SlowAmount` | Power.Applier |
| `LEADERSHIP_POWER` | 其他玩家身上的 `LeadershipPower.Amount` | 给该玩家记按 M 放大的 Amount，不读取某次独立助攻结果 |
| `STRENGTH_POWER` | 收到的正数 Strength/TemporaryStrength 记录与当前 StrengthPower.Amount | `_externalStrengthGrants` 中各外部玩家的赠予量；总有效量限制为当前力量与累计外部赠予量较小者，再按赠予比例分配 |

Vulnerable、Debilitate 等来源份额可用 `BuildCappedSourceShares` 按当前状态量缩放，但每份至少 1，不能解释为完全精确的逐层追踪。未列出的其他游戏或 Mod 增伤效果，不能默认已纳入。

### 6.3 减免来源和归因

| 来源键 | 原始来源 | 分配依据 |
| --- | --- | --- |
| `WEAK_POWER` | 攻击者 Weak，动态变量 DamageDecrease，默认 0.75 | Weak/PaperKrane/Debilitate 联合倍率反推，再按对数权重分配；玩家份额用 `_weakSources` |
| `PAPER_KRANE` | 受击玩家 PaperKrane 的 `ModifyWeakMultiplier` | 归受击玩家，按相对倍率 |
| `DEBILITATE_POWER` | 攻击者 Debilitate 的 `ModifyWeakMultiplier` | 来源账本或 Applier |
| `SHRINK_POWER` | 攻击者 Shrink 的 DamageDecrease，默认 30 | `_shrinkSources` 按施加量分，缺失时 Applier |
| `GUARDED_POWER` | 受击者各 Guarded 实例 | 符合条件的玩家 Applier |
| `FRAIL_POWER` | 敌方格挡事件及 Frail.Applier | 玩家 Applier；没有单独 Frail 多来源份额账本 |

这里的“减免”混合了反推伤害和少获得的格挡，且 Weak、Shrink 分开估算相加，不能视为所有减伤效果的严格、无重复反事实总和。

## 7. 仪表盘如何使用这些统计

| 视图/图表 | 数据来源 | 是否新增独立统计 | 解释注意事项 |
| --- | --- | --- | --- |
| 玩家摘要 KPI | 输出、rDPS、DPT、格挡、承伤、出牌、最高单击 | 否 | 摘要的 rDPS 直接用输出+助攻−受助，没有分类 A01 的 `max(0, ...)` 截断；多人时输出、格挡、出牌最高者高亮 |
| 回合柱状图 | `DamagePerTurn` 或 `EnergyWastedPerTurn` | 否 | 均线为“字典值之和 ÷ 最大回合键”，不必等于分类使用 CurrentTurn 算出的平均 |
| 出牌类型圆环 | `CardTypeCount` | B01 | 进攻和资源上下文都可能显示它 |
| 格挡来源圆环 | `BlockByCard` 前 6 项 | 否 | 图上占比是传入这 6 项的合计作分母，未必是全量格挡 |
| 减益圆环 | `DebuffsApplied` 前 6 项 | 否 | 同样是截取后集合的比例 |
| 能量使用条 | `TotalEnergySpent`、`TotalEnergyWasted` | 否 | 已用/未用及百分比；不是能量生成流水 |
| 横向排行 | 对应字典通常取前 7 项 | 否 | 右侧非资源图默认使用主要伤害来源，需看实际绑定字段 |
| 分类小面板、悬浮 Top、明细条 | 各 `IStatCategory.GetPlayerBars/GetDetailBars` | 否 | 同一数据的不同层级展示 |

有几处标题与图表数据不是一一对应：选择“助攻格挡”时左图实际用 `BlockByCard`（个人收到格挡来源），不是 `AssistBlockByRecipient`；选择“伤害/能量”时左图用 `CardPlayCount`，不是效率；选择 rDPS、助攻伤害或过度杀伤时，左侧回合图仍使用 `DamagePerTurn`。分析时应以字段绑定为准。

## 8. 保存位置、覆盖范围与复核索引

### 8.1 本地数据与输出

| 位置/对象 | 保存内容 | 写入时机或用途 |
| --- | --- | --- |
| `user://DamageMeter_combat_state.json` | schema、run_seed、turn、encounter、tracking、当前玩家、归档 segments、当前 events | 回合结束和战斗结束等路径保存；用于重载恢复 |
| 上述文件的玩家对象 | PS 所有声明的统计属性和字典，以及姓名、玩家键、角色、颜色 | `SerializePlayerStats`；空字典可省略 |
| `user://DamageMeter_settings.json` | 历史最佳记录及 UI/分享设置 | `DamageMeterSettings.Save`；界面位置等设置不是战斗统计 |
| `user://analytics/时间_角色_W或L.json` | C01—C06 | `AnalyticsWriter.WriteRunData`；随后调用上传 |
| `user://analytics/sync_state.json` | 已同步分析文件的文件名水位 | `AnalyticsSyncer`；支持补传旧分析 JSON |
| `user://analytics/archive_sync_state.json` | 原始 .run 档案同步水位 | `ArchiveSyncer` |
| `%APPDATA%/SlayTheSpire2/skada_run_token.json` | C05 的进度比较 token | 重载/撤销标记辅助 |

`user://` 是 Godot 的用户数据路径别名，不是 Mod 安装目录。分析 JSON 使用 `/api/runs/submit`、`/api/runs/submit-batch` 等接口；本次分析没有触发这些游戏数据上传。

战斗保存文件没有写入完整的 `_deathLogs`、各状态来源账本、外部力量赠予账本或敌人 HP 快照。因此“恢复了累计数字”不代表“恢复了所有辅助归因上下文”。新跑局通常按种子重置；相同种子的读取被当作重载以保留数据，具体也受 AutoResetOnNewRun 设置影响。

### 8.2 便于重新定位的代码索引

| 要复核的问题 | 程序集中的位置 |
| --- | --- |
| 为什么是 18 类 | `DamageMeter.Scripts.DamageMeterUI.Initialize` |
| 所有局内累计字段 | `DamageMeter.Scripts.CombatDataCollector.PlayerStats` 的属性、`Clone`、`MergeFrom` |
| 读取哪些游戏事件 | `DamageMeter.Scripts.HistoryTailer.DispatchEntry` |
| 何时开始、结束、绑定回合 | `DamageMeter.Scripts.PublicApiLifecycle` |
| 输出/承伤/格挡/出牌/资源的来源 | `CombatDataCollector.RecordDamage/RecordBlockGained/RecordCardPlay/OnTurnEnded/Record*` |
| 助攻推算公式 | `CombatDataCollector.ComputeAndRecordAssists/SplitAssistCredit` |
| 减免推算公式 | `ComputeWeakPrevention/ComputeGuardedPrevention/ComputeFrailBlockPrevention/DistributePreventionCredit` |
| 无施伤者的状态伤害判断 | `HistoryTailer.InferPowerDamageContext` |
| 毒、Strangle、Doom 的分摊 | `RecordPoisonDamage/RecordProportionalPowerDamage/RecordDoomKillWithHp` |
| 每类实际显示字段与公式 | A01—A18 对应的 `*Category.GetPlayerBars/GetDetailBars` |
| 仪表盘实际绑定了什么 | `DamageMeterUI.BuildHeroSummary/BuildLeftChart/BuildCenterChart/BuildRightChart` |
| 历史纪录按谁统计 | `DamageMeterSettings.UpdateRecords` 及 `CDC.ArchiveAndStartNew` 调用点 |
| 整局、逐层、战斗 JSON 字段全集 | `AnalyticsWriter.WriteRunData`、`CombatSummary.SnapshotCurrent` |
| 地图路径 | `AnalyticsWriter.ExtractMapActs`、`RunDataCollector` 的地图与坐标缓存 |
| 远端同步前是否有字段筛选 | `ArchiveSyncer.SyncArchives/InjectMetadata` |

### 8.3 使用该文档做后续设计时的关键边界

1. **计数时以 A01—A18 为独立分类。** B 组是额外采集维度，C 组是复盘数据分组，图表和 JSON 副本不重复计数。
2. **个人伤害含格挡与过量；承伤只取未格挡伤害。** 二者不对称。rDPS 是贡献重分配，不是穿透伤害。
3. **助攻与减伤主要是推算。** 支持特定状态集合，使用当前状态与施加量账本，不能承诺覆盖所有来源或精确复原因果。
4. **格挡统计的是获得量，能量浪费统计的是结束剩余量。** 未验证最终格挡利用率，也未识别能量保留机制。
5. **总览 DPT 与历史最佳有特别口径。** 总览不是整局总回合平均；历史最佳混合全队合计与个人极值，且在归档时更新。
6. **日志受容量限制且不是完整战斗回放。** 当前事件缓冲最多 200 条，单人明细最多 50 条，死亡前最多 8 条。
7. **采集、局内展示、本地保存、分析上传不是同一集合。** B02—B04 已采集但无独立局内分类；定制分析快照缺少一些局内归因字段；原始 .run 同步则透传游戏文件。
8. 本样本未发现独立的治疗量、实际格挡吸收/浪费、按牌过量伤害、星星消耗、按球类型效果、逐事件 HP 快照统计分类。逐层 HP 推算会用到游戏历史 `HpHealed`，这不等于 Mod 实现了治疗排行榜。
