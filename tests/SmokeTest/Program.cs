using System.Diagnostics;
using DshLauncher.Core;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.WriteLine("== 1) SettingsService.LoadAsync ==");
var settings = AppServices.Settings;
await settings.LoadAsync();
Console.WriteLine($"  ConfigPath     = {settings.ConfigPath}");
Console.WriteLine($"  Port           = {settings.Port}");
Console.WriteLine($"  AutoStart      = {settings.AutoStartOnLaunch}");
Console.WriteLine($"  StartWithWin   = {settings.StartWithWindows}");
Console.WriteLine($"  ConfigExists   = {File.Exists(settings.ConfigPath)}");

Console.WriteLine();
Console.WriteLine("== 2) PortProbe (should be true for 3080 if harness running) ==");
Console.WriteLine($"  3080 listening  = {PortProbe.IsListening(3080)}");
Console.WriteLine($"  39999 listening = {PortProbe.IsListening(39999)}");

Console.WriteLine();
Console.WriteLine("== 3) DshService.StartAsync(39999) — spawn + capture output ==");
var dsh = new DshService();
dsh.StdoutReceived += (_, l) => Console.WriteLine($"  [stdout] {l}");
dsh.StderrReceived += (_, l) => Console.WriteLine($"  [stderr] {l}");
try
{
    var started = await dsh.StartAsync(39999);
    Console.WriteLine($"  started={started} IsRunning={dsh.IsRunning} Pid={dsh.Pid}");
    Console.WriteLine($"  CommandLine: {dsh.SafeCommandLine}");
    Console.WriteLine("  waiting 10s for output/port…");
    var sw = Stopwatch.StartNew();
    while (sw.Elapsed < TimeSpan.FromSeconds(10) && dsh.IsRunning)
        await Task.Delay(250);
    Console.WriteLine($"  after 10s: IsRunning={dsh.IsRunning} Pid={dsh.Pid} Port39999={PortProbe.IsListening(39999)}");
}
catch (Exception ex)
{
    Console.WriteLine($"  StartAsync threw: {ex.GetType().Name}: {ex.Message}");
}

Console.WriteLine();
Console.WriteLine("== 4) DshService.StopAsync — should kill whole tree ==");
try
{
    var stopped = await dsh.StopAsync();
    Console.WriteLine($"  stopped={stopped} IsRunning={dsh.IsRunning}");
    await Task.Delay(1500);
    Console.WriteLine($"  after stop: IsRunning={dsh.IsRunning} Port39999={PortProbe.IsListening(39999)}");
}
catch (Exception ex)
{
    Console.WriteLine($"  StopAsync threw: {ex.GetType().Name}: {ex.Message}");
}

Console.WriteLine();

Console.WriteLine();
Console.WriteLine("== 6) PluginService（只读） ==");
try
{
    var plugins = await AppServices.Plugin.GetPluginsAsync();
    Console.WriteLine($"  count={plugins.Count}");
    foreach (var p in plugins)
    {
        var desc = p.Description ?? "";
        if (desc.Length > 60) desc = desc[..60] + "…";
        Console.WriteLine($"  - {p.Id} v{p.Version} isolated={p.Isolated} desc={desc}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"  PluginService threw: {ex.GetType().Name}: {ex.Message}");
}

Console.WriteLine();
Console.WriteLine("== 7) UpdateService.GetVersionsAsync ==");
try
{
    var v = await AppServices.Update.GetVersionsAsync();
    Console.WriteLine($"  local={v.Local} latest={v.Latest} available={v.UpdateAvailable}");
}
catch (Exception ex)
{
    Console.WriteLine($"  UpdateService threw: {ex.GetType().Name}: {ex.Message}");
}

Console.WriteLine();
Console.WriteLine("== 8) DoctorService.RunAllAsync ==");
try
{
    var checks = await AppServices.Doctor.RunAllAsync();
    foreach (var c in checks) Console.WriteLine($"  [{c.Status}] {c.Name}: {c.Detail}");
}
catch (Exception ex)
{
    Console.WriteLine($"  DoctorService threw: {ex.GetType().Name}: {ex.Message}");
}

Console.WriteLine();
Console.WriteLine("== 9) DshPaths（官方 DSH_HOME 解析） ==");
Console.WriteLine($"  Home         = {DshPaths.Home}");
Console.WriteLine($"  WebNodeModules = {DshPaths.WebNodeModules}");

Console.WriteLine();
Console.WriteLine("== 10) DshApiClient（官方 HTTP API，只读，需 DSH 运行中） ==");
try
{
    var api = new DshApiClient(3080);
    var host = await api.DescribeHostAsync();
    Console.WriteLine($"  host.describe: version={host?.Version} model={host?.Provider}/{host?.Model} home={host?.Home} sessions={host?.AttachedSessions}");
    var ns = await api.DescribeSettingsAsync();
    Console.WriteLine($"  settings.describe: {ns.Count} namespaces");
    foreach (var n in ns.Take(6)) Console.WriteLine($"    - {n.Ns} applies={n.Applies} rev={n.Revision} value={n.ValueJson[..Math.Min(80, n.ValueJson.Length)]}");
    // 非 DSH 端口应失败
    var bad = await new DshApiClient(39999).DescribeHostAsync();
    Console.WriteLine($"  non-DSH port host.describe => {(bad is null ? "null (正确)" : bad.Version)}");
}
catch (Exception ex)
{
    Console.WriteLine($"  DshApiClient threw: {ex.GetType().Name}: {ex.Message}");
}

Console.WriteLine();
Console.WriteLine("== 11) UpdateService 三源（P-C） ==");
try
{
    var s = await AppServices.Update.GetSourcesAsync();
    Console.WriteLine($"  current={s.Current} latest={s.Latest} next={s.Next}");
}
catch (Exception ex)
{
    Console.WriteLine($"  GetSourcesAsync threw: {ex.GetType().Name}: {ex.Message}");
}

Console.WriteLine();
Console.WriteLine("== 12) DshApiClient session/workspace（官方只读） ==");
try
{
    var api = new DshApiClient(3080);
    var sessions = await api.ListSessionsAsync();
    var workspaces = await api.ListWorkspacesAsync();
    Console.WriteLine($"  sessions={sessions.Count} running={sessions.Count(s => s.Running)}");
    foreach (var s in sessions.Take(3))
        Console.WriteLine($"    - {s.Title} running={s.Running} preset={s.AgentPreset} cwd={s.Cwd}");
    Console.WriteLine($"  workspaces={workspaces.Count}");
    foreach (var w in workspaces.Take(3))
        Console.WriteLine($"    - {w.Title} @ {w.Path} sessions={w.SessionIds.Count}");
}
catch (Exception ex)
{
    Console.WriteLine($"  session/workspace threw: {ex.GetType().Name}: {ex.Message}");
}

Console.WriteLine();
Console.WriteLine("SMOKE TEST DONE");
