using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

// Native asynchronous WAV playback; no browser, decoder process or frame-thread polling.
internal sealed class LocalAudioPlayer : IDisposable
{
    bool playing;
    public static void Validate(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("缺少本地音频。请把 reference-audio.wav 和程序放在同一文件夹。", path);
        using (FileStream file = File.OpenRead(path))
        using (BinaryReader reader = new BinaryReader(file))
        {
            if (file.Length < 44 || Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF") throw new InvalidDataException("音频文件不是有效 WAV。");
            reader.ReadUInt32();
            if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE") throw new InvalidDataException("音频文件不是有效 WAV。");
        }
    }
    public void Start(string path)
    {
        Validate(path); Stop();
        if (!PlaySound(path, IntPtr.Zero, 0x2000B)) throw new InvalidOperationException("Windows 无法播放本地音频，请检查音频输出设备。");
        playing = true;
    }
    public void Stop() { if (playing) { PlaySound(null, IntPtr.Zero, 0); playing = false; } }
    public void Dispose() { Stop(); }
    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", CharSet = CharSet.Unicode)]
    static extern bool PlaySound(string sound, IntPtr module, uint flags);
}
