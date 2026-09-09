using System.IO;
using System.Windows;
using System.Windows.Media;
using TheCelestialDiviner.Helpers;
using TheCelestialDiviner.Models;

namespace TheCelestialDiviner.Services;

/// <summary>可自定义的提示音用途（语音设置对话框的三行）。</summary>
public enum SoundCue
{
    /// <summary>总开关开启（“启动”音；衍天高手变体不参与自定义）。</summary>
    Start,

    /// <summary>总开关关闭（“停止”音）。</summary>
    Stop,

    /// <summary>切换方案热键触发（“切换”音）。</summary>
    Cycle,
}

/// <summary>
/// 提示语音服务：播放内嵌 MP3（总开关开启 = 启动 / 衍天高手启动，关闭 = 停止，
/// 切换方案热键触发 = 切换），并支持把上述三种语音替换为用户导入的自定义音频。
/// WPF MediaPlayer 的媒体管线不解析 pack://application 资源 URI——Open 会静默失败
/// （HasAudio=false、不触发异常、Play 无声），故启动时把内嵌 MP3 解包到临时目录，
/// 以文件 URI 播放；自定义音频在导入时已复制到语音目录，直接从该目录播放。
/// 音量（0.0~1.0）在每次播放前应用，Play 异步非阻塞。
/// </summary>
public sealed class SoundCueService
{
    private static readonly Uri StartCueUri =
        new("pack://application:,,,/Resources/Sounds/StartVoice.mp3");

    private static readonly Uri StopCueUri =
        new("pack://application:,,,/Resources/Sounds/StopVoice.mp3");

    private static readonly Uri DivinerStartCueUri =
        new("pack://application:,,,/Resources/Sounds/DivinerStartVoice.mp3");

    private static readonly Uri CycleCueUri =
        new("pack://application:,,,/Resources/Sounds/CycleVoice.mp3");

    private readonly MediaPlayer _startPlayer = new();
    private readonly MediaPlayer _stopPlayer = new();
    private readonly MediaPlayer _divinerStartPlayer = new();
    private readonly MediaPlayer _cyclePlayer = new();

    // 内嵌默认音频（解包到临时目录后的文件 URI）。
    private Uri? _defaultStart;
    private Uri? _defaultStop;
    private Uri? _defaultDivinerStart;
    private Uri? _defaultCycle;

    // 自定义音频（语音目录下的文件 URI；null = 未设置 / 文件缺失，回退默认）。
    private Uri? _customStart;
    private Uri? _customStop;
    private Uri? _customCycle;

    private double _volume = Constants.DefaultSoundVolume / 100.0;

