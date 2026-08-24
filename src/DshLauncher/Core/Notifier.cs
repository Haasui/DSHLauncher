namespace DshLauncher.Core;

/// <summary>
/// Windows 通知（托盘气泡，DSH 就绪/审批请求时提示，含审批桥）。
/// 复用 App.Tray 图标弹气泡，不再自建常驻图标（修复托盘出现两个图标）。
/// </summary>
public static class Notifier
{
    /// <summary>当前审批通知的点击回调（BalloonTipClicked 触发后清空）。</summary>
    private static Action? _balloonClick;

    /// <summary>绑定托盘气泡点击（App.SetupTray 调用一次）。</summary>
    public static void BindBalloonClick()
    {
        if (DshLauncher.App.Tray is { } tray)
        {
            tray.BalloonTipClicked += (_, _) =>
            {
                var a = _balloonClick;
                _balloonClick = null;
                a?.Invoke();
            };
        }
    }

    public static void ShowDshReady(int port)
    {
        try
        {
            if (DshLauncher.App.Tray is { } tray)
            {
                tray.ShowBalloonTip(4000, "DeepSeek Harness 已就绪",
                    $"http://127.0.0.1:{port} 可访问。", System.Windows.Forms.ToolTipIcon.Info);
            }
        }
        catch
        {
            // 通知失败不崩溃
        }
    }

    /// <summary>DeepSeek Harness 请求审批 → 托盘气泡；点击执行 onClick（如打开嵌入界面）。</summary>
    public static void ShowApproval(string toolName, string reason, Action? onClick = null)
    {
        try
        {
            _balloonClick = onClick;
            if (DshLauncher.App.Tray is { } tray)
            {
                var text = string.IsNullOrEmpty(reason) ? toolName : $"{toolName}：{reason}";
                tray.ShowBalloonTip(8000, "DeepSeek Harness 请求审批", text, System.Windows.Forms.ToolTipIcon.Warning);
            }
        }
        catch
        {
            // 通知失败不崩溃
        }
    }
}
