using System.Windows.Media;
using TheCelestialDiviner.Helpers;

namespace TheCelestialDiviner.Services;

/// <summary>
/// 全局开关提示语音服务：播放内嵌 WAV（开启 = “启动”，关闭 = “关闭”）。
/// MediaPlayer 走 WPF 媒体管线，Play 异步非阻塞；音量（0.0~1.0）在每次播放前应用。
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
        try
        {
            // 预打开媒体资源：首次切换提示音零解码延迟。
            _startPlayer.Open(StartCueUri);
            _stopPlayer.Open(StopCueUri);
        }
        catch (Exception ex)
        {
            Logger.Error("提示语音资源加载失败（开关切换将静音）。", ex);
        }
    }

    /// <summary>提示语音音量（0.0 ~ 1.0）。</summary>
    public double Volume
    {
        get => _volume;
        set => _volume = Math.Clamp(value, 0.0, 1.0);
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