    /// <summary>自定义语音目录：导入的音频复制到此处，配置只记录文件名。</summary>
    public static string SoundsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Constants.AppFolderName, "sounds");

    public SoundCueService()
    {
        _startPlayer.MediaFailed += (_, e) =>
            Logger.Error("启动提示音加载失败。", e.ErrorException);
        _stopPlayer.MediaFailed += (_, e) =>
            Logger.Error("停止提示音加载失败。", e.ErrorException);
        _divinerStartPlayer.MediaFailed += (_, e) =>
            Logger.Error("衍天高手启动提示音加载失败。", e.ErrorException);
        _cyclePlayer.MediaFailed += (_, e) =>
            Logger.Error("切换提示音加载失败。", e.ErrorException);

        try
        {
            var dir = Path.Combine(Path.GetTempPath(), Constants.AppFolderName);
            Directory.CreateDirectory(dir);
            _defaultStart = ExtractToTemp(StartCueUri, dir, "StartVoice.mp3");
            _defaultStop = ExtractToTemp(StopCueUri, dir, "StopVoice.mp3");
            _defaultDivinerStart = ExtractToTemp(DivinerStartCueUri, dir, "DivinerStartVoice.mp3");
            _defaultCycle = ExtractToTemp(CycleCueUri, dir, "CycleVoice.mp3");
        }
        catch (Exception ex)
        {
            Logger.Error("提示语音资源加载失败（开关切换将静音）。", ex);
        }

        Reload(_startPlayer, _defaultStart);
        Reload(_stopPlayer, _defaultStop);
        Reload(_divinerStartPlayer, _defaultDivinerStart);
        Reload(_cyclePlayer, _defaultCycle);
    }

    /// <summary>
    /// 应用自定义语音配置（null / 空项 = 内嵌默认音频；文件缺失时静默回退默认并留痕）。
    /// 启动加载、导入配置、导入 / 重置单个语音后调用；重复调用安全。
    /// </summary>
    public void ApplyCustom(CustomSoundConfig? config)
    {
        _customStart = ResolveCustom(config?.Start);
        _customStop = ResolveCustom(config?.Stop);
        _customCycle = ResolveCustom(config?.Cycle);

        Reload(_startPlayer, _customStart ?? _defaultStart);
        Reload(_stopPlayer, _customStop ?? _defaultStop);
        Reload(_cyclePlayer, _customCycle ?? _defaultCycle);
        Reload(_divinerStartPlayer, _defaultDivinerStart);   // 衍天高手启动音不可自定义，恒为默认
    }

    /// <summary>自定义文件名 → 文件 URI（空 / 文件缺失返回 null 并留痕）。</summary>
    private static Uri? ResolveCustom(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        var path = Path.Combine(SoundsDir, fileName);
        if (File.Exists(path)) return new Uri(path);
        Logger.Warn($"自定义提示音文件缺失，已回退默认音频：{path}");
        return null;
    }

    /// <summary>（重新）装载媒体源：先停播并关闭旧源再打开新源（null = 不打开，保持静音）。</summary>
    private static void Reload(MediaPlayer player, Uri? uri)
    {
        try
        {
            player.Stop();
            player.Close();
            if (uri is not null) player.Open(uri);
        }
        catch (Exception ex)
        {
            Logger.Error("提示语音装载失败。", ex);
        }
    }

    /// <summary>
    /// 释放某个语音的媒体源占用（导入覆盖 / 删除文件前调用；随后 ApplyCustom 会重新装载）。
    /// 不释放时 MediaPlayer 持有文件句柄，覆盖 / 删除自定义音频会失败。
    /// </summary>
    public void UnloadCue(SoundCue cue)
    {
        try
        {
            var player = cue switch
            {
                SoundCue.Start => _startPlayer,
                SoundCue.Stop => _stopPlayer,
                _ => _cyclePlayer,
            };
            player.Stop();
            player.Close();
        }
        catch (Exception ex)
        {
            Logger.Error("提示语音释放失败。", ex);
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

    /// <summary>播放“启动”提示音（总开关开启时）。自定义开启音优先；否则 diviner = “成为衍天高手”启动音。</summary>
    public void PlayStart(bool diviner = false) =>
        Play(diviner && _customStart is null ? _divinerStartPlayer : _startPlayer);

    /// <summary>播放“停止”提示音（总开关停用时）。</summary>
    public void PlayStop() => Play(_stopPlayer);

    /// <summary>播放“切换”提示音（切换方案热键触发并实际切换档位时；独立播放器，可与开关语音叠加）。</summary>
    public void PlayCycle() => Play(_cyclePlayer);

    /// <summary>试听指定语音（语音设置对话框用；开启语音试听“启动”音，非衍天高手变体）。</summary>
    public void PreviewCue(SoundCue cue) => Play(cue switch
    {
        SoundCue.Start => _startPlayer,
        SoundCue.Stop => _stopPlayer,
        _ => _cyclePlayer,
    });

    /// <summary>
    /// 导入自定义语音：复制到语音目录并清理该用途的其他旧文件，返回存储文件名。
    /// 先复制后清理——复制失败时旧自定义音频原样保留。调用前须先 <see cref="UnloadCue"/>
    /// 释放旧文件占用（同名单文件覆盖需要）；失败抛异常，由调用方提示。
    /// </summary>
    public static string ImportCustom(SoundCue cue, string sourcePath)
    {
        Directory.CreateDirectory(SoundsDir);
        var prefix = CuePrefix(cue);
        var name = prefix + "_" + Path.GetFileName(sourcePath);
        var dest = Path.Combine(SoundsDir, name);

        File.Copy(sourcePath, dest, overwrite: true);   // 失败则旧文件原样保留
        foreach (var old in Directory.EnumerateFiles(SoundsDir, prefix + "_*"))
        {
            if (string.Equals(old, dest, StringComparison.OrdinalIgnoreCase)) continue;
            try { File.Delete(old); }
            catch (Exception ex) { Logger.Warn($"清理旧自定义语音失败（{Path.GetFileName(old)}）：{ex.Message}"); }
        }
        return name;
    }

    /// <summary>删除自定义语音文件（重置回默认时调用；缺失 / 占用静默忽略）。</summary>
    public static void DeleteCustom(string? storedName)
    {
        if (string.IsNullOrWhiteSpace(storedName)) return;
        try
        {
            var path = Path.Combine(SoundsDir, storedName);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            Logger.Warn($"删除自定义语音失败（{storedName}）：{ex.Message}");
        }
    }

    /// <summary>用途 → 语音目录文件名前缀（start / stop / cycle）。</summary>
    private static string CuePrefix(SoundCue cue) => cue switch
    {
        SoundCue.Start => "start",
        SoundCue.Stop => "stop",
        _ => "cycle",
    };

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
