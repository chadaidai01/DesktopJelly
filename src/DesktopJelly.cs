using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

// Windows 10/11 x64, .NET Framework 4.x. No installation or registry writes.
internal static class Program
{
    [STAThread] static void Main(string[] args)
    {
        try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) { Native.SetProcessDPIAware(); }
        if (args.Length == 3 && args[0] == "--audio-test")
        {
            try
            {
                using (LocalAudioPlayer player = new LocalAudioPlayer()) { player.Start(args[1]); Thread.Sleep(30); player.Stop(); }
                LocalAudioPlayer.Validate(Path.Combine(Application.StartupPath, "reference-audio.wav"));
                File.WriteAllText(args[2], "PASS: native asynchronous loop playback accepted for silent probe; stop succeeded; packaged audio WAV validated.\r\n", Encoding.UTF8);
            }
            catch (Exception error) { File.WriteAllText(args[2], error.ToString()); Environment.ExitCode = 1; }
            return;
        }
        if (args.Length == 3 && args[0] == "--watch")
        {
            IntPtr window = new IntPtr(long.Parse(args[2]));
            uint explorer; Native.GetWindowThreadProcessId(window, out explorer);
            try { Process.GetProcessById(int.Parse(args[1])).WaitForExit(); } catch (ArgumentException) { }
            uint current; Native.GetWindowThreadProcessId(window, out current);
            if (Native.IsWindow(window) && current == explorer) Native.ShowWindow(window, 5);
            return;
        }
        if (args.Length == 2 && args[0] == "--self-test")
        {
            try
            {
                for (int i = 0; i < 5000; i++)
                {
                    double x, y; Motion.Scale(i / 1000.0, 0.65, out x, out y);
                    if (x < 0.3 || y < 0.3 || double.IsNaN(x + y)) throw new Exception("Invalid scale");
                    double nx, ny; Motion.Scale(i / 1000.0 + 1, 0.65, out nx, out ny);
                    if (Math.Abs(x - nx) + Math.Abs(y - ny) > 0.00001) throw new Exception("Non-periodic motion");
                }
                using (Bitmap b = SystemIcons.Application.ToBitmap())
                using (Bitmap frame = JellyIcon.Render(b, "测试图标", 48, 190, 150, 1.3, 0.6))
                    frame.Save(Path.ChangeExtension(args[1], ".png"), ImageFormat.Png);
                string pacing = "";
                using (Form host = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), ShowInTaskbar = false })
                {
                    host.Show();
                    uint resourcesBefore = Native.GetGuiResources(new IntPtr(-1), 0);
                    for (int test = 0; test < 20; test++)
                    {
                        List<DesktopItem> data = new List<DesktopItem>();
                        int number = test == 0 ? 1 : 16;
                        for (int i = 0; i < number; i++) data.Add(new DesktopItem { Index = i, Name = "测试", Rect = new Rectangle(40 + i % 4 * 72, 40 + i / 4 * 72, 48, 48), Image = SystemIcons.Application.ToBitmap() });
                        List<JellyLayer> layers = JellyLayer.CreateGroups(host.Handle, data);
                        try
                        {
                            foreach (JellyLayer layer in layers)
                            {
                                layer.Prepare(); layer.Show(); layer.UpdatePhase(0, .65, false); layer.UpdatePhase(.5, .65, false);
                                if (test == 0) using (Bitmap shot = layer.CaptureForTest())
                                {
                                    shot.Save(Path.ChangeExtension(args[1], ".batch.png"), ImageFormat.Png);
                                    for (int y = 0; y < 60; y++) for (int x = 0; x < shot.Width; x++)
                                        if (shot.GetPixel(x, y).A != 0) throw new Exception("Old stretched pixels remain after compression");
                                }
                            }
                        }
                        finally { foreach (JellyLayer layer in layers) layer.Dispose(); }
                    }
                    uint resourcesAfter = Native.GetGuiResources(new IntPtr(-1), 0);
                    if (resourcesAfter > resourcesBefore + 4) throw new Exception("GDI handles leaked: " + resourcesBefore + " -> " + resourcesAfter);
                    pacing += "Batched windows: 20 create/render/dispose cycles, no stretched-image trails; GDI handles " + resourcesBefore + " -> " + resourcesAfter + ".\r\n";
                    using (JellyIcon icon = new JellyIcon(host.Handle, new DesktopItem { Name = "测试", Rect = new Rectangle(20, 20, 48, 48), Image = SystemIcons.Application.ToBitmap() }))
                    {
                        icon.Prepare();
                        Native.RECT before, after; Native.GetWindowRect(icon.Handle, out before);
                        icon.Show(); icon.UpdateFrame(0.6, 1.3); Application.DoEvents();
                        Native.GetWindowRect(icon.Handle, out after);
                        if (before.Left != after.Left || before.Top != after.Top) throw new Exception("Window moved when shown");
                        foreach (bool hidden in new bool[] { false, true })
                        foreach (int rate in new int[] { 60, 120 })
                        {
                            if (hidden) host.Hide(); else host.Show();
                            int frames = 0; Stopwatch elapsed = Stopwatch.StartNew();
                            using (FramePump pump = new FramePump(host, delegate { icon.UpdateFrame(1, 1); frames++; }))
                            {
                                pump.Fps = rate; pump.Start();
                                pacing += "Hidden=" + hidden + ", timer override=" + pump.TimerProtectionActive + ". ";
                                while (elapsed.Elapsed.TotalSeconds < 1.1) { Application.DoEvents(); Thread.Sleep(1); }
                                pump.Stop(); int stoppedCount = frames; Application.DoEvents();
                                if (frames != stoppedCount) throw new Exception("Queued frame rendered after Stop");
                            }
                            double actual = frames / elapsed.Elapsed.TotalSeconds;
                            pacing += "Target " + rate + " FPS: " + actual.ToString("0.0") + " FPS in isolated window test.\r\n";
                            if (actual < rate * 0.6) throw new Exception("Frame pacing too slow: " + actual);
                        }
                    }
                }
                File.WriteAllText(args[1], "PASS: positive scale; seamless loop; transparent icon/label rendering; native layered child visible; parent/size/position stable after Show and message pump; no queued frames after Stop.\r\n" + pacing + "Explorer desktop visual appearance requires interactive verification.\r\n", Encoding.UTF8);
            }
            catch (Exception e) { File.WriteAllText(args[1], e.ToString()); Environment.ExitCode = 1; }
            return;
        }
        bool created;
        using (Mutex mutex = new Mutex(true, "Local\\DesktopJelly_20261010", out created))
        {
            if (!created) { MessageBox.Show("程序已经在运行，请查看任务栏右下角的托盘图标。", "桌面图标弹弹弹"); return; }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Controller());
        }
    }
}

