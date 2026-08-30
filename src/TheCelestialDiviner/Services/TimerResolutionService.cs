using TheCelestialDiviner.Helpers;

namespace TheCelestialDiviner.Services;

/// <summary>
/// 系统定时器分辨率服务。
/// 通过 timeBeginPeriod(1) 将系统定时器分辨率提升到 1ms，
/// 保障 Sleep(1) / Task.Delay(1) 级别连发间隔的稳定性。
/// 应用退出时必须调用 <see cref="Dispose"/> 恢复（timeEndPeriod(1)）。
/// </summary>
public sealed class TimerResolutionService : IDisposable
{
    private bool _started;

    /// <summary>定时器分辨率是否已成功提升至 1ms（状态栏显示用）。</summary>
    public bool IsHighResolution => _started;

    /// <summary>提升定时器分辨率到 1ms。重复调用安全。</summary>
    public void Start()
    {
        if (_started) return;
        var ret = NativeMethods.timeBeginPeriod(1);
        _started = ret == 0; // TIMERR_NOERROR = 0
        Logger.Info(_started ? "定时器分辨率已提升至 1ms。" : "timeBeginPeriod(1) 调用失败。");
    }

    /// <summary>恢复定时器分辨率。重复调用安全。</summary>
    public void Dispose()
    {
        if (!_started) return;
        NativeMethods.timeEndPeriod(1);
        _started = false;
        Logger.Info("定时器分辨率已恢复。");
    }
}
