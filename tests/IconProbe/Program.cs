using System.Runtime.InteropServices;

// 枚举 exe 的 RT_ICON 资源，打印每帧尺寸（PNG=256px / BITMAPINFOHEADER=宽x高）
internal static class Program
{
    private const uint LOAD_LIBRARY_AS_DATAFILE = 0x2;
    private static readonly IntPtr RT_ICON = new(3);
    private static readonly List<(int Id, string Desc)> Frames = new();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hFile, uint dwFlags);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumResourceNames(IntPtr hModule, IntPtr lpszType, EnumResNameProc lpEnumFunc, IntPtr lParam);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr FindResource(IntPtr hModule, IntPtr lpName, IntPtr lpType);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadResource(IntPtr hModule, IntPtr hResInfo);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SizeofResource(IntPtr hModule, IntPtr hResInfo);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LockResource(IntPtr hResData);

    private delegate bool EnumResNameProc(IntPtr hModule, IntPtr lpszType, IntPtr lpszName, IntPtr lParam);

    private static bool Callback(IntPtr hModule, IntPtr lpszType, IntPtr lpszName, IntPtr lParam)
    {
        var id = lpszName.ToInt32();
        var hRes = FindResource(hModule, lpszName, RT_ICON);
        if (hRes != IntPtr.Zero)
        {
            var size = SizeofResource(hModule, hRes);
            var data = LockResource(LoadResource(hModule, hRes));
            if (data != IntPtr.Zero && size >= 8)
            {
                var first = Marshal.ReadByte(data);
                var second = Marshal.ReadByte(data, 1);
                if (first == 0x89 && second == 0x50)
                {
                    Frames.Add((id, "PNG (256x256 高清)"));
                }
                else
                {
                    var w = (uint)Marshal.ReadInt32(data, 4);
                    var h = (uint)Marshal.ReadInt32(data, 8);
                    if (w == 0) w = 256; if (h == 0) h = 256;
                    Frames.Add((id, $"BITMAP {w}x{h}"));
                }
            }
        }
        return true;
    }

    private static void Probe(string path)
    {
        Frames.Clear();
        var mod = LoadLibraryEx(path, IntPtr.Zero, LOAD_LIBRARY_AS_DATAFILE);
        if (mod == IntPtr.Zero)
        {
            Console.WriteLine($"  {Path.GetFileName(path)}: 加载失败 (0x{Marshal.GetLastWin32Error():X})");
            return;
        }
        EnumResourceNames(mod, RT_ICON, Callback, IntPtr.Zero);
        FreeLibrary(mod);
        Console.WriteLine($"  {Path.GetFileName(path)}: {Frames.Count} 个图标资源");
        foreach (var (id, desc) in Frames)
            Console.WriteLine($"    id={id}  {desc}");
    }

    private static void Main(string[] args)
    {
        // 用法：IconProbe <exe路径> [exe路径2..]
        if (args.Length == 0)
        {
            Console.WriteLine("用法: IconProbe <exe路径> [exe路径2..]");
            Console.WriteLine("未提供参数，仅探测系统 notepad.exe 作为对照。");
            Probe(@"C:\Windows\System32\notepad.exe");
            return;
        }
        foreach (var a in args) Probe(a);
        Probe(@"C:\Windows\System32\notepad.exe");
    }
}
