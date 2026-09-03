using System.Windows.Threading;
using TheCelestialDiviner.Helpers;
using TheCelestialDiviner.Models;

namespace TheCelestialDiviner.Services;

/// <summary>
/// 按键录制器：订阅低级钩子捕获一次按下事件（键盘或鼠标），
/// 5 秒超时自动结束。回调封送回启动线程（UI）。
/// </summary>
public sealed class KeyRecorder
{
    /// <summary>是否有录制器正在捕获（volatile 供钩子线程读取；录制期间总开关暂停响应）。</summary>
    public static volatile bool IsAnyRecording;

    private readonly InputHookService _hooks;
    private DispatcherTimer? _timer;
    private Action<InputSource>? _onCaptured;
    private Action? _onTimeout;
    private Dispatcher? _dispatcher;

    /// <summary>是否正在录制。</summary>
    public bool IsRecording { get; private set; }

    /// <param name="hooks">低级钩子服务（捕获事件源）。</param>
    public KeyRecorder(InputHookService hooks) => _hooks = hooks;

    /// <summary>开始录制：首个捕获事件或超时后自动结束。</summary>
    public void Start(Action<InputSource> onCaptured, Action? onTimeout = null)
    {
        Stop();
        _onCaptured = onCaptured;
        _onTimeout = onTimeout;
        _dispatcher = Dispatcher.CurrentDispatcher; // 回调封送目标（UI 线程）
        IsRecording = true;
        IsAnyRecording = true;
        _hooks.SourceDown += OnSourceDown;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Constants.RecordTimeoutMs) };
        _timer.Tick += (_, _) => StopInternal(callTimeout: true);
        _timer.Start();
    }

    /// <summary>手动停止（不触发超时回调）。</summary>
    public void Stop() => StopInternal(callTimeout: false);

    private void OnSourceDown(InputSource source)
    {
        if (!IsRecording) return;
        var callback = _onCaptured;
        StopInternal(callTimeout: false);
        // 钩子线程 → UI 线程封送。
        _dispatcher?.BeginInvoke(() => callback?.Invoke(source));
    }

    private void StopInternal(bool callTimeout)
    {
        if (!IsRecording) return;
        IsRecording = false;
        IsAnyRecording = false;
        _hooks.SourceDown -= OnSourceDown;
        _timer?.Stop();
        _timer = null;
        _onCaptured = null;
        var timeout = _onTimeout;
        _onTimeout = null;
        if (callTimeout)
        {
            Logger.Info("录制超时（5 秒内未捕获按键）。");
            _dispatcher?.BeginInvoke(() => timeout?.Invoke());
        }
    }
}
