using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace costats.App.Services;

/// <summary>
/// 트레이 아이콘을 그린다 — 기본 제공 모양은 JHJ 공통 바탕(둥근 사각·팔레트 그라데이션) 위에, 사용자 그림은 그대로.
/// 규격(작은 판은 칸을 채우고 정수 픽셀에 맞춘다): JHJ_DEV/000.AGENTS_MD/070.아이콘/070.아이콘-규격.md
/// 함정: 사용자 그림은 설치 폴더가 아니라 데이터 폴더(icons)에 둔다 — 설치 폴더는 업데이트 때 통째로 교체된다.
/// </summary>
public static class TrayIconRenderer
{
    public const string CustomPrefix = "custom:";
    public const string TileStyle = "tile";
    public const int CanvasSize = 16;

    public static readonly string[] Presets = ["ai", "j", "bars", "ring", "spark"];

    public static string IconDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiUsageMonitor", "icons");

    // 계약: 사용자 아이콘은 icons\drawn-<시각>.png(직접 그림) · icons\image-<시각>.png(불러온 그림), 모양 id 는 "custom:<파일 이름>"
    public static IReadOnlyList<string> CustomStyles() => Directory.Exists(IconDir)
        ? Directory.GetFiles(IconDir, "*.png")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name!.StartsWith("drawn-", StringComparison.Ordinal) || name.StartsWith("image-", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(name => CustomPrefix + name)
            .ToList()
        : [];

    public static bool IsCustom(string? style) => style?.StartsWith(CustomPrefix, StringComparison.Ordinal) == true;

    public static bool IsDrawing(string style) => style.StartsWith(CustomPrefix + "drawn-", StringComparison.Ordinal);

    private static string PathOf(string style) => Path.Combine(IconDir, Path.GetFileName(style[CustomPrefix.Length..]) + ".png");

    public static void Delete(string style)
    {
        if (IsCustom(style) && File.Exists(PathOf(style)))
        {
            File.Delete(PathOf(style));
        }
    }

    private static string NewName(string kind) => $"{kind}-{DateTime.Now:yyMMddHHmmss}";

    /// <summary>모양·팔레트·사용자 그림이 바뀌면 트레이가 아이콘을 다시 그린다.</summary>
    public static event Action? Changed;

    public static void NotifyChanged() => Changed?.Invoke();

    // 계약: 070.아이콘 생성기(make_jhj_icon.py)의 PALETTES 와 같은 값 — 바탕 위 · 바탕 아래 · 글자 · 표지
    private static readonly Dictionary<string, string[]> PaletteHex = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bull"] = ["#CF3A2E", "#8A1A14", "#FFF8F3", "#E6C36A"],
        ["navy"] = ["#2F64C2", "#163C80", "#FFFFFF", "#5FD0D8"],
        ["emerald"] = ["#159463", "#0A5A3D", "#F2FFF9", "#E0C068"],
        ["violet"] = ["#7D52D8", "#4A2893", "#FAF7FF", "#F08CC0"],
        ["slate"] = ["#46566E", "#222B38", "#F8FAFC", "#F59E0B"],
    };

    public static Color[] ColorsOf(string palette) =>
        (PaletteHex.TryGetValue(palette, out var hex) ? hex : PaletteHex["bull"]).Select(ColorTranslator.FromHtml).ToArray();

    public static Bitmap Render(string? style, string palette, int s)
    {
        var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.PixelOffsetMode = PixelOffsetMode.Half;

        // 왜: 16px 그림은 픽셀을 그대로 키워야 또렷하고, 큰 사진은 부드럽게 줄여야 깨지지 않는다
        if (IsCustom(style) && TryDrawImage(g, PathOf(style!), s,
                IsDrawing(style!) ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic))
        {
            return bmp;
        }

        var c = ColorsOf(palette);
        DrawTile(g, s, c[0], c[1]);
        switch (style)
        {
            case TileStyle:
                break;
            case "ai":
                DrawAI(g, s, c[2]);
                break;
            case "bars":
                DrawBars(g, s, c[2], c[3]);
                break;
            case "ring":
                DrawRing(g, s, c[2], c[3]);
                break;
            case "spark":
                DrawSpark(g, s, c[2], c[3]);
                break;
            default:
                DrawJ(g, s, c[2]);
                break;
        }

        return bmp;
    }

