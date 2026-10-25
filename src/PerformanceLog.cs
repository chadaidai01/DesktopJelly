using System;
using System.Globalization;
using System.IO;
using System.Text;

internal sealed class PerformanceLog
{
    struct Sample
    {
        public double Time, Gap, Draw, Paint, Submit;
        public int Target, Icons, Layers;
        public bool Hidden;
        public long Busy, Deadline;
    }
    readonly Sample[] ring = new Sample[7200];
    int count, head;
    double previous;
    public void Clear() { count = head = 0; previous = 0; }
    public void Add(double time, double draw, double paint, double submit, int target, int icons, int layers, bool hidden, long busy, long deadline)
    {
        ring[head] = new Sample { Time = time, Gap = previous > 0 ? (time - previous) * 1000 : 0, Draw = draw, Paint = paint,
            Submit = submit, Target = target, Icons = icons, Layers = layers, Hidden = hidden, Busy = busy, Deadline = deadline };
        previous = time; head = (head + 1) % ring.Length; count = Math.Min(count + 1, ring.Length);
    }
    public string Summary()
    {
        int n = Math.Min(count, 120); if (n < 2) return "提交帧率：采样中";
        double[] gaps = new double[n]; double sum = 0, draw = 0; int valid = 0;
        for (int i = 0; i < n; i++) { Sample s = ring[(head - n + i + ring.Length) % ring.Length]; gaps[i] = s.Gap; sum += s.Gap; draw += s.Draw; if (s.Gap > 0) valid++; }
        Array.Sort(gaps);
        return "提交 " + (valid * 1000 / sum).ToString("0.0") + " FPS · 间隔P95 " + gaps[(int)((n - 1) * .95)].ToString("0.0") + " ms\n绘制平均 " + (draw / n).ToString("0.0") + " ms（非屏幕呈现帧率）";
    }
    public void Save(string path)
    {
        StringBuilder text = new StringBuilder("seconds,gap_ms,draw_ms,paint_ms,submit_ms,target_fps,icons,layers,settings_hidden,skipped_busy_total,missed_deadline_total\r\n");
        double cutoff = previous - 60;
        for (int i = 0; i < count; i++)
        {
            Sample s = ring[(head - count + i + ring.Length) % ring.Length]; if (s.Time < cutoff) continue;
            double[] values = { s.Time, s.Gap, s.Draw, s.Paint, s.Submit };
            foreach (double value in values) { text.Append(value.ToString("0.000", CultureInfo.InvariantCulture)); text.Append(','); }
            text.Append(s.Target).Append(',').Append(s.Icons).Append(',').Append(s.Layers).Append(',').Append(s.Hidden).Append(',').Append(s.Busy).Append(',').Append(s.Deadline).Append("\r\n");
        }
        File.WriteAllText(path, text.ToString(), Encoding.UTF8);
    }
}
