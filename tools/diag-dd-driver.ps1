<#
    衍天高手 · DD 驱动蓝屏现场采集（PAGE_FAULT_IN_NONPAGED_AREA / 0x50）

    用途：客户出现 dd63330.sys 蓝屏时，一次性收集判读所需的全部环境信息，
          生成一份可直接回传的文本报告，并把应用日志、minidump 一起打包。

    用法（推荐，绕开执行策略）：
        powershell -NoProfile -ExecutionPolicy Bypass -File ".\diag-dd-driver.ps1"
    可选参数：
        -AppDir "D:\衍天高手"   指定程序目录（默认自动猜常见位置）
        -OutDir "D:\report"     指定报告输出目录（默认桌面）
        -SkipElevate            不提权（部分项会缺，仅用于自测）
        -NoPause                结束时不等待按键（自动化用）

    只读：不安装、不删除、不启动任何驱动，全部是查询类操作。
    唯一写操作：把报告/日志/转储复制到输出目录。
#>
[CmdletBinding()]
param(
    [string]$AppDir = '',
    [string]$OutDir = '',
    [switch]$SkipElevate,
    [switch]$NoPause
)

$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin -and -not $SkipElevate) {
    Write-Host "需要管理员权限读取驱动服务与内核隔离状态，正在提权..." -ForegroundColor Yellow
    try {
        $argLine = '-NoProfile -ExecutionPolicy Bypass -File "{0}"' -f $PSCommandPath
        if ($AppDir) { $argLine += ' -AppDir "{0}"' -f $AppDir }
        Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $argLine
        exit
    } catch {
        Write-Host "提权被取消，继续以普通权限采集（部分项目会缺失）。" -ForegroundColor Yellow
    }
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$pc = $env:COMPUTERNAME
if (-not $OutDir) {
    $desktop = [Environment]::GetFolderPath('Desktop')
    if (-not $desktop) { $desktop = Join-Path $env:USERPROFILE 'Desktop' }
    $OutDir = Join-Path $desktop ("dd-diag-$pc-$stamp")
}
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null
$reportPath = Join-Path $OutDir ("dd-diag-$pc-$stamp.txt")
$collectDir = Join-Path $OutDir 'collected'
New-Item -ItemType Directory -Path $collectDir -Force | Out-Null

$script:Lines = New-Object System.Collections.Generic.List[string]
$script:Flags = New-Object System.Collections.Generic.List[string]

function W([string]$text) {
    $script:Lines.Add($text)
    Write-Host $text
}
function Sec([string]$title) {
    W ''
    W ('-' * 70)
    W ('## ' + $title)
    W ('-' * 70)
}
function WObj($obj, [int]$width = 300) {
    if ($null -eq $obj) { return }
    $s = ($obj | Format-Table -AutoSize | Out-String -Width $width).TrimEnd()
    if ($s.Trim().Length -gt 0) { W $s }
}
function Try-Get([scriptblock]$block, [string]$failText = '(查询失败)') {
    try { & $block } catch { W ("  " + $failText + "：" + $_.Exception.Message); $null }
}
function Normalize-Path([string]$p) {
    if (-not $p) { return $null }
    $p = $p.Trim()
    $p = $p -replace '^\\\?\?\\', ''
    $p = $p.Trim('"')
    if ($p -match '^\\SystemRoot\\(.*)$') { $p = Join-Path $env:SystemRoot $matches[1] }
    elseif ($p -match '^[Ss]ystem32\\(.*)$') { $p = Join-Path $env:SystemRoot ('system32\' + $matches[1]) }
    return $p
}

W "衍天高手 · DD 驱动蓝屏现场采集报告"
W ("生成时间：{0}    计算机：{1}    管理员运行：{2}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $pc, $isAdmin)

# ---------------------------------------------------------------- 1 系统版本
Sec '1. 系统版本（确认是否 25H2）'
$build = 0
try {
    $cv = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
    $build = [int]$cv.CurrentBuildNumber
    $verName = '未知版本'
    if ($build -ge 26200) { $verName = 'Windows 11 25H2 或更新' }
    elseif ($build -ge 26100) { $verName = 'Windows 11 24H2' }
    elseif ($build -ge 22631) { $verName = 'Windows 11 23H2' }
    elseif ($build -ge 22621) { $verName = 'Windows 11 22H2' }
    elseif ($build -ge 22000) { $verName = 'Windows 11 21H2' }
    elseif ($build -ge 19045) { $verName = 'Windows 10 22H2' }
    elseif ($build -ge 19041) { $verName = 'Windows 10 2004 及以上' }
    else { $verName = 'Windows 10 早期版本' }

    W ("  产品名    ：" + $cv.ProductName)
    W ("  显示版本  ：" + $cv.DisplayVersion + "    内部版本：" + $cv.CurrentBuildNumber + "." + $cv.UBR)
    W ("  判定      ：" + $verName + "    (Build " + $build + ")")
    $os = Get-CimInstance Win32_OperatingSystem
    W ("  安装时间  ：" + $os.InstallDate + "    上次启动：" + $os.LastBootUpTime)
    W ("  架构/类型 ：" + $os.OSArchitecture + " / " + $cv.InstallationType)
    if ($build -ge 26200) {
        $script:Flags.Add('系统是 25H2（Build 26200+）：这代内核对未文档化 API、代码页 hook 的校验更严，老式键盘过滤驱动是首要嫌疑。')
    }
} catch { W ("  读取版本失败：" + $_.Exception.Message) }

# ------------------------------------------------------- 2 内核隔离 / 安全启动
Sec '2. 内核隔离（内存完整性 HVCI）、VBS、安全启动'
try {
    $dgRoot = 'HKLM:\SYSTEM\CurrentControlSet\Control\DeviceGuard'
    $vbsOn = (Get-ItemProperty -Path $dgRoot -Name 'EnableVirtualizationBasedSecurity' -ErrorAction SilentlyContinue).EnableVirtualizationBasedSecurity
    W ("  VBS 总开关（注册表）：" + $(if ($null -eq $vbsOn) { '未设置' } else { $vbsOn }))

    $hvciKey = Join-Path $dgRoot 'Scenarios\HypervisorEnforcedCodeIntegrity'
    $hvciRaw = (Get-ItemProperty -Path $hvciKey -Name 'Enabled' -ErrorAction SilentlyContinue).Enabled
    if ($null -eq $hvciRaw) {
        W '  内存完整性（HVCI）：注册表项不存在（通常表示从未开启）'
    } elseif ($hvciRaw -eq 1) {
        W "  内存完整性（HVCI）：已开启   <== 重点"
        $script:Flags.Add('内存完整性（HVCI）已开启：DD 这类老驱动在代码页被拦下时，典型表现就是 0x50。建议让客户关闭后复测。')
    } else {
        W ("  内存完整性（HVCI）：已关闭  (Enabled=" + $hvciRaw + ")")
    }
} catch { W ('  读取 HVCI 失败：' + $_.Exception.Message) }

Try-Get {
    $kss = (Get-ItemProperty -Path (Join-Path $dgRoot 'Scenarios\KernelShadowStacks') -Name 'Enabled' -ErrorAction SilentlyContinue).Enabled
    if ($null -eq $kss) { W '  内核模式硬件强制堆栈保护（Kernel Shadow Stacks）：未配置' }
    else { W ("  内核模式硬件强制堆栈保护（Kernel Shadow Stacks）：Enabled=" + $kss) }
    if ($kss -eq 1) {
        $script:Flags.Add('内核模式硬件强制堆栈保护已开启：它假定驱动代码遵守 shadow stack 约定，未适配的老驱动会直接异常，0x50/0x1E 都常见。')
    }
} 'Kernel Shadow Stacks 查询失败'

Try-Get {
    $sac = (Get-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy' -Name 'VerifiedAndReputablePolicyState' -ErrorAction SilentlyContinue).VerifiedAndReputablePolicyState
    if ($null -eq $sac) { W '  Smart App Control：未配置（旧系统或已关闭）' }
    else {
        $sacText = @{ 0 = '已关闭'; 1 = '强制模式'; 2 = '评估模式' }
        $t = $sacText[[int]$sac]
        if (-not $t) { $t = [string]$sac }
        W ("  Smart App Control：" + $t + "  (VerifiedAndReputablePolicyState=" + $sac + ")")
        if ([int]$sac -ne 0) {
            $script:Flags.Add('Smart App Control 处于开启/评估状态（25H2 全新安装默认如此）：它会拦截未通过信誉校验的驱动与程序，DD 驱动首当其冲。')
        }
    }
} 'Smart App Control 查询失败'

Try-Get {
    $dg = Get-CimInstance -Namespace 'root\Microsoft\Windows\DeviceGuard' -ClassName Win32_DeviceGuard -ErrorAction Stop
    W ("  VBS 运行状态：{0}   (0=未启用 1=已启用未运行 2=运行中)" -f $dg.VirtualizationBasedSecurityStatus)
    W ("  已运行的安全服务：" + ($dg.SecurityServicesRunning -join ',') + "   (1=Credential Guard 2=HVCI 3=System Guard)")
    W ("  已配置的安全服务：" + ($dg.SecurityServicesConfigured -join ','))
} 'VBS 状态查询失败（多为权限不足）'

Try-Get {
    $sb = Confirm-SecureBootUEFI -ErrorAction Stop
    W ("  安全启动：" + $(if ($sb) { '已开启' } else { '已关闭' }))
} '安全启动查询失败（可能为 Legacy BIOS）'

Try-Get {
    $vul = (Get-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Config' -Name 'VulnerableDriverBlocklistEnable' -ErrorAction SilentlyContinue).VulnerableDriverBlocklistEnable
    if ($null -eq $vul) { W '  易受攻击驱动阻止列表：未显式配置（默认跟随系统策略，通常为开启）' }
    else { W ("  易受攻击驱动阻止列表：VulnerableDriverBlocklistEnable=" + $vul + "   (1=开启 0=关闭)") }
}

# ------------------------------------------------------------- 3 安全软件
Sec '3. 安全软件'
Try-Get {
    $av = @(Get-CimInstance -Namespace 'root\SecurityCenter2' -ClassName AntiVirusProduct -ErrorAction SilentlyContinue |
            Select-Object displayName, productState, pathToSignedProductExe)
    if ($av.Count -eq 0) {
        W '  安全中心未返回杀软（可能被第三方安全软件接管，看第 8 节的内核驱动公司名）。'
    } else {
        WObj $av
        W ("  已注册：" + (($av | ForEach-Object { $_.displayName }) -join ' / '))
    }
} '安全中心查询失败'

# ------------------------------------------------------------- 4 硬件
Sec '4. 硬件（内存 / 机型 / CPU）'
Try-Get {
    $mem = @(Get-CimInstance Win32_PhysicalMemory |
             Select-Object @{n='插槽';e={$_.DeviceLocator}}, @{n='容量GB';e={[math]::Round($_.Capacity/1GB,0)}},
                           Speed, ConfiguredClockSpeed, Manufacturer, PartNumber)
    WObj $mem
    $mismatch = @($mem | Where-Object { $_.ConfiguredClockSpeed -ne $_.Speed -and $_.ConfiguredClockSpeed -gt 0 })
    if ($mismatch.Count -gt 0) {
        W '  注意：标称频率与实际运行频率不一致（XMP/EXPO 或降频）。'
        $script:Flags.Add('内存标称频率与实际运行频率不一致：若排除驱动后仍随机蓝屏，让客户关 XMP/EXPO 复测。')
    }
    $cs = Get-CimInstance Win32_ComputerSystem
    W ("  机型：{0} {1}    内存合计：{2} GB" -f $cs.Manufacturer, $cs.Model, [math]::Round($cs.TotalPhysicalMemory/1GB,1))
    $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
    W ("  CPU ：" + $cpu.Name)
} '硬件信息读取失败'

# --------------------------------------------------- 5 dd63330 服务与注册表
Sec '5. dd63330 内核服务（关键）'
$svcKey = 'HKLM:\SYSTEM\CurrentControlSet\Services\dd63330'
$imagePath = $null
$svcExists = Test-Path -LiteralPath $svcKey
W ("  服务项存在：" + $svcExists)
if ($svcExists) {
    $svc = Get-ItemProperty -LiteralPath $svcKey
    $imagePath = Normalize-Path ([string]$svc.ImagePath)
    W ("  ImagePath（原始）：" + $svc.ImagePath)
    W ("  ImagePath（解析）：" + $imagePath)
    $startMap = @{ 0 = 'Boot 引导'; 1 = 'System 系统'; 2 = 'Auto 自动'; 3 = 'Manual 手动'; 4 = 'Disabled 已禁用' }
    $startText = $startMap[[int]$svc.Start]
    if (-not $startText) { $startText = [string]$svc.Start }
    W ("  Type=" + $svc.Type + " (1=内核驱动)    Start=" + $svc.Start + " (" + $startText + ")    ErrorControl=" + $svc.ErrorControl)
    $imageExists = ($imagePath -and (Test-Path -LiteralPath $imagePath))
    W ("  映像文件存在：" + $imageExists)
    if ($imagePath -match '\\Temp\\') {
        W '  <== 指向 %TEMP%：极易被清理软件/杀软删除，或被换成旧版本。'
        $script:Flags.Add('dd63330 服务的 ImagePath 指向 %TEMP%：这是"两份驱动打架"最典型的来源，应删服务重建到固定目录。')
    }
    if (-not $imageExists) {
        $script:Flags.Add('dd63330 服务的 ImagePath 指向的文件已不存在：服务项是残留，必须删除后重建，否则加载的驱动身份不明。')
    }
    if ([int]$svc.Start -eq 4) {
        $script:Flags.Add('dd63330 服务被设为"已禁用"：多为安全软件所为，DD 初始化失败与此直接相关。')
    }
}
Try-Get {
    $drv = @(Get-CimInstance Win32_SystemDriver -ErrorAction Stop | Where-Object { $_.Name -like 'dd*' } |
             Select-Object Name, State, StartMode, PathName)
    if ($drv.Count -eq 0) { W '  系统中没有已注册的 dd* 驱动。' }
    else {
        W '  已注册的 dd* 驱动：'
        foreach ($d in $drv) { W ("    " + $d.Name + "  State=" + $d.State + "  StartMode=" + $d.StartMode + "  " + $d.PathName) }
    }
} '驱动枚举失败'

# -------------------------------------------- 6 驱动的实际加载路径（应用日志）
Sec '6. 程序实际加载的 DD 驱动（来自应用日志）'
$appDataRoot = Join-Path $env:APPDATA 'TheCelestialDiviner'
$logDir = Join-Path $appDataRoot 'logs'
$script:latestLog = $null
$script:loggedDll = $null
Try-Get {
    if (-not (Test-Path -LiteralPath $logDir)) { W ('  没有找到日志目录：' + $logDir); return }
    $logs = @(Get-ChildItem -LiteralPath $logDir -Filter 'app-*.log' -File -ErrorAction SilentlyContinue |
              Sort-Object LastWriteTime -Descending)
    if ($logs.Count -eq 0) { W '  日志目录为空。'; return }
    W '  最近的日志文件：'
    foreach ($l in ($logs | Select-Object -First 5)) {
        W ("    " + $l.Name + "   " + [math]::Round($l.Length/1KB,1) + " KB   " + $l.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'))
    }
    $script:latestLog = $logs[0]

    $dllHits = @(Select-String -LiteralPath $script:latestLog.FullName -Pattern '已加载 DD 驱动库' -ErrorAction SilentlyContinue)
    if ($dllHits.Count -gt 0) {
        W ''
        W '  最近加载的 DLL：'
        foreach ($h in ($dllHits | Select-Object -Last 3)) { W ('    ' + $h.Line.Trim()) }
        if ($dllHits[-1].Line -match '：(.+)$') { $script:loggedDll = $matches[1].Trim() }
    }

    $svcHits = @(Select-String -LiteralPath $script:latestLog.FullName -Pattern '预置 DD 内核服务' -ErrorAction SilentlyContinue)
    if ($svcHits.Count -gt 0) {
        W ''
        W '  最近的服务预置记录（最后 6 条）：'
        foreach ($h in ($svcHits | Select-Object -Last 6)) { W ('    ' + $h.Line.Trim()) }
    }

    $failHits = @(Select-String -LiteralPath $script:latestLog.FullName -Pattern 'DD 虚拟驱动初始化失败|DD 驱动未就绪' -ErrorAction SilentlyContinue)
    if ($failHits.Count -gt 0) {
        W ''
        W '  相关失败记录（最后 5 条）：'
        foreach ($h in ($failHits | Select-Object -Last 5)) { W ('    ' + $h.Line.Trim()) }
    }

    W ''
    W '  日志最后 25 行（蓝屏前的最后动作就在这里）：'
    $tail = @(Get-Content -LiteralPath $script:latestLog.FullName -Tail 25 -Encoding UTF8 -ErrorAction SilentlyContinue)
    foreach ($t in $tail) { W ('    ' + $t) }
    if ($tail.Count -gt 0) {
        $script:Flags.Add('已取到应用日志尾部：蓝屏前最后一行动作是最直接线索，重点看"驱动已启动/初始化成功"之后是否直接断掉。')
    }
} '应用日志读取失败'

# ------------------------------------------------- 7 驱动文件分布与指纹
Sec '7. dd63330.dll / .sys 文件分布与指纹'

$searchDirs = New-Object System.Collections.Generic.List[string]
if ($AppDir) { $searchDirs.Add($AppDir) }
if ($script:loggedDll) {
    try { $searchDirs.Add((Split-Path -Parent $script:loggedDll)) } catch { }
}
$searchDirs.Add((Join-Path $appDataRoot 'drivers'))
$searchDirs.Add($env:TEMP)
$searchDirs.Add((Join-Path $env:ProgramFiles 'TheCelestialDiviner'))
if (${env:ProgramFiles(x86)}) { $searchDirs.Add((Join-Path ${env:ProgramFiles(x86)} 'TheCelestialDiviner')) }

$found = New-Object System.Collections.Generic.List[string]
foreach ($d in $searchDirs) {
    if (-not $d) { continue }
    if (-not (Test-Path -LiteralPath $d)) { continue }
    foreach ($n in @('dd63330.dll', 'dd63330.sys')) {
        $p = Join-Path $d $n
        if ((Test-Path -LiteralPath $p) -and -not $found.Contains($p)) { $found.Add($p) }
    }
}
foreach ($root in @(($env:USERPROFILE + '\Desktop'), ($env:USERPROFILE + '\Downloads'), 'C:\', 'D:\', 'E:\', 'F:\')) {
    if (-not (Test-Path -LiteralPath $root)) { continue }
    try {
        $hits = Get-ChildItem -LiteralPath $root -Recurse -Depth 2 -File -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -eq 'dd63330.dll' -or $_.Name -eq 'dd63330.sys' }
        foreach ($h in $hits) { if (-not $found.Contains($h.FullName)) { $found.Add($h.FullName) } }
    } catch { }
}
if ($imagePath -and (Test-Path -LiteralPath $imagePath) -and -not $found.Contains($imagePath)) { $found.Add($imagePath) }

if ($found.Count -eq 0) {
    W '  常见位置未找到 dd63330.dll / .sys（可能已删除，或装在自定义目录）。'
    W '  若客户的程序装在别的盘，请用 -AppDir "程序目录" 重跑一次。'
} else {
    $idx = 1
    $sysHashes = @{}
    $dllHashes = @{}
    foreach ($f in $found) {
        try {
            $i = Get-Item -LiteralPath $f
            $h = (Get-FileHash -LiteralPath $f -Algorithm SHA256).Hash
            $v = $i.VersionInfo
            $sig = Get-AuthenticodeSignature -LiteralPath $f
            $signer = ''
            if ($sig.SignerCertificate) { $signer = $sig.SignerCertificate.Subject }
            $tag = ''
            if ($script:loggedDll -and $f -ieq $script:loggedDll) { $tag = '    <== 程序当前加载的 DLL' }
            elseif ($imagePath -and $f -ieq $imagePath) { $tag = '    <== 服务 ImagePath 指向的驱动' }
            W ''
            W ("  [" + $idx + "] " + $f + $tag)
            W ("      大小 {0} 字节    修改 {1}" -f $i.Length, $i.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'))
            W ("      SHA256 " + $h)
            W ("      公司 {0}    版本 {1}" -f $v.CompanyName, $v.FileVersion)
            W ("      签名 {0}    {1}" -f $sig.Status, $signer)
            $idx++
            if ($f -like '*.sys') { $sysHashes[$h] = $f } else { $dllHashes[$h] = $f }
        } catch { W ("      读取失败：" + $_.Exception.Message) }
    }
    W ''
    W ("  合计 {0} 个文件；.sys 有 {1} 种内容，.dll 有 {2} 种内容。" -f $found.Count, $sysHashes.Keys.Count, $dllHashes.Keys.Count)
    if ($sysHashes.Keys.Count -gt 1) {
        W '  <== 多个版本的 .sys 同时存在，服务加载的可能不是 DD_btn 配套的那一份。'
        $script:Flags.Add('机器上存在多份内容不同的 dd63330.sys：驱动与用户态 DLL 版本不配套，是本次蓝屏的首要嫌疑。')
    }
    if ($dllHashes.Keys.Count -gt 1) {
        $script:Flags.Add('机器上存在多份内容不同的 dd63330.dll：多个程序目录各放一份，容易与服务加载的驱动版本错位。')
    }
}

# ------------------------------------------------- 8 第三方内核驱动清单
Sec '8. 已运行的第三方内核驱动'
Try-Get {
    $running = @(Get-CimInstance Win32_SystemDriver -ErrorAction Stop | Where-Object { $_.State -eq 'Running' })
    $rows = @()
    foreach ($d in $running) {
        $cp = Normalize-Path ([string]$d.PathName)
        if (-not $cp -or -not (Test-Path -LiteralPath $cp)) { continue }
        try {
            $vi = (Get-Item -LiteralPath $cp).VersionInfo
            if ($vi.CompanyName -and $vi.CompanyName -notmatch 'Microsoft') {
                $rows += [pscustomobject]@{ 驱动 = $d.Name; 公司 = $vi.CompanyName; 说明 = $vi.FileDescription }
            }
        } catch { }
    }
    if ($rows.Count -eq 0) { W '  未发现第三方内核驱动。' } else { WObj $rows 300 }
} '第三方驱动扫描失败'

Sec '9. 键盘/输入类驱动（冲突排查重点）'
$suspectPattern = 'ddhid|kbdflt|keyboard|mouse|kbfilter|interception|vmulti|i8042|logi|lghub|razer|roccat|corsair|icue|bloody|a4tech|dwg|daji|tdriver|hmc'
Try-Get {
    $hits = @()
    foreach ($d in @(Get-CimInstance Win32_SystemDriver -ErrorAction Stop)) {
        if ($d.Name -match $suspectPattern) {
            $cp = Normalize-Path ([string]$d.PathName)
            $company = ''
            if ($cp -and (Test-Path -LiteralPath $cp)) {
                try { $company = (Get-Item -LiteralPath $cp).VersionInfo.CompanyName } catch { }
            }
            if ($company -match 'Microsoft') { continue }
            $hits += [pscustomobject]@{ 驱动 = $d.Name; 状态 = $d.State; 启动 = $d.StartMode; 公司 = $company }
        }
    }
    if ($hits.Count -eq 0) { W '  未匹配到第三方输入类驱动。' }
    else {
        WObj $hits 300
        W '  提示：与 ddhid / Interception / 反作弊的键盘过滤驱动共存时，卸载顺序不对就会踩空指针。'
    }
} '嫌疑驱动扫描失败'

Sec '10. 已安装的相关软件（连发 / 宏 / 模拟器 / 外设工具）'
Try-Get {
    $keys = @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*'
    )
    $pattern = '衍天|连发|按键精灵|大漠|万象|Change Box|模拟器|雷蛇|罗技|Razer|Logitech|Corsair|ddxoft|DD63330'
    $apps = @(Get-ItemProperty $keys -ErrorAction SilentlyContinue |
              Where-Object { $_.DisplayName -and $_.DisplayName -match $pattern } |
              Select-Object DisplayName, DisplayVersion, InstallDate -Unique)
    if ($apps.Count -eq 0) { W '  未匹配到相关软件。' } else { WObj $apps 300 }
} '软件清单读取失败'

# ------------------------------------------------------------- 11 蓝屏记录
Sec '11. 蓝屏记录（BugCheck 事件 / dump 文件）'
$script:dumpFiles = @()
Try-Get {
    $ev = @(Get-WinEvent -FilterHashtable @{ LogName = 'System'; ProviderName = 'Microsoft-Windows-WER-SystemErrorReporting'; Id = 1001 } -MaxEvents 10 -ErrorAction SilentlyContinue)
    if ($ev.Count -eq 0) { W '  没有 BugCheck 事件记录（可能从未蓝屏，或日志被清理）。' }
    else {
        foreach ($e in $ev) {
            $msg = ($e.Message -replace "`r?`n", ' ')
            if ($msg.Length -gt 300) { $msg = $msg.Substring(0, 300) }
            W ("  [" + $e.TimeCreated.ToString('yyyy-MM-dd HH:mm:ss') + "] " + $msg)
        }
        $script:Flags.Add('有 BugCheck 事件：核对参数 2（0 读 / 2 写 / 0x10 执行）与参数 4（0x0 已释放 / 0x2 无页表 / 0xF 访问用户态地址）。')
    }
} '事件日志读取失败'

Try-Get {
    if (Test-Path 'C:\Windows\Minidump') {
        $script:dumpFiles = @(Get-ChildItem 'C:\Windows\Minidump' -Filter *.dmp -File -ErrorAction SilentlyContinue |
                       Sort-Object LastWriteTime -Descending)
    }
    if ($script:dumpFiles.Count -eq 0) {
        W '  C:\Windows\Minidump 下没有 dump（可能未开启小内存转储）。'
        W '  开启方法：系统属性 → 高级 → 启动和故障恢复 → 写入调试信息选"小内存转储(256KB)"。'
    } else {
        W ''
        W '  Minidump：'
        foreach ($d in ($script:dumpFiles | Select-Object -First 10)) {
            W ("    " + $d.Name + "   " + [math]::Round($d.Length/1KB,0) + " KB   " + $d.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'))
        }
        $script:Flags.Add('机器上有 minidump：已自动打包最近几个，可直接用 WinDbg 跑 !analyze -v 定位。')
    }
} 'Minidump 读取失败'

if (Test-Path -LiteralPath 'C:\Windows\MEMORY.DMP') {
    Try-Get {
        $m = Get-Item 'C:\Windows\MEMORY.DMP' -ErrorAction Stop
        W ''
        W ("  完整内存转储：C:\Windows\MEMORY.DMP  " + [math]::Round($m.Length/1MB,0) + " MB  " + $m.LastWriteTime)
    } '完整转储读取失败'
}

Try-Get {
    $k41 = @(Get-WinEvent -FilterHashtable @{ LogName = 'System'; Id = 41 } -MaxEvents 5 -ErrorAction SilentlyContinue)
    if ($k41.Count -gt 0) {
        W ''
        W '  异常关机（Kernel-Power 41）：'
        foreach ($e in $k41) { W ("    " + $e.TimeCreated.ToString('yyyy-MM-dd HH:mm:ss')) }
    }
} '无异常关机记录'

# --------------------------------------------- 12 驱动加载被拦截 / 更新
Sec '12. 驱动被 CodeIntegrity 拦截的记录'
Try-Get {
    $ci = @(Get-WinEvent -LogName 'Microsoft-Windows-CodeIntegrity/Operational' -MaxEvents 100 -ErrorAction SilentlyContinue |
            Where-Object { $_.Id -in 3033, 3077, 3076, 3004 })
    if ($ci.Count -eq 0) { W '  没有拦截记录（或该日志未启用）。' }
    else {
        foreach ($e in $ci) {
            $msg = ($e.Message -replace "`r?`n", ' ')
            if ($msg.Length -gt 220) { $msg = $msg.Substring(0, 220) }
            W ("  [" + $e.TimeCreated.ToString('yyyy-MM-dd HH:mm:ss') + "] ID=" + $e.Id + "  " + $msg)
        }
        $script:Flags.Add('CodeIntegrity 有驱动被拦截/校验失败的记录：25H2 的阻止列表可能已经在动这个驱动。')
    }
} 'CodeIntegrity 日志不可读'

Sec '13. 最近安装的系统更新（判断是否更新后才开始蓝屏）'
Try-Get {
    $wu = @(Get-WinEvent -FilterHashtable @{ LogName = 'Microsoft-Windows-WindowsUpdateClient/Operational'; Id = 19 } -MaxEvents 12 -ErrorAction SilentlyContinue)
    if ($wu.Count -eq 0) {
        WObj (Get-HotFix -ErrorAction SilentlyContinue | Sort-Object InstalledOn -Descending | Select-Object -First 6 HotFixID, InstalledOn)
    } else {
        foreach ($e in $wu) {
            $msg = ($e.Message -replace "`r?`n", ' ')
            if ($msg.Length -gt 150) { $msg = $msg.Substring(0, 150) }
            W ("  [" + $e.TimeCreated.ToString('yyyy-MM-dd') + "] " + $msg)
        }
    }
} '更新记录读取失败'

# ------------------------------------------------------------- 收集附件
Sec '14. 附件打包'
$copied = 0
Try-Get {
    if ($script:latestLog) {
        Copy-Item -LiteralPath $script:latestLog.FullName -Destination (Join-Path $collectDir $script:latestLog.Name) -Force
        $copied++
        W ('  应用日志：' + $script:latestLog.Name)
        $prev = @(Get-ChildItem -LiteralPath $logDir -Filter 'app-*.log' -File -ErrorAction SilentlyContinue |
                  Sort-Object LastWriteTime -Descending | Select-Object -Skip 1 -First 2)
        foreach ($p in $prev) {
            Copy-Item -LiteralPath $p.FullName -Destination (Join-Path $collectDir $p.Name) -Force
            $copied++
            W ('  应用日志：' + $p.Name)
        }
    }
    $n = 0
    foreach ($d in $script:dumpFiles) {
        if ($n -ge 2) { break }
        if ($d.Length -gt 300MB) { W ('  跳过过大转储：' + $d.Name); continue }
        Copy-Item -LiteralPath $d.FullName -Destination (Join-Path $collectDir $d.Name) -Force
        $copied++
        W ('  内存转储：' + $d.Name + '  ' + [math]::Round($d.Length/1MB,1) + ' MB')
        $n++
    }
    if ($copied -eq 0) { W '  没有可附带的文件。' }
} '附件复制失败'

# ------------------------------------------------------------- 自动判读
Sec '15. 自动判读（脚本给的方向，仅供参考）'
if ($script:Flags.Count -eq 0) {
    W '  没有命中任何已知模式。请回传本报告 + 附件，人工分析。'
} else {
    $i = 1
    foreach ($f in $script:Flags) { W ("  " + $i + ". " + $f); $i++ }
}

W ''
W '---------------- 报告结束 ----------------'

$script:Lines | Out-File -LiteralPath $reportPath -Encoding UTF8

$zipPath = $OutDir + '.zip'
try {
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
    Compress-Archive -Path (Join-Path $OutDir '*') -DestinationPath $zipPath -Force -CompressionLevel Optimal
    Write-Host ''
    Write-Host ('已生成压缩包：' + $zipPath) -ForegroundColor Green
} catch {
    Write-Host ('打包失败（报告已单独生成）：' + $_.Exception.Message) -ForegroundColor Yellow
}

Write-Host ''
Write-Host ('采集完成。请把报告 txt 或 zip 回传给开发者。') -ForegroundColor Green
Write-Host ('报告位置：' + $reportPath)
Write-Host ''
if (-not $NoPause) {
    Write-Host '按任意键关闭窗口...'
    try { $null = $Host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown') } catch { }
}
