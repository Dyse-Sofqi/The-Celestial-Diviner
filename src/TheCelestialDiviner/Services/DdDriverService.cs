using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
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

    private static bool _initialized;

    /// <summary>最近一次初始化失败的原因（供 UI 日志展示）。</summary>
    public static string? LastError { get; private set; }

    /// <summary>DD 键码缓存：VK → DD code（-1 表示不支持）。DD_todc 查询一次后缓存。</summary>
    private static readonly Dictionary<int, int> VkCodeCache = new();

    // ---------- 初始化 / 释放 ----------

    /// <summary>
    /// 加载 DD DLL 并初始化虚拟驱动（DD_btn(0)）。重复调用安全：已就绪时直接返回 true。
    /// 需要管理员权限；首次调用可能触发系统安装虚拟设备（自动完成，无需交互）。
    /// </summary>
    public static bool EnsureReady()
    {
        lock (Gate)
        {
            if (_ddKey is not null && _initialized) return true;
            LastError = null;

            var path = ProbeDll();
            if (path is null)
            {
                LastError = "未找到 dd63330.dll（请将其放到程序目录，或使用官方 2026 x64 版）";
                return false;
            }

            if (_dllHandle == IntPtr.Zero)
            {
                // DD DLL 无静态第三方依赖，但仍设置搜索目录以稳妥处理运行库代理。
                SetDllDirectoryW(Path.GetDirectoryName(path)!);
                _dllHandle = LoadLibraryW(path);
                if (_dllHandle == IntPtr.Zero)
                {
                    LastError = $"加载 {Path.GetFileName(path)} 失败（err={Marshal.GetLastWin32Error()}）";
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
            // 因此带退避重试数次，仍失败才判定不可用。
            const int maxAttempts = 5;
            var ret = 0;
            var seh = false;
            EnsureKernelService();
            Logger.Info("正在初始化 DD 虚拟设备（DD_btn(0)）...");
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                ret = SafeCall(_ddBtn, 0, out seh);
                if (!seh && ret == 1) break;
                if (attempt < maxAttempts)
                {
                    Logger.Warn($"DD 虚拟设备初始化第 {attempt}/{maxAttempts} 次失败（ret={ret}），{400 * attempt}ms 后重试。");
                    Thread.Sleep(400 * attempt);
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
    /// 本方法在 DD_btn 之前确保服务就绪：服务缺失时用随包分发的
    /// dd63330.sys（与 DD 释放的文件字节一致，微软 WHQL 签名）自行创建并启动，
    /// 使 DD_btn 走"服务已存在"的快速路径，不再触发其安装例程。
    /// </summary>
    private static void EnsureKernelService()
    {
        try
        {
            var sysPath = ResolveDriverSysPath();
            var scm = OpenSCManagerW(null, null, SC_MANAGER_ALL_ACCESS);
            if (scm == IntPtr.Zero)
            {
                Logger.Warn($"预置 DD 内核服务：打开 SCM 失败（err={Marshal.GetLastWin32Error()}），交由 DD 自行安装。");
                return;
            }
            try
            {
                var service = OpenServiceW(scm, "dd63330", SERVICE_ALL_ACCESS);
                if (service == IntPtr.Zero)
                {
                    var err = Marshal.GetLastWin32Error();
                    if (sysPath is null)
                    {
                        Logger.Info("预置 DD 内核服务：服务不存在且无 dd63330.sys，交由 DD 自行安装。");
                        return;
                    }
                    // 旧服务可能处于"标记删除"未完成状态（1072），稍等重试。
                    for (var attempt = 1; ; attempt++)
                    {
                        service = CreateServiceW(scm, "dd63330", "dd63330", SERVICE_ALL_ACCESS,
                            SERVICE_KERNEL_DRIVER, SERVICE_DEMAND_START, SERVICE_ERROR_NORMAL,
                            sysPath, null, IntPtr.Zero, null, null, null);
                        if (service != IntPtr.Zero) break;
                        err = Marshal.GetLastWin32Error();
                        if (err != ERROR_SERVICE_MARKED_FOR_DELETE || attempt >= 5)
                        {
                            Logger.Warn($"预置 DD 内核服务：CreateService 失败（err={err}），交由 DD 自行安装。");
                            return;
                        }
                        Thread.Sleep(600);
                    }
                    Logger.Info("预置 DD 内核服务：已创建 dd63330 服务。");
                }

                if (QueryServiceStatus(service, out var status) && status.dwCurrentState == SERVICE_RUNNING)
                {
                    Logger.Info("预置 DD 内核服务：dd63330 驱动已在运行。");
                    return;
                }
                if (!StartServiceW(service, 0, IntPtr.Zero))
                {
                    var err = Marshal.GetLastWin32Error();
                    if (err == ERROR_SERVICE_ALREADY_RUNNING) return;

                    // 服务被安全软件禁用（1058）：改回按需启动后重试；仍失败则删除重建。
                    if (err == ERROR_SERVICE_DISABLED && sysPath is not null)
                    {
                        Logger.Warn("预置 DD 内核服务：服务被禁用（err=1058），尝试恢复启动类型...");
                        if (ChangeServiceConfigW(service, SERVICE_NO_CHANGE, SERVICE_DEMAND_START,
                                SERVICE_NO_CHANGE, null, null, IntPtr.Zero, null, null, null, null))
                        {
                            if (StartServiceW(service, 0, IntPtr.Zero))
                            {
                                Logger.Info("预置 DD 内核服务：已恢复被禁用的服务并启动。");
                                return;
                            }
                            err = Marshal.GetLastWin32Error();
                        }
                        else
                        {
                            err = Marshal.GetLastWin32Error();
                        }

                        // 仍失败：删除旧服务，用随包 sys 文件重建（摆脱被标记/损坏的服务项）。
                        Logger.Warn($"预置 DD 内核服务：恢复失败（err={err}），删除并重建服务...");
                        DeleteService(service);
                        CloseServiceHandle(service);
                        service = CreateServiceW(scm, "dd63330", "dd63330", SERVICE_ALL_ACCESS,
                            SERVICE_KERNEL_DRIVER, SERVICE_DEMAND_START, SERVICE_ERROR_NORMAL,
                            sysPath, null, IntPtr.Zero, null, null, null);
                        if (service != IntPtr.Zero && StartServiceW(service, 0, IntPtr.Zero))
                        {
                            Logger.Info("预置 DD 内核服务：已重建并启动 dd63330 驱动。");
                            return;
                        }
                        err = Marshal.GetLastWin32Error();
                        Logger.Warn($"预置 DD 内核服务：重建启动失败（err={err}），交由 DD 自行处理。");
                        if (service != IntPtr.Zero) CloseServiceHandle(service);
                        return;
                    }

                    Logger.Warn($"预置 DD 内核服务：StartService 失败（err={err}），交由 DD 自行处理。");
                    return;
                }
                Logger.Info("预置 DD 内核服务：dd63330 驱动已启动。");
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
        }
    }

    /// <summary>定位内核驱动文件（预置服务用）：exe 目录 → %APPDATA% drivers → %TEMP%（DD 自身释放位置）。</summary>
    private static string? ResolveDriverSysPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "dd63330.sys"),
            Path.Combine(AppDataDriversDir, "dd63330.sys"),
            Path.Combine(Path.GetTempPath(), "dd63330.sys")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// 首次初始化成功后把 DD 自释放到 %TEMP% 的 dd63330.sys 转存到 %APPDATA% drivers
    /// （尽力而为）：后续启动预置内核服务可走快速路径，不受 %TEMP% 清理影响。
    /// </summary>
    private static void KeepKernelDriverSys()
    {
        try
        {
            var target = Path.Combine(AppDataDriversDir, "dd63330.sys");
            if (File.Exists(target)) return;
            var temp = Path.Combine(Path.GetTempPath(), "dd63330.sys");
            if (!File.Exists(temp)) return;
            Directory.CreateDirectory(AppDataDriversDir);
            File.Copy(temp, target, overwrite: true);
            Logger.Info("已备份 DD 内核驱动到 %APPDATA%\\TheCelestialDiviner\\drivers（预置服务用）。");
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

    /// <summary>驱动自动获取是否正在进行（供 UI / VM 判断，无需精确）。</summary>
    public static bool IsAutoFetchRunning => Volatile.Read(ref _autoFetchRunning) == 1;

    /// <summary>
    /// 驱动自动获取完成并已安装到 %APPDATA% drivers（线程池线程触发，无订阅者安全）。
    /// VM 订阅后在 UI 线程重试 EnsureReady 并自动升回 DD 模式。
    /// </summary>
    public static event Action? AutoFetchCompleted;

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

    /// <summary>自动获取主流程（后台线程调用；任何失败以日志收尾）。</summary>
    private static void RunAutoFetch()
    {
        // 只认可加载的 x64 版 dd63330.dll（exe / APPDATA）；Change Box 的 DD64.dll 是
        // 32 位时间锁版（x64 必加载失败），不算"已有驱动"，命中它反而正需要自动获取。
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "dd63330.dll"))
            || File.Exists(Path.Combine(AppDataDriversDir, "dd63330.dll")))
        {
            Logger.Info("DD 驱动自动获取：探测到已有 dd63330.dll，跳过下载。");
            return;
        }

        var workDir = Path.Combine(AppDataDriversDir, "fetch-" + Environment.TickCount.ToString("x"));
        Directory.CreateDirectory(workDir);
        try
        {
            // 1. 解析官方资产直链：优先 GitHub API 拿最新 Release，失败退回硬编码直链。
            var assetUrl = ResolveLatestAssetUrl() ?? DdFallbackAssetUrl;

            // 2. 下载官方 7z 包（约 3.7MB，3 分钟超时兜底）。
            var archivePath = Path.Combine(workDir, "dd.7z");
            using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3)))
            using (var response = AutoFetchHttp.GetAsync(assetUrl, cts.Token).ConfigureAwait(false).GetAwaiter().GetResult())
            {
                response.EnsureSuccessStatusCode();
                File.WriteAllBytes(archivePath,
                    response.Content.ReadAsByteArrayAsync().ConfigureAwait(false).GetAwaiter().GetResult());
            }
            Logger.Info($"DD 驱动自动获取：官方包已下载（{new FileInfo(archivePath).Length / 1024}KB）。");

            // 3. 解包：随包 7zr.exe（7-Zip 独立版，LGPL）。缺失视为安装包不完整。
            var sevenZip = Path.Combine(AppContext.BaseDirectory, "7zr.exe");
            if (!File.Exists(sevenZip))
            {
                Logger.Error("DD 驱动自动获取失败：缺少 7zr.exe 解包工具（请从官方发布包完整安装）。");
                return;
            }
            var extractDir = Path.Combine(workDir, "x");
            using (var proc = Process.Start(new ProcessStartInfo
            {
                FileName = sevenZip,
                Arguments = $"x \"{archivePath}\" -o\"{extractDir}\" -y",
                UseShellExecute = false,
                CreateNoWindow = true
            }))
            {
                if (proc is null || !proc.WaitForExit(60_000) || proc.ExitCode != 0)
                {
                    Logger.Error($"DD 驱动自动获取失败：官方包解包失败（exit={(proc?.HasExited == true ? proc.ExitCode : -1)}）。");
                    return;
                }
            }

            // 4. 定位 dd63330.dll：官方包内含多个版本变体（1.simple 为免费标准版），优先取 simple。
            var dll = Directory.EnumerateFiles(extractDir, "dd63330.dll", SearchOption.AllDirectories)
                .OrderBy(path => path.IndexOf("simple", StringComparison.OrdinalIgnoreCase) >= 0 ? 0 : 1)
                .ThenBy(path => path.Length)
                .FirstOrDefault();
            if (dll is null)
            {
                Logger.Error("DD 驱动自动获取失败：官方包内未找到 dd63330.dll（上游内容可能已变化）。");
                return;
            }

            // 5. 安装到 %APPDATA% drivers（探测顺序中的既有位置；包内含 sys 时一并安装）。
            Directory.CreateDirectory(AppDataDriversDir);
            File.Copy(dll, Path.Combine(AppDataDriversDir, "dd63330.dll"), overwrite: true);
            var sys = Directory.EnumerateFiles(extractDir, "dd63330.sys", SearchOption.AllDirectories)
                .FirstOrDefault();
            if (sys is not null)
                File.Copy(sys, Path.Combine(AppDataDriversDir, "dd63330.sys"), overwrite: true);
            Logger.Info("DD 驱动自动获取完成，已安装到 %APPDATA%\\TheCelestialDiviner\\drivers。");
        }
        finally
        {
            try { Directory.Delete(workDir, true); } catch { /* 临时目录清理失败不影响主流程 */ }
        }
    }

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
            var stamp = EchoGuard.NoteInjection(vk, down);
            var ret = SafeCall(_ddKey, MakeKeyArgs(code, down), out var seh);
            if (!seh && ret == 0) return true;
            EchoGuard.CancelNote(vk, down, stamp);
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
    /// 探测可用 DD DLL（返回绝对路径）：
    /// 1. exe 目录 dd63330.dll（官方 x64）
    /// 2. %APPDATA%\TheCelestialDiviner\drivers\dd63330.dll（自动获取的安装位置）
    /// 3. Change box 目录 DD64.dll（32 位时间锁版 —— x64 进程加载必失败，仅列为探测目标，
    ///    加载失败时 LastError 会提示替换为 x64 版）
    /// </summary>
    private static string? ProbeDll()
    {
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, "dd63330.dll"),
            Path.Combine(AppDataDriversDir, "dd63330.dll")
        };

        var changeBox = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Change Box", "DLL", "dd63330.dll");
        candidates.Add(changeBox);
        candidates.Add(Path.Combine(changeBox, "..", "DD64.dll"));

        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>%APPDATA%\TheCelestialDiviner\drivers（自动获取驱动的安装位置；用户目录可写，无需管理员）。</summary>
    private static string AppDataDriversDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Constants.AppFolderName, "drivers");
}
