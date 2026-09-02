using System.IO;
using System.Runtime.InteropServices;

namespace TheCelestialDiviner.Services;

/// <summary>
/// DD 虚拟驱动（ddxoft DD 虚拟鼠标键盘驱动）键盘注入服务。
/// 通过 DD_key 注入的事件不携带 LLKHF_INJECTED 标记，对游戏表现为"物理键盘"，
/// 可绕过基于注入标记的输入过滤（SendInput / PostMessage 模式失效时的备选）。
///
/// 加载策略：优先 <c>dd63330.dll</c>（官方 2026 x64 用户态版），其次兼容万象等第三方
/// 部署的 <c>DD64.dll</c>（32 位时间锁版，x64 进程无法加载，仅探测并给出提示）。
/// 探测顺序：exe 目录 → %APPDATA%\TheCelestialDiviner\drivers → Change box 安装目录。
/// 依赖文件随发布包提供（原生 DLL 无法内嵌单文件）。
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

            // DD_btn(0)：初始化虚拟设备。返回 1 = 成功；0/-1 = 失败（权限不足或驱动异常）。
            var ret = SafeCall(_ddBtn, 0, out var seh);
            if (seh || ret != 1)
            {
                LastError = $"DD 虚拟驱动初始化失败（ret={ret}）。请确认以管理员身份运行。";
                _ddTodc = null;
                _ddKey = null;
                _ddBtn = null;
                FreeAll();
                return false;
            }

            _initialized = true;
            VkCodeCache.Clear();
            return true;
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

            var ret = SafeCall(_ddKey, MakeKeyArgs(code, down), out _);
            // 实测成功返回 0，失败返回非 0（-2 等）。
            return ret == 0;
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
    /// 2. %APPDATA%\TheCelestialDiviner\drivers\dd63330.dll
    /// 3. Change box 目录 DD64.dll（32 位时间锁版 —— x64 进程加载必失败，仅列为探测目标，
    ///    加载失败时 LastError 会提示替换为 x64 版）
    /// </summary>
    private static string? ProbeDll()
    {
        var candidates = new List<string>();

        var exeDir = AppContext.BaseDirectory;
        candidates.Add(Path.Combine(exeDir, "dd63330.dll"));

        var appDataDrivers = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TheCelestialDiviner", "drivers", "dd63330.dll");
        candidates.Add(appDataDrivers);

        var changeBox = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Change Box", "DLL", "dd63330.dll");
        candidates.Add(changeBox);
        candidates.Add(Path.Combine(changeBox, "..", "DD64.dll"));

        return candidates.FirstOrDefault(File.Exists);
    }
}
