using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using TheCelestialDiviner.Helpers;

namespace TheCelestialDiviner.Services;

/// <summary>
/// Interception 驱动（oblitum/Interception，LGPL 开源键盘 / 鼠标过滤驱动）键盘注入服务。
/// 与 <see cref="DdDriverService"/> 并列为「物理级注入」的第二条路线（键盘模式 4）：
/// 两者都是内核层注入，事件不带 LLKHF_INJECTED，对游戏表现为物理键盘；
/// 区别在来源与代价——
/// <list type="bullet">
/// <item>DD：闭源、免重启、有免费版在线授权，且已知内核缺陷（0x50 蓝屏，见 DdCrashGuard）；</item>
/// <item>Interception：开源（LGPL 3.0）、官方 GitHub 发布、驱动签名可验证；
/// 但装的是键盘 / 鼠标**类过滤驱动**，安装后必须重启，且与内存完整性（HVCI）冲突。</item>
/// </list>
///
/// 为什么只「发」不「收」：Interception 的上下文默认 filter 为 NONE —— 不注册过滤谓词，
/// 驱动就不会把物理按键截流到本进程。这是本服务刻意保持的形态：
/// 一旦 set_filter(KEY_ALL) 而不在后台把事件原样 send 回去，用户整块键盘就会被吞掉
/// （进程卡顿 = 键盘卡顿，进程崩溃 = 键盘失灵到重启）。本程序只需要注入，
/// 不需要接管物理输入，所以**绝不设置过滤器**，把风险面压到"发送失败即返回 false"。
///
/// 回环：注入事件与物理输入在系统层同源（无注入标记、Raw Input 归因到同一设备），
/// 低级钩子无法凭事件区分，由 <see cref="EchoGuard"/> 按注入时间线消除 —— 与 DD 模式共用同一套机制。
///
/// 官方渠道与授权：驱动 / 安装器来自 github.com/oblitum/Interception 的 Release（v1.0.1）。
/// 其双许可中的非商业分支（LGPL 3.0）明确允许**再分发驱动与安装器二进制**，
/// 前提是「与驱动的通信仅经由 interception.dll 的 API」——本服务严格遵守这一点：
/// 不直开 <c>\\.\interceptionXX</c> 设备、不发原始 IOCTL，一切经库导出完成
/// （连"驱动加载了没"的探测都用 create_context/destroy_context 做，见 TryProbeContext）。
///
/// 因此文件**随发布包内置**（<c>interception\</c> 目录：官方 dll + 官方安装器 + LGPL 文本），
/// 安装时直接就地取用、无需联网；随包缺失（源码构建 / 精简包）才回退官方 Release 下载。
/// 无论哪条路径：不修改、不重签驱动，也不自行实现安装逻辑（安装 / 卸载都交给官方安装器）。
/// </summary>
public static class InterceptionDriverService
{
    // ---------- 驱动状态 ----------

    /// <summary>Interception 驱动的可用状态。</summary>
    public enum DriverState
    {
        /// <summary>未安装（键盘类过滤驱动未注册 / 未落到 System32\drivers）。</summary>
        NotInstalled,
        /// <summary>已安装但驱动未加载：绝大多数情况是「装完还没重启」。</summary>
        NeedsReboot,
        /// <summary>驱动已加载，可创建上下文注入。</summary>
        Ready
    }

    // ---------- 常量 ----------

    /// <summary>用户态库文件名（官方 x64 版，约 11KB）。</summary>
    private const string DllFileName = "interception.dll";

    /// <summary>官方命令行安装器（x86，兼容 32 / 64 位系统；驱动本体以资源形式内嵌在它里面）。</summary>
    private const string InstallerFileName = "install-interception.exe";

    /// <summary>键盘类过滤驱动注册的服务名（官方安装器固定用它，也是回滚判据）。</summary>
    private const string KeyboardServiceName = "keyboard";

    /// <summary>键盘设备类 GUID（UpperFilters 挂在它下面）。</summary>
    private const string KeyboardClassKey =
        @"SYSTEM\CurrentControlSet\Control\Class\{4D36E96B-E325-11CE-BFC1-08002BE10318}";

    /// <summary>官方 v1.0.1 的 x64 interception.dll 指纹（与 GitHub Release 资产逐字节一致）。</summary>
    private const string KnownDllSha256 = "AB88164C11B1B48488772D4C3BFAA4509D5B0AE9DBC5A691DC4F96F0260443C8";

    /// <summary>官方包 Release 资产（固定版本直链；API 不可达时用它）。</summary>
    private const string KnownZipUrl =
        "https://github.com/oblitum/Interception/releases/download/v1.0.1/Interception.zip";

    private const string LatestApiUrl =
        "https://api.github.com/repos/oblitum/Interception/releases/latest";

