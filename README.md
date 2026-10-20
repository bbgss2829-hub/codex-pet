# Codex Pet 0.1.0

独立 Windows 桌面宠物，C# / WPF / .NET 10。中文界面，原创像素机器人“薄荷号”。不是 OpenAI 官方产品。

## 使用

解压便携包后双击 CodexPet.exe。内置运行时，无需管理员权限或额外安装 .NET。

- 点击机器人：展开或收起监控面板。
- 按住机器人拖动：移动窗口；滚轮或面板按钮：60%–160% 缩放。
- 任务下拉框：自动跟随最近活动，或手动选择会话 ID。不会读取聊天标题/正文显示。
- 托盘右键：显示/隐藏、展开、刷新、开机启动、退出。开机启动默认关闭，只有手动勾选才写当前用户 Run 项。移动 EXE 后请重新勾选。
- 额度默认每 120 秒刷新，通知触发刷新；JSONL 每 3 秒增量扫描。失败自动退避重试，旧额度标记为旧数据。
- 退出只清理本程序创建的 App Server 子进程，不结束 Codex Desktop。

首次使用需要本机已有可用的 codex.exe 和 Codex 自身的登录状态。程序依次检查 CODEX_PET_CODEX_PATH、PATH 和 Codex 用户安装 bin 目录。自定义路径必须指向 codex.exe，不执行 shell 命令或 .cmd 脚本。

## 数据口径

订阅剩余百分比来自 account/rateLimits/read，Token 来自本机 sessions JSONL，不能相互换算。本次回答是最近 turn 的累计差值，而非最后一次模型调用。输入为零时缓存率显示“—”。

当前任务是最近活动/手动选择，不等同于 Desktop 当前前台聊天。本地累计只含当前 sessions 中的非派生根会话，排除 fork、子代理、归档及已删除记录。遇到计数回退或坏行会提示不完整。详见 docs/RESEARCH.md。

程序不读取 auth.json 凭据，不上传会话、工具输出或 Token，不改 Codex 文件、代理、hooks 或登录。官方 App Server 自行使用已有认证获取额度。设置只保存在 %LOCALAPPDATA%\CodexPet；可取消开机启动后退出并删除程序目录及该设置目录完成卸载。

## 构建和测试

需要 .NET 10 SDK（若 tools/dotnet 有私有 SDK，脚本优先使用）。

```powershell
.\build.ps1
```

脚本依次运行 34 个不依赖第三方测试包的断言测试，然后发布自包含单 EXE 到 dist/win-x64。测试失败会停止发布。

```powershell
.\dist\win-x64\CodexPet.exe --demo
.\dist\win-x64\CodexPet.exe --probe .\probe.txt
```

--demo 只使用虚构数据，不连接 Codex；--probe 只输出接口可用性和文件数量，不输出实际用量。docs/preview.png 是演示截图。

## 验证范围

已在本机 Windows 11 x64 构建及连接测试。Windows 10 与 ARM64 尚未实机验证。EXE 未签名，当前交付为 MVP 便携版。单元测试记录见 docs/test-results.txt。本地接口检查和性能诊断不包含在公开源码中。

三个参考项目均为 MIT，完整声明位于 licenses/。实现方案及提交版本见 docs/RESEARCH.md。

