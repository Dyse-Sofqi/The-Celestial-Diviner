<#
    衍天高手 · Interception 驱动现场诊断（双驱动路线第二条）

    用途：装了 Interception 但「键盘注入 → Interception 驱动」点不上 / 提示要重启时，
          一次性把判读所需的全部状态打出来，判断卡在哪一层：
            ① 库文件在不在、是不是 x64、指纹与签名对不对
            ② 键盘类 UpperFilters 有没有挂上 keyboard（官方安装器的唯一判据）
            ③ System32\drivers\keyboard.sys 在不在、签名者是谁
            ④ 服务是否注册 / 是否在跑
            ⑤ \\.\interception00 设备能不能打开（= 驱动是否真的加载）
            ⑥ 内核隔离（HVCI）/ Secure Boot 是否把驱动拦在门外

    用法（推荐，绕开执行策略）：
        powershell -NoProfile -ExecutionPolicy Bypass -File ".\diag-interception.ps1"
    可选参数：
        -AppDir "D:\衍天高手"   指定程序目录（默认自动猜常见位置）
        -NoPause                结束时不等待按键（自动化用）

    只读：不安装、不卸载、不启动任何驱动、不改注册表，全部是查询类操作。
#>
[CmdletBinding()]
param(
    [string]$AppDir = '',
    [switch]$NoPause
)

$ErrorActionPreference = 'Continue'

