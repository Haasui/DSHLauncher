# DeepSeek Harness 启动器 (DshLauncher)

DeepSeek Harness的Windows桌面启动器（本质只是`npx @deepseek-ai/dsh web`啦。）
> 个人项目，功能按个人偏好设置，覆盖不全面、可能存在极其大量的bug。

## 作用
- 一键启动/停止/重启DeepSeek Harness，异常退出自动重启
- 窗口内嵌DeepSeek Harness网页界面
- 会话导出Markdown、工作区会话树
- 桌面审批、插件安装/管理

## 运行
自包含单文件 `DshLauncher.exe`，双击启动。

从源码构建：
```powershell
dotnet build -c Release          # 0 警告 0 错误
dotnet publish -c Release -o release
```

## 技术栈
C# .NET 8 · WPF · WebView2 · CommunityToolkit.Mvvm · ZstdSharp.Port

## License
[MIT](LICENSE)
