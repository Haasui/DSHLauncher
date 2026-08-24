using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace IconGen;

/// <summary>
/// 多帧 ICO 生成器 v4：
///   16/24/32/48/64/128 帧 → BITMAP（BMP DIB：BITMAPINFOHEADER + XOR/AND mask）
///   256 帧 → PNG
/// 关键修复：全 PNG 的 ICO 被 dotnet 编译进 exe 后全部变 256px，Windows 详情窗格取不到小帧 → 图标显示小。
/// 标准 ICO 用小帧用 BMP、256 用 PNG，dotnet 编译后各尺寸帧完整（对齐 notepad.exe 的资源布局）。
/// 输出：app.ico（透明）、app-square.ico（蓝底满格，exe 用）、app-brand.png（侧边栏）。
/// </summary>
internal static class Program
{
    private static readonly int[] Sizes = { 16, 24, 32, 48, 64, 128, 256 };

    private static int Main(string[] args)
    {
        try
        {
            var sourcePath = args.Length > 0
                ? args[0]
                : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "assets", "app.png");
            sourcePath = Path.GetFullPath(sourcePath);
            if (!File.Exists(sourcePath))
            {
                Console.Error.WriteLine($"源图不存在: {sourcePath}");
                return 2;
            }

            var assetsDir = Path.GetDirectoryName(sourcePath)!;
            Console.WriteLine($"源:  {sourcePath}");

            using var source = new Bitmap(sourcePath);
            using var src32 = source.PixelFormat == PixelFormat.Format32bppArgb
                ? source
                : CloneAs32bpp(source);
            var crop = CropToContent(src32);
            Console.WriteLine($"透明裁剪: x={crop.X} y={crop.Y} 尺寸={crop.Width}x{crop.Height}");

            // 统一透明底：只生成透明版 ICO（托盘/标题栏/exe 同源）+ 侧边栏品牌图
            var icoPath = Path.Combine(assetsDir, "app.ico");
            BuildIco(src32, crop, icoPath, background: false);

            var brandPath = Path.Combine(assetsDir, "app-brand.png");
            SaveBrandPng(src32, crop, brandPath, 48);

            Console.WriteLine($"完成:\n  {icoPath} ({new FileInfo(icoPath).Length} bytes)\n  {brandPath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"失败: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static void BuildIco(Bitmap src, Rectangle crop, string outPath, bool background)
    {
        var frames = new List<(int Size, byte[] Data, bool IsPng)>();
        foreach (var s in Sizes)
        {
            using var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.CompositingMode = CompositingMode.SourceOver;
                if (background)
                {
                    DrawSquareBackground(g, s);
                    DrawCropCentered(g, src, crop, s, 0.80f);
                }
                else
                {
                    DrawCropCentered(g, src, crop, s, 0.92f);
                }
            }
            if (s == 256)
            {
                using var ms = new MemoryStream();
                bmp.Save(ms, ImageFormat.Png);
                frames.Add((s, ms.ToArray(), true));
            }
            else
            {
                frames.Add((s, EncodeBmpFrame(bmp), false));
            }
        }

        using var fs = File.Create(outPath);
        using var bw = new BinaryWriter(fs);
        bw.Write((ushort)0);
        bw.Write((ushort)1);
        bw.Write((ushort)frames.Count);
        long dataOffset = 6 + 16L * frames.Count;
        foreach (var (s, data, _) in frames)
        {
            byte w = (byte)(s == 256 ? 0 : s);
            byte h = (byte)(s == 256 ? 0 : s);
            bw.Write(w);
            bw.Write(h);
            bw.Write((byte)0);
            bw.Write((byte)0);
            bw.Write((ushort)1);             // planes
            bw.Write((ushort)32);            // bit count
            bw.Write((uint)data.Length);
            bw.Write((uint)dataOffset);
            dataOffset += data.Length;
        }
        foreach (var (_, data, _) in frames)
        {
            bw.Write(data);
        }
    }

    /// <summary>把 32bpp ARGB 位图编码为 ICO BITMAP 帧（BITMAPINFOHEADER + XOR(bottom-up BGRA) + AND mask）。</summary>
    private static byte[] EncodeBmpFrame(Bitmap bmp)
    {
        var w = bmp.Width;
        var h = bmp.Height;
        var xorSize = w * h * 4;
        var andStride = (w + 31) / 32 * 4;
        var andSize = andStride * h;

        var rect = new Rectangle(0, 0, w, h);
        var bd = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var stride = bd.Stride;
        var pixels = new byte[stride * h];
        Marshal.Copy(bd.Scan0, pixels, 0, pixels.Length);
        bmp.UnlockBits(bd);

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write(40);                        // biSize
        bw.Write(w);                         // biWidth
        bw.Write(h * 2);                     // biHeight = XOR + AND
        bw.Write((ushort)1);                 // biPlanes
        bw.Write((ushort)32);                // biBitCount
        bw.Write(0);                         // biCompression = BI_RGB
        bw.Write((uint)(xorSize + andSize)); // biSizeImage
        bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);

        // XOR: bottom-up，每行 w*4 字节 BGRA
        for (var y = h - 1; y >= 0; y--)
        {
            bw.Write(pixels, y * stride, w * 4);
        }
        // AND mask: alpha==0 → 1（透明），否则 0
        for (var y = h - 1; y >= 0; y--)
        {
            var row = new byte[andStride];
            for (var x = 0; x < w; x++)
            {
                if (pixels[y * stride + x * 4 + 3] == 0)
                    row[x / 8] |= (byte)(0x80 >> (x % 8));
            }
            bw.Write(row);
        }
        return ms.ToArray();
    }

