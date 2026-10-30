# drivers/ 目录说明（本目录二进制不入库、不入安装包）

DD 虚拟鼠标键盘驱动（ddxoft）为**闭源第三方组件**，不随本仓库与发布包分发。
如需开发 / 调试 DD 驱动注入模式，请自行从官方渠道获取后放到本目录：

- 官方网站：<http://www.ddxoft.com/>（免费版加载时需联网授权；
  如需离线 / 定制授权请联络原作者）

需要放置的文件（构建发布包时自动带入；缺失时 DD 模式不可用并自动回退普通模式）：

- `dd63330.dll` — DD 官方 x64 用户态版驱动 DLL（原封未修改的二进制）。
  仅在"DD 驱动（物理级）"键盘注入模式下使用。

- `dd63330.sys` — 上述 DLL 内嵌内核驱动的完整副本（WHQL 签名有效）。
  用途：DD 自身的安装例程（释放 sys → CreateServiceW → StartService）受安全软件
  （如火绒）拦截或旧服务"标记删除"未完成时会间歇性失败，弹出"驱动安装错误"
  模态框并阻塞调用线程。程序在 DD_btn(0) 之前检测 `dd63330` 服务，
  缺失时用本文件自行创建/启动服务（见 DdDriverService.EnsureKernelService），
  使 DD_btn 走"服务已存在"的快速路径，规避其安装例程。

- `7zr.exe` — [7-Zip](https://www.7-zip.org) 官方独立精简版（LGPL，仅解 .7z 格式）。
  随发布包分发并入库（LGPL 允许再分发）：DD 驱动缺失时程序自动从 DD 官方发布渠道
  （github.com/ddxoft）下载官方 7z 包，用本工具解出 dd63330.dll 安装到
  %APPDATA%\TheCelestialDiviner\drivers（见 DdDriverService.StartAutoFetch）。

## Interception（双驱动路线第二条，开源）

Interception 是 oblitum 的开源键盘 / 鼠标过滤驱动（LGPL 3.0），官方发布在
<https://github.com/oblitum/Interception/releases>（最新 v1.0.1，`Interception.zip` 约 380KB）。

**可以随包分发**（与 DD 不同）：它的双许可里，非商业分支是 LGPL 3.0，作者明确写了
"with rights of distribution of the related binary assets (drivers and installers)"，
条件是「与驱动的通信仅经由 interception.dll 的 API」——本程序全程只用库导出通信
（连"驱动加载了没"的探测都用 create_context/destroy_context，不直开驱动设备）。

所以本目录下的 `interception\` 子目录（构建时拷到程序目录的 `interception\`）随发布包附带：

- `interception.dll` — 官方 v1.0.1 x64 用户态库（原封未修改、未重签）
- `install-interception.exe` — 官方命令行安装器（安装 / 卸载的唯一入口；驱动本体以其资源形式内嵌其中）
- `LGPL-3.0.txt` — LGPL 3.0 许可全文（随包分发时必须附带，合规要求）

获取方式（三者任一即可，推荐第一个）：

1. 官方 Release：<https://github.com/oblitum/Interception/releases/download/v1.0.1/Interception.zip>
   → 取 `Interception/library/x64/interception.dll`、`Interception/command line installer/install-interception.exe`、
   `Interception/licenses/non-commercial-usage/LGPL 3.0.txt`
2. 直接运行一次程序内「安装 Interception 驱动…」（随包缺失时会走官方下载），
   下载校验后的副本会落在 `%APPDATA%\TheCelestialDiviner\drivers\interception\`
3. 本目录留空也可以：程序会自动回退到官方 Release 下载（联网 + 自动重试）

- 二进制不入库（`.gitignore` 已排除，与 DD 一致）；打包发布前请把上述三个文件放进
  `drivers\interception\`，否则发布包不带驱动、用户首次安装需联网
- 官方 v1.0.1 的 x64 `interception.dll` SHA-256：
  `AB88164C11B1B48488772D4C3BFAA4509D5B0AE9DBC5A691DC4F96F0260443C8`
  （安装器 `install-interception.exe`：`E137863A79DA797F08E7A137280FF2A123809044A888FD75CE9C973198915ABE`）
- 只允许从官方 Release 获取：驱动是内核组件，来路不明的同名文件一律不要用
  （程序的指纹 / 签名双校验会拒绝安装）
- 许可：非商业使用走 LGPL 3.0（允许再分发驱动与安装器二进制，前提是仅经 `interception.dll`
  的 API 与驱动通信，且随包附带 LGPL 全文）；**商业闭源分发需向作者购买授权**
  （francisco@oblita.com，见官方包内 `licenses/commercial-usage/`）

