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
