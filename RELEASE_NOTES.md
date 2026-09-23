# Codex Strip v0.1.0

Windows Codex 线程状态条首个打包版本。

## 下载与运行

推荐下载 **CodexStrip-portable.zip**，解压后运行 `CodexStrip.exe`，无需安装。

也可单独下载 `CodexStrip.exe` 和 `CodexStrip.exe.config`，放在同一文件夹。配置文件声明 .NET Framework 版本及 DPI 行为，建议保留。

要求 Windows、.NET Framework 4.8，以及正在运行的 Codex 桌面应用。软件使用内部桌面接口，Codex 更新后可能需要适配。

## 功能

- 线程最新进展、等待回答和未读完成高亮。
- 右键预览、点击跳转并激活 Codex。
- 横竖布局、转置位置记忆、四边停靠和托盘收起。
- 白天／夜晚模式、周额度与重置卡信息。

程序设置保存在运行目录的 `data/` 中。本发布包不含个人设置、线程数据或日志。

`SHA256SUMS.txt` 提供 EXE 和便携 ZIP 的校验值。
