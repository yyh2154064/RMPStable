# RMPStable 项目组织与部署

## 使用范围和发布入口

- 面向 Slay the Spire 2 单人和联机玩家。联机队员关闭游戏后使用同一游戏版本和 Mod 版本。本项目通过 GitHub Release 分发 Mod 文件夹，由玩家在本机安装。日常专项使用 F 盘独立环境与隔离档案，保护现有游戏和真实存档。
- `origin` 为公开仓库 [yyh2154064/RMPStable](https://github.com/yyh2154064/RMPStable)，默认分支 `main`。既有 `liveSharingTest` 为本地模拟，`multiInstanceTest` 为原生多实例。开发和发布授权见 [AGENTS.md](../AGENTS.md) 及 [开发流程 Skill](../.agents/skills/rmpstable-development/SKILL.md)。每轮检查当前工作区、远端目标、分支引用及版本标签。
- 2026-11-13 核对公开页面和附件列表，[RMP Stable v0.4.5](https://github.com/yyh2154064/RMPStable/releases/tag/v0.4.5) 显示为 Latest，标签指向源码提交 `d048e62`，[公开附件](https://github.com/yyh2154064/RMPStable/releases/expanded_assets/v0.4.5) `RMPStable-v0.4.5.zip` 的 SHA-256 与本地 ZIP 一致。[v0.4.4](https://github.com/yyh2154064/RMPStable/releases/tag/v0.4.4) 和 [v0.4.5 本地文案](release-v0.4.5.md) 是标题与正文格式参考。
- 当前开发资源在 `F:/projectFile/RMPStable-development`。构建输出 `build/package/RMPStable/`，同级存放发布 ZIP。引用、runtime、profile seed 和证据目录详见 [开发资源](development-resources.md)。以实际路径与候选文件哈希为准。
- v0.4.6 使用 `build/v046/package/` 保存候选文件夹和 ZIP。`runtime/v0.113.0` 复用 F 盘隔离环境，匹配游戏 PCK 从 Steam 安装只读加载。游戏程序集、测试探针和隔离档案均在候选包之外。
- v0.4.6 本地候选 ZIP 为 2,883,776 字节，SHA-256 为 `1B5B5C13311D5308067AD6499EF85114AF4DF6B42FEADFC6753F0EE760158533`。五个归档文件与候选目录逐一核对。v0.4.5 原更新器用模拟 GitHub Latest 和附件下载完成升级，三项安装文件的哈希均与候选一致。公开 Latest、附件和更新器联网复验待发布后完成。

## 版本和更新器约定

| 项目 | 正式发布约定 |
| --- | --- |
| 版本 | 用户批准的数字 `X.Y.Z`。正式 Latest 须可与当前版本正常比较，通常高于已有 Latest。 |
| 源码 | 同步 `RMPStable/RMPStable.json`、两个 `AssemblyInfo.cs`、两个 `ModEntry.cs` 中的版本文字、交付说明和发布文案。程序集版本沿用 `X.Y.Z.0`。 |
| 标签与标题 | `vX.Y.Z`、`RMP Stable vX.Y.Z`，标签指向实际对应验证包的源提交。 |
| 正文 | 在 `docs/release-vX.Y.Z.md` 沿用 `## vX.Y.Z 更新内容`、改动列表、关闭游戏并同版本更新的安装说明、实际专项结果。 |
| 附件 | `RMPStable-vX.Y.Z.zip`，顶层 `RMPStable/`，含 `RMPStable.dll`、`RMPStable.pck`、`RMPStable.json`、`README-multiInstance.md`、`update.cmd`。 |

[update.cmd](../RMPStable-Packager/update.cmd)优先读 `/releases/latest` 的跳转标签，失败时读取远端 `main` 的 `RMPStable/RMPStable.json`。它按 `/releases/download/vX.Y.Z/RMPStable-vX.Y.Z.zip` 下载，检查清单 `id=RMPStable`、数字版本一致及 DLL、PCK、JSON，安装这三个文件。相同版本跳过，较旧的远端版本不会覆盖本机新版。若交付包含更新器自身的变化，在发布说明写明用完整文件夹替换的步骤。正式分发时保持 Latest、`main` 清单和附件可用性一致。若用户选用测试分支，选择框明确 Release 是否用于公开自动更新，并说明 `main` 回退路径的影响。

## 助手上传流程

1. **审核候选。** 展示目标分支、版本、标签、ZIP、文案和测试证据后收集发布选择。核对 `origin`、远端分支引用、版本冲突和授权范围。若从测试分支向 `main` 发布，先检查拟整合的提交和最终差异，只纳入本次批准内容。发现分歧、混入其他修改或包与目标源码不一致时先解决，必要时补充审批并针对受影响内容重新验证。
2. **核验本地包。** 检查完整目录和 ZIP 内容、版本、文件哈希及大小，记录验证过的 DLL 和 ZIP 的 SHA-256。使用本轮包和本轮 Markdown。[Publish-V045.ps1](../tools/Publish-V045.ps1)固定了 v0.4.5，仅供参考，包含远端写入逻辑。
3. **更新源码与 Release。** 仅提交已审核改动，将本轮发布所需的分支和标签正常推送，核对远端 SHA 与标签指向。在可用的已认证 GitHub 工具中创建或继续本轮草稿，上传 ZIP，核对上传结果后按授权发布。正式分发为公开 Latest，关闭草稿和预发布标志。缩短 `main` 清单推送与附件上线之间的间隔。重试前读取实际远端状态；遇到同名标签、附件或 Release 内容冲突，先确认其归属，保护已有发布与历史。
4. **公开验证。** 核对目标分支及标签、Release 标题和正文、正式 Latest 标志、附件公开下载、大小和 SHA-256。在 F 盘隔离 Mod 副本中用现有 `update.cmd` 从上一正式版更新，核对退出状态、清单及三个安装文件哈希。必要时验证当前版本再次运行时的跳过。关闭游戏或使用未启动游戏的隔离副本。网络或工具限制时，列出已核验及待核验项，避免把附件检查写成实际更新成功。
5. **记录交付。** 回报源码和 Release 链接、标签、附件和更新器验证结果，更新 [会话上下文](session-context.md) 及本页发布事实。若用户自行上传，交付同一候选、文案、发布顺序与验证步骤，待取得证据后更新远端状态。

## 公开资料和认证

- 提交、发布文案、截图和 ZIP 都按公开可见处理。发布前检查实际待推送提交、待发布文案与归档内容，去除令牌、私钥、Cookie、私有地址、个人资料、本地账号路径、真实存档、未脱敏日志和游戏引用程序集。公开文档优先使用仓库相对路径；本机资料引用使用通用资源根，历史证据留在本地。工作区既有的敏感内容或凭据异常需私下告知用户，描述位置和影响时隐藏原值。
- 使用已授权的 GitHub 连接器、已登录的 GitHub CLI 或浏览器会话，凭据留在认证工具内。权限限定于目标仓库的源码与发布操作。避免显示认证值、导出登录会话或索取聊天中的明文密钥。认证或网络受限时保留已验证的本地产物和可恢复的发布状态，说明具体受限步骤。
- 发布前按实际差异确认是否会带出历史遗留内容。未经授权不重写历史、强推、删除标签或覆盖已有公开附件。已发布内容出现异常时先核实状态，再按本轮授权处理恢复动作。
