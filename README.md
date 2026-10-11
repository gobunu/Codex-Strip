# Codex Strip

Windows 上的 Codex 任务状态条，支持横向或竖向副屏。显示任务进展、待办提示和额度，空闲时可切换为系统资源监控。

[下载便携版](https://github.com/gobunu/Codex-Strip/releases/latest) · [使用说明](使用说明.md) · [反馈问题](https://github.com/gobunu/Codex-Strip/issues)

## 界面示例

### 任务看板

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="screenshots/tasks-dark.png">
  <source media="(prefers-color-scheme: light)" srcset="screenshots/tasks-light.png">
  <img src="screenshots/tasks-light.png" alt="任务看板：主机、任务标题、最新进展、状态与周剩余额度" width="100%">
</picture>

显示任务主机、状态、最新消息和周剩余额度。完成提醒跟随 Codex 的已读状态。截图使用演示数据，提供明暗两种主题。

### 资源监控

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="screenshots/resources-dark.png">
  <source media="(prefers-color-scheme: light)" srcset="screenshots/resources.png">
  <img src="screenshots/resources.png" alt="静息资源页：CPU、GPU、内存占用，以及网络和磁盘速率曲线" width="100%">
</picture>

显示 CPU、GPU、内存占用及网络、磁盘速率。没有运行中的任务或待办时，可在设定时间后自动进入；新任务、提问或未读结果出现时返回任务看板。截图使用演示数据。

## 功能一览

| 功能 | 说明 |
| :--- | :--- |
| 任务显示 | 本地、云端和 SSH 任务的进展、待办提示与未读结果 |
| 会话跳转 | 点击打开对应会话；临时子线程按已知父线程关联跳到主线程 |
| 额度 | 周剩余额度与详情 |
| 资源监控 | CPU、GPU、内存及主要程序占比，网络和磁盘速率曲线 |
| 布局 | 横向 / 竖向、屏幕四边停靠、浮动窗口、全屏和系统托盘 |
| 显示设置 | 100%–200% 倍率，每块屏幕独立记忆位置与倍率 |
| 外观 | 明暗主题、自定义壁纸、清透玻璃或边缘折射效果 |

## 快速开始

**运行条件：** Windows、.NET Framework 4.8，以及正在运行的 Codex 桌面应用。

1. 从 [最新 Release](https://github.com/gobunu/Codex-Strip/releases/latest) 下载 `CodexStrip-portable.zip` 并解压。
2. 打开 Codex 桌面应用，再运行 `CodexStrip.exe`。请保留同目录的 `CodexStrip.exe.config`。
3. 在设置中选择显示器、停靠位置、任务数量与界面倍率。

> 更新时，先退出 Strip，再替换程序文件；保留完整的 `data/` 文件夹。本文介绍当前源码功能，已发布版本的变化见 [发布记录](https://github.com/gobunu/Codex-Strip/releases)。

### 常用操作

| 操作 | 结果 |
| :--- | :--- |
| 点击任务卡 / 右键任务卡 | 打开会话 / 预览消息 |
| 点击「周剩余」 | 查看额度详情 |
| `F11` / `Esc` | 切换全屏 / 退出并恢复原位置 |
| 点击静息页面 | 返回任务看板 |
| 顶栏收起按钮 | 收到系统托盘 |

## 常见问题

### 需要 API Key 吗？会消耗 Codex 额度吗？

不需要 API Key。Strip 读取正在运行的 Codex 桌面应用中的任务状态、消息与额度信息，用于界面展示；它不会替你发送对话或启动模型任务。

### 为什么显示未连接，或者打不开某个任务？

先确认 Codex 桌面应用正在运行，并与 Strip 使用相同的 Windows 用户权限。接入依赖 Codex 内部桌面接口，Codex 更新后可能需要安装适配后的 Strip。

临时子线程有可靠父线程关联时会跳到主线程；关联信息不可用时，需要从 Codex 侧栏打开主线程。

### 为什么没有自动进入静息？

静息计时需要保持连接，且没有进行中的任务、未读结果、等待回答或待确认事项。操作看板会重新计时；已空闲的历史线程不会仅因详情同步过期而阻止静息。也可从设置或托盘菜单手动进入。

### 壁纸和资源统计

选择壁纸后，程序会将其复制到 `data/wallpapers/`，之后可删除外部原图。更新时保留完整的 `data/` 即可。

采样间隔、动画和统计口径会影响资源显示。GPU 取最繁忙显卡引擎的占用，性能计数器不可用时显示「—」；详细口径见 [使用说明](使用说明.md)。

## 开发与构建

在 Windows PowerShell 中运行：

```powershell
./source/build.ps1
./CodexStrip.exe --self-test
./CodexStrip.exe --settings-test
./source/package.ps1
```

使用系统 .NET Framework 编译器，不依赖第三方运行库。便携包、SHA-256 校验文件与当前版本的发布说明输出到 `dist/`。推送版本标签后，GitHub Actions 从源码构建并上传 Release。

`source/tests/` 包含开发验证程序：桌面接口读取与跳转、临时线程到主线程的关联，以及设置、布局、静息与资源显示。部分测试会操作窗口或临时预留屏幕工作区。

## 文件

| 路径 | 内容 |
| :--- | :--- |
| `source/` | C# 源码、构建脚本与验证程序 |
| `assets/`、`screenshots/` | 图标与演示截图 |
| [使用说明.md](使用说明.md) | 详细功能、配置与已知限制 |
| [RELEASE_NOTES.md](RELEASE_NOTES.md) | 版本变化 |
| [DESIGN.md](DESIGN.md) | 设计记录 |

个人设置、壁纸副本、EXE 和构建产物不纳入版本控制。设计参考资料的许可证见 [DESIGN-LICENSE.txt](DESIGN-LICENSE.txt)，它不代表整个项目的许可证；第三方声明见 [THIRD_PARTY_NOTICES.txt](THIRD_PARTY_NOTICES.txt)。
