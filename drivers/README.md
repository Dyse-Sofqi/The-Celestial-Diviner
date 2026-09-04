# drivers/ 目录说明

- `dd63330.dll` — DD 虚拟鼠标键盘驱动（ddxoft）官方 2026 x64 用户态版，
  来源：https://github.com/ddxoft/master （`2026.DD.EV.HVCI.63xxx.7z` 内 `1.simple\dd63330.dll`）。
  仅在"DD 驱动（物理级）"键盘注入模式下使用；缺失时该模式不可用并自动回退普通模式。

- 该 DLL 为原作者（ddxoft / 重庆貔赑貅软件科技工作室）署名发布的原封未修改二进制。
  免费版加载时会做在线授权校验；如需离线/定制授权请联络原作者。

- `dd63330.sys` — 上述 DLL 内嵌的内核驱动完整副本（从 DLL 资源中原样提取，
  微软 Windows 硬件兼容性 WHQL 签名有效，与 DD 自行释放到 %TEMP% 的文件字节一致）。
  用途：DD 的安装例程（释放 sys → CreateServiceW → StartService）受安全软件
  （如火绒）拦截或旧服务"标记删除"未完成时会间歇性失败，弹出"驱动安装错误"
  模态框并阻塞调用线程。程序在 DD_btn(0) 之前检测 `dd63330` 服务，
  缺失时用本文件自行创建/启动服务（见 DdDriverService.EnsureKernelService），
  使 DD_btn 走"服务已存在"的快速路径，规避其安装例程。
