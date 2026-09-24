# Codex Strip

Windows 上的 Codex 线程状态条，适用于横向或竖向副屏。支持任务进展、未读高亮、消息预览、额度、托盘、屏幕停靠、位置记忆和线程跳转。

支持 100%–200% 可保存的界面倍率（默认 150%），以及顶部全屏按钮。按 F11 切换全屏，按 Esc 退出全屏；退出后恢复原来的浮动位置或屏幕停靠。

没有进行中的任务和待处理事项后，看板会在可设置的静息时间结束时切换为资源监控。资源视图显示 CPU、GPU、内存、网速和磁盘读写；点击视图即可返回任务。显示项目与顺序、网卡、速率单位、颜色和背景图片可在设置中调整。

## 运行

从 [dist/CodexStrip-portable.zip](dist/CodexStrip-portable.zip) 下载便携包并解压，运行 `CodexStrip.exe`。保留同目录的 `CodexStrip.exe.config`。无需安装，需要 Windows .NET Framework 4.8，且 Codex 桌面应用保持运行。

运行时个人设置保存在 `data/`，该目录不纳入版本控制。程序使用 Codex 内部桌面接口，桌面应用升级后可能需要适配。

## 构建

在 Windows PowerShell 中运行：

```powershell
./source/build.ps1
```

源码使用系统 .NET Framework 4.8 编译器，不依赖第三方运行库。`source/tests/` 为开发验证程序，部分测试会操作窗口或临时预留屏幕工作区。

## 文件

- `source/`：C# 源码、构建脚本和测试源码。
- `assets/`：应用图标。
- `CodexStrip.exe`：当前构建。
- `使用说明.md`：功能说明与已知限制。
- `DESIGN.md`：设计记录。
- `screenshots/`：演示数据截图。
- `dist/`：便携版 ZIP 和校验信息。

设计参考资料许可证见 `DESIGN-LICENSE.txt`，不代表整个项目以该许可证发布。
