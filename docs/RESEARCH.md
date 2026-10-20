# Codex Pet — 开源研究与实现方案

研究日期：2026-10-20。代码按下列提交固定；未执行第三方项目或修改它们。

| 项目 | 固定提交 | 架构与可复用部分 | 本项目取舍 |
|---|---|---|---|
| [codex-usage-widget](https://github.com/ognjeeen/codex-usage-widget) | 940d722d4044242bbe73f8056014449c2ef827a7 | C#/WPF；Domain、Application、Infrastructure 分层；CodexAppServerSession、JsonRpcConnection、CodexRateLimitsParser、托盘及用户级启动服务 | 参考 stdio 初始化、请求关联、额度桶及窗口的设计；独立编写简化客户端。不引入 hooks、额度重置消费或配置编辑功能。 |
| [codex-token-overlay](https://github.com/soleillevant0125/codex-token-overlay) | b3a38d727fb2e0cf8e8c92ffff3f65da9a592dc5 | Windows .NET 10/WinForms；macOS Swift；本地 IPC 识别选中任务，JSONL 最新累计快照、缓存命中和差值统计，窗口附着及托盘 | 参考 token_count 结构及累计快照语义。不把 last_token_usage 当作整个回答。MVP 不依赖未公开的 Desktop IPC；提供最近活动和手动选择。 |
| [codex-barbar](https://github.com/naipi11/codex-barbar) | 507917af0ecc884d17d9996b998d51cda3c16287 | Rust 2024/Tauri 2/React 18；App Server、账户层、悬浮球、托盘、多账户凭据管理 | 参考额度与活动状态分别展示的思路；不移植凭据备份、auth.json 读取或账户切换。无需 WebView2。 |

三个仓库根许可证均为 MIT，版权所有者依次为 Ognjen Marinković（2026）、soleillevant0125（2026）、Peter Steinberger（2025）。完整声明保存于 licenses/。本项目源码和机器人均为新写，没有直接链接这些项目的二进制或复制它们的素材。未来移植代码应保留相应版权和 MIT 许可全文；不能因为仓库公开就省略声明。

## 环境检查

- Windows NT 10.0.26200.0，x64（Windows 11）。Git 2.55.0.windows.5。
- 系统仅有 .NET 6.0.25 Runtime，没有 SDK。
- 使用微软 dotnet-install.ps1 将 .NET SDK 10.0.402 装到本项目 tools/dotnet；不更改全局 PATH。
- Codex CLI 0.163.0-alpha.10.2，来自本机 Codex 用户安装目录。
- D:\codex 虽在工具授权范围内，但 Windows ACL 拒绝写入。项目保存在会话可写目录，不更改 ACL、不申请管理员权限。
- 沙箱启动工具报 setup refresh 错误；通过已获自动审批的普通用户沙箱外命令进行构建。没有以管理员身份安装组件。

## 官方协议

[OpenAI App Server 官方说明](https://learn.chatgpt.com/docs/app-server)定义：stdio 上使用逐行 JSON 消息；先 initialize，再 initialized。account/rateLimits/read 提供额度，account/rateLimits/updated 提示变化。rateLimitsByLimitId 为多桶视图，rateLimits 为兼容视图，不能重复计算。windowDurationMins 为时长，resetsAt 为 Unix 秒；缺失窗口不等于剩余 100%。

本程序只向自己启动的 codex app-server 子进程发送初始化与额度读取。官方 Codex 进程负责认证及必要的额度网络请求；宠物本身没有 HTTP 上传功能、不获取凭据、不发起模型推理。使用进程级 analytics/feedback 关闭覆盖，不写 config.toml。独立 App Server 不保证接收 Desktop 另一进程的全部实时通知，因此保留 120 秒轮询和手动刷新。

## 模块

- CodexPet.Core/Metrics.cs：纯函数额度解析、Token 值对象和每个会话的 turn 累计器。
- CodexPet.Core/SessionStore.cs：共享只读打开 JSONL，按字节偏移增量读取，完整行才提交；三秒检查一次目录和文件元数据。
- CodexPet.Core/AppServer.cs：进程定位、JSON RPC 请求 ID、超时、通知、进程清理；UI 负责 5/10/20/40/80/120 秒退避。
- CodexPet/PetWindow.cs：透明置顶窗口、拖动、滚轮缩放、中文面板、托盘、任务选择、刷新和错误提示。
- CodexPet/PetView.cs：原创代码绘制像素机器人；IPetSkin 可替换皮肤。4 FPS 的待机、眨眼、走动、思考、开心、休息动画；隐藏时停绘。
- Preferences.cs：仅保存窗口位置、缩放到 %LOCALAPPDATA%/CodexPet/settings.json。

## 统计口径

- 当前任务：手动选定或最近有文件活动的根会话。明确不代表 Desktop 前台选中的聊天。
- 本次回答：最近 turn_id 起始基线到当前累计值的差；多次模型调用合并。重复 turn_context 不重设基线。没有 turn 边界时显示未知。
- 任务累计：total_token_usage 累计快照；相同快照不加总。正常增加只计差值。last_token_usage 是模型调用粒度，不累加它来猜测整次回答。
- 计数回退：重建后续基线并标记不完整，不凭空补入可能重复的消耗；此时显示值只能视为可确认部分。
- 本地累计：当前 sessions 目录内按会话 ID 去重后的非派生根会话相加；排除 subagent 和 fork，避免继承历史重复。不是账户终身累计，不含 archived_sessions、其他机器或删除的文件。
- 缓存命中率 = cached_input_tokens / input_tokens × 100%；输入为零或缓存大于输入时显示“—”；未命中 = max(0,input-cached)。缓存属于输入，不再加到总 Token。
- 推理 Token 通常包含在输出内，本 MVP 不额外加到总数。总数使用源字段，不做订阅额度换算。
- 半行等到换行后处理；坏行跳过并警示；单行大于 4 MiB 跳过并提示。截断后从头重建。异常导致的统计缺失不伪装成真实零消耗。

## MVP 边界与后续

当前实现优先确保真实额度、统计口径和可运行独立 EXE。原画像素动画已通过 IPetSkin 提供六种轻量状态，走动仅在宠物局部区域内发生，不自动抢占桌面位置。

尚未接入 Desktop 私有 IPC 的选中聊天识别；未统计分支新增贡献或归档历史；未提供远端账户汇总、自动更新、签名安装包。对于被原地替换为更大内容且没有文件缩短的 JSONL，当前偏移读取可能无法检测重写；Codex 正常追加写入路径已覆盖。大规模历史目录初次索引仍需遍历全部文件，可进一步添加仅含计数的本地索引和文件标识检测。

Windows 11 x64 上完成真实接口与运行测试；Windows 10、ARM64、多显示器混合 DPI 的完整矩阵仍需实机验证。性能结果仅代表当前电脑与当前日志规模。