internal static class Motion
{
    public static void Scale(double phase, double amount, out double sx, out double sy)
    {
        double wave = Math.Cos(phase * Math.PI * 2);
        // Fixed bottom edge; opposite horizontal and vertical deformation.
        sy = 1 + amount * wave;
        sx = 1 - amount * 0.72 * wave;
    }
}

internal sealed class Controller : Form
{
    readonly TrackBar strength = new TrackBar { Minimum = 5, Maximum = 65, Value = 38, TickFrequency = 10, Width = 330 };
    readonly TrackBar speed = new TrackBar { Minimum = 20, Maximum = 1000, Value = 110, TickFrequency = 100, SmallChange = 10, LargeChange = 50, Width = 330 };
    readonly Label speedValue = new Label { AutoSize = true, Text = "速度：1.10 次/秒（上限 10 次/秒）" };
    readonly CheckBox stagger = new CheckBox { Text = "错开节奏（关闭后所有图标同步弹）", AutoSize = true };
    readonly CheckBox autoAudio = new CheckBox { Text = "自动播放参考音频（本地循环）", AutoSize = true };
    readonly LocalAudioPlayer audio = new LocalAudioPlayer();
    readonly Button toggle = new Button { Text = "开始弹弹弹", Width = 150, Height = 38 };
    readonly Label status = new Label { AutoSize = true, Text = "尚未开始；原图标正常显示" };
    readonly NotifyIcon tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "桌面图标弹弹弹", Visible = true };
    readonly ComboBox fps = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    readonly FramePump timer;
    readonly Stopwatch clock = new Stopwatch();
    readonly List<JellyLayer> icons = new List<JellyLayer>();
    readonly HashSet<IntPtr> watched = new HashSet<IntPtr>();
    IntPtr list;
    bool running;
    bool exiting;
    double phase;
    double last;
    int iconCount;
    readonly PerformanceLog diagnostics = new PerformanceLog();
    readonly Label measured = new Label { AutoSize = true, Text = "提交帧率：尚未开始" };
    double nextReport;
    public Controller()
    {
        Text = "桌面图标弹弹弹 1.5"; ClientSize = new Size(450, 680);
        timer = new FramePump(this, delegate { Tick(null, EventArgs.Empty); });
        fps.Items.AddRange(new object[] { "30 FPS", "60 FPS", "90 FPS", "120 FPS" }); fps.SelectedIndex = 1;
        fps.SelectedIndexChanged += delegate { timer.Fps = new int[] { 30, 60, 90, 120 }[fps.SelectedIndex]; };
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen; Font = new Font("Microsoft YaHei UI", 10);
        FlowLayoutPanel panel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        panel.Controls.Add(new Label { Text = "脚底固定 · 压扁变宽 ↔ 拉高变窄", AutoSize = true });
        panel.Controls.Add(new Label { Text = "变形幅度", AutoSize = true }); panel.Controls.Add(strength);
        panel.Controls.Add(speedValue); panel.Controls.Add(speed);
        speed.ValueChanged += delegate { speedValue.Text = "速度：" + (speed.Value / 100.0).ToString("0.00") + " 次/秒（上限 10 次/秒）"; };
        panel.Controls.Add(new Label { Text = "目标帧率", AutoSize = true }); panel.Controls.Add(fps);
        panel.Controls.Add(stagger); panel.Controls.Add(autoAudio); panel.Controls.Add(toggle); panel.Controls.Add(status);
        autoAudio.CheckedChanged += delegate { SyncAudio(); };
        panel.Controls.Add(measured);
        Button export = new Button { Text = "保存性能诊断（最近 60 秒）", AutoSize = true };
        export.Click += delegate
        {
            try
            {
                string path = Path.Combine(Application.StartupPath, "DesktopJelly-diagnostics.csv");
                diagnostics.Save(path); MessageBox.Show("诊断已保存：\n" + path, Text);
            }
            catch (Exception error) { MessageBox.Show("保存失败：" + error.Message, Text); }
        };
        panel.Controls.Add(export);
        Button restore = new Button { Text = "紧急恢复原图标", AutoSize = true };
        restore.Click += delegate { Stop(); IntPtr original = Desktop.FindList(); if (original != IntPtr.Zero) Native.ShowWindow(original, 5); };
        panel.Controls.Add(restore);
        panel.Controls.Add(new Label { Text = "关闭窗口进入托盘；Ctrl + Alt + Q 退出并恢复\n双击动画图标可打开；整理/右键图标前请暂停。", AutoSize = true, Margin = new Padding(3, 12, 3, 3) });
        Controls.Add(panel);
        ContextMenuStrip menu = new ContextMenuStrip();
        menu.Items.Add("开始 / 暂停并恢复", null, delegate { Toggle(); });
        menu.Items.Add("打开设置", null, delegate { Show(); WindowState = FormWindowState.Normal; Activate(); });
        menu.Items.Add("退出并恢复", null, delegate { exiting = true; Close(); });
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += delegate { Show(); WindowState = FormWindowState.Normal; Activate(); };
        toggle.Click += delegate { Toggle(); };
        Resize += delegate { if (WindowState == FormWindowState.Minimized) Hide(); };
    }
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e); Native.RegisterHotKey(Handle, 1, 0x4003, (uint)Keys.Q);
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x312 && m.WParam.ToInt32() == 1) { exiting = true; Close(); return; }
        base.WndProc(ref m);
    }
    void Toggle()
    {
        if (running) { Stop(); return; }
        List<DesktopItem> items = null;
        try
        {
            list = Desktop.FindList();
            if (list == IntPtr.Zero) throw new Exception("未找到 Windows 桌面图标窗口。请使用 Windows 10/11 的标准资源管理器桌面。");
            if (!Native.IsWindowVisible(list)) throw new Exception("桌面图标当前处于隐藏状态。请先在桌面右键 → 查看 → 显示桌面图标。");
            items = Desktop.ReadItems(list);
            if (items.Count == 0) throw new Exception("桌面没有可显示的图标。");
            IntPtr host = Native.GetParent(list);
            iconCount = items.Count;
            for (int i = 0; i < items.Count; i++) items[i].Index = i;
            icons.AddRange(JellyLayer.CreateGroups(host, items));
            foreach (JellyLayer icon in icons) icon.Prepare();
            // A separate process restores visibility even if the animation process crashes.
            if (!watched.Contains(list))
            {
                using (Process watcher = Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--watch " + Process.GetCurrentProcess().Id + " " + list.ToInt64()) { UseShellExecute = false, CreateNoWindow = true })) { }
                watched.Add(list);
            }
            foreach (JellyLayer icon in icons) icon.Show();
            running = true;
            Native.ShowWindow(list, 0);
            SyncAudio();
            diagnostics.Clear(); clock.Restart(); last = 0; phase = 0; nextReport = 1; timer.Start();
            toggle.Text = "暂停并恢复图标"; status.Text = "正在循环：" + iconCount + " 个图标，" + icons.Count + " 个动画层";
        }
        catch (Exception e)
        {
            Stop();
            if (items != null) foreach (DesktopItem item in items) if (item.Image != null) { item.Image.Dispose(); item.Image = null; }
            MessageBox.Show(e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
    void Tick(object sender, EventArgs e)
    {
        try
        {
            if (!Native.IsWindow(list)) { Stop(); return; }
            double now = clock.Elapsed.TotalSeconds;
            long drawStart = Stopwatch.GetTimestamp();
            phase += (now - last) * speed.Value / 100.0; last = now;
            double painting = 0, submitting = 0;
            for (int i = 0; i < icons.Count; i++)
            {
                icons[i].UpdatePhase(phase, strength.Value / 100.0, stagger.Checked);
                painting += icons[i].LastPaintMs; submitting += icons[i].LastSubmitMs;
            }
            diagnostics.Add(now, (Stopwatch.GetTimestamp() - drawStart) * 1000.0 / Stopwatch.Frequency,
                painting, submitting, timer.Fps, iconCount, icons.Count, !Visible, timer.SkippedBusy, timer.MissedDeadline);
            if (now >= nextReport) { measured.Text = diagnostics.Summary(); nextReport = now + 1; }
        }
        catch (Exception error) { Stop(); MessageBox.Show("动画已停止并恢复图标：\n" + error.Message, Text); }
    }
    void Stop()
    {
        audio.Stop();
        timer.Stop();
        if (running && Native.IsWindow(list)) Native.ShowWindow(list, 5);
        running = false;
        foreach (JellyLayer icon in icons) icon.Dispose(); icons.Clear();
        toggle.Text = "开始弹弹弹"; status.Text = "已暂停；原图标正常显示";
    }
    void SyncAudio()
    {
        if (!running || !autoAudio.Checked) { audio.Stop(); return; }
        try { audio.Start(Path.Combine(Application.StartupPath, "reference-audio.wav")); }
        catch (Exception error) { autoAudio.Checked = false; MessageBox.Show("音频未播放，图标动画可继续：\n" + error.Message, Text); }
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && !exiting) { e.Cancel = true; Hide(); return; }
        Stop(); audio.Dispose(); Native.UnregisterHotKey(Handle, 1); tray.Visible = false; tray.Dispose(); timer.Dispose(); base.OnFormClosing(e);
    }
}

// A paced background thread posts at most one frame to the UI queue.
// Rendering stays on the UI thread; slow rendering never builds a frame backlog.
internal sealed class FramePump : IDisposable
{
    readonly Control target;
    readonly Action draw;
    sealed class Run { public volatile bool Active = true; public int Pending; public readonly ManualResetEvent Wake = new ManualResetEvent(false); public Thread Worker; }
    Run current;
    public volatile int Fps = 60;
    public long SkippedBusy, MissedDeadline;
    public bool TimerProtectionActive { get; private set; }
    public FramePump(Control control, Action callback) { target = control; draw = callback; }
    public void Start()
    {
        Stop(); SkippedBusy = 0; MissedDeadline = 0; Run run = new Run(); current = run;
        TimerPolicy policy = new TimerPolicy(); TimerProtectionActive = policy.Active;
        uint resolution = Native.timeBeginPeriod(1);
        run.Worker = new Thread(delegate()
        {
            try
            {
                double next = Stopwatch.GetTimestamp();
                while (run.Active)
                {
                    double now = Stopwatch.GetTimestamp();
                    double wait = (next - now) * 1000 / Stopwatch.Frequency;
                    if (wait >= 1) { run.Wake.WaitOne(Math.Max(1, (int)wait)); continue; }
                    if (now < next) { Thread.Sleep(0); continue; }
                    double interval = (double)Stopwatch.Frequency / Math.Max(1, Fps);
                    next += interval;
                    if (next < now) { Interlocked.Add(ref MissedDeadline, (long)((now - next) / interval) + 1); next = now + interval; }
                    if (Interlocked.CompareExchange(ref run.Pending, 1, 0) == 0)
                    {
                        try
                        {
                            target.BeginInvoke(new Action(delegate
                            {
                                try { if (run.Active && !target.IsDisposed) draw(); }
                                finally { Interlocked.Exchange(ref run.Pending, 0); }
                            }));
                        }
                        catch (InvalidOperationException) { break; }
                    }
                    else Interlocked.Increment(ref SkippedBusy);
                }
            }
            finally { if (resolution == 0) Native.timeEndPeriod(1); policy.Dispose(); }
        });
        run.Worker.IsBackground = true; run.Worker.Name = "DesktopJelly frame pacing"; run.Worker.Start();
    }
    public void Stop()
    {
        Run run = current; current = null;
        if (run == null) return;
        run.Active = false; run.Wake.Set(); run.Worker.Join(); run.Wake.Dispose();
    }
    public void Dispose() { Stop(); }
}

// Opt out of Windows 11's automatic timer-resolution suppression only while
// animation runs. Preserve unrelated process policy and restore the old state.
internal sealed class TimerPolicy : IDisposable
{
    Native.POWER_STATE original;
    public bool Active { get; private set; }
    public TimerPolicy()
    {
        try
        {
            original = new Native.POWER_STATE { Version = 1 };
            if (!Native.GetProcessInformation(new IntPtr(-1), 4, ref original, 12)) return;
            Native.POWER_STATE requested = original;
            requested.ControlMask |= 4; requested.StateMask &= ~4u;
            Active = Native.SetProcessInformation(new IntPtr(-1), 4, ref requested, 12);
        }
        catch (EntryPointNotFoundException) { }
    }
    public void Dispose()
    {
        if (Active) { Native.SetProcessInformation(new IntPtr(-1), 4, ref original, 12); Active = false; }
    }
}

internal sealed class DesktopItem
{
    public int Index;
    public string Name, Path;
    public Rectangle Rect;
    public Bitmap Image;
}

internal sealed class JellyIcon : NativeWindow, IDisposable
{
    readonly IntPtr host;
    readonly DesktopItem item;
    readonly int iconSize;
    readonly int FrameWidth;
    readonly int frameHeight;
    IntPtr memory, dib, previous;
    Bitmap frame, labels;
    Graphics painter;
    public double LastPaintMs, LastSubmitMs;
    public JellyIcon(IntPtr parent, DesktopItem data)
    {
        host = parent; item = data; iconSize = Math.Max(16, Math.Min(256, data.Rect.Height));
        frameHeight = iconSize * 2 + 64; FrameWidth = Math.Max(210, iconSize * 2 + 32);
    }
    public void Prepare()
    {
        int center = item.Rect.Left + item.Rect.Width / 2;
        CreateHandle(new CreateParams {
            Caption = "DesktopJellyIcon", Parent = host,
            Style = 0x40000000, ExStyle = 0x08080080,
            ClassStyle = 8, X = center - FrameWidth / 2,
            Y = item.Rect.Bottom - iconSize * 2, Width = FrameWidth, Height = frameHeight
        });
        Native.BITMAPINFO info = new Native.BITMAPINFO();
        info.Header.Size = (uint)Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER));
        info.Header.Width = FrameWidth; info.Header.Height = -frameHeight;
        info.Header.Planes = 1; info.Header.BitCount = 32;
        IntPtr pixels;
        dib = Native.CreateDIBSection(IntPtr.Zero, ref info, 0, out pixels, IntPtr.Zero, 0);
        if (dib == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
        memory = Native.CreateCompatibleDC(IntPtr.Zero);
        if (memory == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
        previous = Native.SelectObject(memory, dib);
        frame = new Bitmap(FrameWidth, frameHeight, FrameWidth * 4, PixelFormat.Format32bppPArgb, pixels);
        painter = Graphics.FromImage(frame);
        painter.InterpolationMode = InterpolationMode.HighQualityBilinear;
        painter.PixelOffsetMode = PixelOffsetMode.HighQuality;
        using (Bitmap blank = new Bitmap(1, 1)) labels = Render(blank, item.Name, iconSize, FrameWidth, frameHeight, 1, 1);
        UpdateFrame(1, 1);
    }
    public void Show()
    {
        Native.ShowWindow(Handle, 4);
        Native.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x53);
        UpdateFrame(1, 1);
        if (!Native.IsWindowVisible(Handle) || Native.GetParent(Handle) != host)
            throw new Exception("动画窗口没有正确显示，已保留原桌面图标。");
        Native.RECT bounds;
        if (!Native.GetWindowRect(Handle, out bounds) || bounds.Right - bounds.Left != FrameWidth || bounds.Bottom - bounds.Top != frameHeight)
            throw new Exception("动画窗口尺寸异常，已保留原桌面图标。");
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x203) { OpenItem(); return; }
        if (m.Msg == 0x21) { m.Result = new IntPtr(3); return; }
        base.WndProc(ref m);
    }
    public void UpdateFrame(double sx, double sy)
    {
        long started = Stopwatch.GetTimestamp();
        painter.Clear(Color.Transparent);
        painter.DrawImageUnscaled(labels, 0, 0);
        float w = (float)(iconSize * sx), h = (float)(iconSize * sy);
        painter.DrawImage(item.Image, new RectangleF((FrameWidth - w) / 2, iconSize * 2 - h, w, h));
        painter.Flush(FlushIntention.Sync);
        long submitted = Stopwatch.GetTimestamp();
        Native.SIZE size = new Native.SIZE(FrameWidth, frameHeight);
        Native.POINT origin = new Native.POINT(0, 0);
        Native.BLEND blend = new Native.BLEND { SourceConstantAlpha = 255, AlphaFormat = 1 };
        if (!Native.UpdateLayeredWindow(Handle, IntPtr.Zero, IntPtr.Zero, ref size, memory, ref origin, 0, ref blend, 2))
            throw new System.ComponentModel.Win32Exception();
        LastPaintMs = (submitted - started) * 1000.0 / Stopwatch.Frequency;
        LastSubmitMs = (Stopwatch.GetTimestamp() - submitted) * 1000.0 / Stopwatch.Frequency;
    }
    public static Bitmap Render(Bitmap image, string text, int size, int width, int height, double sx, double sy)
    {
        Bitmap b = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(b))
        {
            g.Clear(Color.Transparent); g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float w = (float)(size * sx), h = (float)(size * sy), bottom = size * 2;
            g.DrawImage(image, new RectangleF((width - w) / 2, bottom - h, w, h));
            using (Font font = new Font("Microsoft YaHei UI", 9, FontStyle.Regular))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near, Trimming = StringTrimming.EllipsisCharacter })
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                RectangleF label = new RectangleF(12, bottom + 5, width - 24, 48);
                RectangleF shadow = label; shadow.Offset(1, 1);
                g.DrawString(text, font, Brushes.Black, shadow, format); g.DrawString(text, font, Brushes.White, label, format);
            }
        }
        return b;
    }
    void OpenItem()
    {
        if (String.IsNullOrEmpty(item.Path)) return;
        try
        {
            if (item.Path.StartsWith("::")) Process.Start("explorer.exe", "shell:" + item.Path);
            else Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });
        }
        catch (Exception e) { MessageBox.Show(e.Message, "无法打开图标"); }
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
        if (item.Image != null) { item.Image.Dispose(); item.Image = null; }
    }
}