function Section($title) {
    Write-Host ""
    Write-Host "=== $title ===" -ForegroundColor Cyan
}

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
Write-Host "衍天高手 · Interception 驱动诊断" -ForegroundColor Green
Write-Host ("时间：{0}    管理员：{1}    系统：{2}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $isAdmin, (Get-CimInstance Win32_OperatingSystem).Caption)

# ---------- ① 库文件 ----------
Section "① interception.dll"
$candidates = @()
if ($AppDir) { $candidates += (Join-Path $AppDir 'interception.dll') }
$candidates += @(
    (Join-Path (Split-Path $PSCommandPath -Parent) 'interception\interception.dll'),   # 随包子目录
    (Join-Path (Split-Path $PSCommandPath -Parent) 'interception.dll'),
    (Join-Path $env:APPDATA 'TheCelestialDiviner\drivers\interception\interception.dll'),
    (Join-Path $env:APPDATA 'TheCelestialDiviner\drivers\interception.dll')
)
$found = $false
$script:dllPath = $null
foreach ($p in $candidates) {
    if (-not (Test-Path $p)) { continue }
    $found = $true
    if (-not $script:dllPath) { $script:dllPath = $p }
    $f = Get-Item $p
    $hash = (Get-FileHash $p -Algorithm SHA256).Hash
    $sig = Get-AuthenticodeSignature $p
    Write-Host ("  路径：{0}" -f $p)
    Write-Host ("  大小：{0} 字节    SHA256：{1}" -f $f.Length, $hash)
    Write-Host ("  签名：{0}    签名者：{1}" -f $sig.Status, $sig.SignerCertificate.Subject)
    if ($hash -eq 'AB88164C11B1B48488772D4C3BFAA4509D5B0AE9DBC5A691DC4F96F0260443C8') {
        Write-Host "  → 与官方 v1.0.1 x64 逐字节一致" -ForegroundColor Green
    } else {
        Write-Host "  → 与已知官方 v1.0.1 指纹不同（可能是后续版本或非官方文件）" -ForegroundColor Yellow
    }
}
if (-not $found) { Write-Host "  未找到 interception.dll（驱动模式必然不可用）" -ForegroundColor Red }

# ---------- ② 键盘类 UpperFilters ----------
Section "② 键盘类 UpperFilters（官方安装器的判据）"
$clsKey = 'HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4D36E96B-E325-11CE-BFC1-08002BE10318}'
$upper = (Get-ItemProperty -Path $clsKey -Name UpperFilters -ErrorAction SilentlyContinue).UpperFilters
if ($null -eq $upper) {
    Write-Host "  读不到 UpperFilters（可能无权限）" -ForegroundColor Yellow
} else {
    Write-Host ("  内容：{0}" -f ($upper -join ', '))
    if ($upper -contains 'keyboard') {
        Write-Host "  → 已挂上 keyboard 过滤驱动（安装器执行过）" -ForegroundColor Green
    } else {
        Write-Host "  → 未挂上（未安装，或安装失败）" -ForegroundColor Red
    }
}

# ---------- ③ 驱动文件 ----------
Section "③ 驱动文件与签名"
foreach ($sys in @('keyboard.sys', 'mouse.sys')) {
    $p = Join-Path $env:SystemRoot ("System32\drivers\" + $sys)
    if (-not (Test-Path $p)) { Write-Host ("  {0}：不存在" -f $sys) -ForegroundColor Red; continue }
    $sig = Get-AuthenticodeSignature $p
    $item = Get-Item $p
    Write-Host ("  {0}：{1} 字节    写入时间 {2:yyyy-MM-dd HH:mm}" -f $sys, $item.Length, $item.LastWriteTime)
    Write-Host ("      签名状态 {0}    签名者 {1}" -f $sig.Status, $sig.SignerCertificate.Subject)
}

# ---------- ④ 服务 ----------
Section "④ 内核服务 keyboard / mouse"
foreach ($svc in @('keyboard', 'mouse')) {
    $s = Get-Service -Name $svc -ErrorAction SilentlyContinue
    if ($null -eq $s) { Write-Host ("  {0}：服务不存在" -f $svc) -ForegroundColor Yellow; continue }
    $img = (Get-ItemProperty -Path ('HKLM:\SYSTEM\CurrentControlSet\Services\' + $svc) -Name ImagePath -ErrorAction SilentlyContinue).ImagePath
    if ([string]::IsNullOrWhiteSpace($img)) {
        # 官方安装器建服务时不给 ImagePath：内核加载器默认取 System32\drivers\<服务名>.sys，
        # 这正是 keyboard.sys / mouse.sys 的位置，空值属正常，不是坏掉。
        $img = "（未设 → 默认 System32\drivers\$svc.sys，正常）"
    }
    Write-Host ("  {0}：状态 {1} / 启动类型 {2}    ImagePath {3}" -f $svc, $s.Status, $s.StartType, $img)
}

# ---------- ⑤ 驱动是否真的加载（只用官方库 API） ----------
Section "⑤ 驱动是否已加载（create_context 探测）"
# 注意：这里刻意**不**用 CreateFile("\\.\interceptionXX") 直开设备 —— Interception 的
# 再分发许可以"与驱动的通信仅经 interception.dll 的 API"为前提（见 README 许可章节），
# 诊断脚本也不越线：加载同一个官方 dll，用 create_context / destroy_context 判断。
if ([string]::IsNullOrEmpty($dllPath) -or -not (Test-Path $dllPath)) {
    Write-Host "  跳过：未找到 interception.dll（见 ①）" -ForegroundColor Yellow
} else {
    try {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class IcptProbe {
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryW(string p);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr h, string n);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr CreateCtx();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DestroyCtx(IntPtr c);
    // 返回 1 = 上下文建得起来（驱动已加载）；0 = 建不起来；-1 = 库加载/导出失败
    public static int Probe(string dll) {
        IntPtr h = LoadLibraryW(dll);
        if (h == IntPtr.Zero) return -1;
        IntPtr pc = GetProcAddress(h, "interception_create_context");
        IntPtr pd = GetProcAddress(h, "interception_destroy_context");
        if (pc == IntPtr.Zero) return -1;
        var create = (CreateCtx)Marshal.GetDelegateForFunctionPointer(pc, typeof(CreateCtx));
        IntPtr ctx = IntPtr.Zero;
        try { ctx = create(); return ctx != IntPtr.Zero ? 1 : 0; }
        catch { return 0; }
        finally {
            if (ctx != IntPtr.Zero && pd != IntPtr.Zero) {
                var destroy = (DestroyCtx)Marshal.GetDelegateForFunctionPointer(pd, typeof(DestroyCtx));
                try { destroy(ctx); } catch { }
            }
        }
    }
}
'@ -ErrorAction Stop
        $probe = [IcptProbe]::Probe($dllPath)
        if ($probe -eq 1) {
            Write-Host "  → 上下文创建成功：驱动已加载，可直接注入" -ForegroundColor Green
        } elseif ($probe -eq 0) {
            Write-Host "  → 上下文创建失败：驱动未加载" -ForegroundColor Red
            Write-Host "     若 ② 显示已挂上 keyboard，则通常只是『还没重启』；重启后仍如此，见 ⑥" -ForegroundColor Yellow
        } else {
            Write-Host "  → 库加载或导出查找失败（文件损坏 / 位数不符？）" -ForegroundColor Red
        }
    } catch {
        Write-Host ("  无法编译探测代码（{0}）：请改用程序内『选项 → 键盘注入 → Interception 驱动』" -f $_.Exception.Message) -ForegroundColor Yellow
        Write-Host "     能否点选来验证（程序走官方 API，结果等价）" -ForegroundColor Yellow
    }
}

# ---------- ⑥ 拦截面 ----------
Section "⑥ 可能拦下该驱动的系统策略"
$hvci = (Get-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity' -Name Enabled -ErrorAction SilentlyContinue).Enabled
Write-Host ("  内存完整性 HVCI：{0}" -f $(if ($null -eq $hvci) { '未配置（默认关）' } elseif ($hvci -eq 1) { '已开启 —— 会拒绝加载 2012 年签名的驱动' } else { '已关闭' }))
try {
    $sb = Confirm-SecureBootUEFI -ErrorAction Stop
    Write-Host ("  Secure Boot：{0}" -f $(if ($sb) { '已开启' } else { '已关闭' }))
} catch {
    Write-Host "  Secure Boot：查询不到（非 UEFI 或无权限）"
}
Write-Host ""
Write-Host "判读顺序：① 库不对 → 重装；② 未挂 keyboard → 走「安装 Interception 驱动…」；" -ForegroundColor Gray
Write-Host "          ② 已挂但 ⑤ 上下文建不起来 → 重启；重启后仍失败且 ⑥ HVCI 开启 → 关掉内存完整性再重启。" -ForegroundColor Gray

if (-not $NoPause) {
    Write-Host ""
    Read-Host "按回车关闭"
}