    /// <summary>
    /// GitHub 直连在国内常被阻断（实测 TLS 握手直接失败），
    /// 与 DD 自动获取一样保留镜像兜底；镜像只转发同一个官方资产，指纹校验仍在本机进行。
    /// </summary>
    private static readonly string[] ZipMirrors =
    {
        "https://ghproxy.net/" + KnownZipUrl,
        "https://ghfast.top/" + KnownZipUrl,
        "https://gh-proxy.com/" + KnownZipUrl
    };

    // ---------- P/Invoke（动态绑定：DLL 缺失时不影响程序启动） ----------

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr CreateContextFunc();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DestroyContextFunc(IntPtr context);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SendFunc(IntPtr context, int device, [In] InterceptionKeyStroke[] strokes, uint count);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int IsKeyboardFunc(int device);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint GetHardwareIdFunc(IntPtr context, int device, byte[] buffer, uint size);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibraryW(string lpFileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint MapVirtualKeyW(uint wCode, uint wMapType);

    /// <summary>Interception 键盘敲击结构（与 interception.h 的 InterceptionKeyStroke 逐字段对齐）。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct InterceptionKeyStroke
    {
        /// <summary>扫描码（低 8 位）。</summary>
        public ushort code;
        /// <summary>状态位：DOWN / UP / E0 / E1 的按位或。</summary>
        public ushort state;
    }

    private const ushort KeyDownState = 0x00;   // INTERCEPTION_KEY_DOWN
    private const ushort KeyUpState = 0x01;     // INTERCEPTION_KEY_UP
    private const ushort KeyE0State = 0x02;     // INTERCEPTION_KEY_E0（扩展键前缀）
    private const ushort KeyE1State = 0x04;     // INTERCEPTION_KEY_E1（Pause）

    // ---------- 状态 ----------

    private static readonly object Gate = new();
    private static IntPtr _dllHandle;
    private static IntPtr _context;
    private static CreateContextFunc? _createContext;
    private static DestroyContextFunc? _destroyContext;
    private static SendFunc? _send;
    private static IsKeyboardFunc? _isKeyboard;
    private static GetHardwareIdFunc? _getHardwareId;
    private static string? _loadedPath;
    private static int _keyboardDevice;      // 已解析的键盘设备号（0 = 未解析）
    private static int _lastLatencyLogTick;

    /// <summary>已加载的 DLL 路径（未加载为 null）。</summary>
    public static string? LoadedPath { get { lock (Gate) return _loadedPath; } }

    /// <summary>上下文是否已建立（可注入）。</summary>
    public static bool IsReady { get { lock (Gate) return _context != IntPtr.Zero && _send is not null; } }

    /// <summary>最近一次失败原因（供 UI 日志 / 注释区展示）。</summary>
    public static string? LastError { get; private set; }

    /// <summary>
    /// 诊断开关：不加载 Interception（命令行 <c>--no-intercept</c> 置位），
    /// 用于排查"注入路径本身"与"驱动行为"的边界。
    /// </summary>
    public static bool SuppressDriver { get; set; }

    /// <summary>用户态库是否在位（不算驱动是否安装）。</summary>
    public static bool DllPresent => ProbeDll(out _) is not null;

    /// <summary>驱动是否已安装（键盘类 UpperFilters 里有 keyboard 项 + System32\drivers\keyboard.sys 占位）。</summary>
    public static bool DriverInstalled => ReadKeyboardUpperFilters().Any(
        f => string.Equals(f, KeyboardServiceName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 驱动是否**仍在系统里**：已安装，或已卸载但还没重启（注册项删了、内核里仍挂着）。
    /// 这是"与 DD 冲突"的正确判据 —— 只看 <see cref="DriverInstalled"/> 会在
    /// "卸载了但没重启"这段窗口里误判为"已经没有冲突"，从而放出假的 DD 模式。
    /// </summary>
    public static bool IsDriverPresent => DriverInstalled || IsReady || TryProbeContext();

    /// <summary>
    /// 菜单项能否点选：库在位且驱动已安装。装完还没重启也算可用 ——
    /// 点选时由 <see cref="EnsureReady"/> 给出"请重启"这种可执行的具体原因，
    /// 比灰着不给点更有指导意义。
    /// </summary>
    public static bool IsAvailable => DllPresent && DriverInstalled;

    /// <summary>驱动当前状态（读注册表 + 探测设备，供 UI 展示）。</summary>
    public static DriverState State
    {
        get
        {
            // 本进程已持有上下文 = 必然已加载；此时再探测设备可能因独占共享失败，
            // 不能据此回判成"需重启"。
            if (IsReady) return DriverState.Ready;
            if (!DriverInstalled) return DriverState.NotInstalled;
            return TryProbeContext() ? DriverState.Ready : DriverState.NeedsReboot;
        }
    }

    // ---------- 初始化 / 释放 ----------

    /// <summary>
    /// 加载 interception.dll、建立上下文并解析键盘设备。重复调用安全。
    /// 失败时不抛异常，只写 <see cref="LastError"/>（中文、可执行），返回 false。
    /// </summary>
    public static bool EnsureReady()
    {
        if (SuppressDriver)
        {
            LastError = "诊断模式：已通过 --no-intercept 禁用 Interception 驱动";
            return false;
        }

        lock (Gate)
        {
            if (_context != IntPtr.Zero && _send is not null) return true;
            LastError = null;

            var path = ProbeDll(out var wrongArch);
            if (path is null)
            {
                LastError = wrongArch is null
                    ? "未找到 interception.dll（已查找：随包目录 interception\\、程序目录、%APPDATA%\\TheCelestialDiviner\\drivers\\interception）"
                    : $"{Path.GetFileName(wrongArch)} 不是 64 位（x64）DLL，本程序是 64 位进程，无法加载。";
                return false;
            }

            // "已卸载但还没重启"是个合法的过渡态：注册项删了，驱动仍挂在键盘栈上 ——
            // 本次会话继续可用（别把用户当场踢回普通模式），下次开机会自然失效。
            if (!DriverInstalled)
            {
                var stillLoaded = TryProbeContext();
                if (!stillLoaded)
                {
                    LastError = "Interception 驱动尚未安装。可在「选项 → 键盘注入 → 安装 Interception 驱动…」"
                                + "一键从官方发布渠道获取并安装（安装完成后必须重启电脑）。";
                    return false;
                }
                Logger.Warn("Interception 驱动已卸载但重启前仍然加载：本次会话可用，重启后失效。");
            }

            if (_dllHandle == IntPtr.Zero)
            {
                _dllHandle = LoadLibraryW(path);
                if (_dllHandle == IntPtr.Zero)
                {
                    var err = Marshal.GetLastWin32Error();
                    LastError = err == 193
                        ? $"加载 {Path.GetFileName(path)} 失败：位数不匹配（请换用 x64 版）"
                        : $"加载 {Path.GetFileName(path)} 失败（err={err}）";
                    return false;
                }
                _loadedPath = path;
                Logger.Info($"已加载 Interception 库：{path}");
            }

            _createContext = GetProc<CreateContextFunc>("interception_create_context");
            _destroyContext = GetProc<DestroyContextFunc>("interception_destroy_context");
            _send = GetProc<SendFunc>("interception_send");
            _isKeyboard = GetProc<IsKeyboardFunc>("interception_is_keyboard");
            _getHardwareId = GetProc<GetHardwareIdFunc>("interception_get_hardware_id");
            if (_createContext is null || _send is null || _getHardwareId is null)
            {
                LastError = "interception.dll 缺少必要导出（不是官方 Interception 库）";
                FreeAll();
                return false;
            }

            _context = _createContext();
            if (_context == IntPtr.Zero)
            {
                // 两种成因按可能性排序给出，用户按顺序排即可：没重启（最常见）→ HVCI 拦下。
                LastError = "Interception 驱动已安装但未生效 —— 请重启电脑后重试；"
                            + "若重启后仍如此，通常是「内存完整性 / 内核隔离（HVCI）」拒绝加载该驱动："
                            + "可在 Windows 安全中心 → 设备安全性 → 内核隔离 里关闭内存完整性并重启。"
                            + "（也可能是设备被其他使用同一驱动的工具独占，关掉它们再试。）";
                Logger.Error(LastError);
                FreeAll();
                return false;
            }

            var device = ResolveKeyboardDevice();
            if (device == 0)
            {
                LastError = "Interception 未找到可注入的键盘设备（键盘类设备可能只有虚拟键盘 / 远程输入）。";
                Logger.Error(LastError);
                FreeAll();
                return false;
            }

            _keyboardDevice = device;
            Logger.Info($"Interception 驱动就绪（{Path.GetFileName(_loadedPath)}，键盘设备 #{device}）。");
            return true;
        }
    }

    /// <summary>释放上下文（退出时序调用；模式切换时保留，便于快速回切）。</summary>
    public static void Shutdown()
    {
        lock (Gate)
        {
            if (_context != IntPtr.Zero && _destroyContext is not null)
            {
                try { _destroyContext(_context); }
                catch (Exception ex) { Logger.Warn($"释放 Interception 上下文失败（忽略）：{ex.Message}"); }
            }
            _context = IntPtr.Zero;
            _keyboardDevice = 0;
            // 不 FreeLibrary：进程退出时随进程回收，避免与其他线程的绑定失效错位。
        }
    }

    private static void FreeAll()
    {
        if (_context != IntPtr.Zero && _destroyContext is not null)
        {
            try { _destroyContext(_context); } catch { /* 释放失败不影响后续重建 */ }
        }
        _context = IntPtr.Zero;
        _keyboardDevice = 0;
        _send = null;
        _createContext = null;
        _destroyContext = null;
        _isKeyboard = null;
        _getHardwareId = null;
        if (_dllHandle != IntPtr.Zero) { FreeLibrary(_dllHandle); _dllHandle = IntPtr.Zero; }
        _loadedPath = null;
    }

    /// <summary>
    /// 解析可注入的键盘设备号：遍历 1..10（INTERCEPTION_KEYBOARD(i) = i + 1），
    /// 取第一个能读到硬件 ID 的设备。读不到 = 该槽位没有物理键盘（或设备未就绪）。
    /// </summary>
    private static int ResolveKeyboardDevice()
    {
        if (_getHardwareId is null || _context == IntPtr.Zero) return 0;
        var buffer = new byte[512];
        for (var index = 0; index < 10; index++)
        {
            var device = index + 1;
            if (_isKeyboard is not null && _isKeyboard(device) == 0) continue;
            var length = _getHardwareId(_context, device, buffer, (uint)buffer.Length);
            if (length == 0) continue;
            if (length > 2)
            {
                // 硬件 ID 为宽字符串（\0 结尾），日志里保留前 60 字符便于定位是哪块键盘。
                var id = Encoding.Unicode.GetString(buffer, 0, (int)Math.Min(length, 120)).TrimEnd('\0');
                Logger.Info($"Interception 键盘设备 #{device}：{id}");
            }
            return device;
        }
        return 0;
    }

    /// <summary>
    /// 探测驱动是否已加载：建一个上下文再立刻销毁（**只用库 API**，不直接打开驱动设备）。
    /// 为什么不 CreateFile("\\\\.\\interception00")：许可条款要求"与驱动的通信仅经
    /// interception.dll 的 API"，直开设备节点会踩在条款边上 —— 随包分发时不能留这种把柄。
    /// 内部取 <see cref="Gate"/>（锁可重入）：探测会读/改 _dllHandle 与导出指针，
    /// 必须与 EnsureReady 的加载/释放互斥，否则可能 FreeLibrary 掉正在用的函数指针。
    /// </summary>
    private static bool TryProbeContext()
    {
        lock (Gate)
        {
            if (_createContext is null && !TryLoadLibrary()) return false;
            if (_createContext is null) return false;
            var ctx = IntPtr.Zero;
            try
            {
                ctx = _createContext();
                return ctx != IntPtr.Zero;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (ctx != IntPtr.Zero && _destroyContext is not null)
                {
                    try { _destroyContext(ctx); } catch { /* 探测用上下文，销毁失败不影响结论 */ }
                }
            }
        }
    }

    /// <summary>加载库并绑定导出（不建上下文）。供探测与 <see cref="EnsureReady"/> 复用。</summary>
    private static bool TryLoadLibrary()
    {
        if (_dllHandle != IntPtr.Zero) return _createContext is not null || BindExports();
        var path = ProbeDll(out _);
        if (path is null) return false;
        _dllHandle = LoadLibraryW(path);
        if (_dllHandle == IntPtr.Zero) return false;
        _loadedPath = path;
        return BindExports();
    }

    /// <summary>GetProcAddress 绑定全部导出；缺关键导出返回 false。</summary>
    private static bool BindExports()
    {
        _createContext = GetProc<CreateContextFunc>("interception_create_context");
        _destroyContext = GetProc<DestroyContextFunc>("interception_destroy_context");
        _send = GetProc<SendFunc>("interception_send");
        _isKeyboard = GetProc<IsKeyboardFunc>("interception_is_keyboard");
        _getHardwareId = GetProc<GetHardwareIdFunc>("interception_get_hardware_id");
        return _createContext is not null && _send is not null && _getHardwareId is not null;
    }

    // ---------- 注入 ----------

    /// <summary>
    /// 发送键盘按下 / 抬起。返回是否成功（含初始化）。
    /// 与 DD 模式一致：先登记回环名额再注入，失败撤销（见 <see cref="EchoGuard"/>）。
    /// </summary>
    /// <param name="extended">
    /// 调用方给出的扩展键标记（目标键配置里的 Extended）。必须传：
    /// 实测 <c>MapVirtualKey(MAPVK_VK_TO_VSC_EX)</c> 只对个别键（如 Win 键）返回 0xE0 前缀，
    /// 方向键 / Insert / Home 等一律只给基础扫描码 —— 不补 E0 的话，
    /// 方向键会被当成小键盘 4/8/6/2（NumLock 关时语义虽同为方向，
    /// 但依赖扩展位区分的游戏 / 程序会认错键）。
    /// </param>
    public static bool SendKey(int vk, bool down, bool extended = false)
    {
        lock (Gate)
        {
            if (_send is null || _context == IntPtr.Zero) return false;

            var scan = MapScanCode(vk);
            if (scan == 0) return false;   // 无扫描码的 VK（多媒体键等）交回普通模式

            var uScan = (ushort)scan;
            var prefix = (byte)(uScan >> 8);
            var state = down ? KeyDownState : KeyUpState;
            if (prefix == 0xE1) state |= KeyE1State;
            else if (prefix == 0xE0 || extended || IsExtendedVk(vk)) state |= KeyE0State;

            var stroke = new[] { new InterceptionKeyStroke { code = (ushort)(uScan & 0xFF), state = state } };

            var note = EchoGuard.NoteInjection(vk, down);
            var start = Stopwatch.GetTimestamp();
            int sent;
            var seh = false;
            try { sent = _send(_context, _keyboardDevice, stroke, 1); }
            catch (Exception ex) { seh = ex is SEHException; sent = int.MinValue; }
            var ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
            note.CostMs = (int)Math.Round(ms);
            if (ms > 50 && unchecked(Environment.TickCount - _lastLatencyLogTick) > 2000)
            {
                _lastLatencyLogTick = Environment.TickCount;
                Logger.Warn($"Interception 注入耗时 {ms:0}ms（vk=0x{vk:X2}, down={down}）。");
            }

            if (!seh && sent == 1) return true;
            EchoGuard.CancelNote(vk, down, note);
            return false;
        }
    }

    /// <summary>当前 VK 是否有可用扫描码（不注入，仅查询）。</summary>
    public static bool IsVkSupported(int vk) => MapScanCode(vk) != 0;

    /// <summary>
    /// 标准 104 键里属于"扩展键"的那一批（扫描码需带 E0 前缀）：
    /// 方向键、编辑键区 6 键、右 Ctrl / 右 Alt、Win 键、菜单键、小键盘除号、NumLock。
    /// 目标键配置里的 Extended 优先，这里只是兜底 —— 用户没勾扩展位时也不至于把方向键发成小键盘键。
    /// </summary>
    private static bool IsExtendedVk(int vk) => vk is
        0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or   // PgUp/PgDn/End/Home/方向
        0x2D or 0x2E or                                                    // Insert / Delete
        0x5B or 0x5C or 0x5D or                                            // LWin / RWin / Apps
        0x6F or 0x90 or                                                    // 小键盘除号 / NumLock
        0xA3 or 0xA5;                                                      // RCtrl / RAlt

    /// <summary>
    /// VK → 扫描码（低字节 scan，高字节 0xE0 / 0xE1 前缀）。
    /// MAPVK_VK_TO_VSC_EX(4) 能直接给出带前缀的扩展键扫描码；
    /// 退化为 MAPVK_VK_TO_VSC(0) 时高字节为 0（普通键）。
    /// </summary>
    private static ushort MapScanCode(int vk)
    {
        var code = MapVirtualKeyW((uint)vk, 4);
        if (code == 0) code = MapVirtualKeyW((uint)vk, 0);
        return (ushort)code;
    }

    // ---------- 官方包获取 / 安装 ----------

    /// <summary>准备阶段（供 UI 区分文案）。</summary>
    public enum PrepareStage
    {
        /// <summary>下载官方包。</summary>
        Downloading,
        /// <summary>校验指纹 + 解包。</summary>
        Verifying,
        /// <summary>执行官方安装器。</summary>
        Installing,
        /// <summary>某次尝试失败，等待自动重试。</summary>
        Retrying,
        /// <summary>安装成功（需重启）。</summary>
        Succeeded,
        /// <summary>硬性失败，已终止。</summary>
        Failed
    }

    /// <summary>一次进度快照（后台线程触发）。</summary>
    public readonly record struct PrepareProgress(PrepareStage Stage, int Attempt, string Message);

    /// <summary>进度回调（后台线程触发，UI 侧必须自行封送）。</summary>
    public static event Action<PrepareProgress>? PrepareStateChanged;

    /// <summary>流程结束（成功 / 失败均触发，后台线程）。</summary>
    public static event Action? PrepareCompleted;

    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TheCelestialDiviner");
        return client;
    }

    private static int _preparing;

    /// <summary>准备流程是否进行中。</summary>
    public static bool IsPreparing => Volatile.Read(ref _preparing) == 1;

    /// <summary>
    /// 后台单飞启动「取官方文件（随包优先，缺失才下载）→ 指纹校验 → 官方安装器 /install」。
    /// 安装由 <see cref="InstallerFileName"/> 完成（LGPL 允许随包分发，本程序仍用官方原版），
    /// 成功后必须重启，驱动才会作为键盘类过滤驱动挂上。
    /// </summary>
    public static void StartPrepare()
    {
        if (Interlocked.CompareExchange(ref _preparing, 1, 0) != 0) return;
        Logger.Info("Interception 驱动准备：开始（官方发布渠道 github.com/oblitum/Interception）...");
        Report(PrepareStage.Downloading, 0, HasBundled
            ? "Interception 驱动准备中：正在校验随包官方文件…"
            : "Interception 驱动准备中：正在连接官方发布渠道…");
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            try { RunPrepare(); }
            catch (Exception ex) { Logger.Error("Interception 驱动准备失败。", ex); }
            finally
            {
                Volatile.Write(ref _preparing, 0);
                PrepareCompleted?.Invoke();
            }
        });
    }