internal static class Desktop
{
    public static IntPtr FindList()
    {
        IntPtr result = IntPtr.Zero;
        Native.EnumWindows(delegate(IntPtr w, IntPtr p)
        {
            IntPtr def = Native.FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (def != IntPtr.Zero) result = Native.FindWindowEx(def, IntPtr.Zero, "SysListView32", null);
            return result == IntPtr.Zero;
        }, IntPtr.Zero);
        return result;
    }
    static IntPtr Send(IntPtr window, uint message, IntPtr wp, IntPtr lp)
    {
        IntPtr result;
        if (Native.SendMessageTimeout(window, message, wp, lp, 2, 1500, out result) == IntPtr.Zero) throw new Exception("桌面图标窗口没有响应，请稍后重试。");
        return result;
    }
    public static List<DesktopItem> ReadItems(IntPtr list)
    {
        if (IntPtr.Size != 8) throw new Exception("请运行 x64 版本。");
        Dictionary<string, Queue<string>> paths = new Dictionary<string, Queue<string>>(StringComparer.CurrentCultureIgnoreCase);
        object shellObject = null, folderObject = null, itemsObject = null;
        try
        {
            shellObject = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
            dynamic shell = shellObject; folderObject = shell.NameSpace(0); dynamic folder = folderObject;
            itemsObject = folder.Items(); dynamic shellItems = itemsObject;
            for (int i = 0; i < shellItems.Count; i++)
            {
                object entryObject = shellItems.Item(i);
                try
                {
                    dynamic entry = entryObject; string name = entry.Name, path = entry.Path;
                    Queue<string> queue; if (!paths.TryGetValue(name, out queue)) paths[name] = queue = new Queue<string>();
                    queue.Enqueue(path);
                }
                finally { if (entryObject != null) Marshal.FinalReleaseComObject(entryObject); }
            }
        }
        finally
        {
            if (itemsObject != null) Marshal.FinalReleaseComObject(itemsObject);
            if (folderObject != null) Marshal.FinalReleaseComObject(folderObject);
            if (shellObject != null) Marshal.FinalReleaseComObject(shellObject);
        }
        uint pid; Native.GetWindowThreadProcessId(list, out pid);
        IntPtr process = Native.OpenProcess(0x38, false, pid);
        if (process == IntPtr.Zero) throw new Exception("无法读取桌面图标位置。请让程序与资源管理器使用相同权限运行。");
        IntPtr remote = IntPtr.Zero;
        List<DesktopItem> result = new List<DesktopItem>();
        try
        {
            remote = Native.VirtualAllocEx(process, IntPtr.Zero, new UIntPtr(4096), 0x3000, 4);
            if (remote == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
            int count = Send(list, 0x1004, IntPtr.Zero, IntPtr.Zero).ToInt32();
            if (count > 2000) throw new Exception("图标数量过多（超过 2000），请减少桌面图标后重试。");
            for (int i = 0; i < count; i++)
            {
                Native.LVITEM item = new Native.LVITEM { iItem = i, pszText = IntPtr.Add(remote, 256), cchTextMax = 1024 };
                byte[] buffer = Bytes(item); Write(process, remote, buffer);
                Send(list, 0x1073, new IntPtr(i), remote);
                byte[] text = Read(process, IntPtr.Add(remote, 256), 2048);
                string name = Encoding.Unicode.GetString(text).Split('\0')[0];
                buffer = new byte[16]; buffer[0] = 1; Write(process, remote, buffer);
                if (Send(list, 0x100E, new IntPtr(i), remote) == IntPtr.Zero) throw new Exception("读取图标范围失败。");
                buffer = Read(process, remote, 16);
                int x = BitConverter.ToInt32(buffer, 0), y = BitConverter.ToInt32(buffer, 4), right = BitConverter.ToInt32(buffer, 8), bottom = BitConverter.ToInt32(buffer, 12);
                Native.POINT origin = new Native.POINT(x, y); Native.MapWindowPoints(list, Native.GetParent(list), ref origin, 1);
                Queue<string> queue; string path = paths.TryGetValue(name, out queue) && queue.Count > 0 ? queue.Dequeue() : null;
                result.Add(new DesktopItem { Name = name, Path = path, Rect = new Rectangle(origin.X, origin.Y, right - x, bottom - y), Image = GetIcon(path) });
            }
            return result;
        }
        catch { foreach (DesktopItem item in result) item.Image.Dispose(); throw; }
        finally { if (remote != IntPtr.Zero) Native.VirtualFreeEx(process, remote, UIntPtr.Zero, 0x8000); Native.CloseHandle(process); }
    }
    static Bitmap GetIcon(string path)
    {
        if (!String.IsNullOrEmpty(path))
        {
            IntPtr pidl;
            if (Native.SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, IntPtr.Zero) == 0)
            {
                try
                {
                    Native.SHFILEINFO info = new Native.SHFILEINFO();
                    Native.SHGetFileInfo(pidl, 0, ref info, (uint)Marshal.SizeOf(info), 0x108);
                    if (info.hIcon != IntPtr.Zero)
                    {
                        try { using (Icon icon = Icon.FromHandle(info.hIcon)) return icon.ToBitmap(); }
                        finally { Native.DestroyIcon(info.hIcon); }
                    }
                }
                finally { Marshal.FreeCoTaskMem(pidl); }
            }
        }
        return SystemIcons.Application.ToBitmap();
    }
    static byte[] Bytes(object value)
    {
        int size = Marshal.SizeOf(value); IntPtr pointer = Marshal.AllocHGlobal(size);
        try { Marshal.StructureToPtr(value, pointer, false); byte[] result = new byte[size]; Marshal.Copy(pointer, result, 0, size); return result; }
        finally { Marshal.FreeHGlobal(pointer); }
    }
    static void Write(IntPtr process, IntPtr target, byte[] bytes)
    {
        UIntPtr count; if (!Native.WriteProcessMemory(process, target, bytes, new UIntPtr((uint)bytes.Length), out count) || count.ToUInt64() != (ulong)bytes.Length) throw new System.ComponentModel.Win32Exception();
    }
    static byte[] Read(IntPtr process, IntPtr target, int size)
    {
        byte[] result = new byte[size]; UIntPtr count;
        if (!Native.ReadProcessMemory(process, target, result, new UIntPtr((uint)size), out count) || count.ToUInt64() != (ulong)size) throw new System.ComponentModel.Win32Exception();
        return result;
    }
}

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct POWER_STATE { public uint Version, ControlMask, StateMask; }
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool GetProcessInformation(IntPtr process, int informationClass, ref POWER_STATE state, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool SetProcessInformation(IntPtr process, int informationClass, ref POWER_STATE state, uint size);
    [DllImport("user32.dll")] internal static extern uint GetGuiResources(IntPtr process, uint flags);
    [StructLayout(LayoutKind.Sequential)] internal struct BITMAPINFOHEADER { public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, SizeImage; public int XPelsPerMeter, YPelsPerMeter; public uint ColorsUsed, ColorsImportant; }
    [StructLayout(LayoutKind.Sequential)] internal struct BITMAPINFO { public BITMAPINFOHEADER Header; public uint Color; }
    [DllImport("gdi32.dll", SetLastError = true)] internal static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFO info, uint usage, out IntPtr pixels, IntPtr section, uint offset);
    [DllImport("winmm.dll")] internal static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")] internal static extern uint timeEndPeriod(uint period);
    internal delegate bool EnumProc(IntPtr h, IntPtr p);
    [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)] internal struct SIZE { public int X, Y; public SIZE(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] internal struct BLEND { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
    [StructLayout(LayoutKind.Sequential)] internal struct LVITEM
    {
        public uint mask; public int iItem, iSubItem; public uint state, stateMask; public IntPtr pszText;
        public int cchTextMax, iImage; public IntPtr lParam; public int iIndent, iGroupId; public uint cColumns; public IntPtr puColumns, piColFmt; public int iGroup;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct SHFILEINFO
    {
        public IntPtr hIcon; public int iIcon; public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }
    [DllImport("user32.dll")] internal static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc proc, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string name);
    [DllImport("user32.dll")] internal static extern IntPtr GetParent(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window, out RECT rect);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr window, int mode);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wp, IntPtr lp, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] internal static extern int MapWindowPoints(IntPtr from, IntPtr to, ref POINT p, uint count);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int height, uint flags);
    [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr h, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr h, int id);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, UIntPtr size, uint allocation, uint protect);
    [DllImport("kernel32.dll")] internal static extern bool VirtualFreeEx(IntPtr process, IntPtr address, UIntPtr size, uint type);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] bytes, UIntPtr size, out UIntPtr count);
    [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] bytes, UIntPtr size, out UIntPtr count);
    [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr handle);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] internal static extern int SHParseDisplayName(string name, IntPtr binding, out IntPtr pidl, uint attrs, IntPtr result);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr SHGetFileInfo(IntPtr pidl, uint attributes, ref SHFILEINFO info, uint size, uint flags);
    [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] internal static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] internal static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] internal static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] internal static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] internal static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] internal static extern bool DeleteDC(IntPtr dc);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destDC, IntPtr destination, ref SIZE size, IntPtr sourceDC, ref POINT origin, uint color, ref BLEND blend, uint flags);
}