    private static void DrawTile(Graphics g, int s, Color top, Color bottom)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = s * 0.22f * 2;
        using var path = new GraphicsPath();
        path.AddArc(0, 0, r, r, 180, 90);
        path.AddArc(s - r, 0, r, r, 270, 90);
        path.AddArc(s - r, s - r, r, r, 0, 90);
        path.AddArc(0, s - r, r, r, 90, 90);
        path.CloseFigure();
        using var brush = new LinearGradientBrush(new Rectangle(0, 0, s, s + 1), top, bottom, LinearGradientMode.Vertical);
        g.FillPath(brush, path);
    }

    // 왜: 획을 정수 픽셀에 맞춰야 16px 에서 경계가 번지지 않는다 — 직선 획은 안티앨리어싱을 끈다
    // 왜: 16px 에서 두 글자가 읽히려면 획을 정수 픽셀에 맞춰야 한다 — 안티앨리어싱 없이 사각형으로만 그린다
    private static void DrawAI(Graphics g, int s, Color ink)
    {
        var t = Math.Max(2, (int)Math.Round(s * 0.13));
        int top = (int)Math.Round(s * 0.19), bottom = s - (int)Math.Round(s * 0.19);
        int aw = (int)Math.Round(s * 0.44), iw = (int)Math.Round(s * 0.25);
        var gap = Math.Max(1, (int)Math.Round(s * 0.12));
        var x0 = (s - (aw + gap + iw)) / 2;
        var mid = top + (bottom - top) / 2;
        using var brush = new SolidBrush(ink);
        g.SmoothingMode = SmoothingMode.None;
        g.FillRectangle(brush, x0, top + t, t, bottom - top - t);
        g.FillRectangle(brush, x0 + aw - t, top + t, t, bottom - top - t);
        g.FillRectangle(brush, x0 + 1, top, aw - 2, t);
        g.FillRectangle(brush, x0 + t, mid, aw - t * 2, t);
        var ix = x0 + aw + gap;
        g.FillRectangle(brush, ix, top, iw, t);
        g.FillRectangle(brush, ix, bottom - t, iw, t);
        g.FillRectangle(brush, ix + (iw - t) / 2, top, t, bottom - top);
    }

    private static void DrawJ(Graphics g, int s, Color ink)
    {
        var t = Math.Max(3, (int)Math.Round(s * 0.21));
        int left = (int)Math.Round(s * 0.20), right = s - (int)Math.Round(s * 0.17);
        int top = (int)Math.Round(s * 0.14), bottom = s - (int)Math.Round(s * 0.14);
        if ((right - left) % 2 == 1)
        {
            left -= 1; // 함정: 폭이 홀수면 갈고리 중심이 반 픽셀에 걸려 세로획과 사이에 틈이 생긴다
        }

        var radius = (right - left) / 2;
        var cy = bottom - radius;
        using var ink1 = new SolidBrush(ink);
        g.SmoothingMode = SmoothingMode.None;
        g.FillRectangle(ink1, left + (int)Math.Round((right - left) * 0.3), top, right - left - (int)Math.Round((right - left) * 0.3), t);
        g.FillRectangle(ink1, right - t, top, t, cy + 1 - top);
        g.FillRectangle(ink1, left, cy - 1, t, 2);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = radius - t / 2f;
        var cx = (left + right) / 2f;
        using var pen = new Pen(ink, t);
        g.DrawArc(pen, cx - r, cy - r, r * 2, r * 2, 0, 180);
    }

    private static void DrawBars(Graphics g, int s, Color ink, Color mark)
    {
        var bw = Math.Max(2, (int)Math.Round(s * 0.17));
        var gap = Math.Max(1, (int)Math.Round(s * 0.08));
        var x = (s - (bw * 3 + gap * 2)) / 2;
        var baseLine = s - (int)Math.Round(s * 0.18);
        double[] heights = [0.30, 0.48, 0.66];
        g.SmoothingMode = SmoothingMode.None;
        for (var i = 0; i < 3; i++)
        {
            var h = (int)Math.Round(s * heights[i]);
            using var brush = new SolidBrush(i == 2 ? mark : ink);
            g.FillRectangle(brush, x + i * (bw + gap), baseLine - h, bw, h);
        }
    }

    private static void DrawRing(Graphics g, int s, Color ink, Color mark)
    {
        var t = Math.Max(2, (int)Math.Round(s * 0.16));
        var r = s * 0.30f;
        var c = s / 2f;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var track = new Pen(Color.FromArgb(90, ink), t);
        using var fill = new Pen(mark, t);
        g.DrawEllipse(track, c - r, c - r, r * 2, r * 2);
        g.DrawArc(fill, c - r, c - r, r * 2, r * 2, -90, 270);
    }

    private static void DrawSpark(Graphics g, int s, Color ink, Color mark)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var c = s / 2f;
        var points = Enumerable.Range(0, 8).Select(k =>
        {
            var angle = (-90 + k * 45) * Math.PI / 180;
            var radius = k % 2 == 0 ? s * 0.38 : s * 0.15;
            return new PointF((float)(c + radius * Math.Cos(angle)), (float)(c + radius * Math.Sin(angle)));
        }).ToArray();
        using var brush = new SolidBrush(ink);
        g.FillPolygon(brush, points);
        using var dot = new SolidBrush(mark);
        var d = Math.Max(2f, s * 0.16f);
        g.FillEllipse(dot, s * 0.78f - d / 2, s * 0.22f - d / 2, d, d);
    }

    private static bool TryDrawImage(Graphics g, string path, int s, InterpolationMode mode)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            // 왜: Image.FromFile 은 파일을 잠가 다시 불러오기·덮어쓰기가 막힌다 — 메모리로 읽어 쓴다
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            using var image = Image.FromStream(stream);
            g.InterpolationMode = mode;
            g.DrawImage(image, new Rectangle(0, 0, s, s));
            return true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>고른 그림을 가운데 정사각형으로 잘라 256px PNG 로 새로 저장한다.</summary>
    /// <returns>새 아이콘의 모양 id</returns>
    public static string ImportFile(string sourcePath)
    {
        using var stream = new MemoryStream(File.ReadAllBytes(sourcePath));
        using var image = Image.FromStream(stream);
        var side = Math.Min(image.Width, image.Height);
        var crop = new Rectangle((image.Width - side) / 2, (image.Height - side) / 2, side, side);
        using var bmp = new Bitmap(256, 256, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(image, new Rectangle(0, 0, 256, 256), crop, GraphicsUnit.Pixel);
        }

        Directory.CreateDirectory(IconDir);
        var name = NewName("image");
        bmp.Save(Path.Combine(IconDir, name + ".png"), ImageFormat.Png);
        return CustomPrefix + name;
    }

    /// <summary>16×16 칸의 색(행 우선)을 새 그림으로 저장한다.</summary>
    /// <returns>새 아이콘의 모양 id</returns>
    public static string SaveDrawing(IReadOnlyList<Color> pixels)
    {
        using var bmp = new Bitmap(CanvasSize, CanvasSize, PixelFormat.Format32bppArgb);
        for (var i = 0; i < pixels.Count && i < CanvasSize * CanvasSize; i++)
        {
            bmp.SetPixel(i % CanvasSize, i / CanvasSize, pixels[i]);
        }

        Directory.CreateDirectory(IconDir);
        var name = NewName("drawn");
        bmp.Save(Path.Combine(IconDir, name + ".png"), ImageFormat.Png);
        return CustomPrefix + name;
    }

    /// <summary>그리기 칸의 시작 그림 — 지금 모양을 16px 로 그린 색(행 우선).</summary>
    public static Color[] PixelsOf(string? style, string palette)
    {
        using var bmp = Render(style, palette, CanvasSize);
        var pixels = new Color[CanvasSize * CanvasSize];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = bmp.GetPixel(i % CanvasSize, i / CanvasSize);
        }

        return pixels;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static Icon ToIcon(Bitmap bitmap)
    {
        var handle = bitmap.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    public static System.Windows.Media.ImageSource Preview(string style, string palette, int size)
    {
        using var bmp = Render(style, palette, size);
        using var stream = new MemoryStream();
        bmp.Save(stream, ImageFormat.Png);
        stream.Position = 0;
        var image = new System.Windows.Media.Imaging.BitmapImage();
        image.BeginInit();
        image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
