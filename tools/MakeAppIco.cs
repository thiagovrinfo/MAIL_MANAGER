using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

var icoDir = args[0];
var logosDir = args[1];
var appPng = args[2];
var appIco = args[3];

Directory.CreateDirectory(logosDir);

var folders = new (string Src, string Dst)[]
{
    ("Entrada.jpg", "inbox.png"),
    ("Inovafarma.jpg", "inovafarma.png"),
    ("Hiper.jpg", "hiper.png"),
    ("Google.jpg", "contas.png"),
    ("Contabilidade.jpg", "contabilidade.png"),
    ("Discord.jpg", "discord.png"),
    ("Rascunhos.jpg", "drafts.png"),
    ("Enviados.jpg", "sent.png"),
    ("Lixeira.jpg", "trash.png"),
};

foreach (var (src, dst) in folders)
{
    var png = ProcessIcon(Path.Combine(icoDir, src), round: true);
    png.Save(Path.Combine(logosDir, dst), ImageFormat.Png);
    png.Dispose();
    Console.WriteLine("PNG " + dst);
}

using var app = ProcessIcon(Path.Combine(icoDir, "VRINFO.jpg"), round: true);
app.Save(appPng, ImageFormat.Png);
WriteIco(app, appIco);
Console.WriteLine("PNG app.png + ICO app.ico");

static Bitmap ProcessIcon(string path, bool round)
{
    using var src = Image.FromFile(path);
    var side = Math.Min(src.Width, src.Height);
    var sx = (src.Width - side) / 2;
    var sy = (src.Height - side) / 2;
    const int size = 512;
    var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using (var g = Graphics.FromImage(bmp))
    {
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.CompositingMode = CompositingMode.SourceCopy;
        g.Clear(Color.Transparent);
        g.DrawImage(src, new Rectangle(0, 0, size, size), new Rectangle(sx, sy, side, side), GraphicsUnit.Pixel);
    }

    KnockOutExternalWhite(bmp);
    if (round)
        ApplyCircleMask(bmp);
    return bmp;
}

static void KnockOutExternalWhite(Bitmap bmp)
{
    var w = bmp.Width;
    var h = bmp.Height;
    var rect = new Rectangle(0, 0, w, h);
    var data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
    var stride = data.Stride;
    var raw = new byte[stride * h];
    Marshal.Copy(data.Scan0, raw, 0, raw.Length);

    var seen = new bool[w * h];
    var q = new Queue<int>();

    void Enqueue(int x, int y)
    {
        var i = y * w + x;
        if (seen[i]) return;
        seen[i] = true;
        q.Enqueue(i);
    }

    for (var x = 0; x < w; x++)
    {
        Enqueue(x, 0);
        Enqueue(x, h - 1);
    }
    for (var y = 0; y < h; y++)
    {
        Enqueue(0, y);
        Enqueue(w - 1, y);
    }

    while (q.Count > 0)
    {
        var i = q.Dequeue();
        var x = i % w;
        var y = i / w;
        var p = y * stride + x * 4;
        var b = raw[p];
        var g = raw[p + 1];
        var r = raw[p + 2];
        if (!IsExternalWhite(r, g, b))
            continue;

        raw[p] = 0;
        raw[p + 1] = 0;
        raw[p + 2] = 0;
        raw[p + 3] = 0;

        if (x > 0) Enqueue(x - 1, y);
        if (x + 1 < w) Enqueue(x + 1, y);
        if (y > 0) Enqueue(x, y - 1);
        if (y + 1 < h) Enqueue(x, y + 1);
    }

    // suaviza halo branco na borda do recorte
    for (var y = 0; y < h; y++)
    {
        for (var x = 0; x < w; x++)
        {
            var p = y * stride + x * 4;
            if (raw[p + 3] == 0) continue;
            var b = raw[p];
            var g = raw[p + 1];
            var r = raw[p + 2];
            var white = Whiteness(r, g, b);
            if (white < 0.55) continue;
            if (!TouchesTransparent(raw, stride, w, h, x, y)) continue;
            var alpha = (byte)Math.Clamp(255 * (1 - white) / 0.45, 0, 255);
            raw[p + 3] = alpha;
            if (alpha == 0)
            {
                raw[p] = 0;
                raw[p + 1] = 0;
                raw[p + 2] = 0;
            }
        }
    }

    Marshal.Copy(raw, 0, data.Scan0, raw.Length);
    bmp.UnlockBits(data);
}

static bool TouchesTransparent(byte[] raw, int stride, int w, int h, int x, int y)
{
    for (var dy = -1; dy <= 1; dy++)
    for (var dx = -1; dx <= 1; dx++)
    {
        var nx = x + dx;
        var ny = y + dy;
        if (nx < 0 || ny < 0 || nx >= w || ny >= h) return true;
        if (raw[ny * stride + nx * 4 + 3] == 0) return true;
    }
    return false;
}

