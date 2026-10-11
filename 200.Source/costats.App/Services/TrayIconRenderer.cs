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

    // 계약: 앱의 기본 아이콘 모양 — 트레이·exe·설치 관리자가 모두 이것 하나를 쓴다.
    //       바꾸면 800.Deploy\make-icons.ps1 을 다시 돌려 .ico 를 새로 뽑는다.
    // 계약: AppSettings.TrayIconStyle 의 기본값과 같은 값이어야 한다 — 층이 반대라 상수를 공유하지 못한다
    // 왜: 「AI」 두 글자만으로는 남의 앱과 헷갈린다 — 기본은 JHJ + AI 표지이고 「AI」는 고르는 선택지로 남긴다(080.CS앱-화면-공통 §2)
    public const string DefaultStyle = "jhj-ai";

    // 계약: 기본 아이콘의 팔레트 — ThemeManager 의 기본 팔레트와 같다
    public const string DefaultPalette = "bull";

    public static readonly string[] Presets = [DefaultStyle, "ai", "j", "bars", "ring", "spark"];

    // 계약: 070.아이콘-규격의 아홉 크기다 — 20·40 은 화면 배율 125%·250% 의 트레이·작업 표시줄이 고른다
    private static readonly int[] IcoSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];

    /// <summary>기본 아이콘을 여러 크기로 그려 .ico 한 파일로 쓴다 — 리소스 아이콘의 유일한 출처다.</summary>
    // 왜: 그리는 코드가 두 벌이면 트레이와 exe 아이콘이 조용히 어긋난다 — 같은 Render() 에서 뽑는다
    public static void SaveIcoFile(string path, string? style = null, string? palette = null)
    {
        var frames = IcoSizes.Select(size =>
        {
            using var bitmap = Render(style ?? DefaultStyle, palette ?? DefaultPalette, size);
            // 함정: 작은 판을 PNG 로 넣으면 Windows 의 작은 아이콘 경로(작업 표시줄 · Alt+Tab)가 못 읽어 기본 아이콘이 뜬다 — 256 만 PNG 다
            if (size < 256)
            {
                return (Size: size, Bytes: ToDib(bitmap));
            }

            using var buffer = new MemoryStream();
            bitmap.Save(buffer, ImageFormat.Png);
            return (Size: size, Bytes: buffer.ToArray());
        }).ToList();

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var file = File.Create(path);
        using var w = new BinaryWriter(file);

        // 계약: ICONDIR — 예약 0, 종류 1(아이콘), 그림 수
        w.Write((ushort)0);
        w.Write((ushort)1);
        w.Write((ushort)frames.Count);

        var offset = 6 + (16 * frames.Count);
        foreach (var (size, bytes) in frames)
        {
            // 함정: 256 은 한 바이트에 안 들어가 0 으로 적는다 — 읽는 쪽이 256 으로 해석한다
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)(size >= 256 ? 0 : size));
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((ushort)1);
            w.Write((ushort)32);
            w.Write((uint)bytes.Length);
            w.Write((uint)offset);
            offset += bytes.Length;
        }

        foreach (var (_, bytes) in frames)
        {
            w.Write(bytes);
        }
    }

    /// 계약: ICO 안의 32bpp BGRA 한 판 — 머리의 높이는 두 배(색 판 + AND 마스크), 줄은 아래부터 적는다
    private static byte[] ToDib(Bitmap bitmap)
    {
        int w = bitmap.Width, h = bitmap.Height;
        using var buffer = new MemoryStream();
        using var writer = new BinaryWriter(buffer);
        writer.Write(40);
        writer.Write(w);
        writer.Write(h * 2);
        writer.Write((ushort)1);
        writer.Write((ushort)32);
        writer.Write(new byte[24]);

        for (var y = h - 1; y >= 0; y--)
        {
            for (var x = 0; x < w; x++)
            {
                var c = bitmap.GetPixel(x, y);
                writer.Write(c.B);
                writer.Write(c.G);
                writer.Write(c.R);
                writer.Write(c.A);
            }
        }

        writer.Write(new byte[((w + 31) / 32) * 4 * h]);
        writer.Flush();
        return buffer.ToArray();
    }

    public static string IconDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JHJ_AI-Usage-Monitor", "icons");

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
            case DefaultStyle:
                // 계약: 32px 이하는 「J + 표지」로 줄여 그린다 — 070.아이콘-규격 §1 「작은 판」
                if (s <= 32)
                {
                    DrawJAi(g, s, c[2], c[3]);
                }
                else
                {
                    DrawJhjAi(g, s, c[2], c[3]);
                }

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

    // 계약: 256 좌표로 그린다 — 070.아이콘 생성기(make_jhj_icon.py)의 monogram() · feat_ai() 와 같은 숫자다
    private static void DrawJhjAi(Graphics g, int s, Color ink, Color mark)
    {
        var state = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.ScaleTransform(s / 256f, s / 256f);

        const float wj = 52, wh = 58, gap = 14, top = 46, h = 90, w = 24;
        var x = (256 - (wj * 2 + wh + gap * 2)) / 2;
        using var pen = new Pen(ink, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var letters = new GraphicsPath();
        AddLetterJ(letters, x, top, wj, h, w);
        AddLetterH(letters, x + wj + gap, top, wh, h, w);
        AddLetterJ(letters, x + wj + gap + wh + gap, top, wj, h, w);
        g.DrawPath(pen, letters);

        using var gold = new SolidBrush(mark);
        using var soft = new SolidBrush(Color.FromArgb(215, ink));
        g.FillPolygon(gold, Sparkle(120, 197, 34, 10));
        g.FillPolygon(soft, Sparkle(172, 178, 15, 5));
        g.Restore(state);
    }

    // 계약: 070.아이콘 생성기(make_jhj_icon.py)의 duo_j() · duo_ai() 와 같은 숫자다
    private static void DrawJAi(Graphics g, int s, Color ink, Color mark)
    {
        var t = Math.Max(2, (int)Math.Round(s * 0.17));
        int left = (int)Math.Round(s * 0.10), right = (int)Math.Round(s * 0.47);
        int top = (int)Math.Round(s * 0.16), bottom = s - (int)Math.Round(s * 0.16);
        if ((right - left) % 2 == 1)
        {
            right += 1; // 함정: 폭이 홀수면 갈고리 중심이 반 픽셀에 걸려 세로획과 사이에 틈이 생긴다
        }

        var radius = (right - left) / 2;
        var cy = bottom - radius;
        var head = left + (int)Math.Round((right - left) * 0.25);
        using var inkBrush = new SolidBrush(ink);
        g.SmoothingMode = SmoothingMode.None;
        g.FillRectangle(inkBrush, head, top, right - head, t);
        g.FillRectangle(inkBrush, right - t, top, t, cy + 1 - top);
        g.FillRectangle(inkBrush, left, cy - 1, t, 2);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = radius - t / 2f;
        var cx = (left + right) / 2f;
        using var pen = new Pen(ink, t);
        g.DrawArc(pen, cx - r, cy - r, r * 2, r * 2, 0, 180);

        static float Snap(double v) => (float)(Math.Round(v * 2) / 2);
        using var gold = new SolidBrush(mark);
        g.FillPolygon(gold, Sparkle(Snap(s * 0.73), Snap(s * 0.54), s * 0.21f, s * 0.06f));
        if (s >= 24)
        {
            using var soft = new SolidBrush(Color.FromArgb(230, ink));
            g.FillPolygon(soft, Sparkle(Snap(s * 0.88), Snap(s * 0.28), s * 0.09f, s * 0.03f));
        }
    }

    private static void AddLetterJ(GraphicsPath path, float x, float top, float width, float height, float w)
    {
        var i = w / 2;
        float stem = x + width - i, ty = top + i, by = top + height - i;
        var r = (width - w) / 2;
        float cx = x + i + r, cy = by - r;
        path.StartFigure();
        path.AddLine(x + width * 0.30f, ty, stem, ty);
        path.AddLine(stem, ty, stem, cy);
        path.AddArc(cx - r, cy - r, r * 2, r * 2, 0, 180);
    }

    private static void AddLetterH(GraphicsPath path, float x, float top, float width, float height, float w)
    {
        var i = w / 2;
        path.StartFigure();
        path.AddLine(x + i, top + i, x + i, top + height - i);
        path.StartFigure();
        path.AddLine(x + width - i, top + i, x + width - i, top + height - i);
        path.StartFigure();
        path.AddLine(x + i, top + height / 2, x + width - i, top + height / 2);
    }

    /// 네 갈래 반짝임 — AI 를 뜻하는 표지. outer 는 뾰족한 끝, inner 는 사이 골의 반지름이다
    private static PointF[] Sparkle(float cx, float cy, float outer, float inner) =>
        Enumerable.Range(0, 8).Select(k =>
        {
            var angle = (-90 + k * 45) * Math.PI / 180;
            var radius = k % 2 == 0 ? outer : inner;
            return new PointF((float)(cx + radius * Math.Cos(angle)), (float)(cy + radius * Math.Sin(angle)));
        }).ToArray();

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