    private static void Report(PrepareStage stage, int attempt, string message)
    {
        try { PrepareStateChanged?.Invoke(new PrepareProgress(stage, attempt, message)); }
        catch (Exception ex) { Logger.Warn($"Interception 进度回调异常（忽略）：{ex.Message}"); }
    }

    private static void RunPrepare()
    {
        var attempt = 0;
        while (true)
        {
            attempt++;
            try
            {
                PrepareOnce(attempt);
                Report(PrepareStage.Succeeded, attempt,
                    "Interception 驱动已安装：请重启电脑，重启后即可在「键盘注入」里选择它。");
                Logger.Info("Interception 驱动安装完成，等待重启生效。");
                return;
            }
            catch (NoRetryException ex)
            {
                Logger.Error($"Interception 驱动准备终止：{ex.Message}");
                Report(PrepareStage.Failed, attempt, $"Interception 驱动安装失败：{ex.Message}");
                return;
            }
            catch (Exception ex)
            {
                var delaySec = Math.Min(5 * attempt, 60);
                Logger.Warn($"Interception 驱动准备第 {attempt} 次失败：{ex.Message}；{delaySec}s 后重试。");
                Report(PrepareStage.Retrying, attempt,
                    $"Interception 驱动第 {attempt} 次尝试失败：{ex.Message.TrimEnd('。', '.', ' ')}，"
                    + $"{delaySec} 秒后自动重试。");
                Thread.Sleep(delaySec * 1000);
            }
        }
    }

