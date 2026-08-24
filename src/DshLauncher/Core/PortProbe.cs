using System.Net.NetworkInformation;

namespace DshLauncher.Core;

/// <summary>端口探测：用 IPGlobalProperties 查询 TCP 监听状态。</summary>
public static class PortProbe
{
    /// <summary>端口当前是否被监听（任何地址）。探测失败按未开放处理。</summary>
    public static bool IsListening(int port)
    {
        try
        {
            foreach (var ep in IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners())
            {
                if (ep.Port == port) return true;
            }
        }
        catch
        {
            // 忽略
        }
        return false;
    }
}
