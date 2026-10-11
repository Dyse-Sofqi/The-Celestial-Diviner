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
/// 提示语音服务：播放各主题配套的基础提示音（开启 / 关闭 / 切换，散装音频随发布包分发，
/// 见 SectThemes.CueFiles：默认主题与衍天高手 = 高松灯、莫问高手 = 墨提斯），
/// 以及内嵌的主题启动变体（衍天高手启动 / 莫问糕手启动，“成为衍天/莫问高手”激活时替代启动音）。
/// 解析优先级：主题自定义（语音设置按主题选择，存于配置 ThemeSounds）> 主题配套音频 >
/// 静音（配套文件缺失留痕——基础提示音已从程序分离，不再内嵌）；启动变体不可自定义，恒为内嵌资源。
/// WPF MediaPlayer 的媒体管线不解析 pack://application 资源 URI——Open 会静默失败
/// （HasAudio=false、不触发异常、Play 无声），故内嵌资源解包到临时目录以文件 URI 播放；
/// 主题配套与自定义音频均为磁盘文件，直接以文件 URI 播放。
/// 音量（0.0~1.0）在每次播放前应用，Play 异步非阻塞。
/// </summary>
public sealed class SoundCueService
{
    private static readonly Uri DivinerStartCueUri =
        new("pack://application:,,,/Resources/Sounds/DivinerStartVoice.mp3");

    private static readonly Uri MoWenDivinerStartCueUri =
        new("pack://application:,,,/Resources/Sounds/MoWenDivinerStartVoice.wav");

    private readonly MediaPlayer _startPlayer = new();
    private readonly MediaPlayer _stopPlayer = new();
    private readonly MediaPlayer _divinerStartPlayer = new();
    private readonly MediaPlayer _mowenDivinerStartPlayer = new();
    private readonly MediaPlayer _cyclePlayer = new();
    private readonly MediaPlayer _previewPlayer = new();   // 试听专用（可播当前主题之外的主题方案音频）

    // 主题启动变体（内嵌资源解包到临时目录后的文件 URI；不可自定义，恒为内嵌）。
    private Uri? _defaultDivinerStart;
    private Uri? _defaultMowenDivinerStart;

    // 当前主题的自定义音频（语音目录下的文件 URI；null = 未设置 / 文件缺失，回退主题配套音频）。
    private Uri? _customStart;
    private Uri? _customStop;
    private Uri? _customCycle;

    // 当前主题的配套音频（程序目录文件 URI；null = 配套文件缺失，留痕后静音）。
    private Uri? _themeStart;
    private Uri? _themeStop;
    private Uri? _themeCycle;

    // 当前主题是否莫问高手（启动音变体选择：莫问主题播「莫问糕手启动」，其余播「衍天高手启动」）。
    private bool _moWenActive;

    private double _volume = Constants.DefaultSoundVolume / 100.0;

