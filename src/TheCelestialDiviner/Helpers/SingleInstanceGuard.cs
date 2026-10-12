using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace TheCelestialDiviner.Helpers;

/// <summary>
/// 单实例保护：同一登录会话内只允许一个衍天高手进程。
///
/// 为什么必须有（有实证，非推测）：DD 虚拟驱动的服务（<c>dd63330</c>）是**全局单例**。
/// 两个进程各自调用 DD_btn(0) 时，一方的 DeleteService 会把服务置入"标记删除"状态，
/// 另一方紧接着的 CreateServiceW 便返回 <b>1072 ERROR_SERVICE_MARKED_FOR_DELETE</b>，
/// 于是 DD 弹出「DD install error. / 驱动安装错误.」模态框（它对该错误不重试）。
/// 本机日志实证（app-20260905.log）：服务被禁用(1058) → 恢复失败(1072) → 删除重建仍 1072
/// → DD_btn 连续 3 次 ret=-1，直到另一个实例退出后第 4 次才成功。
/// 此外多实例还会：两个进程都装低级钩子并注入按键（按键被注入两次，EchoGuard 是进程内的
/// 消不掉跨进程回环）、互相覆盖 config.json（日志中已见 4 次写失败）、界面卡在 DD 重试上。
///
/// 判据：命名互斥体（<c>Local\</c> = 当前会话，不同登录用户互不干扰）为主；
/// 另加同名进程扫描，用于拦住**未带本保护的旧版本**——旧版本不持有互斥体，
/// 而"从两个不同目录各启动一份"正是本问题最常见的形态（互斥体与程序路径无关，故能拦住）。
/// </summary>
public static class SingleInstanceGuard
{
    /// <summary>主窗口标题（激活已有实例时优先匹配；键帽悬浮窗标题为 KeycapOverlay，据此避开）。</summary>
    private const string MainWindowTitle = "衍天高手";

    private const string MutexName = @"Local\TheCelestialDiviner.SingleInstance";

    /// <summary>等待同名旧进程退出的上限（用户"关掉再立刻开"时不该被误拦）。</summary>
    private const int OthersExitWaitMs = 2500;

    private static Mutex? _mutex;
    private static bool _owned;

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private const int SW_RESTORE = 9;

    /// <summary>
    /// 尝试取得单实例所有权。返回 false = 已有实例在运行，调用方应
    /// <see cref="ActivateExisting"/> 后退出，不要再做任何初始化。
    /// </summary>
    public static bool TryAcquire()
    {
        try
        {
            _mutex = new Mutex(initiallyOwned: false, MutexName);
            try
            {
                _owned = _mutex.WaitOne(0, false);
            }
            catch (AbandonedMutexException)
            {
                // 上一个实例被强杀 / 崩溃，互斥体被遗弃但内核对象仍在。
                // 必须接管，否则程序从此再也起不来。
                _owned = true;
                Logger.Warn("单实例保护：上一个实例未正常释放互斥体（被强杀或崩溃退出），本次接管。");
            }
        }
        catch (Exception ex)
        {
            // 互斥体本身不可用（极罕见）：不阻断启动，只留痕。
            Logger.Warn($"单实例保护：创建互斥体失败，跳过保护：{ex.Message}");
            _owned = true;
        }

        if (!_owned) return false;

        // 互斥体只拦得住持有它的版本；旧版本（无本保护）不持有它，只能靠进程名扫描兜住。
        if (FindOtherInstancePids().Count == 0) return true;

        // 也可能是正在退出的旧实例——短暂等它消失，避免"关掉再立刻开"被误拦。
        var deadline = Environment.TickCount + OthersExitWaitMs;
        while (Environment.TickCount < deadline)
        {
            Thread.Sleep(250);
            if (FindOtherInstancePids().Count == 0) return true;
        }

        Logger.Warn("单实例保护：检测到旧版本实例仍在运行，本次启动取消。");
        // 必须释放：本进程已取得所有权，若不释放就退出，互斥体会被遗弃，
        // 下一次启动将收到 AbandonedMutexException 并打出误导性的"崩溃接管"日志。
        Release();
        return false;
    }

    /// <summary>把已在运行的实例主窗口激活到前台。返回是否成功（失败时调用方给用户一句提示）。</summary>
    public static bool ActivateExisting()
    {
        var target = IntPtr.Zero;
        foreach (var pid in FindOtherInstancePids())
        {
            target = FindMainWindow(pid);
            if (target != IntPtr.Zero) break;
        }
        if (target == IntPtr.Zero)
        {
            Logger.Warn("单实例保护：未找到运行中实例的主窗口（可能已最小化到托盘或正在退出）。");
            return false;
        }

        Logger.Info($"单实例保护：找到运行中实例的主窗口（hwnd=0x{target.ToInt64():X}）。");
        // 新进程此刻是前台进程（用户刚双击启动），因此允许 SetForegroundWindow。
        ShowWindow(target, SW_RESTORE);
        var ok = SetForegroundWindow(target);
        if (!ok) Logger.Warn("单实例保护：窗口已还原但未能置前（前台锁定）。");
        return ok;
    }

    /// <summary>释放互斥体（正常退出路径调用；须与取得所有权同一线程）。</summary>
    public static void Release()
    {
        if (!_owned) return;
        _owned = false;
        try { _mutex?.ReleaseMutex(); } catch { /* 非持有线程 / 已释放：忽略 */ }
        try { _mutex?.Dispose(); } catch { /* 忽略 */ }
        _mutex = null;
    }

    /// <summary>同名进程（不含自己）。</summary>
    private static List<int> FindOtherInstancePids()
    {
        var pids = new List<int>();
        var self = Process.GetCurrentProcess().Id;
        var name = Process.GetCurrentProcess().ProcessName;
        foreach (var p in Process.GetProcessesByName(name))
        {
            try
            {
                if (p.Id != self) pids.Add(p.Id);
            }
            catch { /* 进程已退出：忽略 */ }
            finally { p.Dispose(); }
        }
        return pids;
    }

    /// <summary>
    /// 按 PID 找顶层窗口：优先标题等于主窗口标题的那个；无标题的顶层窗口（键帽悬浮窗、
    /// 各类弹出层）先跳过，仅在完全找不到时作为兜底。
    /// </summary>
    private static IntPtr FindMainWindow(int pid)
    {
        var hit = IntPtr.Zero;
        var fallback = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out var owner);
            if (owner != (uint)pid) return true;

            var len = GetWindowTextLength(hWnd);
            if (len <= 0) return true;

            var title = new StringBuilder(len + 1);
            GetWindowText(hWnd, title, title.Capacity);
            if (title.ToString() == MainWindowTitle) { hit = hWnd; return false; }
            if (fallback == IntPtr.Zero) fallback = hWnd;
            return true;
        }, IntPtr.Zero);

        return hit != IntPtr.Zero ? hit : fallback;
    }
}