    private static void PrepareOnce(int attempt)
    {
        var stagedDll = Path.Combine(StagingDir, DllFileName);
        var stagedInstaller = Path.Combine(StagingDir, InstallerFileName);
        Directory.CreateDirectory(StagingDir);

        // 1. 取官方文件：优先随包（interception\ 目录，LGPL 允许再分发）—— 不联网、不依赖 GitHub；
        //    随包缺失（源码构建 / 精简发布包）时才走官方 Release 下载。
        if (HasBundled)
        {
            Report(PrepareStage.Verifying, attempt, "正在校验随包官方文件（interception.dll）…");
            File.Copy(BundledDll, stagedDll, overwrite: true);
            File.Copy(BundledInstaller, stagedInstaller, overwrite: true);
            Logger.Info("Interception：使用随包官方文件（跳过下载）。");
        }
        else if (!File.Exists(stagedDll) || !File.Exists(stagedInstaller))
        {
            var zipPath = Path.Combine(StagingDir, "Interception.zip");
            DownloadZip(zipPath, attempt);
            Report(PrepareStage.Verifying, attempt, "Interception 驱动准备中：正在校验官方包…");
            ExtractOfficial(zipPath, stagedDll, stagedInstaller);
            try { File.Delete(zipPath); } catch { /* 临时包删除失败不影响安装 */ }
        }

        VerifyOfficialDll(stagedDll);

        // 2. 官方安装器 /install（静默，无交互：无 pause / 提示输入）。
        Report(PrepareStage.Installing, attempt, "Interception 驱动准备中：正在安装驱动（官方安装器）…");
        var (exitCode, output) = RunInstaller(stagedInstaller, "/install");
        var installed = DriverInstalled;
        if (!installed)
            throw new Exception($"官方安装器未完成安装（exit={exitCode}）{Shorten(output)}");

        Logger.Info($"Interception 官方安装器执行完成（exit={exitCode}），键盘类 UpperFilters 已注册。");
        // 安装成功但设备尚未加载（本次会话内必然如此）＝ 等重启，属正常终态。
    }

