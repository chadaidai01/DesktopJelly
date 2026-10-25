using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Merge spatially close icons, splitting sparse layouts so empty desktop pixels
// do not turn a small animation into a full-screen texture upload.
internal sealed class JellyLayer : NativeWindow, IDisposable
{
    readonly IntPtr host;
    readonly List<DesktopItem> items;
    readonly Rectangle bounds;
    readonly RectangleF[] hits;
    IntPtr memory, dib, previous;
    Bitmap frame, labels;
    Graphics painter;
    public double LastPaintMs, LastSubmitMs;
    public int PixelCount { get { return bounds.Width * bounds.Height; } }
    public Bitmap CaptureForTest() { return frame.Clone(new Rectangle(0, 0, frame.Width, frame.Height), PixelFormat.Format32bppPArgb); }
    public JellyLayer(IntPtr parent, List<DesktopItem> data)
    {
        host = parent; items = data; bounds = Union(data); hits = new RectangleF[data.Count];
    }
    static int Size(DesktopItem item) { return Math.Max(16, Math.Min(256, item.Rect.Height)); }
    static Rectangle SpriteBounds(DesktopItem item)
    {
        int size = Size(item), width = Math.Max(160, (int)Math.Ceiling(size * 1.468) + 8), high = (int)Math.Ceiling(size * 1.65) + 4;
        return new Rectangle(item.Rect.Left + item.Rect.Width / 2 - width / 2, item.Rect.Bottom - high, width, high + 56);
    }
    static Rectangle Union(List<DesktopItem> data)
    {
        Rectangle result = SpriteBounds(data[0]);
        for (int i = 1; i < data.Count; i++) result = Rectangle.Union(result, SpriteBounds(data[i]));
        return result;
    }
    public static List<JellyLayer> CreateGroups(IntPtr host, List<DesktopItem> data)
    {
        List<JellyLayer> result = new List<JellyLayer>();
        if (data.Count > 0) Split(host, new List<DesktopItem>(data), result);
        return result;
    }
    static void Split(IntPtr host, List<DesktopItem> data, List<JellyLayer> result)
    {
        Rectangle all = Union(data); long occupied = 0;
        foreach (DesktopItem item in data) { Rectangle r = SpriteBounds(item); occupied += (long)r.Width * r.Height; }
        if (data.Count == 1 || ((long)all.Width * all.Height <= occupied * 1.25 && (long)all.Width * all.Height <= 2500000))
        { result.Add(new JellyLayer(host, data)); return; }
        bool horizontal = all.Width >= all.Height;
        data.Sort(delegate(DesktopItem a, DesktopItem b) { return horizontal ? a.Rect.Left.CompareTo(b.Rect.Left) : a.Rect.Top.CompareTo(b.Rect.Top); });
        int half = data.Count / 2;
        Split(host, data.GetRange(0, half), result); Split(host, data.GetRange(half, data.Count - half), result);
    }
    public void Prepare()
    {
        CreateHandle(new CreateParams { Caption = "DesktopJellyBatch", Parent = host, Style = 0x40000000, ExStyle = 0x08080080, ClassStyle = 8,
            X = bounds.Left, Y = bounds.Top, Width = bounds.Width, Height = bounds.Height });
        Native.BITMAPINFO info = new Native.BITMAPINFO();
        info.Header.Size = (uint)Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER)); info.Header.Width = bounds.Width;
        info.Header.Height = -bounds.Height; info.Header.Planes = 1; info.Header.BitCount = 32;
        IntPtr pixels; dib = Native.CreateDIBSection(IntPtr.Zero, ref info, 0, out pixels, IntPtr.Zero, 0);
        if (dib == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
        memory = Native.CreateCompatibleDC(IntPtr.Zero);
        if (memory == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
        previous = Native.SelectObject(memory, dib);
        frame = new Bitmap(bounds.Width, bounds.Height, bounds.Width * 4, PixelFormat.Format32bppPArgb, pixels);
        painter = Graphics.FromImage(frame); painter.InterpolationMode = InterpolationMode.HighQualityBilinear; painter.PixelOffsetMode = PixelOffsetMode.HighQuality;
        labels = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb);
        using (Graphics g = Graphics.FromImage(labels))
        using (Font font = new Font("Microsoft YaHei UI", 9))
        using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter })
        {
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            foreach (DesktopItem item in items)
            {
                Rectangle r = SpriteBounds(item);
                RectangleF label = new RectangleF(r.Left - bounds.Left + 8, item.Rect.Bottom - bounds.Top + 5, r.Width - 16, 48);
                RectangleF shadow = label; shadow.Offset(1, 1);
                g.DrawString(item.Name, font, Brushes.Black, shadow, format); g.DrawString(item.Name, font, Brushes.White, label, format);
            }
        }
        UpdatePhase(0, 0, false);
    }
    public void Show()
    {
        Native.ShowWindow(Handle, 4); Native.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x53); UpdatePhase(0, 0, false);
        Native.RECT r;
        if (!Native.IsWindowVisible(Handle) || Native.GetParent(Handle) != host || !Native.GetWindowRect(Handle, out r) || r.Right - r.Left != bounds.Width || r.Bottom - r.Top != bounds.Height)
            throw new Exception("动画层显示检查失败，已保留原图标。");
    }
    public void UpdatePhase(double phase, double amount, bool stagger)
    {
        long started = Stopwatch.GetTimestamp();
        painter.CompositingMode = CompositingMode.SourceCopy; painter.DrawImageUnscaled(labels, 0, 0);
        painter.CompositingMode = CompositingMode.SourceOver;
        for (int i = 0; i < items.Count; i++)
        {
            DesktopItem item = items[i]; double sx, sy;
            Motion.Scale(phase + (stagger ? item.Index * .13 : 0), amount, out sx, out sy);
            float w = (float)(Size(item) * sx), h = (float)(Size(item) * sy);
            RectangleF r = new RectangleF(item.Rect.Left + item.Rect.Width / 2f - bounds.Left - w / 2, item.Rect.Bottom - bounds.Top - h, w, h);
            hits[i] = r; painter.DrawImage(item.Image, r);
        }
        painter.Flush(FlushIntention.Sync); long submitted = Stopwatch.GetTimestamp();
        Native.SIZE size = new Native.SIZE(bounds.Width, bounds.Height); Native.POINT origin = new Native.POINT(0, 0);
        Native.BLEND blend = new Native.BLEND { SourceConstantAlpha = 255, AlphaFormat = 1 };
        if (!Native.UpdateLayeredWindow(Handle, IntPtr.Zero, IntPtr.Zero, ref size, memory, ref origin, 0, ref blend, 2)) throw new System.ComponentModel.Win32Exception();
        LastPaintMs = (submitted - started) * 1000.0 / Stopwatch.Frequency;
        LastSubmitMs = (Stopwatch.GetTimestamp() - submitted) * 1000.0 / Stopwatch.Frequency;
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x21) { m.Result = new IntPtr(3); return; }
        if (m.Msg == 0x203)
        {
            long xy = m.LParam.ToInt64(); Point p = new Point((short)(xy & 65535), (short)((xy >> 16) & 65535));
            for (int i = items.Count - 1; i >= 0; i--)
            {
                Rectangle r = SpriteBounds(items[i]); RectangleF label = new RectangleF(r.Left - bounds.Left, items[i].Rect.Bottom - bounds.Top, r.Width, 53);
                if (!hits[i].Contains(p) && !label.Contains(p)) continue;
                string path = items[i].Path;
                try { if (!String.IsNullOrEmpty(path)) { if (path.StartsWith("::")) Process.Start("explorer.exe", "shell:" + path); else Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } }
                catch (Exception error) { MessageBox.Show(error.Message, "无法打开图标"); }
                break;
            }
            return;
        }
        base.WndProc(ref m);
    }
    public void Dispose()
    {
        if (Handle != IntPtr.Zero) DestroyHandle();
        if (painter != null) { painter.Dispose(); painter = null; }
        if (frame != null) { frame.Dispose(); frame = null; }
        if (labels != null) { labels.Dispose(); labels = null; }
        if (previous != IntPtr.Zero) { Native.SelectObject(memory, previous); previous = IntPtr.Zero; }
        if (dib != IntPtr.Zero) { Native.DeleteObject(dib); dib = IntPtr.Zero; }
        if (memory != IntPtr.Zero) { Native.DeleteDC(memory); memory = IntPtr.Zero; }
        foreach (DesktopItem item in items) if (item.Image != null) { item.Image.Dispose(); item.Image = null; }
    }
}
