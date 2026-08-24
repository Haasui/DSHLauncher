# DeepSeek Harness 启动器 (DshLauncher)

DeepSeek Harness 的 Windows 桌面启动器：把 `npx @deepseek-ai/dsh web` 变成一个双击就能用的窗口。

> 个人项目，功能按自己的使用习惯来，覆盖不全面、可能有 bug，欢迎提 issue。非 DeepSeek 官方。

## 能干嘛

- 一键启动 / 停止 / 重启 DeepSeek Harness，异常退出自动重启
- 窗口内直接嵌 DSH 网页界面，不用另开浏览器
- 会话导出成 Markdown、跨会话搜索、工作区会话树
- 桌面审批、插件安装/管理、深浅色主题、全局热键、自更新

## 跑起来

发布版是自包含单文件 `DshLauncher.exe`，双击就跑，不用装 .NET / Node。

从源码构建（开发向，需要 .NET 8 SDK）：

```powershell
dotnet build -c Release          # 0 警告 0 错误
dotnet publish -c Release -o release
```

## 技术栈

C# .NET 8 · WPF · WebView2 · CommunityToolkit.Mvvm · ZstdSharp.Port

## License

[MIT](LICENSE)