    /// <summary>
    /// 下载官方 zip：GitHub API 查最新 Release → 固定 v1.0.1 直链 → 镜像链。
    /// 每次尝试都从第一个来源开始，失败抛异常交给外层重试。
    /// </summary>
    private static void DownloadZip(string zipPath, int attempt)
    {
        Directory.CreateDirectory(StagingDir);
        var urls = new List<string>();
        var latest = ResolveLatestZipUrl();
        if (latest is not null) urls.Add(latest);
        urls.Add(KnownZipUrl);
        urls.AddRange(ZipMirrors);

        Exception? last = null;
        foreach (var url in urls.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var host = new Uri(url).Host;
                Report(PrepareStage.Downloading, attempt, $"Interception 驱动准备中：正在下载官方包（{host}）…");
                Logger.Info($"Interception 官方包下载：{url}");
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                using var response = Http.GetAsync(url, cts.Token).ConfigureAwait(false).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();
                using var stream = response.Content.ReadAsStreamAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                using var file = File.Create(zipPath);
                stream.CopyTo(file);
                if (new FileInfo(zipPath).Length < 100 * 1024)
                    throw new Exception($"下载内容过小（{new FileInfo(zipPath).Length} 字节），可能不是官方包");
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                Logger.Warn($"Interception 官方包下载失败（{url}）：{ex.Message}");
            }
        }
        throw last ?? new Exception("所有下载来源均不可达");
    }

    /// <summary>从官方 zip 里取出 x64 interception.dll 与命令行安装器。</summary>
    private static void ExtractOfficial(string zipPath, string dllTarget, string installerTarget)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var dll = archive.Entries.FirstOrDefault(e =>
            e.FullName.EndsWith("/x64/" + DllFileName, StringComparison.OrdinalIgnoreCase));
        var installer = archive.Entries.FirstOrDefault(e =>
            e.Name.Equals(InstallerFileName, StringComparison.OrdinalIgnoreCase));
        if (dll is null || installer is null)
            throw new NoRetryException("官方包内未找到 x64 interception.dll 或 install-interception.exe（上游结构可能已变化）");

        Directory.CreateDirectory(StagingDir);
        dll.ExtractToFile(dllTarget, overwrite: true);
        installer.ExtractToFile(installerTarget, overwrite: true);
        Logger.Info($"Interception 官方包已解出：{DllFileName}（{dll.Length} 字节）、{InstallerFileName}（{installer.Length} 字节）。");
    }

    /// <summary>
    /// 校验 x64 库的来历：优先比对官方 v1.0.1 指纹；指纹不认识（上游更新）时
    /// 退回 Authenticode 签名者校验（必须是 Francisco Lopes da Silva 的签名）。
    /// 两者都不通过 = 拒绝安装（内核驱动不做"看着像就装"）。
    /// </summary>
    private static void VerifyOfficialDll(string dllPath)
    {
        var hash = TryHashFile(dllPath);
        if (string.Equals(hash, KnownDllSha256, StringComparison.OrdinalIgnoreCase))
        {
            Logger.Info($"Interception 库指纹校验通过（官方 v1.0.1，SHA256 {hash!.Substring(0, 16)}…）。");
            return;
        }
        var signer = TryGetSigner(dllPath);
        if (signer is not null && signer.IndexOf("Francisco Lopes", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            Logger.Warn($"Interception 库指纹与已知 v1.0.1 不一致（{(hash is null ? "未知" : hash.Substring(0, 16))}…），"
                        + $"但签名者为 {signer}，按官方后续版本继续。");
            return;
        }
        throw new NoRetryException(
            $"官方包内 interception.dll 校验未通过（SHA256 {(hash is null ? "读取失败" : hash.Substring(0, 16))}…，签名者 {signer ?? "无"}），"
            + "已拒绝安装。若为手动放置的文件，请从官方 Release 重新获取。");
    }

    /// <summary>读取 PE 的 Authenticode 签名者（无签名 / 读不到返回 null）。</summary>
    private static string? TryGetSigner(string path)
    {
        try
        {
            using var cert = X509Certificate.CreateFromSignedFile(path);
            return cert.Subject;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>执行官方安装器（/install 或 /uninstall）。安装器是控制台程序且无交互提示。</summary>
    private static (int ExitCode, string Output) RunInstaller(string installerPath, string argument)
    {
        var info = new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = argument,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            // 不重定向 stderr：官方安装器输出极小，而两个管道同时重定向再顺序 ReadToEnd
            // 在极端情况下会互相等满管道缓冲（死锁）。stderr 丢给无效句柄即可。
            RedirectStandardError = false,
            WorkingDirectory = Path.GetDirectoryName(installerPath) ?? Environment.CurrentDirectory
        };
        using var process = Process.Start(info);
        if (process is null) throw new Exception("无法启动官方安装器");
        var stdout = process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(90_000))
        {
            try { process.Kill(); } catch { /* 杀掉失败也不影响后续判定 */ }
            throw new Exception("官方安装器执行超时（90 秒）");
        }
        return (process.ExitCode, stdout);
    }

    /// <summary>卸载 Interception 驱动（需重启后彻底移除）。</summary>
    public static bool Uninstall()
    {
        var staged = Path.Combine(StagingDir, InstallerFileName);
        var installer = File.Exists(staged) ? staged : File.Exists(BundledInstaller) ? BundledInstaller : null;
        if (installer is null)
        {
            LastError = "未找到官方安装器（install-interception.exe），无法卸载。";
            return false;
        }
        try
        {
            var (code, output) = RunInstaller(installer, "/uninstall");
            var removed = !DriverInstalled;
            Logger.Info($"Interception 卸载完成（exit={code}，注册项已清除={removed}）{Shorten(output)}");
            if (!removed) LastError = $"卸载未完成（exit={code}）{Shorten(output)}";
            return removed;
        }
        catch (Exception ex)
        {
            LastError = $"卸载失败：{ex.Message}";
            Logger.Error(LastError);
            return false;
        }
    }

    private static string Shorten(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return "";
        return trimmed.Length <= 200 ? $"：{trimmed}" : $"：{trimmed.Substring(0, 200)}…";
    }

    /// <summary>查询官方最新 Release 的 zip 资产（API 不可达 / 限流返回 null，走固定直链）。</summary>
    private static string? ResolveLatestZipUrl()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var response = Http.GetAsync(LatestApiUrl, cts.Token).ConfigureAwait(false).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode) return null;
            var json = response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
                if (url.Length > 0 && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) return url;
            }
            return null;
        }
        catch (Exception ex)
        {
            Logger.Info($"Interception 最新版本查询失败（改用内置直链）：{ex.Message}");
            return null;
        }
    }

    // ---------- 文件 / 注册表辅助 ----------

    /// <summary>候选 x64 库路径：程序目录 → %APPDATA% drivers → %APPDATA% drivers\interception。</summary>
    private static IEnumerable<string> DllCandidates
    {
        get
        {
            yield return Path.Combine(BundledDir, DllFileName);   // 随包（发布包内置，首选）
            yield return Path.Combine(AppContext.BaseDirectory, DllFileName);   // 手动放到程序目录
            yield return Path.Combine(DriversDir, DllFileName);
            yield return Path.Combine(StagingDir, DllFileName);
        }
    }

    /// <summary>探测可加载的 x64 库（只读 PE 头，不加载模块）。</summary>
    private static string? ProbeDll(out string? wrongArch)
    {
        wrongArch = null;
        foreach (var path in DllCandidates)
        {
            if (!File.Exists(path)) continue;
            if (ReadPeMachine(path) == MachineAmd64) return path;
            wrongArch ??= path;
        }
        return null;
    }

    private const ushort MachineAmd64 = 0x8664;

    private static ushort? ReadPeMachine(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            using var br = new BinaryReader(fs);
            if (br.ReadUInt16() != 0x5A4D) return null;
            fs.Position = 0x3C;
            var peOffset = br.ReadInt32();
            if (peOffset <= 0 || peOffset > fs.Length - 6) return null;
            fs.Position = peOffset;
            if (br.ReadUInt32() != 0x00004550) return null;
            return br.ReadUInt16();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>键盘类 UpperFilters（REG_MULTI_SZ，读不到返回空数组）。</summary>
    private static string[] ReadKeyboardUpperFilters()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(KeyboardClassKey);
            return key?.GetValue("UpperFilters") as string[] ?? Array.Empty<string>();
        }
        catch (Exception ex)
        {
            Logger.Warn($"读取键盘类 UpperFilters 失败：{ex.Message}");
            return Array.Empty<string>();
        }
    }

    private static string? TryHashFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            var sb = new StringBuilder(64);
            foreach (var b in sha.ComputeHash(stream)) sb.Append(b.ToString("X2"));
            return sb.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static T? GetProc<T>(string name) where T : class, Delegate
    {
        var p = GetProcAddress(_dllHandle, name);
        return p == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
    }

    /// <summary>重试无意义的硬性失败（包装不完整 / 校验不过），重试循环收到后终止。</summary>
    private sealed class NoRetryException : Exception
    {
        public NoRetryException(string message) : base(message) { }
    }

    /// <summary>随包目录（exe 同级的 interception\）：官方 dll + 官方安装器 + LGPL 许可文本。</summary>
    private static string BundledDir => Path.Combine(AppContext.BaseDirectory, "interception");

    /// <summary>随包的官方 x64 库。</summary>
    private static string BundledDll => Path.Combine(BundledDir, DllFileName);

    /// <summary>随包的官方命令行安装器（安装 / 卸载的唯一入口）。</summary>
    private static string BundledInstaller => Path.Combine(BundledDir, InstallerFileName);

    /// <summary>随包文件是否齐备（齐备则不联网）。</summary>
    private static bool HasBundled => File.Exists(BundledDll) && File.Exists(BundledInstaller);

    /// <summary>%APPDATA%\TheCelestialDiviner\drivers（与 DD 共用根目录）。</summary>
    private static string DriversDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Constants.AppFolderName, "drivers");

    /// <summary>Interception 专用暂存目录（库 + 官方安装器 + 解包临时包）。</summary>
    private static string StagingDir => Path.Combine(DriversDir, "interception");
}