    /// <summary>自定义语音目录：导入的音频复制到此处，配置只记录文件名。</summary>
    public static string SoundsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Constants.AppFolderName, "sounds");

    /// <summary>程序目录（exe 同级）：候选提示音的放置位置（不集成进程序，散装文件即可识别）。</summary>
    public static string AppDir => AppDomain.CurrentDomain.BaseDirectory;

    /// <summary>可识别为提示音的扩展名（与「导入…」过滤器一致）。</summary>
    private static readonly string[] AudioExtensions = [".mp3", ".wav", ".m4a", ".aac", ".wma"];

    /// <summary>
    /// 扫描程序目录里的候选提示音（exe 同目录 + 可选 audio 子目录，按文件名排序）。
    /// 这些文件**不内嵌进程序**：用户把音频放进程序目录即可在「语音设置」下拉列表里选择，
    /// 选中后与手动「导入…」同效（复制到语音目录 + 落盘）。
    /// </summary>
    public static List<string> ScanCandidates()
    {
        var found = new List<string>();
        foreach (var dir in new[] { AppDir, Path.Combine(AppDir, "audio") })
        {
            try
            {
                if (!Directory.Exists(dir)) continue;
                foreach (var path in Directory.EnumerateFiles(dir))
                {
                    if (Array.Exists(AudioExtensions,
                            ext => string.Equals(ext, Path.GetExtension(path), StringComparison.OrdinalIgnoreCase)))
                        found.Add(path);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"候选提示音扫描失败（{dir}）：{ex.Message}");
            }
        }
        return found
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public SoundCueService()
    {
        _startPlayer.MediaFailed += (_, e) =>
            Logger.Error("启动提示音加载失败。", e.ErrorException);
        _stopPlayer.MediaFailed += (_, e) =>
            Logger.Error("停止提示音加载失败。", e.ErrorException);
        _divinerStartPlayer.MediaFailed += (_, e) =>
            Logger.Error("衍天高手启动提示音加载失败。", e.ErrorException);
        _mowenDivinerStartPlayer.MediaFailed += (_, e) =>
            Logger.Error("莫问糕手启动提示音加载失败。", e.ErrorException);
        _cyclePlayer.MediaFailed += (_, e) =>
            Logger.Error("切换提示音加载失败。", e.ErrorException);
        _previewPlayer.MediaFailed += (_, e) =>
            Logger.Error("试听音频加载失败。", e.ErrorException);

        try
        {
            var dir = Path.Combine(Path.GetTempPath(), Constants.AppFolderName);
            Directory.CreateDirectory(dir);
            _defaultDivinerStart = ExtractToTemp(DivinerStartCueUri, dir, "DivinerStartVoice.mp3");
            _defaultMowenDivinerStart = ExtractToTemp(MoWenDivinerStartCueUri, dir, "MoWenDivinerStartVoice.wav");
        }
        catch (Exception ex)
        {
            Logger.Error("启动变体提示音资源加载失败。", ex);
        }

        ReloadAll();
    }

    /// <summary>
    /// 应用主题语音方案：解析该主题的自定义选择（配置 ThemeSounds 对应项，按主题独立）
    /// 与主题配套音频（SectThemes.CueFiles：高松灯 / 墨提斯，散装于程序目录）并整套装载。
    /// 切换方案档位 / 方案定制 / 导入配置（ApplyAppearance）与语音设置修改（编辑当前主题时）调用
    /// ——语音方案随主题整套切换。配套文件缺失时该用途静音并留痕（基础提示音已不内嵌）。
    /// </summary>
    public void ApplyTheme(SectTheme theme, CustomSoundConfig? custom)
    {
        _moWenActive = ReferenceEquals(theme, SectThemes.MoWen);
        _customStart = ResolveCustom(custom?.Start);
        _customStop = ResolveCustom(custom?.Stop);
        _customCycle = ResolveCustom(custom?.Cycle);
        var (start, stop, cycle) = SectThemes.CueFiles(theme);
        _themeStart = ResolveThemeFile(start);
        _themeStop = ResolveThemeFile(stop);
        _themeCycle = ResolveThemeFile(cycle);
        ReloadAll();
    }

    /// <summary>主题配套候选文件名 → 程序目录文件 URI（缺失返回 null 并留痕）。</summary>
    private static Uri? ResolveThemeFile(string fileName)
    {
        var path = FindCandidate(fileName);
        if (path is null)
        {
            Logger.Warn($"主题配套提示音缺失，该用途将静音：{fileName}（随发布包 audio/ 分发）");
            return null;
        }
        return new Uri(path);
    }

    /// <summary>按文件名在程序目录（exe 同级 + audio 子目录）查找候选音频（大小写不敏感）；缺失返回 null。</summary>
    public static string? FindCandidate(string fileName)
    {
        foreach (var dir in new[] { AppDir, Path.Combine(AppDir, "audio") })
        {
            try
            {
                var path = Path.Combine(dir, fileName);
                if (File.Exists(path)) return path;
            }
            catch (Exception ex)
            {
                Logger.Warn($"主题配套提示音查找失败（{dir}）：{ex.Message}");
            }
        }
        return null;
    }

    /// <summary>按当前优先级重装全部播放器：主题自定义 > 主题配套音频（文件缺失静音）；
    /// 衍天 / 莫问启动变体不可自定义，恒为内嵌资源。</summary>
    private void ReloadAll()
    {
        Reload(_startPlayer, _customStart ?? _themeStart);
        Reload(_stopPlayer, _customStop ?? _themeStop);
        Reload(_cyclePlayer, _customCycle ?? _themeCycle);
        Reload(_divinerStartPlayer, _defaultDivinerStart);
        Reload(_mowenDivinerStartPlayer, _defaultMowenDivinerStart);
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
    /// 释放某个语音的媒体源占用（导入覆盖 / 删除文件前调用；随后 ApplyTheme 会重新装载）。
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

    /// <summary>播放“启动”提示音（总开关开启时）。自定义开启音优先；否则按激活与主题选变体：
    /// diviner（成为衍天/莫问高手激活）→ 莫问主题播「莫问糕手启动」、其余播「衍天高手启动」；普通态播「启动」。</summary>
    public void PlayStart(bool diviner = false) =>
        Play(diviner && _customStart is null
            ? (_moWenActive ? _mowenDivinerStartPlayer : _divinerStartPlayer)
            : _startPlayer);

    /// <summary>播放“停止”提示音（总开关停用时）。</summary>
    public void PlayStop() => Play(_stopPlayer);

    /// <summary>播放“切换”提示音（切换方案热键触发并实际切换档位时；独立播放器，可与开关语音叠加）。</summary>
    public void PlayCycle() => Play(_cyclePlayer);

    /// <summary>试听指定音频文件（语音设置对话框用；经独立播放器，可试听当前主题之外的主题方案）。
    /// 路径为空 / 文件缺失时静默忽略。</summary>
    public void PreviewFile(string? path)
    {
        if (path is null || !File.Exists(path)) return;
        Reload(_previewPlayer, new Uri(path));
        Play(_previewPlayer);
    }

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

    /// <summary>
    /// 存储文件名（"{用途前缀}_{原文件名}"）→ 原始文件名（界面显示用）。
    /// 只按本用途的前缀剥离，避免把文件名里本来就有的下划线误当分隔符。
    /// </summary>
    public static string OriginalFileName(SoundCue cue, string? storedName)
    {
        // net48 参考程序集里 string.IsNullOrEmpty 无 NotNullWhen 注解，用模式匹配让编译器确认非空
        if (storedName is not { Length: > 0 }) return "";
        var prefix = CuePrefix(cue) + "_";
        return storedName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? storedName.Substring(prefix.Length)
            : storedName;
    }

    /// <summary>精简展示名：去路径与扩展名（下拉列表显示用，不展示路径）。</summary>
    public static string ShortDisplayName(string? pathOrFileName)
    {
        if (pathOrFileName is not { Length: > 0 }) return "";
        var fileName = Path.GetFileName(pathOrFileName);
        return Path.GetFileNameWithoutExtension(fileName);
    }

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
