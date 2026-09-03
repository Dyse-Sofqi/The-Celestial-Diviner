using System.IO;
using System.Windows;
using System.Windows.Media;
using TheCelestialDiviner.Helpers;

namespace TheCelestialDiviner.Services;

/// <summary>
/// 全局开关提示语音服务：播放内嵌 WAV（开启 = “启动”，关闭 = “关闭”）。
/// WPF MediaPlayer 的媒体管线不解析 pack://application 资源 URI——Open 会静默失败
/// （HasAudio=false、不触发异常、Play 无声），故启动时把 WAV 解包到临时目录，
/// 以文件 URI 播放；音量（0.0~1.0）在每次播放前应用，Play 异步非阻塞。
/// </summary>
public sealed class SoundCueService
{
    private static readonly Uri StartCueUri =
        new("pack://application:,,,/Resources/Sounds/StartVoice.wav");

    private static readonly Uri StopCueUri =
        new("pack://application:,,,/Resources/Sounds/StopVoice.wav");

    private readonly MediaPlayer _startPlayer = new();
    private readonly MediaPlayer _stopPlayer = new();
    private double _volume = Constants.DefaultSoundVolume / 100.0;

    public SoundCueService()
    {
        _startPlayer.MediaFailed += (_, e) =>
            Logger.Error("启动提示音加载失败。", e.ErrorException);
        _stopPlayer.MediaFailed += (_, e) =>
            Logger.Error("关闭提示音加载失败。", e.ErrorException);

        try
        {
            var dir = Path.Combine(Path.GetTempPath(), Constants.AppFolderName);
            Directory.CreateDirectory(dir);
            _startPlayer.Open(ExtractToTemp(StartCueUri, dir, "StartVoice.wav"));
            _stopPlayer.Open(ExtractToTemp(StopCueUri, dir, "StopVoice.wav"));
        }
        catch (Exception ex)
        {
            Logger.Error("提示语音资源加载失败（开关切换将静音）。", ex);
        }
    }

    /// <summary>把内嵌 WAV 解包到临时目录并返回文件 URI（已存在时直接复用）。</summary>
    private static Uri ExtractToTemp(Uri packUri, string dir, string fileName)
    {
        var path = Path.Combine(dir, fileName);
        if (!File.Exists(path))
        {
            var sri = Application.GetResourceStream(packUri)
                ?? throw new InvalidOperationException($"内嵌资源缺失：{packUri}");
            using var src = sri.Stream;
            using var dst = File.Create(path); // 内容固定，无需每次重写
            src.CopyTo(dst);
        }
        return new Uri(path);
    }

    /// <summary>提示语音音量（0.0 ~ 1.0）。</summary>
    public double Volume
    {
        get => _volume;
        set => _volume = Compat.Clamp(value, 0.0, 1.0);
    }

    /// <summary>播放“启动”提示音（总开关开启时）。</summary>
    public void PlayStart() => Play(_startPlayer);

    /// <summary>播放“关闭”提示音（总开关停用时）。</summary>
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
