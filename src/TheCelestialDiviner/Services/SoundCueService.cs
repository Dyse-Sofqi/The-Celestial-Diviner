using System.IO;
using System.Windows;
using System.Windows.Media;
using TheCelestialDiviner.Helpers;

namespace TheCelestialDiviner.Services;

/// <summary>
/// 全局开关提示语音服务：播放内嵌 MP3（开启 = 启动 / 衍天高手启动，关闭 = 停止）。
/// WPF MediaPlayer 的媒体管线不解析 pack://application 资源 URI——Open 会静默失败
/// （HasAudio=false、不触发异常、Play 无声），故启动时把 MP3 解包到临时目录，
/// 以文件 URI 播放；音量（0.0~1.0）在每次播放前应用，Play 异步非阻塞。
/// </summary>
public sealed class SoundCueService
{
    private static readonly Uri StartCueUri =
        new("pack://application:,,,/Resources/Sounds/StartVoice.mp3");

    private static readonly Uri StopCueUri =
        new("pack://application:,,,/Resources/Sounds/StopVoice.mp3");

    private static readonly Uri DivinerStartCueUri =
        new("pack://application:,,,/Resources/Sounds/DivinerStartVoice.mp3");

    private readonly MediaPlayer _startPlayer = new();
    private readonly MediaPlayer _stopPlayer = new();
    private readonly MediaPlayer _divinerStartPlayer = new();
    private double _volume = Constants.DefaultSoundVolume / 100.0;

    public SoundCueService()
    {
        _startPlayer.MediaFailed += (_, e) =>
            Logger.Error("启动提示音加载失败。", e.ErrorException);
        _stopPlayer.MediaFailed += (_, e) =>
            Logger.Error("停止提示音加载失败。", e.ErrorException);
        _divinerStartPlayer.MediaFailed += (_, e) =>
            Logger.Error("衍天高手启动提示音加载失败。", e.ErrorException);

        try
        {
            var dir = Path.Combine(Path.GetTempPath(), Constants.AppFolderName);
            Directory.CreateDirectory(dir);
            _startPlayer.Open(ExtractToTemp(StartCueUri, dir, "StartVoice.mp3"));
            _stopPlayer.Open(ExtractToTemp(StopCueUri, dir, "StopVoice.mp3"));
            _divinerStartPlayer.Open(ExtractToTemp(DivinerStartCueUri, dir, "DivinerStartVoice.mp3"));
        }
        catch (Exception ex)
        {
            Logger.Error("提示语音资源加载失败（开关切换将静音）。", ex);
        }
    }

    /// <summary>
    /// 把内嵌 MP3 解包到临时目录并返回文件 URI。临时文件按内容与内嵌资源比对，
    /// 一致才复用；音频更换后（同名不同内容）自动重新解包，避免继续播放旧缓存。
    /// </summary>
    private static Uri ExtractToTemp(Uri packUri, string dir, string fileName)
    {
        var path = Path.Combine(dir, fileName);
        var sri = Application.GetResourceStream(packUri)
            ?? throw new InvalidOperationException($"内嵌资源缺失：{packUri}");
        using var src = sri.Stream;
        using var buffer = new MemoryStream();
        src.CopyTo(buffer);
        var data = buffer.ToArray();

        if (!File.Exists(path) || !ContentMatches(path, data))
        {
            using var dst = File.Create(path);   // 缓存缺失 / 内容过旧：重写
            dst.Write(data, 0, data.Length);
        }
        return new Uri(path);
    }

    /// <summary>临时文件与内嵌资源内容是否一致（长度 + 逐字节比对；文件被占用等异常按不一致处理）。</summary>
    private static bool ContentMatches(string path, byte[] data)
    {
        try
        {
            using var fs = File.OpenRead(path);
            if (fs.Length != data.Length) return false;
            var buf = new byte[data.Length];
            var read = 0;
            while (read < buf.Length)
            {
                var n = fs.Read(buf, read, buf.Length - read);
                if (n <= 0) return false;
                read += n;
            }
            for (var i = 0; i < data.Length; i++)
                if (buf[i] != data[i]) return false;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>提示语音音量（0.0 ~ 1.0）。</summary>
    public double Volume
    {
        get => _volume;
        set => _volume = Compat.Clamp(value, 0.0, 1.0);
    }

    /// <summary>播放“启动”提示音（总开关开启时；diviner = “成为衍天高手”激活，改播衍天高手启动音）。</summary>
    public void PlayStart(bool diviner = false) => Play(diviner ? _divinerStartPlayer : _startPlayer);

    /// <summary>播放“停止”提示音（总开关停用时）。</summary>
    public void PlayStop() => Play(_stopPlayer);

    private void Play(MediaPlayer player)
    {
        try
        {
            player.Position = TimeSpan.Zero; // 连续触发时从头重播
            player.Volume = _volume;
            player.Play();
        }
        catch (Exception ex)
        {
            Logger.Error("提示语音播放失败。", ex);
        }
    }
}