    private static void DrawSquareBackground(Graphics g, int size)
    {
        var r = new RectangleF(0, 0, size, size);
        using var path = RoundedRect(r, size * 0.22f);
        using var brush = new LinearGradientBrush(r,
            Color.FromArgb(0x66, 0xA3, 0xFF),
            Color.FromArgb(0x35, 0x74, 0xE8),
            LinearGradientMode.Vertical);
        g.FillPath(brush, path);
    }

    private static void DrawCropCentered(Graphics g, Bitmap src, Rectangle crop, int size, float heightFill)
    {
        var target = size * heightFill;
        var scale = target / crop.Height;
        var dw = crop.Width * scale;
        var dh = crop.Height * scale;
        var dx = (size - dw) / 2f;
        var dy = (size - dh) / 2f;
        g.DrawImage(src, new RectangleF(dx, dy, dw, dh), crop, GraphicsUnit.Pixel);
    }

    private static Rectangle CropToContent(Bitmap bmp)
    {
        int minX = bmp.Width, minY = bmp.Height, maxX = -1, maxY = -1;
        for (var y = 0; y < bmp.Height; y++)
        {
            for (var x = 0; x < bmp.Width; x++)
            {
                if (bmp.GetPixel(x, y).A <= 8) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }
        return maxX < 0 ? new Rectangle(0, 0, bmp.Width, bmp.Height)
                        : Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, radius * 2, radius * 2, 180, 90);
        p.AddArc(r.Right - radius * 2, r.Y, radius * 2, radius * 2, 270, 90);
        p.AddArc(r.Right - radius * 2, r.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
        p.AddArc(r.X, r.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
        p.CloseFigure();
        return p;
    }

    private static void SaveBrandPng(Bitmap src, Rectangle crop, string path, int size)
    {
        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.CompositingMode = CompositingMode.SourceOver;
            DrawCropCentered(g, src, crop, size, 0.92f);
        }
        bmp.Save(path, ImageFormat.Png);
    }

    private static Bitmap CloneAs32bpp(Bitmap src)
    {
        var dst = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(dst);
        g.DrawImage(src, 0, 0);
        return dst;
    }
}