static bool IsExternalWhite(byte r, byte g, byte b)
{
    var max = Math.Max(r, Math.Max(g, b));
    var min = Math.Min(r, Math.Min(g, b));
    return max >= 228 && min >= 210 && (max - min) <= 28;
}

static double Whiteness(byte r, byte g, byte b)
{
    var min = Math.Min(r, Math.Min(g, b));
    var max = Math.Max(r, Math.Max(g, b));
    if (max - min > 40) return 0;
    return (r + g + b) / (3.0 * 255.0);
}

static void ApplyCircleMask(Bitmap bmp)
{
    var w = bmp.Width;
    var h = bmp.Height;
    var cx = (w - 1) / 2.0;
    var cy = (h - 1) / 2.0;
    var radius = Math.Min(w, h) / 2.0 - 0.5;
    var rect = new Rectangle(0, 0, w, h);
    var data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
    var stride = data.Stride;
    var raw = new byte[stride * h];
    Marshal.Copy(data.Scan0, raw, 0, raw.Length);
    for (var y = 0; y < h; y++)
    {
        for (var x = 0; x < w; x++)
        {
            var dx = x - cx;
            var dy = y - cy;
            var d = Math.Sqrt(dx * dx + dy * dy);
            var p = y * stride + x * 4;
            double cover;
            if (d >= radius) cover = 0;
            else if (d <= radius - 1.25) cover = 1;
            else cover = radius - d;
            var a = (byte)Math.Clamp(raw[p + 3] * cover, 0, 255);
            raw[p + 3] = a;
            if (a == 0)
            {
                raw[p] = 0;
                raw[p + 1] = 0;
                raw[p + 2] = 0;
            }
        }
    }
    Marshal.Copy(raw, 0, data.Scan0, raw.Length);
    bmp.UnlockBits(data);
}

static void WriteIco(Bitmap source, string icoPath)
{
    int[] sizes = [16, 24, 32, 48, 64, 128, 256];
    var images = new List<byte[]>();
    foreach (var size in sizes)
    {
        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingMode = CompositingMode.SourceCopy;
            g.Clear(Color.Transparent);
            g.DrawImage(source, 0, 0, size, size);
        }
        images.Add(ToBmpIcoImage(bmp));
    }

    using var fs = File.Create(icoPath);
    using var bw = new BinaryWriter(fs);
    bw.Write((ushort)0);
    bw.Write((ushort)1);
    bw.Write((ushort)images.Count);
    var offset = 6 + 16 * images.Count;
    for (var i = 0; i < images.Count; i++)
    {
        var size = sizes[i];
        bw.Write((byte)(size >= 256 ? 0 : size));
        bw.Write((byte)(size >= 256 ? 0 : size));
        bw.Write((byte)0);
        bw.Write((byte)0);
        bw.Write((ushort)1);
        bw.Write((ushort)32);
        bw.Write(images[i].Length);
        bw.Write(offset);
        offset += images[i].Length;
    }
    foreach (var image in images)
        bw.Write(image);
}

static byte[] ToBmpIcoImage(Bitmap bmp)
{
    var w = bmp.Width;
    var h = bmp.Height;
    var rect = new Rectangle(0, 0, w, h);
    var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
    var stride = data.Stride;
    var raw = new byte[stride * h];
    Marshal.Copy(data.Scan0, raw, 0, raw.Length);
    bmp.UnlockBits(data);

    var xor = new byte[w * h * 4];
    for (var y = 0; y < h; y++)
        Buffer.BlockCopy(raw, (h - 1 - y) * stride, xor, y * w * 4, w * 4);

    var andStride = ((w + 31) / 32) * 4;
    var and = new byte[andStride * h];
    for (var y = 0; y < h; y++)
    {
        var srcY = h - 1 - y;
        for (var x = 0; x < w; x++)
        {
            var alpha = raw[srcY * stride + x * 4 + 3];
            if (alpha >= 128) continue;
            var bit = 7 - (x % 8);
            and[y * andStride + x / 8] |= (byte)(1 << bit);
        }
    }

    using var ms = new MemoryStream();
    using var bw = new BinaryWriter(ms);
    bw.Write(40);
    bw.Write(w);
    bw.Write(h * 2);
    bw.Write((ushort)1);
    bw.Write((ushort)32);
    bw.Write(0);
    bw.Write(xor.Length);
    bw.Write(0);
    bw.Write(0);
    bw.Write(0);
    bw.Write(0);
    bw.Write(xor);
    bw.Write(and);
    return ms.ToArray();
}
