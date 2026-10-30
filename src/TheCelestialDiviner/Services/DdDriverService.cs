using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using TheCelestialDiviner.Helpers;

namespace TheCelestialDiviner.Services;

/// <summary>
/// DD 虚拟驱动（ddxoft DD 虚拟鼠标键盘驱动）键盘注入服务。
/// 通过 DD_key 注入的事件不携带 LLKHF_INJECTED 标记，对游戏表现为"物理键盘"，
/// 可绕过基于注入标记的输入过滤（SendInput / PostMessage 模式失效时的备选）。
/// 同理本程序的低级钩子也无法凭事件本身区分回环与物理输入，
/// 由 <see cref="EchoGuard"/> 按注入时间线消除（SendInput 模式用 dwExtraInfo 魔数）。
///
/// 加载策略：优先 <c>dd63330.dll</c>（官方 2026 x64 用户态版），其次兼容万象等第三方
/// 部署的 <c>DD64.dll</c>（32 位时间锁版，x64 进程无法加载，仅探测并给出提示）。
/// 探测顺序：exe 目录 → %APPDATA%\TheCelestialDiviner\drivers → Change box 安装目录。
/// 依赖文件随发布包提供（原生 DLL 无法内嵌单文件）。
///
/// 自动获取：驱动缺失时按需从 DD 官方发布渠道（github.com/ddxoft，作者自己的发布页）
/// 下载官方压缩包，用随包的 7zr.exe（7-Zip 独立版，LGPL）解出 dd63330.dll 安装到
/// %APPDATA% drivers 目录——本程序只做"下载器"，不二次分发闭源驱动；
/// 全程后台执行，期间注入自动回退普通模式，就绪后自动升回 DD 模式（见 AutoFetchCompleted）。
/// </summary>
public static class DdDriverService
{
    // ---------- P/Invoke（GetProcAddress 动态绑定，兼容 stdcall/cdecl 差异） ----------

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int DdBtnFunc(int mask);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int DdKeyFunc(int ddCode, int flag);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int DdTodcFunc(int vk);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibraryW(string lpFileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetDllDirectoryW(string lpPathName);

    // 内核服务预置（绕开 DD 自身间歇性安装失败，见 EnsureKernelService）。
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenSCManagerW(string? lpMachineName, string? lpDatabaseName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenServiceW(IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateServiceW(IntPtr hSCManager, string lpServiceName, string lpDisplayName,
        uint dwDesiredAccess, uint dwServiceType, uint dwStartType, uint dwErrorControl, string lpBinaryPathName,
        string? lpLoadOrderGroup, IntPtr lpTagId, string? lpDependencies, string? lpServiceStartName, string? lpPassword);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool StartServiceW(IntPtr hService, int dwNumServiceArgs, IntPtr lpServiceArgVectors);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool QueryServiceStatus(IntPtr hService, out SERVICE_STATUS lpServiceStatus);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool ChangeServiceConfigW(IntPtr hService, uint dwServiceType, uint dwStartType,
        uint dwErrorControl, string? lpBinaryPathName, string? lpLoadOrderGroup, IntPtr lpTagId,
        string? lpDependencies, string? lpServiceStartName, string? lpPassword, string? lpDisplayName);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DeleteService(IntPtr hService);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr hSCObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS
    {
        public int dwServiceType;
        public int dwCurrentState;
        public int dwControlsAccepted;
        public int dwWin32ExitCode;
        public int dwServiceSpecificExitCode;
        public int dwCheckPoint;
        public int dwWaitHint;
    }

    private const uint SC_MANAGER_ALL_ACCESS = 0xF003F;
    private const uint SERVICE_ALL_ACCESS = 0xF01FF;
    private const uint SERVICE_KERNEL_DRIVER = 1;
    private const uint SERVICE_DEMAND_START = 3;
    private const uint SERVICE_ERROR_NORMAL = 1;
    private const int SERVICE_RUNNING = 0x4;
    private const int ERROR_SERVICE_ALREADY_RUNNING = 1056;
    private const int ERROR_SERVICE_DISABLED = 1058;
    private const int ERROR_SERVICE_MARKED_FOR_DELETE = 1072;
    private const uint SERVICE_NO_CHANGE = 0xFFFFFFFF;

    /// <summary>内核驱动服务名（DD 官方固定，且为全局单例）。</summary>
    private const string DriverServiceName = "dd63330";

    /// <summary>
    /// DD_btn(0) 单进程最大调用次数。它做的是"重建虚拟设备"的重操作，失败后反复重入
    /// 有把驱动内部状态搞坏的风险（0x50 的典型诱因）。曾经 5 次递增退避，实测的瞬时失败
    /// （免费版在线认证抖动、上一个会话被强杀后的 PnP 重建）在等一次之后基本都能过，
    /// 故收敛为 2 次。要严格只试一次，把这个常量改成 1 即可。
    /// </summary>
    private const int DdInitMaxAttempts = 2;

    /// <summary>两次 DD_btn(0) 之间的等待（覆盖虚拟设备 PnP 重建的常见耗时）。</summary>
    private const int DdInitRetryDelayMs = 900;

    /// <summary>驱动身份不一致时的统一提示（UI 注释区 / LastError 展示）。</summary>
    private const string IdentityRejectedMessage =
        "系统中已加载的 dd63330 驱动与程序自带的不是同一份（常见于换过安装目录、或装过旧版本 / 其他软件）。"
        + "为避免版本错位导致蓝屏，本次运行不启用 DD 驱动：请重启电脑后重试，"
        + "重启后残留的服务项会按当前版本重建。";

    // ---------- 状态 ----------

    private static readonly object Gate = new();
    private static IntPtr _dllHandle;
    private static DdBtnFunc? _ddBtn;
    private static DdKeyFunc? _ddKey;
    private static DdTodcFunc? _ddTodc;
    private static string? _loadedPath;

    /// <summary>已加载的 DLL 路径（未加载为 null）。</summary>
    public static string? LoadedPath { get { lock (Gate) return _loadedPath; } }

    /// <summary>DD 驱动是否已加载并初始化成功。</summary>
    public static bool IsReady { get { lock (Gate) return _ddKey is not null && _initialized; } }

    /// <summary>
    /// 可加载的 x64 驱动文件（dd63330.dll）是否在位 —— 只读文件头、不加载 DLL、不初始化虚拟设备，
    /// 供 UI 判断「键盘注入 → DD 驱动」是否可选（下载没成功 / 位数不对 = 灰显不可选）。
    /// 驱动在位但初始化失败（如免费版在线认证未过）仍算可用：点选时由 <see cref="EnsureReady"/>
    /// 给出具体原因并回退普通模式，是用户可感知、可重试的路径。
    /// Change box 的 DD64.dll 是 32 位时间锁版（x64 进程必加载失败），不计入可用。
    /// </summary>
    public static bool IsAvailable => ProbeDll(out _) is not null;

    /// <summary>候选 x64 驱动文件路径：exe 目录 → %APPDATA% drivers → Change box 安装目录。</summary>
    private static IEnumerable<string> X64DllCandidates
    {
        get
        {
            yield return Path.Combine(AppContext.BaseDirectory, "dd63330.dll");
            yield return Path.Combine(AppDataDriversDir, "dd63330.dll");
            yield return Path.Combine(ChangeBoxDllDir, "dd63330.dll");
        }
    }

    /// <summary>Change box（第三方 DD 部署）的 DLL 目录；32 位系统上为空目录，探测自然落空。</summary>
    private static string ChangeBoxDllDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Change Box", "DLL");

    private static bool _initialized;

    /// <summary>
    /// 本次进程已因"驱动身份不一致"主动放弃 DD。同会话内不再重试：
    /// 服务项虽已标记删除，内核里跑的仍是旧驱动，再调 DD_btn 就是拿新 DLL 去驱动旧 sys。
    /// </summary>
    private static bool _driverIdentityRejected;

    /// <summary>最近一次初始化失败的原因（供 UI 日志展示）。</summary>
    public static string? LastError { get; private set; }

    /// <summary>DD 键码缓存：VK → DD code（-1 表示不支持）。DD_todc 查询一次后缓存。</summary>
    private static readonly Dictionary<int, int> VkCodeCache = new();

    // ---------- 初始化 / 释放 ----------

    /// <summary>
    /// 诊断开关：不加载 DD 驱动（由命令行 `--no-dd` 置位）。用于判断"拖动发涩"是否由
    /// DD 内核驱动（虚拟设备常驻）引入。置位后键盘注入自动走普通模式，仅供诊断。
    /// </summary>
    public static bool SuppressDriver { get; set; }

    /// <summary>
    /// 加载 DD DLL 并初始化虚拟驱动（DD_btn(0)）。重复调用安全：已就绪时直接返回 true。
    /// 需要管理员权限；首次调用可能触发系统安装虚拟设备（自动完成，无需交互）。
    /// </summary>
    public static bool EnsureReady()
    {
        if (SuppressDriver)
        {
            LastError = "诊断模式：已通过 --no-dd 禁用 DD 驱动";
            return false;
        }

        // 崩溃熔断的"正在使用 DD"标记，必须写在初始化之前：万一初始化本身把机器蓝屏了，
        // 下次启动才看得出"上次是带着 DD 挂掉的"（见 DdCrashGuard）。
        DdCrashGuard.MarkSessionInUse();

        lock (Gate)
        {
            if (_ddKey is not null && _initialized) return true;

            if (_driverIdentityRejected)
            {
                LastError = IdentityRejectedMessage;
                return false;
            }

            LastError = null;

            var path = ProbeDll(out var wrongArch);
            if (path is null)
            {
                LastError = wrongArch is null
                    ? "未找到 dd63330.dll（已查找：程序目录、%APPDATA%\\TheCelestialDiviner\\drivers、Change Box\\DLL）"
                    : $"{Path.GetFileName(wrongArch)} 是 32 位（x86）DLL，本程序是 64 位进程，无法加载。"
                      + "请换用 64 位（x64）版本的 dd63330.dll（官方 2026 版约 3.7 MB）";
                return false;
            }

            if (_dllHandle == IntPtr.Zero)
            {
                // DD DLL 无静态第三方依赖，但仍设置搜索目录以稳妥处理运行库代理。
                SetDllDirectoryW(Path.GetDirectoryName(path)!);
                _dllHandle = LoadLibraryW(path);
                if (_dllHandle == IntPtr.Zero)
                {
                    var err = Marshal.GetLastWin32Error();
                    LastError = err == ErrorBadExeFormat
                        ? $"加载 {Path.GetFileName(path)} 失败：位数不匹配（32 位 DLL 不能用于 64 位进程），请换用 x64 版"
                        : $"加载 {Path.GetFileName(path)} 失败（err={err}）";
                    return false;
                }
                _loadedPath = path;
                Logger.Info($"已加载 DD 驱动库：{path}");
            }

            _ddBtn = GetProc<DdBtnFunc>("DD_btn");
            _ddKey = GetProc<DdKeyFunc>("DD_key");
            _ddTodc = GetProc<DdTodcFunc>("DD_todc");
            if (_ddBtn is null || _ddKey is null)
            {
                LastError = "DLL 缺少 DD_btn / DD_key 导出（不是 DD 虚拟驱动库）";
                FreeAll();
                return false;
            }

            // DD_btn(0)：初始化虚拟设备。返回 1 = 成功；0/-1 = 失败。
            // 实测存在瞬时失败：免费版加载时需在线认证（网络抖动），且前一个
            // 会话被强制结束后虚拟设备 PnP 重建期间（数秒）调用会返回 -1。
            // 重试上限见 DdInitMaxAttempts：不再 5 次递增退避，避免反复重入把驱动状态搞坏。
            var ret = 0;
            var seh = false;
            if (!EnsureKernelService())
            {
                // 服务里跑着来路不明的旧驱动：这次连 DD_btn 都不能调，直接把 DLL 卸掉。
                FreeAll();
                return false;
            }
            Logger.Info("正在初始化 DD 虚拟设备（DD_btn(0)）...");
            for (var attempt = 1; attempt <= DdInitMaxAttempts; attempt++)
            {
                ret = SafeCall(_ddBtn, 0, out seh);
                if (!seh && ret == 1) break;
                if (attempt < DdInitMaxAttempts)
                {
                    Logger.Warn($"DD 虚拟设备初始化第 {attempt}/{DdInitMaxAttempts} 次失败（ret={ret}），{DdInitRetryDelayMs}ms 后重试。");
                    Thread.Sleep(DdInitRetryDelayMs);
                }
            }
            if (seh || ret != 1)
            {
                LastError = $"DD 虚拟驱动初始化失败（ret={ret}）。" +
                            "常见原因：DD 免费版在线认证未通过（需联网）、虚拟设备正被其他进程使用或重建中；请稍后重试。";
                Logger.Error(LastError);
                _ddTodc = null;
                _ddKey = null;
                _ddBtn = null;
                FreeAll();
                return false;
            }

            _initialized = true;
            VkCodeCache.Clear();
            EchoGuard.Reset();
            Logger.Info($"DD 虚拟驱动初始化成功（{Path.GetFileName(_loadedPath)}）。");
            KeepKernelDriverSys();
            return true;
        }
    }

    /// <summary>
    /// 预置 dd63330 内核驱动服务（DD_btn 之前调用）。
    /// DD 的安装例程是"释放 sys 到 %TEMP% → CreateServiceW → StartService"，
    /// 受安全软件拦截（释放文件被查杀）或旧服务"标记删除"未完成时
    /// 会间歇性失败，并弹出"驱动安装错误"模态框阻塞调用线程。
    /// 本方法在 DD_btn 之前确保服务就绪，并先校验"服务登记的映像 == 我们要用的那一份"：
    /// 换过安装目录、装过旧版本、或用过其他软件留下的 dd63330 服务时两者会不一致——
    /// 拿新 DLL 去驱动旧 sys 是 0x50（PAGE_FAULT_IN_NONPAGED_AREA）最典型的成因。
    /// </summary>
    /// <returns>
    /// false = 检测到"正在运行的驱动与本程序自带的不是同一份"，已主动放弃本次初始化，
    /// 调用方不得再调 DD_btn；true = 服务已就绪（或已交由 DD 自行安装，属尽力而为）。
    /// </returns>
    private static bool EnsureKernelService()
    {
        try
        {
            var sys = ResolveDriverSys(out var conflict);
            if (conflict is not null) Logger.Warn(conflict);

            var scm = OpenSCManagerW(null, null, SC_MANAGER_ALL_ACCESS);
            if (scm == IntPtr.Zero)
            {
                Logger.Warn($"预置 DD 内核服务：打开 SCM 失败（err={Marshal.GetLastWin32Error()}），交由 DD 自行安装。");
                return true;
            }
            try
            {
                var service = OpenServiceW(scm, DriverServiceName, SERVICE_ALL_ACCESS);

                // ---- 身份校验：服务登记的映像必须就是我们要用的那一份 ----
                if (service != IntPtr.Zero && sys is not null)
                {
                    var registered = ReadServiceImagePath(DriverServiceName);
                    var running = QueryServiceStatus(service, out var st) && st.dwCurrentState == SERVICE_RUNNING;
                    if (registered is not null && !PathsEqual(registered, sys.Path))
                    {
                        // 路径不同不等于版本不同：DD 自释放到 %TEMP% 再被收编就是"同字节、异路径"。
                        // 必须先比指纹，否则会把正常用户误判成"来路不明"。
                        var registeredHash = File.Exists(registered) ? TryHashFile(registered) : null;
                        var sameContent = registeredHash is not null
                                          && string.Equals(registeredHash, sys.Hash, StringComparison.OrdinalIgnoreCase);

                        if (sameContent && !running)
                        {
                            // 同一份字节、只是路径不同：把服务改指固定副本，下次启动不再依赖 %TEMP%。
                            if (ChangeServiceConfigW(service, SERVICE_NO_CHANGE, SERVICE_NO_CHANGE, SERVICE_NO_CHANGE,
                                    sys.Path, null, IntPtr.Zero, null, null, null, null))
                            {
                                Logger.Info($"预置 DD 内核服务：登记映像已从 {registered} 改指固定副本。");
                            }
                        }
                        else if (sameContent)
                        {
                            Logger.Info($"预置 DD 内核服务：驱动来自 {registered}，与当前版本字节一致。");
                        }
                        else if (running)
                        {
                            // 内核里跑着一份来路不明（或已过时）的驱动：这次绝不碰它，也不能调 DD_btn。
                            // DeleteService 只是"标记删除"，驱动会继续运行到下次重启；
                            // 重启后服务项按当前版本重建（本进程内不再重试，见 _driverIdentityRejected）。
                            Logger.Error($"DD 驱动身份不一致：服务指向 {registered}，当前版本 {sys.Path}；已放弃本次 DD 初始化。");
                            LastError = IdentityRejectedMessage;
                            _driverIdentityRejected = true;
                            DeleteService(service);
                            CloseServiceHandle(service);
                            return false;
                        }
                        else
                        {
                            Logger.Warn($"DD 服务指向 {registered}（与当前版本不一致），驱动未运行：删除后按当前版本重建。");
                            DeleteService(service);
                            CloseServiceHandle(service);
                            service = IntPtr.Zero;
                        }
                    }
                }

                if (service == IntPtr.Zero)
                {
                    if (sys is null)
                    {
                        Logger.Info("预置 DD 内核服务：服务不存在且无自带 dd63330.sys，交由 DD 自行安装。");
                        return true;
                    }
                    var err = 0;
                    // 旧服务可能处于"标记删除"未完成状态（1072），稍等重试。
                    for (var attempt = 1; ; attempt++)
                    {
                        service = CreateServiceW(scm, DriverServiceName, DriverServiceName, SERVICE_ALL_ACCESS,
                            SERVICE_KERNEL_DRIVER, SERVICE_DEMAND_START, SERVICE_ERROR_NORMAL,
                            sys.Path, null, IntPtr.Zero, null, null, null);
                        if (service != IntPtr.Zero) break;
                        err = Marshal.GetLastWin32Error();
                        if (err != ERROR_SERVICE_MARKED_FOR_DELETE || attempt >= 5)
                        {
                            Logger.Warn($"预置 DD 内核服务：CreateService 失败（err={err}），交由 DD 自行安装。");
                            return true;
                        }
                        Thread.Sleep(600);
                    }
                    Logger.Info($"预置 DD 内核服务：已按当前版本创建（{sys.Path}，SHA256 {sys.Hash.Substring(0, 16)}…）。");
                }

                if (QueryServiceStatus(service, out var status) && status.dwCurrentState == SERVICE_RUNNING)
                {
                    Logger.Info("预置 DD 内核服务：dd63330 驱动已在运行。");
                    return true;
                }
                if (!StartServiceW(service, 0, IntPtr.Zero))
                {
                    var err = Marshal.GetLastWin32Error();
                    if (err == ERROR_SERVICE_ALREADY_RUNNING) return true;

                    // 服务被安全软件禁用（1058）：改回按需启动后重试；仍失败则删除重建。
                    if (err == ERROR_SERVICE_DISABLED && sys is not null)
                    {
                        Logger.Warn("预置 DD 内核服务：服务被禁用（err=1058），尝试恢复启动类型...");
                        if (ChangeServiceConfigW(service, SERVICE_NO_CHANGE, SERVICE_DEMAND_START,
                                SERVICE_NO_CHANGE, null, null, IntPtr.Zero, null, null, null, null))
                        {
                            if (StartServiceW(service, 0, IntPtr.Zero))
                            {
                                Logger.Info("预置 DD 内核服务：已恢复被禁用的服务并启动。");
                                return true;
                            }
                            err = Marshal.GetLastWin32Error();
                        }
                        else
                        {
                            err = Marshal.GetLastWin32Error();
                        }

                        // 仍失败：删除旧服务，用同一份 sys 重建（摆脱被标记/损坏的服务项）。
                        Logger.Warn($"预置 DD 内核服务：恢复失败（err={err}），删除并重建服务...");
                        DeleteService(service);
                        CloseServiceHandle(service);
                        service = CreateServiceW(scm, DriverServiceName, DriverServiceName, SERVICE_ALL_ACCESS,
                            SERVICE_KERNEL_DRIVER, SERVICE_DEMAND_START, SERVICE_ERROR_NORMAL,
                            sys.Path, null, IntPtr.Zero, null, null, null);
                        if (service != IntPtr.Zero && StartServiceW(service, 0, IntPtr.Zero))
                        {
                            Logger.Info("预置 DD 内核服务：已重建并启动 dd63330 驱动。");
                            return true;
                        }
                        err = Marshal.GetLastWin32Error();
                        Logger.Warn($"预置 DD 内核服务：重建启动失败（err={err}），交由 DD 自行处理。");
                        if (service != IntPtr.Zero) CloseServiceHandle(service);
                        return true;
                    }

                    Logger.Warn($"预置 DD 内核服务：StartService 失败（err={err}），交由 DD 自行处理。");
                    return true;
                }
                Logger.Info("预置 DD 内核服务：dd63330 驱动已启动。");
                return true;
            }
            finally
            {
                CloseServiceHandle(scm);
            }
        }
        catch (Exception ex)
        {
            // 预置属于尽力而为：任何异常都不阻断 DD 自身的安装路径。
            Logger.Warn($"预置 DD 内核服务异常（交由 DD 自行安装）：{ex.Message}");
            return true;
        }
    }

    /// <summary>随包内核驱动文件（路径 + SHA-256），作为"服务登记的映像是否就是我们这一份"的判据。</summary>
    private sealed record DriverSysFile(string Path, string Hash);

    /// <summary>
    /// 定位并指纹化内核驱动文件：优先"实际加载的 DLL 同目录"（官方按包发布，同目录即同一包，
    /// 配对最可靠）→ 程序目录 → %APPDATA% drivers。
    /// 不再把 %TEMP% 当候选：那是 DD 自己的释放位置，也可能躺着旧版本或别的软件留下的同名文件，
    /// 用它建服务等于给来路不明的驱动发身份证。
    /// </summary>
    /// <param name="conflict">多份文件内容不一致时的说明（仅记日志，不改变选择）。</param>
    private static DriverSysFile? ResolveDriverSys(out string? conflict)
    {
        conflict = null;
        var candidates = new List<string>();
        if (_loadedPath is not null)
        {
            var dir = Path.GetDirectoryName(_loadedPath);
            if (!string.IsNullOrEmpty(dir)) candidates.Add(Path.Combine(dir, "dd63330.sys"));
        }
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "dd63330.sys"));
        candidates.Add(Path.Combine(AppDataDriversDir, "dd63330.sys"));

        DriverSysFile? first = null;
        var hashed = new List<DriverSysFile>();
        foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path)) continue;
            var hash = TryHashFile(path);
            if (hash is null) continue;
            var item = new DriverSysFile(path, hash);
            hashed.Add(item);
            first ??= item;
        }

        if (hashed.Count > 1)
        {
            var kinds = hashed.Select(x => x.Hash).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if (kinds > 1)
            {
                conflict = "找到多份内容不同的 dd63330.sys（"
                    + string.Join("、", hashed.Select(x => $"{x.Path} = {x.Hash.Substring(0, 16)}…"))
                    + "），已按与 DLL 同目录的那份为准。";
            }
        }
        return first;
    }

    /// <summary>计算文件 SHA-256（大写十六进制）；被占用 / 无权限时返回 null。</summary>
    private static string? TryHashFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(stream);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) sb.Append(b.ToString("X2"));
            return sb.ToString();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>读取服务登记的映像路径（HKLM\SYSTEM\CurrentControlSet\Services\&lt;name&gt;\ImagePath）；读不到返回 null。</summary>
    private static string? ReadServiceImagePath(string serviceName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
            return NormalizeServiceImagePath(key?.GetValue("ImagePath") as string);
        }
        catch (Exception ex)
        {
            Logger.Warn($"读取 DD 服务 ImagePath 失败（跳过身份校验）：{ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 把服务 ImagePath 规范成可直接比较的文件全路径：去引号、去命令行参数，
    /// 展开 \??\ 与 \SystemRoot\ 前缀（CreateService 写入的是 NT 路径形式）。
    /// </summary>
    private static string? NormalizeServiceImagePath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var p = raw!.Trim();

        if (p.StartsWith("\"", StringComparison.Ordinal))
        {
            var end = p.IndexOf('"', 1);
            p = end > 1 ? p.Substring(1, end - 1) : p.Trim('"');
        }
        else
        {
            // 未加引号但带参数（…\dd63330.sys /foo）：截到 .sys 为止
            var idx = p.IndexOf(".sys", StringComparison.OrdinalIgnoreCase);
            if (idx > 0) p = p.Substring(0, idx + 4);
        }

        if (p.StartsWith(@"\??\", StringComparison.Ordinal)) p = p.Substring(4);
        if (p.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
        {
            p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                             p.Substring(@"\SystemRoot\".Length));
        }
        else if (p.StartsWith(@"system32\", StringComparison.OrdinalIgnoreCase))
        {
            p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), p);
        }

        try { p = Environment.ExpandEnvironmentVariables(p); } catch { /* 非法变量名忽略 */ }
        try { return Path.GetFullPath(p); } catch { return p; }
    }

    /// <summary>两个路径是否指向同一位置（忽略大小写与 . / .. 段差异）。</summary>
    private static bool PathsEqual(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        try { a = Path.GetFullPath(a!); } catch { }
        try { b = Path.GetFullPath(b!); } catch { }
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// DD 初始化成功时会把内嵌的内核驱动释放到 %TEMP%。这里做两件事：
    /// 1）把它与随包副本比对指纹，不一致说明本次加载的驱动不是程序期望的那份（蓝屏排查的关键线索）；
    /// 2）仅当它是"刚被释放"的、且 %APPDATA% 还没有副本时，才收编为固定副本（后续预置服务用）。
    /// 旧文件可能是别的软件很久以前留下的，收编它等于给来路不明的驱动发身份证。
    /// </summary>
    private static void KeepKernelDriverSys()
    {
        try
        {
            var temp = Path.Combine(Path.GetTempPath(), "dd63330.sys");
            if (!File.Exists(temp)) return;

            var tempHash = TryHashFile(temp);
            var target = Path.Combine(AppDataDriversDir, "dd63330.sys");

            if (File.Exists(target))
            {
                var targetHash = TryHashFile(target);
                if (tempHash is not null && targetHash is not null
                    && !string.Equals(tempHash, targetHash, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Warn($"%TEMP% 的 DD 驱动（{tempHash.Substring(0, 16)}…）与随包副本（{targetHash.Substring(0, 16)}…）不一致："
                                + "本次加载的驱动可能不是程序期望的那一份，请核对驱动来源。");
                }
                return;
            }

            var age = DateTime.Now - File.GetLastWriteTime(temp);
            if (age > TimeSpan.FromMinutes(15))
            {
                Logger.Warn($"%TEMP% 的 dd63330.sys 是 {age.TotalHours:0.#} 小时前的旧文件，未收编（可能来自其他软件）。");
                return;
            }

            Directory.CreateDirectory(AppDataDriversDir);
            File.Copy(temp, target, overwrite: true);
            Logger.Info($"已收编 DD 自释放的内核驱动到 %APPDATA%\\TheCelestialDiviner\\drivers"
                        + $"（SHA256 {tempHash?.Substring(0, 16)}…，预置服务用）。");
        }
        catch (Exception ex)
        {
            Logger.Warn($"备份 DD 内核驱动失败（不影响使用）：{ex.Message}");
        }
    }

    /// <summary>释放 DD 资源（退出时序调用；模式切换时保留已加载 DLL 以便快速回切）。</summary>
    public static void Shutdown()
    {
        lock (Gate)
        {
            _initialized = false;
            // 不 FreeLibrary：进程退出时随进程回收，避免驱动会话中断的时序问题。
        }
        // 走到这里说明是正常退出：撤掉崩溃熔断标记（蓝屏 / 强杀时这行不会被执行，
        // 标记留在磁盘上，下次启动即可判定"上次没干净收场"）。
        DdCrashGuard.MarkCleanExit();
    }

    private static void FreeAll()
    {
        _ddTodc = null;
        _ddKey = null;
        _ddBtn = null;
        if (_dllHandle != IntPtr.Zero) { FreeLibrary(_dllHandle); _dllHandle = IntPtr.Zero; }
        _loadedPath = null;
        _initialized = false;
    }

    // ---------- 驱动自动获取（官方发布包 → 解包 → 安装到 %APPDATA% drivers） ----------

    private const string DdLatestApiUrl =
        "https://api.github.com/repos/ddxoft/master/releases/latest";

    /// <summary>API 不可达 / 配额耗尽时的兜底直链（DD 官方 2026 x64 免费版发布资产）。</summary>
    private const string DdFallbackAssetUrl =
        "https://github.com/ddxoft/master/releases/download/2026.DD.EV.HVCI.63xxx/2026.DD.EV.HVCI.63xxx.7z";

    private static readonly HttpClient AutoFetchHttp = CreateAutoFetchClient();

    private static HttpClient CreateAutoFetchClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        // GitHub API 强制要求 User-Agent，缺失直接 403。
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TheCelestialDiviner");
        return client;
    }

    private static int _autoFetchRunning;   // Interlocked 单飞标志（0 空闲 / 1 进行中）
    private static int _lastDdLatencyLogTick;   // DD_key 耗时告警限频（Environment.TickCount）

    /// <summary>驱动自动获取是否正在进行（供 UI / VM 判断，无需精确）。</summary>
    public static bool IsAutoFetchRunning => Volatile.Read(ref _autoFetchRunning) == 1;

    /// <summary>
    /// 驱动自动获取完成并已安装到 %APPDATA% drivers（线程池线程触发，无订阅者安全）。
    /// VM 订阅后在 UI 线程重试 EnsureReady 并自动升回 DD 模式。
    /// </summary>
    public static event Action? AutoFetchCompleted;

    /// <summary>自动获取阶段（供 UI 区分"进行中"与"已结束"，结束态才需要定时清除提示）。</summary>
    public enum DdFetchStage
    {
        /// <summary>下载中（Message 内含百分比 / 已下载字节）。</summary>
        Downloading,
        /// <summary>下载完成，正在解包 / 安装。</summary>
        Installing,
        /// <summary>某次尝试失败，等待自动重试（Message 内含原因与等待秒数）。</summary>
        Retrying,
        /// <summary>安装成功。</summary>
        Succeeded,
        /// <summary>硬性失败（重试无意义），已终止。</summary>
        Failed
    }

    /// <summary>一次进度快照（后台线程触发）。</summary>
    public readonly record struct DdFetchProgress(DdFetchStage Stage, int Attempt, string Message);

    /// <summary>
    /// 自动获取进度（后台线程触发，无订阅者安全）：开始 / 下载百分比 / 解包 / 每次重试 / 结果。
    /// 供 UI 展示"正在下载、第几次失败、还要等多久"，避免用户在长达数分钟的重试期里
    /// 完全看不出程序在干什么。UI 侧必须自行封送线程。
    /// </summary>
    public static event Action<DdFetchProgress>? AutoFetchProgress;

    /// <summary>触发进度（无订阅者时为空操作）。</summary>
    private static void ReportProgress(DdFetchStage stage, int attempt, string message)
    {
        try { AutoFetchProgress?.Invoke(new DdFetchProgress(stage, attempt, message)); }
        catch (Exception ex) { Logger.Warn($"DD 驱动进度回调异常（忽略）：{ex.Message}"); }
    }

    /// <summary>
    /// 后台单飞启动"自动获取 DD 驱动"：探测 exe / APPDATA 目录无 dd63330.dll 时，
    /// 从 DD 官方发布渠道（github.com/ddxoft，作者自己的发布页，下载器性质不做二次分发）
    /// 下载官方 7z 包，用随包 7zr.exe 解出 dd63330.dll 安装到 %APPDATA% drivers。
    /// 全程不抛异常，进度与结果经 Logger 留痕；完成（无论成败）触发 AutoFetchCompleted。
    /// 重复调用安全（进行中直接忽略）。
    /// </summary>
    public static void StartAutoFetch()
    {
        if (Interlocked.CompareExchange(ref _autoFetchRunning, 1, 0) != 0) return;
        Logger.Info("DD 驱动缺失：开始自动获取（官方发布渠道 github.com/ddxoft）...");
        // 首条状态立刻上报：解析最新 Release 最多要等 10s，否则这段时间里界面毫无动静。
        ReportProgress(DdFetchStage.Downloading, 0, "DD 驱动获取中：正在连接官方发布渠道…");
        _ = Task.Run(() =>
        {
            try
            {
                RunAutoFetch();
            }
            catch (Exception ex)
            {
                Logger.Error("DD 驱动自动获取失败。", ex);
            }
            finally
            {
                Volatile.Write(ref _autoFetchRunning, 0);
                AutoFetchCompleted?.Invoke();
            }
        });
    }

    /// <summary>自动获取主流程（后台线程调用）：用户已确认获取，失败后积极重试直至成功——
    /// DD 官方渠道在 GitHub，国内可达性波动大，重试是常态而非异常。</summary>
    private static void RunAutoFetch()
    {
        // 只认可加载的 x64 版 dd63330.dll：位数不符（用户手动从官网拿到 32 位那份）时
        // 不算"已有驱动"——否则自动获取会直接跳过，用户永远卡在一个加载不了的文件上。
        if (ProbeDll(out var wrongArch) is not null)
        {
            Logger.Info("DD 驱动自动获取：探测到可用的 x64 dd63330.dll，跳过下载。");
            return;
        }
        if (wrongArch is not null)
        {
            Logger.Warn($"DD 驱动自动获取：{wrongArch} 不是 x64 版（无法加载），将由官方 x64 版覆盖安装。");
        }

        var workDir = Path.Combine(AppDataDriversDir, "fetch-" + Environment.TickCount.ToString("x"));
        Directory.CreateDirectory(workDir);
        try
        {
            var attempt = 0;
            while (true)
            {
                attempt++;
                try
                {
                    FetchOnce(workDir, attempt);
                    Logger.Info($"DD 驱动自动获取成功（第 {attempt} 次尝试），已安装到 %APPDATA%\\TheCelestialDiviner\\drivers。");
                    ReportProgress(DdFetchStage.Succeeded, attempt,
                        $"DD 驱动已安装（第 {attempt} 次尝试成功），正在启用 DD 驱动模式…");
                    return;
                }
                catch (NoRetryException ex)
                {
                    // 重试也无济于事的硬性失败（缺解包工具 / 上游内容变化）：终止并留痕，
                    // 用户可稍后在「选项」菜单重试或手动下载。
                    Logger.Error($"DD 驱动自动获取终止：{ex.Message}");
                    ReportProgress(DdFetchStage.Failed, attempt,
                        $"DD 驱动获取失败：{ex.Message}。可在「选项 → 键盘注入 → 获取 DD 驱动…」重试。");
                    throw;
                }
                catch (Exception ex)
                {
                    // 积极重试：间隔 5s 起步线性递增，上限 60s，直至成功。
                    var delaySec = Math.Min(5 * attempt, 60);
                    Logger.Warn($"DD 驱动自动获取第 {attempt} 次尝试失败：{ex.Message}；{delaySec}s 后自动重试（直至成功）。");
                    ReportProgress(DdFetchStage.Retrying, attempt,
                        $"DD 驱动获取第 {attempt} 次尝试失败：{DescribeFetchFailure(ex)}，{delaySec} 秒后自动重试"
                        + "（国内访问 GitHub 不稳定，失败几次、等几分钟都属正常，会一直重试到成功）。");
                    Thread.Sleep(delaySec * 1000);
                }
            }
        }
        finally
        {
            try { Directory.Delete(workDir, true); } catch { /* 临时目录清理失败不影响主流程 */ }
        }
    }

    /// <summary>单次获取尝试：解析官方直链 → 下载 7z → 解包 → 安装到 %APPDATA% drivers。
    /// 抛 <see cref="NoRetryException"/> 表示重试无意义的硬性失败；其余异常由重试循环处理。</summary>
    private static void FetchOnce(string workDir, int attempt)
    {
        // 1. 解析官方资产直链：优先 GitHub API 拿最新 Release，失败退回硬编码直链。
        var assetUrl = ResolveLatestAssetUrl() ?? DdFallbackAssetUrl;

        // 2. 下载官方 7z 包（约 3.7MB，3 分钟超时兜底）。流式读 + 分块报进度：
        //    一次 ReadAsByteArrayAsync 拿不到任何中间状态，用户在整个下载期看不到反馈。
        var archivePath = Path.Combine(workDir, "dd.7z");
        using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3)))
        using (var response = AutoFetchHttp.GetAsync(assetUrl, cts.Token).ConfigureAwait(false).GetAwaiter().GetResult())
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;   // 可能为 null（分块传输）
            ReportProgress(DdFetchStage.Downloading, attempt,
                $"DD 驱动获取中：正在下载官方安装包（{(total is > 0 ? $"{total / 1024.0 / 1024.0:0.0} MB" : "约 3.7 MB")}）…");
            // net48 的 HttpContent.ReadAsStreamAsync 没有 CancellationToken 重载：
            // 取消由下面的分块 ReadAsync(token) 生效（3 分钟总超时兜底）。
            using var stream = response.Content.ReadAsStreamAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            using var file = File.Create(archivePath);
            var buffer = new byte[81920];
            long received = 0;
            var lastTick = Environment.TickCount;
            while (true)
            {
                var read = stream.ReadAsync(buffer, 0, buffer.Length, cts.Token)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                if (read <= 0) break;
                file.Write(buffer, 0, read);
                received += read;
                // 限频（300ms）：3.7MB 在快网络下几百毫秒就完，逐块上报会刷屏。
                if (Environment.TickCount - lastTick < 300) continue;
                lastTick = Environment.TickCount;
                ReportProgress(DdFetchStage.Downloading, attempt, total is > 0
                    ? $"DD 驱动获取中：正在下载 {received * 100 / total}%（{received / 1024.0 / 1024.0:0.0} / {total / 1024.0 / 1024.0:0.0} MB）…"
                    : $"DD 驱动获取中：正在下载（已 {received / 1024.0 / 1024.0:0.0} MB）…");
            }
        }

        // 3. 解包：随包 7zr.exe（7-Zip 独立版，LGPL）。缺失视为安装包不完整（重试无意义）。
        var sevenZip = Path.Combine(AppContext.BaseDirectory, "7zr.exe");
        if (!File.Exists(sevenZip))
            throw new NoRetryException("缺少 7zr.exe 解包工具（请从官方发布包完整安装）");
        ReportProgress(DdFetchStage.Installing, attempt, "DD 驱动获取中：下载完成，正在解包安装…");
        var extractDir = Path.Combine(workDir, "x");
        if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);   // 重试前清理上次残留
        using (var proc = Process.Start(new ProcessStartInfo
        {
            FileName = sevenZip,
            Arguments = $"x \"{archivePath}\" -o\"{extractDir}\" -y",
            UseShellExecute = false,
            CreateNoWindow = true
        }))
        {
            if (proc is null || !proc.WaitForExit(60_000) || proc.ExitCode != 0)
                throw new Exception($"官方包解包失败（exit={(proc?.HasExited == true ? proc.ExitCode : -1)}，可能下载不完整）");
        }

        // 4. 定位 dd63330.dll：官方包内含多个版本变体（1.simple 为免费标准版），优先取 simple。
        var dll = Directory.EnumerateFiles(extractDir, "dd63330.dll", SearchOption.AllDirectories)
            .OrderBy(path => path.IndexOf("simple", StringComparison.OrdinalIgnoreCase) >= 0 ? 0 : 1)
            .ThenBy(path => path.Length)
            .FirstOrDefault();
        if (dll is null)
            throw new NoRetryException("官方包内未找到 dd63330.dll（上游内容可能已变化）");

        // 5. 安装到 %APPDATA% drivers（探测顺序中的既有位置；包内含 sys 时一并安装）。
        Directory.CreateDirectory(AppDataDriversDir);
        File.Copy(dll, Path.Combine(AppDataDriversDir, "dd63330.dll"), overwrite: true);
        var sys = Directory.EnumerateFiles(extractDir, "dd63330.sys", SearchOption.AllDirectories)
            .FirstOrDefault();
        if (sys is not null)
            File.Copy(sys, Path.Combine(AppDataDriversDir, "dd63330.sys"), overwrite: true);
    }

    /// <summary>重试无意义的硬性失败（缺随包工具 / 上游内容变化），重试循环收到后直接终止。</summary>
    private sealed class NoRetryException : Exception
    {
        public NoRetryException(string message) : base(message) { }
    }

    /// <summary>
    /// 把一次尝试的异常转成一句用户能读的失败原因（UI 注释区直接展示）：
    /// 超时单独说明，其余去掉英文句末标点，避免"…success: 404 (Not Found).，5 秒后"这种拼接。
    /// </summary>
    private static string DescribeFetchFailure(Exception ex) => ex switch
    {
        OperationCanceledException => "下载超时（3 分钟未完成）",
        _ => ex.Message.TrimEnd('。', '.', ' ')
    };

    /// <summary>
    /// 查询 DD 官方最新 Release 的 7z 资产直链（GitHub API 匿名限流 / 网络失败返回 null，
    /// 调用方退回硬编码直链）。
    /// </summary>
    private static string? ResolveLatestAssetUrl()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var response = AutoFetchHttp.GetAsync(DdLatestApiUrl, cts.Token).ConfigureAwait(false).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode) return null;
            var json = response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";
                if (url.Length > 0 && name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase)) return url;
            }
            return null;
        }
        catch (Exception ex)
        {
            Logger.Info($"DD 官方最新版本查询失败（改用内置直链）：{ex.Message}");
            return null;
        }
    }

    // ---------- 注入接口（InputSimulatorService 调用） ----------

    /// <summary>
    /// 发送键盘按下 / 抬起。flag：1 = 按下，2 = 抬起。
    /// 返回是否成功（含初始化）。VK 不受支持（如 F13+）时返回 false。
    /// </summary>
    public static bool SendKey(int vk, bool down)
    {
        lock (Gate)
        {
            if (_ddKey is null || !_initialized) return false;

            if (!VkCodeCache.TryGetValue(vk, out var code))
            {
                code = _ddTodc is not null ? SafeCall(_ddTodc, vk, out _) : -1;
                VkCodeCache[vk] = code;
            }

            if (code < 0) return false; // DD 键码表不含该 VK（如 F13+）

            // 预登记待决回环名额（必须在注入调用之前：回环事件可能在
            // DD_key 返回前就到达钩子）；注入失败则撤销（见 EchoGuard）。
            var note = EchoGuard.NoteInjection(vk, down);
            // 诊断埋点：DD_key 内核注入耗时限频告警（>50ms = 驱动 / 系统侧拥堵，
            // 所有任务线程在 Gate 上串行等待，连发节奏与启停响应都会被拖慢）。
            var ddStart = Stopwatch.GetTimestamp();
            var ret = SafeCall(_ddKey, MakeKeyArgs(code, down), out var seh);
            var ddMs = (Stopwatch.GetTimestamp() - ddStart) * 1000.0 / Stopwatch.Frequency;
            note.CostMs = (int)Math.Round(ddMs);   // 回填到名额：延迟日志据此把两段拆开
            if (ddMs > 50 && unchecked(Environment.TickCount - _lastDdLatencyLogTick) > 2000)
            {
                _lastDdLatencyLogTick = Environment.TickCount;
                Logger.Warn($"DD_key 注入耗时 {ddMs:0}ms（vk=0x{vk:X2}, down={down}）。");
            }
            if (!seh && ret == 0) return true;
            EchoGuard.CancelNote(vk, down, note);
            return false;
        }
    }

    /// <summary>SafeCall 用双参委托调用（code, flag）。</summary>
    private static int SafeCall(DdKeyFunc f, (int code, int flag) args, out bool seh)
    {
        try { seh = false; return f(args.code, args.flag); }
        catch (Exception ex) { seh = ex is SEHException; return int.MinValue; }
    }

    private static int SafeCall(DdTodcFunc f, int vk, out bool seh)
    {
        try { seh = false; return f(vk); }
        catch (Exception ex) { seh = ex is SEHException; return int.MinValue; }
    }

    private static (int, int) MakeKeyArgs(int code, bool down) => (code, down ? 1 : 2);

    /// <summary>当前 VK 是否受 DD 键码表支持（不注入，仅查询）。</summary>
    public static bool IsVkSupported(int vk)
    {
        lock (Gate)
        {
            if (!VkCodeCache.TryGetValue(vk, out var code))
            {
                if (_ddTodc is null) return false;
                code = SafeCall(_ddTodc, vk, out _);
                VkCodeCache[vk] = code;
            }
            return code >= 0;
        }
    }

    // ---------- 内部 ----------

    private static T? GetProc<T>(string name) where T : class, Delegate
    {
        var p = GetProcAddress(_dllHandle, name);
        if (p == IntPtr.Zero) return null;
        return Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
    }

    private static int SafeCall(DdBtnFunc f, int mask, out bool seh)
    {
        try { seh = false; return f(mask); }
        catch (Exception ex) { seh = ex is SEHException; return int.MinValue; }
    }

    /// <summary>
    /// 探测可用 DD DLL（返回绝对路径；只认 **x64** 且导出可用的候选）：
    /// 1. exe 目录 dd63330.dll（官方 x64）
    /// 2. %APPDATA%\TheCelestialDiviner\drivers\dd63330.dll（自动获取的安装位置）
    /// 3. Change box 目录 DD64.dll（32 位时间锁版 —— x64 进程加载必失败）
    ///
    /// <paramref name="wrongArch"/> 回传"文件在位但位数不对"的那个路径：DD 官方按位数分包，
    /// 用户手动从官网下载时很容易拿到 32 位那份，放进程目录里 LoadLibrary 必失败（err=193）。
    /// 这里提前读 PE 头判掉，调用方才能给出"你放的是 32 位版本"这种能让人看懂的提示，
    /// 而不是一句 err=193。
    /// </summary>
    private static string? ProbeDll(out string? wrongArch)
    {
        wrongArch = null;
        var candidates = new List<string>(X64DllCandidates)
        {
            // 路径必须由目录拼（早前用 dll 全路径 + ".." 拼出的是非法路径，此分支实际从未命中）。
            Path.Combine(ChangeBoxDllDir, "DD64.dll")
        };

        foreach (var path in candidates)
        {
            if (!File.Exists(path)) continue;
            if (ReadPeMachine(path) == MachineAmd64) return path;
            wrongArch ??= path;   // 记住第一个位数不符的，供 LastError 说明
        }
        return null;
    }

    private const ushort MachineAmd64 = 0x8664;      // IMAGE_FILE_MACHINE_AMD64
    private const int ErrorBadExeFormat = 193;       // 32 位 DLL 塞进 64 位进程

    /// <summary>
    /// 读 PE 头里的 Machine 字段判断位数（0x8664 = x64）；不是有效 PE / 读不到返回 null。
    /// 只读文件前几百字节，不加载模块，开销可忽略（菜单展开时会被调用）。
    /// </summary>
    private static ushort? ReadPeMachine(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            using var br = new BinaryReader(fs);
            if (br.ReadUInt16() != 0x5A4D) return null;          // 'MZ'
            fs.Position = 0x3C;
            var peOffset = br.ReadInt32();
            if (peOffset <= 0 || peOffset > fs.Length - 6) return null;
            fs.Position = peOffset;
            if (br.ReadUInt32() != 0x00004550) return null;      // 'PE\0\0'
            return br.ReadUInt16();
        }
        catch
        {
            return null;   // 被占用 / 无权限 / 被截断：当作不可用，由 EnsureReady 报加载失败
        }
    }

    /// <summary>%APPDATA%\TheCelestialDiviner\drivers（自动获取驱动的安装位置；用户目录可写，无需管理员）。</summary>
    private static string AppDataDriversDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Constants.AppFolderName, "drivers");
}
