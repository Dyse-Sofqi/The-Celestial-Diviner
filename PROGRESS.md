# 衍天高手（The Celestial Diviner）开发进度交接

更新：2026-10-25 — **发版 v1.8.8（方案列表滚动跳顶修复 + 键帽 Caps 缩写 / Web 叠加源显示对齐桌面）；双端 Release 已发布**

## 本次变更（发版 v1.8.8）

- ✅ **方案列表滚动跳顶修复**（用户报告"按键方案列表滚动后跳顶"）：根因 = v1.8.2 引入的
  `ScrollUnit=Pixel` + `Recycling` 虚拟化，遇上"两模式三分区方案全量混排、非当前行
  Visibility=Collapsed 隐藏"的组织——行高 0 / ~22px 交错伪随机，视口外行像素偏移靠估算，
  方案少时可滚动余量（几十 px）小于估算误差 → ScrollViewer 钳制回 0 = 整表跳顶。
  修复 = 移除 ScrollUnit=Pixel 回归按项虚拟化（偏移按行索引锚定）；虚拟化 / 回收保留，
  性能不受影响；XAML 注释详录"勿改回像素滚动"的原因防回归。排查中排除了
  RefreshSchemeList 重建（均在显式操作路径）、ScrollToTop（仅切模式有意调用）、
  自绘 ScrollViewer 模板（PART 接线正确）等嫌疑
- ✅ **键帽 Caps 缩写**：CanonicalLabel / KeycapTargetName 对 VK 0x14 输出 "Caps"
  （物理按下与连发脉冲双路径、桌面与 Web 同源），与主界面键位图块一致；方案列表仍显全名
- ✅ **Web 叠加源显示对齐桌面**（浏览器实测截图验证，800×160 与紧凑画布修复前后对比）：
  face 由绝对定位改布局子元素 → 键帽宽随内容自适应（长键名不再换行溢出，内边距 16px
  对齐桌面），文本 nowrap，cap flex:0 0 auto 防行溢出时挤压；root right 8→16px，
  连击角标不再与画布右缘齐平被裁；图标 / 修饰键 / 提醒键帽（demo 模式）验证正常；
  OBS 指南补画布高度 ≥96 约束
- ✅ 版本 1.8.7 → **1.8.8**；CHANGELOG v1.8.8 详录（滚动跳顶 + 键帽显示两段）；
  发布说明 `docs/release-notes/v1.8.8-release-notes.md`；公告无需改（键帽进 OBS 段仍准确）
- ✅ 发布包：`dist/TheCelestialDiviner-1.8.8-win64.zip`（20 项与 1.8.7 一致 = exe + 依赖 dll +
  7zr.exe + 配置 + 8 段候选提示音；zip 1,751,795 B；SHA256 `5cbbf300…2285`；
  exe 1,100,288 B 内嵌 1.8.8；Python zipfile 打包沿用）
- ✅ 双端 Release 已发布：Gitee <https://gitee.com/sofqi/The-Celestial-Diviner/releases/tag/v1.8.8>
  （release id 1237841，publish-gitee-release.sh 建版 + 附件上传）；GitHub
  <https://github.com/Dyse-Sofqi/The-Celestial-Diviner/releases/tag/v1.8.8>
  （release id 422864942，gh-api-release.ps1 自 bf56793 起逐字节复制提交 9c1e92f + 建版 + 传附件）。
  双端 releases/latest 均已指向 v1.8.8 → 程序内「检查更新」可发现并安装

## 本次变更（发版 v1.8.7）

- 📋 v1.8.6 撤回后的续战：用户需求收敛为三件事——OBS 键帽透明叠加、桌面键帽常显、
  游戏前台 + OBS 下不卡。终局方案 = Web 叠加源（1.8.6 的功能主体）+ 五项修复 + 环境级结论
- ✅ **卡顿攻坚结论链（全部有日志/实测证据）**：
  - 数据链路无辜：服务端发送探针零超时（游戏满负载时段）；悬浮层停摆 12.5s 的同一分钟里
    WS 照常毫秒级送达 → 录制画面键帽与桌面键帽**并联不竞争**
  - **MPO 是结构性饿死的根因**：游戏捕获不占 DWM，窗口化游戏走 MPO/直扫绕过合成器 →
    分层窗口（悬浮层）被饿死（12.5s 级停滞）；显示器捕获时代无此问题（桌面复制强制 DWM
    全量合成）。禁用 MPO（`OverlayTestType=5`）后停滞降到亚秒级直至消失，用户实测确认
  - 负载三定律：OBS 画布=输出=1080p60（4K60 合成+编码像素量 4 倍超载 = "巨额延迟"本体）、
    游戏锁帧 60（60Hz 屏多余帧物理不可见）、游戏 4K→2K（1080p 成片下 4K 渲染零贡献）
  - 浏览器源自愈：CEF 初次加载失败不会重试（OBS 先于程序启动 → 空白页）；
    「场景激活时刷新浏览器」+ 先程序后 OBS 的启动顺序解决
  - 混合显卡机器（AMD 核显 + RTX 3060）上全链路实测通过：游戏捕获 + 1080p60 +
    MPO 关 + 锁帧 → 桌面悬浮层与 OBS 键帽同时跟手
- ✅ 五项修复随版：图标请求补令牌（此前退化文字）、页面/图标 no-store（防 CEF 钉死旧页）、
  连发脉冲广播移出悬浮窗线程、端到端延迟探针（页面标题毫秒数 + 服务端发送耗时告警）、
  配置 v19 迁移（清除撤回前开发构建自动落盘的 WebOverlayMuteDesktop=true，桌面键帽恢复常显）
- ✅ 注释区公告（Notice.md）新增键帽浏览器源使用说明（按钮 → OBS 步骤 → 启动顺序 →
  卡顿三步排查 + MPO 命令 + 指南链接）
- ✅ 与 1.8.5 的等价性：git diff 证明悬浮窗/钩子/调度器四文件**零改动**；
  浏览器源关闭时运行差异仅剩无接收者的微秒级序列化
- ✅ 版本 1.8.6（撤回）→ **1.8.7**；CHANGELOG v1.8.7 详录（含 v1.8.6 撤回说明）；
  发布说明 `docs/release-notes/v1.8.7-release-notes.md`；指南 `docs/obs-recording.md` 终稿
- ✅ 发布包：`dist/TheCelestialDiviner-1.8.7-win64.zip`（20 项；zip 1,730,606 B；
  SHA256 `BBE31FAD…64758F3C`）；提交 `300aa13`（发版）→ 注释标签 `v1.8.7`（对象 `2cc24db6`）
- ✅ 双端已发布：Gitee Release id **1237404** + 附件 id 3484613（1,730,606 B）；
  GitHub Release id **422770561**（附件同名同大小）
- ✅ 三端一致核对：本地 = 双端 main = `300aa13`；标签三端均为注释对象 `2cc24db6`；
  raw CHANGELOG 首标题已是 **v1.8.7**（备用通道数据源正确）；直链 200 全尺寸
- ⚠️ **Gitee 标签重写坑复发**：v1.8.5 记录"连续 3 次未复发"，本轮 v1.8.7 推送后
  gitee 标签被重写为轻量标签（指向提交 300aa13）——`git push gitee v1.8.7 --force`
  强制推回注释对象后恢复三端一致；今后每次发版推完 gitee 标签必须 ls-remote 复核
- ℹ️ gh-api-release.ps1 的注释标签复制（中文消息经 cmd 重定向）v1.8.6/v1.8.7 连续两版
  编码失败 → 统一修复法：python 解析本地 tag 对象 → gh api POST /git/tags + PATCH ref
  （脚本层面待修，暂以手工流程覆盖）
- ℹ️ 用户升级路径：1.8.5 检查更新即可发现 1.8.7（主通道 / raw 备用通道均可用）；
  1.8.6 无可用版本（已撤回）

## 历史变更（v1.8.6 → 已撤回）

- 📋 用户需求：OBS 录制游戏时把键帽可视化录进成片（游戏画面 1:1 无标题栏黑边 + 键帽可叠加）。
  **三轮迭代**：①窗口捕获路线（不透明深色底条，`AllowsTransparency` 双模式销毁重建）→ 用户否决
  （底板丑）；②常显 + SizeToContent 自适应 + 底色三选（深色/绿幕/品红）→ 仍否决；③**最终方案 =
  Web 叠加源**：不走窗口捕获，本机 HTTP+WebSocket 服务把键帽状态推给 OBS「浏览器源」（CEF 原生
  透明背景），屏幕观感完全不变、录制画面键帽透明浮动。窗口捕获实现在发版前整体回退（v1.8.5 基线）
- ✅ **平台约束定论**（写进 docs/obs-recording.md）：OBS 窗口捕获不支持透明通道（透明一律合成黑底）；
  OBS 窗口选择列表只列**当前可见**的窗口（悬浮窗空闲隐藏策略与之天然冲突）——这是放弃窗口捕获
  路线的两条根因，不是实现质量问题
- ✅ `WebOverlayService`（新）：`HttpListener` 绑 **127.0.0.1 + localhost 双前缀**（防 localhost 解析
  ::1 连不上）+ 访问令牌（首启生成随配置持久，全路径统一校验）；`/overlay`（内嵌页）+ `/ws`（WebSocket
  广播）+ `/icon.png`（图标 ico→PNG，按版本缓存）；广播 fire-and-forget，单连接信号量串行发送
  （WebSocket 禁并发 SendAsync）；**页面/图标响应加 `Cache-Control: no-store`**（防 CEF 钉死旧页）
- ✅ `WebOverlayPage`（新）：内嵌网页逐参数复刻 keyviz lowprofile 键帽视觉（size=32/帽高 72/圆角 20/
  下沉 8/角标 24），CSS 动画（按压 50ms / 淡出 300ms / 1.2s 独立宽限 / 角标弹跳）；协议
  hello/snapshot/pulse/reminder/scheme/config 六消息；断线 1s 重连；`token=demo` 本地演示模式
  （普通浏览器直接预览样式，本会话用它截图验证 ✓）；提醒键帽 flex order 恒居末位（与桌面层一致）
- ✅ `KeyVisualizerService` 增三条事件流（WebSnapshot/WebPulse/WebReminder）：与悬浮层渲染同一份
  合并后数据，悬浮窗未创建（`--no-overlay`）时照常流出；VM 接线配色/图标/透明度实时同源
  （ApplyAppearance / KeycapSchemeName / VisualizerOpacity 三处）
- ✅ 实修三坑：①**图标请求不带令牌** → 服务端全路径校验 404 → 文字降级（用户报"图标没继承"的根因，
  img URL 补 token 修复）；②**OBS 浏览器源钉死旧页**——页面随连接建立只加载一次，应用重启后 JS 只会
  重连 WS 不会重载页面，修复必须手动"刷新缓存"（日志证据：客户端重连成功 + 零渲染失败告警 +
  16:38 旧页创建时间线）；③gh-api-release.ps1 中文标签消息经 cmd 重定向编码失败 → GitHub 落成轻量
  标签 → gh api 手工补 POST /git/tags + PATCH ref 强制指向注释对象
- ✅ 验证：demo 页浏览器截图（键帽/角标/图标键帽/降级文字全正常）；实机探测 `/icon.png` HTTP 200
  image/png 7091B（PNG 魔数完整）；用户实测 OBS 透明叠加 + 图标继承成功；raw CHANGELOG 首标题
  **v1.8.6** ✓（备用通道数据源正确，1.8.5 客户端检查即发现新版）；直链 302→200 全尺寸 ✓
- ✅ 版本 1.8.5 → **1.8.6**；CHANGELOG v1.8.6 详录；发布说明 `docs/release-notes/v1.8.6-release-notes.md`；
  配置 v18（WebOverlayEnabled/Port/Token，旧档缺省字段反序列化即默认关闭，无迁移代码）
- ✅ 发布包：`dist/TheCelestialDiviner-1.8.6-win64.zip`（20 项；zip 1,724,803 B；
  SHA256 `7A07A422…16E250`）；提交 `51d183e`（发版）→ 注释标签 `v1.8.6`（对象 `30f2fd25`）
- ✅ 双端已发布：Gitee Release id **1236602** + 附件 id 3482582（1,724,803 B）；
  GitHub Release id **422629038**（附件同名同大小）
- ✅ 三端一致核对：本地 = 双端 main = `51d183e`；标签三端均为注释对象 `30f2fd25`
  （GitHub 轻量标签已手工修正）；raw CHANGELOG 首标题已是 **v1.8.6**
- ℹ️ Gitee 发布令牌取自本机 git 凭据库（`git credential fill`，username=oauth2），未新增明文存储
- ℹ️ 用户升级路径：1.8.5 检查更新即可发现 1.8.6（API 主通道或 raw 备用通道均可用）
- ⚠️ **紧急撤回（发版约 1.5 小时后）**：用户实测"游戏窗口最大化 + OBS 运行"下桌面悬浮层秒级停滞
  （心跳 10s / Show 3s / 渲染滞后 1.3s，与 14:11 旧构建 + OBS 窗口捕获时段同病灶——分层窗口与
  游戏 + OBS 捕获/编码争抢 DWM/GPU，非 1.8.6 代码回归；渲染模式各版本恒为 Default 已排除）。
  处置：双端 Release 删除（Gitee 204 / GitHub ✓）、双端 + 本地标签 v1.8.6 删除、
  CHANGELOG v1.8.6 段转「未发布」（raw 备用通道首个 `## v` 回到 v1.8.5，1.8.5 客户端
  检查更新不再误报）、`docs/release-notes/v1.8.6-release-notes.md` 删除。
  **修复方向（随 v1.8.7 发布）**：Web 脉冲广播移出悬浮窗线程（已实现，录制画面键帽不再受
  桌面停滞影响）；桌面悬浮层静音联动 `WebOverlayMuteDesktop`（已实现，默认关）；OBS 侧
  减压（游戏捕获代替窗口捕获 + 关闭浏览器源硬件加速，见 docs/obs-recording.md）——
  18:02 后日志零卡顿告警证明应用侧管线已健康，剩余卡顿在 OBS/GPU 侧待用户按指引验证

## 历史变更（发版 v1.8.5）

- 📋 用户反馈（v1.8.3 点「检查更新」提示"未能从 Gitee 获取新版本"）：诊断出**根因 = Gitee 匿名 API
  按 IP 限流（403 "Rate Limit Exceeded"，0.24s 即拒）**，而 v1.8.4 Release 本身完好（带令牌 200 + 附件在位）；
  旧客户端把非 2xx 静默当"暂无 Release"。用户拍板"修一下" → 随版发 1.8.5
- ✅ 诊断取证（关键三点）：①带令牌请求同一接口 200、tag/附件全对；②匿名请求稳定 403（今天多次复核）；
  ③当天日志无「检查更新失败」异常记录 → 证明走的是"非 2xx 静默返回 null"分支（仅异常路径写日志）
- ✅ `UpdateService` 重构检查流程（返回值 `UpdateCheck?` → **`UpdateProbe`（Check/Failure 互斥）**）：
  - **主通道** `FetchFromReleaseApiAsync`：失败原因分类——403 单独说明"接口限流（稍后再试或到 Release 页
    手动下载）"、404"暂无 Release"、其他非 2xx 带状态码、超时/网络异常/解析失败各说各话
  - **备用通道** `FetchFromRawChangelogAsync`：API 不可用时从 **Gitee raw 的 CHANGELOG.md** 取首个
    `## vX.Y.Z` 标题（「未发布」段不以 v 开头天然跳过），按发版命名约定拼
    `releases/download/{tag}/TheCelestialDiviner-x.y.z-win64.zip` 直链；raw 走 giteeusercontent 静态分发，
    **不在 API 限流体系内**（限流期间实测可用）；确认框注明"经备用通道获取"
  - **附件按名匹配** `^TheCelestialDiviner-.+-win64\.zip$`：替换旧的"第一个 .zip 结尾"——
    Gitee 自动附带源码包（v1.8.4.zip / .tar.gz），旧逻辑靠附件排序侥幸避开，顺序一变就会误选
    （解包无 exe → "更新包内容异常"）
- ✅ 调用方接入：`RunCheckUpdateAsync` 弹窗带原因（`未能从 Gitee 获取新版本：{原因}`）；
  `CheckUpdateDotAsync` 预检失败原因写日志（原"没有更新的版本"与"远端不可用"分开）
- ✅ 验证：raw CHANGELOG 首标题解析 ✓、拼出直链 HEAD 200 且 Content-Length = zip 实际大小 ✓、
  **限流持续期间**（API 匿名仍 403）备用通道端到端可用 ✓；编译 0 警告 0 错误；
  exe 字节扫描注意——**C# 字符串字面量在程序集里是 UTF-16**（#US 堆），要按 utf-16-le 扫，
  UTF-8 扫描只对内嵌资源（Notice/CHANGELOG）有效（本轮踩坑记录）
- ✅ 版本 1.8.4 → **1.8.5**；CHANGELOG v1.8.5 详录；发布说明 `docs/release-notes/v1.8.5-release-notes.md`
  （写明 **1.8.3/1.8.4 用户需手动覆盖安装一次**，旧版检查更新遇限流仍会失败）
- ✅ 发布包：`dist/TheCelestialDiviner-1.8.5-win64.zip`（20 项；zip 1,707,000 B；SHA256 `e540c742…1f72`）；
  提交 `93ab502`（加固）→ `09bc749`（发版）；注释标签 `v1.8.5`（对象 `d770ed09` → 09bc749）
- ✅ 双端已发布：Gitee Release id **1235129** + 附件 id 3478527（1,707,000 B）；
  GitHub Release id **422234581**（API digest 与本地一致）
- ✅ 三端一致核对：本地 = 双端 main = `09bc749`；标签两端均为注释对象 `d770ed09`；raw CHANGELOG 首标题
  已是 **v1.8.5**（备用通道数据源正确，1.8.5 客户端检查将得到"已是最新"）；**Gitee 标签重写坑连续第 3 次未复发**
- ⚠️ 未做运行时验证：限流弹窗文案、备用通道确认框在真实 UI 的表现仅编译 + 端点验证，待用户实测
- ℹ️ 用户升级路径：先手动下载 1.8.5 覆盖安装（一次性），此后检查更新自带限流提示 + 备用通道

## 历史变更（发版 v1.8.4）

- 📋 用户需求：公告渲染加粗（上轮已完成、未发版）+ 激活"成为衍天/莫问高手"按钮除变色外字体也加粗；
  按老规矩发版 1.8.4
- ✅ 加粗渲染（上轮 `b3297a7`，本轮随版上线）：`**文本**` 渲染加粗，加粗在外层切段
  （`**[链接](URL)**` = 加粗超链接；链接文字内嵌 `**` 不支持；未闭合原样显示）
- ✅ 激活态按钮加粗（`9e1bbe8`）：`DivinerVoiceButton` 的 `DivinerVoiceEnabled=True` DataTrigger 里
  追加 `FontWeight=Bold`（金色 AccentGold 之外），未激活保持常规字重
- ✅ CHANGELOG：「未发布」段改写为 v1.8.4 正式条目（加粗渲染 + 按钮加粗）
- ✅ 版本：csproj `Version` / `FileVersion` 1.8.3 → **1.8.4**；编译 0 警告 0 错误（Debug + Release）
- ✅ 发布包：`dist/TheCelestialDiviner-1.8.4-win64.zip`（20 项与 1.8.3 一致；exe 1,020,928 B；
  zip 1,703,893 B；SHA256 `134e79e9…ffaf34c`；Python zipfile 打包——7zr.exe 只出 7z 格式的坑沿用上轮结论）
  - exe 字节扫描：内嵌 CHANGELOG 含 v1.8.4 两节、无「未发布」残留、内嵌公告 = 最新版（群号 + 句号版）✓
- ✅ 发布说明：`docs/release-notes/v1.8.4-release-notes.md`（双端 Release 正文同一份）
- ✅ 提交与标签：`9e1bbe8`（按钮加粗）→ `c22386f`（v1.8.4 发版）；注释标签 `v1.8.4`
  （对象 `0777e7e7` → c22386f）
- ✅ 双端已发布：Gitee Release id **1235125** + 附件 id 3478514（1,703,893 B）；
  GitHub Release id **422227345** + 附件（API `digest=sha256:134e79e9…ffaf34c` 与本地一致）
- ✅ 三端一致核对：本地 = Gitee main = GitHub main = `c22386f`；标签两端均为注释对象 `0777e7e7`
  （剥离 → c22386f）；Gitee raw/main 的 Notice.md 与本地逐字节一致（465 B）；
  **Gitee 标签重写坑连续第 2 次未复发**
- ⚠️ 未做运行时验证：公告加粗渲染、按钮激活态加粗均为编译验证 + 规则脚本验证，待用户实测
- ℹ️ 至此旧版本（≤1.8.3）看不到公告加粗的顾虑解除：玩家升级 1.8.4 后，公告里写 `**加粗**` 即生效

## 历史变更（注释区公告支持加粗，未发版）

- 📋 用户需求：公告渲染加粗语法，"不然每次更新公告看上去观感很差"；并问"注释区是加不了吗"
  ——答：不是加不了，是渲染器一直只实现了链接语法；本次补上
- ✅ `MainWindow.xaml.cs` 公告渲染管线加一层加粗切段（`s_noticeBoldRegex = \*\*(.+?)\*\*`）：
  - 解析顺序：**加粗在外层**（先按 `**…**` 切段）→ 段内再走 `[文本](链接)` + 裸 URL——
    `**[链接](URL)**` 渲染为加粗超链接；链接文字内嵌 `**` 不支持（按字面输出）
  - 加粗 = Run / Hyperlink 设 `FontWeight=Bold`（新增 `MakeNoticeRun` / `AppendSegment`，
    `AppendPlainTextWithUrls` / `AppendHyperlink` 加 bold 参数）
  - 非贪婪 + 未闭合不成对时原样显示（不误伤普通 `**` 文本）；解析规则已用同规则脚本对 7 个
    用例验证（普通 / 纯加粗 / 加粗混链接 / 加粗包链接 / 未闭合 / 含 `*` 内容 / 链接内加粗）
- ✅ CHANGELOG 增 `## 未发布` 段（ChangelogWindow 按既有约定整段跳过，不发版就不进程序内更新日志）；
  写明**旧版本客户端会原样显示星号**——公告写加粗前先考虑旧版观感（远端同步对全版本生效，渲染器才跟版本走）
- ✅ 编译 0 警告 0 错误（Debug + Release）；提交 `b3297a7` 双端已推送
- ⚠️ 未发版：等下一次发版（建议 1.8.4）时随渲染器上线；届时「未发布」段改写为正式版本号

## 历史变更（发版 v1.8.3）

- 📋 用户需求（连续三轮）：Notice.md 公告更新先推送；注释区文本改成可选中复制（方便复制群号）；
  按老规矩发版 1.8.3
- ✅ 注释区可选中复制（Views/MainWindow.xaml + .xaml.cs）：
  - 公告默认内容 `DefaultComment`：TextBlock → **只读 RichTextBox**（IsReadOnly + IsDocumentEnabled）——
    只读富文本下超链接**直接点击**打开（不需要 Ctrl+Click），全文可划选 / Ctrl+C / 右键"复制 / 全选"；
    渲染从 Inlines+LineBreak 改为逐行 Paragraph（Margin=0、LineHeight=20，排版与旧版一致），
    AppendNoticeLine / AppendPlainTextWithUrls / AppendHyperlink 改为接收 InlineCollection（不再直连控件）
  - 悬停说明 `CommentText`：TextBlock → 只读 TextBox（ShowComment / RestoreComment / SyncDdFetchStatus
    全部免改，Text 读写与可见性切换语义不变）；DD 获取进度等状态照常实时刷新
  - 选区高亮：SelectionBrush=AccentPrimary（随日 / 夜换肤）+ SelectionOpacity=0.3；两控件
    Background=Transparent、BorderThickness=0、Padding=0（TextBox 走全局圆角模板，零边框透明后无外观痕迹）；
    RichTextBox 垂直滚动条 Hidden（超出时滚轮仍可滚——旧版 TextBlock 是被 Border ClipToBounds 直接裁掉）
  - 动机：公告新增了玩家群号，TextBlock 不可选中，用户复制群号只能手抄
- ✅ 公告 Notice.md（用户编辑，收编进发版提交）：去 Obsidian frontmatter（避免程序里显示 aliases/tags 行）+
  登龙句微调；此前 `145c9f8` 推过带 frontmatter 的版本，本次推送后 Gitee raw 即为最终干净版
  （旧版本程序启动远端同步拿到的也是干净内容）
- ✅ 版本：csproj `Version` / `FileVersion` 1.8.2 → **1.8.3**；编译 0 警告 0 错误（Debug + Release）
- ✅ CHANGELOG：v1.8.3 详录（改进 · 注释区文本可选中复制 / 公告 · 内容更新）
- ✅ 发布包：`dist/TheCelestialDiviner-1.8.3-win64.zip`（**20 项**与 1.8.2 一致 = exe + 依赖 dll + 7zr.exe +
  配置 + 8 段候选提示音；不含闭源 DD 驱动；exe 1,019,904 B；zip 1,703,262 B；SHA256 `f777ee28…5ba08e`）
  - ⚠️ 打包坑（新）：`drivers/7zr.exe` 是 7-Zip 独立精简版，**只支持 7z 格式**——`7zr a xxx.zip`
    产出的是 7z 文件（zipfile 校验报 BadZipFile）；改用 Python zipfile 打包（非 ASCII 文件名自动置
    UTF-8 标志位，资源管理器 / 解压工具均可识别）
- ✅ 发布说明：`docs/release-notes/v1.8.3-release-notes.md`（双端 Release 正文同一份）
- ✅ 提交与标签：`9c27ab7`（注释区可选中）→ `244ad44`（v1.8.3 发版，含最终版 Notice.md）；
  注释标签 `v1.8.3`（对象 `5bab2fa4` → 244ad44）
- ✅ 双端已发布：Gitee Release id **1235113** + 附件 id 3478482（1,703,262 B）；
  GitHub Release id **422215011** + 附件（API `digest=sha256:f777ee28…5ba08e` 与本地一致，`state=uploaded`）
  - ℹ️ Gitee 发布用的私人令牌从 **Windows 凭据管理器**（`git credential fill`，username=oauth2）现取现用，
    环境变量里没有 GITEE_TOKEN；本次 API 全程可用（建 Release + 传附件一次成功）
- ✅ 三端一致核对：本地 = Gitee main = GitHub main = `244ad44`；标签 `v1.8.3` 两端均为注释对象 `5bab2fa`
  （剥离 → 244ad44）；Gitee raw/main 的 Notice.md 与本地逐字节一致
  （⚠️ raw 端点对 curl 返回防盗链 HTML 壳，须 `-L` 跟到 raw.giteeusercontent.com 再比对）；
  **Gitee 标签重写坑本次未复发**（Release 创建后标签仍为注释对象）
- ⚠️ 未做运行时验证：注释区划选 / 复制 / 超链接点击的实际表现仅编译验证（只读 + IsDocumentEnabled 的
  超链接直点与右键复制菜单是 WPF 内建行为）；待用户实测
- ℹ️ 程序内更新链路：「选项 → 检查更新」/ 底栏「有更新」应能发现 1.8.3（双端 Release 均带 tag + 附件）

## 历史变更（发版 v1.8.2）

- 📋 用户需求（连续反馈 + 发版）：界面"不跟手"要查到底；管理方案习惯"再点一次取消"；
  切换方案热键默认改 F10（不覆盖已自定义）；公告去掉首行 `---`；按老规矩发 1.8.2
- ✅ **拖动发涩的真凶 = 全局低级鼠标钩子**：`WH_MOUSE_LL` 把每个鼠标事件同步送进本进程、
  等回调返回系统才继续投递 → 实测窗口收到的移动消息从 **370~530 次/秒压到约 30 次/秒**。
  严格对照（同口径连续短拖）：钩子开 41/1359ms、43/1437ms；钩子关 **794/2156ms、1004/1906ms**
- ✅ **鼠标检测改 Raw Input**（`RegisterRawInputDevices` + `RIDEV_INPUTSINK` + 隐藏普通窗口 +
  自己的消息循环）。三个坑（都是这轮实测踩出来的）：
  1. `HWND_MESSAGE` 消息专用窗口**收不到** `WM_INPUT`（注册成功但 0 条）→ 换隐藏 `WS_POPUP` 普通窗口；
  2. `RAWMOUSE` 的 `usFlags` 后有 **2 字节填充**（匿名 union 含 `ULONG`）→ 漏了会"收得到消息但按钮事件恒 0"；
  3. 曾加"40ms 时间窗兜底"过滤自注入 → 连发每 26ms 刷新时间戳，把用户**真实松手**吞掉 →
     左键按压方案停不下来；改为**只靠 `ulExtraInformation` 魔数**过滤
- ✅ **切模式卡顿**：`SharedSizeGroup` 共享尺寸收敛 + 每次重建 32 行 → 去掉 `IsSharedSizeScope`、
  固定列宽、两种模式的行一次建好只翻 `Visibility`：**1613ms → 330ms → 17~37ms**
- ✅ **空闲常驻开销**：渲染时钟激励器（1×1 置顶分层窗口 + `RepeatBehavior.Forever` 16ms 动画）
  改按需创建；进程级 `ProcessRenderMode=SoftwareOnly` 移除（它把主窗口也拖进 CPU 光栅化）；
  `timeBeginPeriod(1)` 改为只在连发运行时持有；`CompositionTarget.Rendering` 按需挂接
- ✅ **管理态交互**：左键再点一次 = 取消（右键保留）；只对当前分区/模式生效；
  修掉"进入后 800ms 内的点击被吞"（吞事件保护是误判——WPF `Button.Click` 在**抬起**时触发）
- ✅ **切换方案热键默认 F10**：`ProfileCycleConfig` 默认 `HasKey=true` + `VirtualKey=0x79`，
  新增 `DefaultApplied` 迁移标记——只给"从未设置过"的用户填 F10，已自定义的不动，日后清空也不回填
- ✅ 公告：`Notice.md` / `Notice2.md` 首行 `---` 删除
- ⚠️ 调试埋点仍在（`拖动统计`、`管理态命中测试`、`UI 线程卡顿` 等），量都很轻，先留着

## 历史变更（发版 v1.8.1）

- 📋 用户需求（一轮连续反馈，六项）：驱动没下成功时 DD 模式应灰显；下载过程要有进度、失败重试要可感知；
  手动放入 `dd63330.dll` 为什么没用；已是最新版不该挂红点；注释区不再放版本更新提示，
  更新日志收进菜单、底栏加「有更新」按钮；方案管理按钮改文案 + 随模式换色 + 光标默认
- ✅ 灰显判定（`DdDriverService.IsAvailable`）：**只查文件头不加载 DLL**，判据 = 候选 x64 `dd63330.dll` 在位；
  「在位但初始化失败」仍保持可选（有原因可看、有重试价值）。灰显后补「获取 DD 驱动…」入口，
  否则菜单里就没有重新发起下载的地方
- ✅ 位数坑（本轮最关键的实证）：下载官方包（`2026.DD.EV.HVCI.63xxx.7z`，3,745,599 B）解出——
  `1.simple\dd63330.dll`（3,860,008 B，= 程序用的那份）与 `2.hid\ddhid.63340.dll`（2,241,576 B）+
  `drv\ddhid63340.{sys,inf,cat}` + `install.bat`。**两个 DLL 导出完全相同的 7 个函数**
  （DD_btn/DD_key/DD_mov/DD_movR/DD_str/DD_todc/DD_whl）→ HID 变体 API 层可直接替换，
  成本主要在装它的 HID 驱动。用户手动放的是 32 位那份 → `LoadLibrary` err=193，而原代码只写日志文件，
  界面一声不吭。现加 `ReadPeMachine` 预检 + 明确文案 + 注释区显示；32 位占位文件也不再挡自动获取
- ✅ 获取反馈：`DdDriverService.AutoFetchProgress`（Downloading/Installing/Retrying/Succeeded/Failed）+
  流式分块下载报百分比（300ms 限频）；VM `DdFetchStatus` → 左下注释区（悬停说明优先），
  成功 8s / 硬性失败 20s 自动消退
- ✅ 红点：`_updateDotVisibility` 未初始化 → `Visibility.Visible == 0` → **默认就亮**；
  预检只点亮不熄灭。改为默认 Collapsed + 预检明确二分
- ✅ 更新入口重组：`CHANGELOG.md` 内嵌（`TheCelestialDiviner.CHANGELOG.md`）+ `ChangelogService` +
  `ChangelogWindow`（手写 Markdown 子集 → FlowDocument，跳过「未发布」段）；「选项 → 更新日志」；
  底栏「有更新」按钮（`Visibility` 绑 `UpdateDotVisibility`，与红点同源），点击 → `InstallKnownUpdateAsync`
  → 抽出共用的 `ConfirmAndInstallAsync`（确认框 → 下载 → 重启）
- ✅ 注释区：`Notice.md` / `Notice2.md` 首行版本提示删除；`检查更新` 悬停说明只留功能描述
  （顺带修：该项 Header 是自绘 Grid，按文本查 `MenuComments` 永远落空，说明此前一直没显示）
- ✅ 方案面板按钮：`SchemeManageButtonStyle`（紫/金随 `SelectedMode`、白字 SemiBold、光标 Arrow）
- ⚠️ 未做运行时验证：注释区/窗口排版、灰显与「有更新」按钮的实际出现时机均只做了编译验证

## 历史变更（发版 v1.8.0）
- 📋 用户需求：按老规矩发版 1.8.0；一句话总结公告；Gitee 令牌由用户提供
- ✅ 起因（用户反馈的现场证据链）：v1.7.6 用户在游戏内弹 DD 的「DD install error. / 驱动安装错误.」模态框。
  **反汇编 drivers/dd63330.dll**（安装例程 = 函数 RVA `3430`..`3f1b`，`DD install error.` / `DD start error.`
  各只有一处引用）确定该框**只在安装阶段**弹：`OpenSCManagerW` 失败（err=5 → 内部文案"没有权限访问服务控制"）
  或 `CreateServiceW` 失败且 err≠1073（1072 → "驱动已经标记为删除"；1073 被容忍不弹框）。
  「DD start error.」（驱动启动错误）才是第二段 `OpenServiceW`/`StartServiceW` 失败——**HVCI / 内核签名策略 /
  服务被禁用(1058) 都归这里**；免费版在线认证是设备打开后 `DeviceIoControl(0x2224A4)` 返回码分支
  （另一组标题为"提示"的框）。→ 文件缺失、"目录里没看见 dd63330" 与本框**无关**（DD 释放 sys 到 %TEMP% 的
  `CreateFileW`/`WriteFile` 失败不检查、`CreateServiceW` 也不校验文件存在）；且 1.7.3 起发布包本就不带驱动，
  dll 正确位置是 `%APPDATA%\TheCelestialDiviner\drivers\`
- ✅ 根因（本机日志实证 = 多实例）：
  - `config.json.tmp 正由另一进程使用` 在 **09-05 / 09-06 / 09-11 / 10-11 四天**都出现；
  - `app-20261011.log` 19:39 出现**两组完全相同的退出三连**（"已停止所有连发任务（退出）/ 低级钩子已卸载 /
    定时器分辨率已恢复"，相隔 1.4 s）= 两个进程先后退出；
  - `app-20260905.log` 22:09 的 `服务被禁用(1058) → 恢复失败(1072) → 删除重建仍 1072 → 交由 DD`，
    随后 DD_btn 连续 3 次 `ret=-1`，直到另一实例 22:09:46 退出后**第 4 次重试成功**。
  - 机制：两进程都调 `DD_btn(0)`，DD 的服务项被反复 `DeleteService`/`CreateServiceW` → 一方撞 **1072**
    → CreateServiceW 失败 → DD 弹框（它对 1072 不重试）。附带伤害：两进程都装低级钩子 → **按键注入两次**
    （EchoGuard 是进程内的，消不掉跨进程回环）、`config.json` 互相覆盖、DD 重试期 **UI 冻结**
    （`EnsureReady` 在 UI 线程，5 次退避最长约 6 s）
- ✅ 单实例保护（commit `c33a192`，2 文件）：
  - 新增 `Helpers/SingleInstanceGuard.cs`：命名互斥体 `Local\TheCelestialDiviner.SingleInstance` 为主判据
    （`WaitOne(0)` + 捕获 `AbandonedMutexException` 接管被强杀/崩溃的上一个实例）；**另加同名进程扫描**
    兜住**不带本保护的旧版本**（旧版本不持互斥体，而"两个目录各开一份"正是多开最常见形态），
    扫描命中先等 2.5 s 再判，避免"关掉再立刻开"被误拦
  - 激活已有实例：`EnumWindows` 按 PID 找顶层窗口，**优先标题 == "衍天高手"**（键帽悬浮窗标题是
    `KeycapOverlay`，无标题窗口跳过），`ShowWindow(SW_RESTORE)` + `SetForegroundWindow`；窗口已在前台
    则不打扰（不弹提示），窗口确实找不到才提示一句「已在运行」
  - `App.xaml.cs`：`OnStartup` 最前接 `TryAcquire()`（false → 激活/提示 → `Shutdown(); return`），
    `OnExit` 接 `Release()`
  - P/Invoke 用 `CharSet.Unicode` + **不带 W 后缀**的函数名（带 W 后缀且 ExactSpelling=false 会被解析成
    `xxxWW` 而找不到——静默失效类陷阱）
- ✅ 测试暴露并修掉一个**真 bug**：拒绝启动的分支没有 `Release()` 已取得的互斥体 → 进程退出时互斥体被遗弃
  → 下一次启动收到 `AbandonedMutexException` 并打出**误导性**的"上一个实例崩溃退出，本次接管"日志。已修
- ✅ 验证（独立 harness `%TEMP%\sig-test\`，**直接编译产品源码里的同名文件、不改一行**）6 用例全过：
  T1 单独启动 → True；T2 互斥体被持有 → False；T3 同名旧版本不持互斥体 → False（耗时 3065 ms ≈ 2.5 s 等待）；
  T5 上一步拒绝后无残留 → True 且**无**接管 WARN（验证上面的修复）；T4 所有者 FailFast + 句柄被他人持有
  → True 且**有**接管 WARN（遗弃分支真能走通）；T6 已有实例带真窗口 → False + 找到 hwnd +
  `ActivateExisting()` True（证明 EnumWindows 回调、标题匹配、SetForegroundWindow 全链路可用）
- ✅ 版本：csproj `Version` / `FileVersion` 1.7.9 → **1.8.0**；编译 0 警告 0 错误（Debug + Release）
- ✅ CHANGELOG：v1.8.0 详录（新增 · 单实例保护 / 修复 · 多开引发的 DD 驱动冲突 / 发布包）
- ✅ 公告：Notice.md / Notice2.md 首行 → 一句话总结（1.8.0 单实例保护，提示尽快更新）；按老规矩在
  **publish 之前**改好，并对 exe 做字节扫描验证内嵌公告 = 1.8.0 新行（旧 1.7.9 / 1.7.8 行与 "±20%" 抖动
  文案均已不在；首行 228 B、UTF-8 无 BOM）
- ✅ 发布包：`dist/TheCelestialDiviner-1.8.0-win64.zip`（**20 项**与 1.7.9 一致 = exe + 依赖 dll + 7zr.exe +
  配置 + 8 段候选提示音；不含闭源 DD 驱动；exe 956,928 B；zip 1,670,820 B；SHA256 `90467fc6…429a`）
- ✅ 发布说明：`dist/v1.8.0-release-notes.md`（双端 Release 正文同一份）
- ✅ 提交与标签：`c33a192`（单实例保护）→ `b1f5570`（v1.8.0 发版）；注释标签 `v1.8.0`
  （对象 `db03651b` → b1f5570）
- ✅ **Gitee 已发布**：git push（main + 标签）成功；Release id **1200986** + 附件 id 3374971（1,670,820 B）
  - ⚠️ **Gitee 标签重写坑第 2 次复发**（1.7.8 时首次）：Release API 把已推上去的注释标签改成**轻量标签**
    （ls-remote 只剩提交行、无 `^{}` 剥离行）→ 用 `git push gitee v1.8.0 --force` 把注释对象推回，
    复核 `db03651b` + `^{}` = b1f5570，与 GitHub 完全一致；Release 与附件**未受影响**
    （force 更新引用不同于 DELETE —— 后者会把 Release 变 draft）
- ✅ **GitHub 已发布（`gh-api-release.ps1`）**：两笔提交逐字节复刻（tree / commit 均 MATCH）+
  main → b1f5570 + Release id **410884045** + 附件 1,670,820 B（API `state=uploaded`，
  `digest=sha256:90467fc6…429a` 与本地一致）
  - ⚠️ **标签缺陷第 4 次复发**（`POST /git/tags` 返回 sha 空 → refs 落轻量标签直指提交）——照例按本地标签
    对象逐字节重建（`db03651b` MATCH，tagger 日期一致）+ `PATCH /git/refs/tags/v1.8.0`（type: tag）。
    脚本该缺陷仍待根因排查
- ✅ 三端一致核对：本地 = Gitee main = GitHub main = `b1f5570`；标签 `v1.8.0` 两端均为注释对象 `db03651b`
  （剥离 → b1f5570）；Gitee raw/main 的 Notice.md / Notice2.md 与本地**逐字节一致**；
  双端附件 size / SHA256 与本地一致（GitHub 用 API `digest` 比对，无需下载）
- ℹ️ 待用户实测：本地双击两次验证单实例保护（应切回已运行窗口、不出现第二个进程；
  日志留 "单实例保护：找到运行中实例的主窗口（hwnd=0x…）"）
- ⚠️ 遗留未做：`EnsureKernelService` 最终未就绪（err=1058/1072）时仍会"交由 DD 自行安装" → DD 弹框并冻结 UI。
  单实例保护只消掉了"多开"这一诱因；安全软件 / 游戏反作弊禁用服务那类仍走老路。建议改为预置失败即判定
  DD 不可用 + 回退普通模式 + 可读提示（改动集中在 `EnsureKernelService` / `EnsureReady`）

## 上次变更（发版 v1.7.9）
- 📋 用户需求：三段内嵌默认音频分离出程序（命名「高松灯启动 / 停止 / 切换」）加入语音设置备选，
  默认绑定默认主题与衍天高手主题；每个主题绑定语音方案、随主题切换而切换（四档位绑不同主题，
  切换按键方案时同步换肤 + 换声）；语音设置中提供主题配套的语音方案设置
- ✅ 主题语音方案（commit `8d86489`，16 文件）：
  - `SectThemes.CueFiles`：默认主题 / 衍天高手 = 「高松灯」三段、莫问高手 = 「墨提斯」三段
    （散装文件按名查找，`SoundCueService.FindCandidate` 扫 exe 同级 + audio 子目录）
  - 基础提示音（启动 / 停止 / 切换）从内嵌 Resources 分离：删除 `Resources/Sounds` 三段 mp3
    （git 记录删除），改散装随包分发；内嵌资源只剩两个启动变体（DivinerStartVoice.mp3 /
    MoWenDivinerStartVoice.wav），exe 1,125,376 → 953,856 B；配套文件缺失该用途静音留痕
  - 配置 v17：`ThemeSounds`（三主题各自 Start / Stop / Cycle）+ `NormalizeThemeSounds`；
    迁移：旧全局 `CustomSounds` 克隆到三主题（保持既有听感）；`CustomSounds` 字段保留仅旧版读档兼容
  - `SoundCueService.ApplyTheme(theme, custom)` 整套装载（解析优先级：主题自定义 > 主题配套音频 > 静音）；
    移除 `ApplyCustom` / `ApplyThemeDefaults`；新增试听专用 `_previewPlayer` + `PreviewFile`
  - 语音设置对话框：顶部主题选择器（默认 = 当前激活主题），按主题独立导入 / 重置 / 试听；
    「默认音频」项标注配套来源（「默认音频（高松灯）」/「默认音频（墨提斯）」）；
    编辑当前主题热应用、其他主题切档时经 ApplyAppearance 生效
  - 上轮未发版功能一并入版：成为衍天 / 莫问高手按钮随主题文案（`DivinerVoiceButtonText`）+
    莫问糕手启动音（内嵌）+ 检查更新启动预检红点（v1.7.8 已发布）无变动
- ✅ 版本：csproj `Version` / `FileVersion` 1.7.8 → **1.7.9**；编译 0 警告 0 错误（Debug + Release）
- ✅ CHANGELOG：v1.7.9 详录（新增·主题语音方案 / 变更·提示音分离 / 发布包）
- ✅ 公告：Notice.md / Notice2.md 首行 → **双版合并一句**（131 字，用户指定在 1.7.8 基础上补充 1.7.9 总结）：
  「点选项检查升级1.7.9版本：1.7.8 连发下限降至5ms（约100发/秒）/ 移除±20%抖动 / 检查更新红点提醒；
  1.7.9 语音方案随主题整套切换（默认/衍天=高松灯、莫问=墨提斯）/ 基础提示音分离随包分发 /
  语音设置按主题独立设置，建议尽快更新。」（exe 内嵌公告已字节级验证含双版总结）
- ✅ 发布包：`dist/TheCelestialDiviner-1.7.9-win64.zip`（**20 项** = exe + 依赖 dll + 7zr.exe + 配置 +
  **8 段候选提示音**——高松灯三段 / 墨提斯三段 / 02 wow / 14 ciallo；不含闭源 DD 驱动与使用说明.txt；
  exe 953,856 B；zip 1,669,153 B；SHA256 `59108b3e…93e001`）——项数 14 → 20（基础提示音分离随包）
- ✅ 发布说明：`dist/v1.7.9-release-notes.md`（双端 Release 正文同一份，GitHub body 与本地一致）
- ✅ 提交与标签：`8d86489`（主题语音方案）→ `f7ba23f`（v1.7.9 发版）；注释标签 `v1.7.9`（对象 `d41b9fe` → f7ba23f）
- ✅ **Gitee 已发布**：git push（main + 标签）成功；Release id 1197777 + 附件 1,669,153 B；
  本轮建 Release **未触发**标签重写坑（注释对象完好，带 `^{}` 剥离行——1.7.8 时曾被 API 重写为轻量标签）
- ✅ **GitHub 已发布（gh-api-release.ps1）**：两笔提交逐字节复刻（tree / commit 均 MATCH）+
  main → f7ba23f + Release id 409683697 + 附件 1,669,153 B（API state=uploaded）
  - ✅ 脚本缺陷修复（新边界）：本提交删除了三段 mp3，`Copy-Commit` 的 diff 路径含删除项 →
    ls-tree 取 blob 抛「path missing」；已修——删除路径以 tree API 的 `sha=null`（mode 100644）表达，
    tree SHA MATCH 验证通过（脚本仅存于本地 dist，不入库）
  - ⚠️ 标签缺陷第三次复发（POST /git/tags 响应 sha 空 → refs 落轻量标签直指提交）——照例按本地标签对象
    逐字节重建（`d41b9fe` MATCH，tagger 日期一致）+ 强制改引用（type: tag）；脚本该缺陷仍待根因排查
- ✅ 三端一致核对（发布提交）：本地 = Gitee main = GitHub main = `f7ba23f`；标签 `v1.7.9` 两端均为
  注释对象 `d41b9fe`（剥离 → f7ba23f）；Gitee 附件下载件 SHA256 与本地一致（1,669,153 B）；
  GitHub 附件经 API 核对 size / state（github.com:443 直连再次不可用，下载件 SHA 校验跳过——老毛病）
- ✅ 公告远端：Gitee raw/main 的 Notice.md / Notice2.md 与本地逐字节一致（双版合并总结已生效）
- ℹ️ 待用户实测：切档位验证语音整套切换（高松灯 ↔ 墨提斯，全程无手动设置）；语音设置按主题
  选择 / 试听 / 重置；旧配置自定义音迁移后各主题听感；从 Release 页下载 1.7.9 覆盖安装全链路

## 上次变更（发版 v1.7.8）
- 📋 用户需求：连发频率上限提升至 5ms（确认连按压时长下限一起降）+ 删除 ±20% 随机浮动；
  按老规矩发版 1.7.8，双版 Notice 一句话总结并提示升级；「选项 → 检查更新」添加小红点提醒
- ✅ 连发时序（commit `a834d85`，9 文件）：
  - `MinIntervalMs` / `MinHoldMs` 10 → 5：最高连发约 100 发/秒（周期 = 按压 + 间隔 = 10ms）；
    5ms 为 timeBeginPeriod(1) 下 WaitOne / Sleep 精度的实用下限（实际落点略高）；
    逐帧轮询游戏在 5ms 区间大多采不到样，主要面向 DirectInput / DD / 消息模式与轮询之外场景
  - 移除 ±20% 抖动：删 `TimingJitterPercent` / `TimingJitterCutoffMs` / `JitterMs` / `Rand` 机制，
    按压与间隔直传配置值（配置加载已钳位）；原「间隔 >100ms 精确模式」语义扩展到全部间隔
  - `EchoGuard.MaxPendingPerVk` 128 → 256（100 发/秒 × 2s 过期窗口 = 200，防回环系统性丢失时
    FIFO 名额溢出漏判回环；原定容注释「45 发/秒 × 2s = 90」按新频率改写）
  - 注入失败日志 2 秒限频（`LogSendFailure`，SendInput / PostMessage 两路径共用）：
    5ms 连发被 UIPI 拦截时不再每秒刷约百行错误日志
- ✅ 检查更新红点（commit `20d412a`，3 文件）：
  - 启动后台预检 `CheckUpdateDotAsync`（Gitee releases/latest，静默无弹窗）：发现新版本 →
    「选项 → 检查更新」右上角亮红点（#E5484D 6px，`UpdateDotVisibility`）；
    手动检查确认已是最新 → 熄灭；检查中 DataTrigger 整体替换 Header，红点随之隐藏；
    注释区悬停说明同步（MenuComments）
- ✅ 版本：csproj `Version` / `FileVersion` 1.7.7 → **1.7.8**；编译 0 警告 0 错误（Debug + Release）
- ✅ CHANGELOG：v1.7.8 详录（新增 · 检查更新红点提醒 / 变更 · 连发时序 / 发布包）
- ✅ 公告：Notice.md / Notice2.md 首行 → 「点选项检查升级1.7.8版本：连发间隔与按压下限降至5ms
  （最高约100发/秒）、移除±20%随机抖动、检查更新新增红点提醒，建议尽快更新。」（70 字）
  - 打包顺序教训：首次打包后才发现公告改动未进 exe（内嵌兜底公告仍旧行）→ 重新 publish +
    重打包，并按字节扫描验证 exe 内嵌公告 = 1.7.8 新行（旧行 / 抖动文案均已不在）
- ✅ 发布包：`dist/TheCelestialDiviner-1.7.8-win64.zip`（**14 项**与 1.7.7 一致，不含闭源 DD 驱动；
  exe 1,016,832 B；zip 1,498,237 B；SHA256 `3a648998…b108c3`）
- ✅ 发布说明：`dist/v1.7.8-release-notes.md`（双端 Release 正文同一份，双端 body 与本地完全一致）
- ✅ 提交与标签：`a834d85`（连发时序）→ `20d412a`（红点）→ `6ed4556`（v1.7.8 发版）；
  注释标签 `v1.7.8`（对象 `24871d3` → 6ed4556）
- ✅ **Gitee 已发布**：git push（main + 标签）成功；Release id 1197582（target 6ed4556）+ 附件 1,498,237 B
  - ⚠️ 标签重写坑：创建 Release 的 API 调用（target_commitish）把 Gitee 注释标签引用重写成了
    轻量标签（ls-remote 无 `^{}` 剥离行）——`git push +refs/tags/v1.7.8` 强推恢复注释对象（MATCH）
- ✅ **GitHub 已发布（gh-api-release.ps1）**：三笔提交逐字节复刻（tree / commit 均 MATCH）+
  main → 6ed4556 + Release id 409639004 + 同尺寸附件
  - ⚠️ 脚本建标签缺陷复发（v1.7.7 同类问题第二次）：POST /git/tags 响应 sha 为空、refs 落成
    轻量标签直指 6ed4556——已按本地标签对象逐字节重建（`24871d3` MATCH，tagger 日期一致）并
    强制改引用（type: tag）；脚本缺陷待修（疑与响应解析有关，待复现排查）
- ✅ 三端一致核对（发布提交）：本地 = Gitee main = GitHub main = `6ed4556`；标签 `v1.7.8` 两端均为
  注释对象 `24871d3`（剥离 → 6ed4556）；两端附件下载件 SHA256 与本地完全一致（1,498,237 B）
- ✅ 公告远端：Gitee raw/main 的 Notice.md / Notice2.md 首行均已是 1.7.8 更新提示
- ℹ️ 待用户实测：5/5 极限组合下 DD 模式与扫描码模式的 down/up 配对与启停响应；
  旧版本运行 → 红点亮起 → 检查更新 → 升级 1.7.8 全链路

## 上次变更（发版 v1.7.7）
- 📋 用户需求：实测确认性能修复生效（键帽响应及时、探针零告警）→ 按老规矩总结更新发版 1.7.7，
  双版 Notice 一句话总结并提示更新
- ✅ 性能修复主体（commit `2cdaa8f`，12 文件 +711/-162，经四轮探针定位 + 用户实测四轮验证）：
  - 输入直通：连发启停 / F9 / 切换方案热键在钩子线程零封送直达调度器
    （修复游戏最大化时 F9 爆发回放、松键停发延迟）；F9 以调度器当前态为翻转基准
  - 悬浮窗迁独立 STA 线程（独立 Dispatcher）+ 启动预显示永不 Hide（运行期零
    Show/Hide/SetWindowPos）+ 80ms 呈现节流（状态取最新、连击计数累加）+ 窗口 560×100
  - 进程级软件渲染（RenderOptions.ProcessRenderMode）：iGPU 3D 队列被游戏打满
    （实测引擎 87%、DWM 3%）时键帽呈现走内存位图 + UpdateLayeredWindow，脱离 GPU 队列
  - 1×1 渲染时钟激励器兜底合成节拍；连发任务线程事件化等待（去 2ms 忙轮询）
  - Logger 异步化（后台队列落盘）+ 移除逐键噪音日志；钩子鼠标移动早退
  - 限频延迟探针七件套（输入管线 / DD_key / 回环到达 / 渲染滞后 / 心跳 / Show 耗时 / F9 呈现）
- ✅ 实测验证：修复后两场（含 F9 反复启停 + F1/F2/F7 连发）探针全静默；
  GPU 实测根因 = JX3 3D 引擎占用 87%（DWM 仅 3%），键帽 GPU 提交排队 0.5~3.9s/帧
- ✅ 版本：csproj `Version` / `FileVersion` 1.7.6 → **1.7.7**（exe FileVersion 1.7.7.0）
- ✅ CHANGELOG：v1.7.7 详录（修复·性能 / 修复·其他 / 发布包）
- ✅ 公告：Notice.md / Notice2.md 首行 →
  「点选项检查升级1.7.7版本：性能修复（游戏高负载下键帽卡顿 / F9 延迟 / 启停拖累），建议尽快更新。」
- ✅ 发布包：`dist/TheCelestialDiviner-1.7.7-win64.zip`（**14 项**与 1.7.6 一致，不含闭源 DD 驱动；
  exe 1,015,808 B；zip 1,497,880 B）
- ✅ 发布说明：`dist/v1.7.7-release-notes.md`（使用范围声明 + 修复·性能 + 升级提示 + 使用说明，
  双端 Release 正文同一份）
- ✅ **Gitee 已发布**：git push（main + 标签）成功；Release 已创建
  （id 1196204，target 4020ef3；附件 `TheCelestialDiviner-1.7.7-win64.zip` / 1,497,880 B）
- ✅ **GitHub 已发布（走 REST API）**：`dist/gh-api-release.ps1` 逐字节复刻提交
  （2cdaa8f / 4020ef3，tree / commit 均 MATCH）+ Release id 409165468 + 同尺寸附件
  - ⚠️ 脚本缺陷与修复：标签目标曾取 `-Since` 后第一个复刻提交（commits[0]）——本次发布提交是
    第二个，GitHub 标签一度错误指向 2cdaa8f；已按本地标签对象逐字节重建（1a0fe50 MATCH，
    tagger 日期一致）并强制改引用；**脚本已修**（tag 目标改取 `rev-parse $Tag^{commit}`）
- ✅ 三端一致核对：本地 HEAD = GitHub main = Gitee main = `4020ef3`；标签 `v1.7.7` 两端均指向发布提交；
  两端附件尺寸一致（1,497,880 B）且 SHA256 与本地一致（93E3993B…BD869）
- ✅ 公告远端：Gitee raw/main 的 Notice.md / Notice2.md 首行均已是 1.7.7 更新提示

## 上次变更（发版 v1.7.6）
- 📋 用户需求：总结本轮更新 → 补进 CHANGELOG 与 README；在 GitHub 与 Gitee 发布 **1.7.6** Release；
  notice 里用一句话总结并提醒用户更新
- ✅ 版本：csproj `Version` / `FileVersion` 1.7.5 → **1.7.6**（标题栏与托盘随程序集版本显示 v1.7.6）
- ✅ 文档：**新增 CHANGELOG.md**（1.7.6 详录「新增 / 改进 / 修复 / 发布包」+ 1.7.2~1.7.5 简录 + 更早版本指引）；
  README 更新（方案定制 / 提示音可换两条功能、使用说明补「方案定制」一步、更新软件处链到 CHANGELOG）；
  新增 `audio/README.md`（候选提示音放置说明）
- ✅ 公告：Notice.md / Notice2.md 首行改为单行 1.7.6 升级提示 ——
  「点选项检查升级1.7.6版本：新增方案定制（档位绑主题 + 自定义图标/名称/配色），建议尽快更新。」
  （48 字，按惯例只保留当版一行）；远端 `raw/main` 已同步，实测应用内公告同步源拉到的即新行
- ✅ 发布包：`dist/TheCelestialDiviner-1.7.6-win64.zip`（**14 项** = exe + 9 个依赖 dll + 7zr.exe +
  exe.config + **两个候选提示音 `02 wow.mp3` / `14 ciallo.mp3`**，与 exe 同目录，开箱可在语音设置中选；
  **不含闭源 DD 驱动**）；exe 1.0MB（1.7.5 为 552KB，增量 = 31 个图标资源 + 新代码）
- ✅ 发布说明：`dist/v1.7.6-release-notes.md`（使用范围声明 + 新功能 / 改进 / 修复 + 使用说明，
  双端 Release 正文同一份）
- ✅ 提交与标签：`54fc902`（v1.7.6）+ 注释标签 `v1.7.6`（对象 `9062bf8`）
- ✅ **Gitee 已发布**：`git push`（main + 标签）成功；Release 已创建
  （id 1170225，附件 `TheCelestialDiviner-1.7.6-win64.zip` / 1467548 B）
- ✅ **GitHub 已发布（走 REST API）**：github.com:443 仍不稳定（git 推送 21s 超时 / 认证失败），
  按 1.7.5 的做法用 `api.github.com` 逐字节复刻提交与标签对象（tree / commit / tag 对象 SHA
  与本地完全一致，`MATCH`），再创建 Release（id 397609349）+ 上传同尺寸附件
  - 脚本升级为通用版 **`dist/gh-api-release.ps1`**（`-Tag` / `-Since` / `-Zip` / `-Notes` / `-SkipTag`，
    幂等：重复执行会复用既有 Release 并跳过已存在附件）
- ✅ 三端一致核对：本地 HEAD = GitHub main = Gitee main = `54fc902`；标签 `v1.7.6` 两端均指向发布提交；
  两端 Release 附件尺寸一致（1467548 B）
- 🔁 发布包修订（用户澄清：两个音频非游戏素材、无版权问题，正常随包发布）：
  已重打 zip 为 **14 项**（补入 `02 wow.mp3` / `14 ciallo.mp3`，新尺寸 1492339 B /
  SHA256 `9FFF7375…8606CB`），并**替换两端 Release 附件**，Release 正文与 CHANGELOG / audio/README
  口径同步更正：
  - Gitee：附件接口不返回附件 id（只有 name / download_url），无法单独删旧附件 → **删除旧 Release
    后用修订说明重建并重传**（旧 id 1170225 → 新 id **1170247**，附件列表仅一份）
  - GitHub：按附件 id 删旧附件（592716500）+ `PATCH /releases/{id}` 更新正文 + 重传新附件
  - 两端下载件实测与本地 zip **SHA256 完全一致**、均 14 条目且含两个音频
- ✅ 三端一致核对：本地 HEAD = GitHub main = Gitee main = `6d6fa2c`；标签 `v1.7.6` 两端均指向发布提交；
  两端 Release 附件尺寸一致（修订后 1492339 B）
- ℹ️ 待用户实测：从 Release 页下载 1.7.6 覆盖安装 → 「选项」→ 方案定制 / 语音设置 /
  公告首行是否提示更新

## 上次变更（语音设置：程序目录候选音频入下拉列表）
- 📋 用户需求：把两个音频加入语音设置作为候选，**不集成进软件**、只放入软件目录，
  与手动导入的音频一样可被识别进下拉列表供选择
- ✅ 候选音频机制：`SoundCueService.ScanCandidates()` 扫描**程序目录**（exe 同目录 + 可选 `audio` 子目录）
  下的 mp3 / wav / m4a / aac / wma（与「导入…」过滤器一致）并按文件名排序；文件**不进资源**
  （不是 Resource / EmbeddedResource），运行期散装识别，用户可随时增删替换
- ✅ 语音设置对话框：每行的「当前音频名」文本改为**下拉列表** —— 默认（内嵌）/ 程序目录候选音频 /
  当前已导入的自定义音频（存储名不在程序目录时追加「…（已导入）」项）；选择即与「导入…」同效
  （复制到 `%APPDATA%\TheCelestialDiviner\sounds\` + 热应用 + 落盘）；
  选中态按「存储名 = `{用途前缀}_{原文件名}`」后缀匹配回候选，手动导入项单独成项；
  导入 / 试听 / 重置按钮保留（下拉与导入两条路径等价）
- ✅ 修复递归：程序化刷新下拉（ItemsSource / SelectedIndex 赋值）会触发 SelectionChanged，
  形成「刷新 → 选择变更 → 再刷新」无限递归（探针实测栈溢出）→ 加 `_refreshing` 抑制，
  并把「默认 / 候选 / 已导入」改成显式项类型，不再靠文案前缀判断
- ✅ 音频放置：仓库新增 `audio/`（`.gitignore` 忽略 `audio/*`，仅保留说明 `audio/README.md`），
  用户给的两个音频（`02 wow.mp3` / `14 ciallo.mp3`）放入其中；csproj 以 **None**（非 Resource）
  在构建 / 发布时拷到程序目录（exe 同级）——即"只放入软件目录、不集成进程序"
- ✅ 验证（探针实测）：
  - 程序目录扫描：扫描到 2 个候选（02 wow.mp3 / 14 ciallo.mp3）
  - 语音设置实机构造：3 行下拉；每行项 = 默认（内嵌）+ 2 个候选；
    「开启」行（手动导入、不在程序目录）→ 追加「（已导入）」项且为选中态；
    「关闭」行（从候选导入）→ 后缀匹配命中候选并回填选中；
    「方案切换」行（无自定义）→ 选中「默认（内嵌）」
  - 构建后 exe 同目录实际存在这两个 mp3（Debug / Release 均验证）
- ✅ 构建 0 警告 0 错误（Release / Debug）
- 🔁 用户追加（下拉文案精简）：默认项 → 「默认音频」；程序目录候选 → 去路径 / 去扩展名的短名
  （`02 wow` / `14 ciallo`）；已导入项 → 去用途前缀 / 去扩展名的短名 +「（已导入）」；
  下拉宽度上限 260（超长名截断，不撑宽对话框）；`SoundCueService.OriginalFileName / ShortDisplayName`
  两个短名助手（只按本用途前缀剥离，避免误吃文件名里原有的下划线）
- ❌→✅ 展示文本缺陷（用户指出"尤其是路径没有必要展示"）：上一版刷新下拉时**漏写 `DisplayMemberPath`**
  → ComboBox 落到记录 `ToString()`，把整条记录连候选**完整路径**一起显示
  （形如 `CueOption { Label = 02 wow.mp3, CandidatePath = E:\...\02 wow.mp3 }`）。
  修复：显式 `DisplayMemberPath = nameof(CueOption.Label)` + 记录重写 `ToString() => Label` 双保险；
  **探针同步改为"按 ComboBox 实际取文本方式"校验**（有 DisplayMemberPath 用它、否则用 ToString）
  并断言显示名不含路径分隔符 —— 原探针只读 `Label` 属性，所以漏检了这个 bug（教训已记在此）
- ⚠️ 待用户实测：语音设置里从下拉选「02 wow / 14 ciallo」→ 试听 → 总开关开关 / 切换档位播该音频
- ℹ️ 说明：候选音频只在**程序目录**扫描（不入库、不内嵌）；选中后会复制到语音目录，
  故删掉程序目录里的原文件后仍可用

## 上次变更（方案命名 → 方案定制：档位主题绑定 + 默认主题自定义外观）
- 📋 用户需求：「方案命名」改名「方案定制」并提升为「选项」菜单第一项；对话框在名称文本框后加主题预设
  下拉（仅默认主题 / 衍天高手 / 莫问高手三个），供方案绑定主题；默认主题继承现在的衍天高手样式、
  原先的衍天高手主题图标改为太玄经、应用默认使用默认主题；**只有绑定默认主题的档位**才提供后续定制：
  应用图标（用户补充的图标 + 程序在用的图标）、应用名文本框（窗口名，版本号不变）、键帽配色下拉、
  开关 / 按压 / 双宏第二配色选择器
- ✅ 需求澄清（提问确认）：① 主题**每个方案各绑一个**（4 个下拉，切档位 = 整套换肤）；
  ② 自定义项**每个方案各一套**（①..④ 各自独立的图标 / 应用名 / 键帽配色 / 三色）；
  ③ 绑定预设主题时**不展示**定制项（仅默认主题提供定制）
- ✅ 主题模型改为三预设（`SectThemes.All`，下标即持久化值）：
  - 0 默认主题：继承衍天高手原样式（#836899 / #cea23a / #a364ea、app.ico、Notice.md、Pansy），
    `Customizable = true`（全项可被档位覆盖）；默认应用名「衍天高手」
  - 1 衍天高手：同紫金配色，**图标改为太玄经**（用户指定），不可定制
  - 2 莫问高手：青绿 #31CBB6 / 翠绿 #66BB4D / 青碧 #2BE9F6、mowen.ico、Notice2.md、Green，不可定制
- ✅ 图标注册表 `SectIcons`（33 项 = 程序自带衍天 / 莫问 + 31 个门派心法图标）：用户提供的 48×48 PNG
  转制为多尺寸 ICO（`Resources/icons/<key>.ico`，16/24/32/48），键为 ASCII 标识
  （如 `taixuanjing` 太玄经）、显示名为心法名 —— 第一批 20 个（输出心法）+ 第二批 11 个（治疗 / 防御心法：
  铁牢律 / 洗髓经 / 相知 / 笑尘诀 / 易筋经 / 云裳心经 / 补天诀 / 离经易道 / 灵素 / 明尊琉璃体 / 铁骨衣）
- ✅ 图标画廊加高度上限（`ScrollViewer MaxHeight=190`）：图标数量继续增长时画廊内部滚动，
  对话框高度不超出屏幕（当前 33 项 = 4 行 ≈ 184px，无滚动条）
- ✅ 外观解析 `Models/ProfileAppearance`：`Resolve()` 产出 `ResolvedAppearance`（自定义覆盖仅对
  可定制主题生效；图标键 / 配色名 / 色值非法即回退主题预设）；`Normalize()` 统一校验
  （主题下标钳位、图标键与配色名合法性、应用名去空白限 16 字、色值 `#RRGGBB` 规范化）
- ✅ 换肤链路统一为「按当前档位外观应用」：`MainViewModel.ApplyAppearance()` 一次完成
  强调色（主题资源 + 键位图块刷子）、键帽配色、应用图标（窗口 / 任务栏 / 托盘 / 状态提醒与调整模式键帽）、
  应用名（窗口标题 = 应用名 + 版本号、托盘提示）、注释区公告（默认 / 衍天 → Notice.md，莫问 → Notice2.md）；
  **切换方案档位即调用**（`ActiveProfile` setter / 切换方案热键），方案定制确定与导入配置同样走它
- ✅ 公告远端同步按主题去重（仅当公告源真的变化才发请求），避免切档位刷请求
- ✅ 键帽配色菜单：当前档位绑定可定制主题时同步写入该档位外观（随档位持久化）；预设主题下仅临时应用
- ✅ 对话框 `Views/ProfileCustomizeWindow` 重做（旧 `ProfileNamingWindow` 删除）：档位页签 ①..④
  （已命名的显示名称）、方案名称 + 主题预设下拉、默认主题定制区（应用图标 22 项画廊 / 应用名 +
  「留空 = 主题默认名」提示 / 键帽配色下拉 / 三色 ColorField）、恢复默认（当前档位）、取消 / 确定，
  回车即确定；切页签前先收下当前档位编辑内容；切到预设主题时定制区**不展示**（已填定制值保留，
  切回默认主题恢复）
- ✅ 新增 `Views/ColorField`（配色选择控件，纯代码）：色块 + `#RRGGBB` 输入框 + 点击展开的
  24 色预设色板（主题资源自适应）；下拉复用 App.xaml 既有主题化 `RoundedComboStyle`
- ✅ 菜单：移除「门派主题」（被档位主题绑定取代），「方案定制」置于「选项」第一项，
  悬停说明走注释区（MenuComments）
- ✅ 配置 v16：`ProfileAppearances`（4 套外观定制）取代全局 `SectTheme` 字段；默认 = 四档位均绑默认主题
  （「应用默认使用默认主题」）；旧配置的全局主题字段被忽略（无需迁移动作）
- ✅ 验证（探针实测，只读注入 `_config`、不触碰用户配置文件）：
  - 主题表：0 默认（可定制，图标 = 衍天）/ 1 衍天（图标 = 太玄经）/ 2 莫问（青绿三色）✓
  - 图标注册表 33 项**逐个实际解码**：均 4 帧 / 最大 48×48（31 个新 ico 资源确认嵌入且可加载）✓
  - 解析：默认主题 + 自定义（图标无方 / 应用名 / Blue / #123abc）→ 生效；非法金色 → 回退预设；
    预设主题携带自定义项 → 全部忽略 ✓；`ApplyTheme` 后主题资源与键位图块刷子均为自定义色 ✓
  - 归一化：主题 9 → 2、-1 → 0；未知图标键 / 配色名 → 空；`112233`（缺 #）→ 空、
    `#abcdef` → `#ABCDEF`；应用名去空白并截断 16；名称截断 12；补齐 4 项 ✓
  - 往返 / Clone 一致且为独立实例；默认档位主题 = 0；配置版本 v16；档位数 4 ✓
  - 方案定制对话框实机构造成功：档位页签 4 个 / 图标画廊 33 项 ✓
- ✅ 构建 0 警告 0 错误（Release / Debug）
- ⚠️ 待用户实测：绑定莫问高手 / 衍天高手的档位切换后外观（配色、标题与图标、托盘、注释区公告）
  与默认主题自定义项是否如预期；预设主题下定制区不展示是否符合手感
- ℹ️ 说明：应用名自定义仅影响窗口标题与托盘提示（exe 文件名 / 文件图标仍是衍天高手，未在本次范围）

## 上次变更（「选项」→ 方案命名）
- 📋 用户需求：「选项」菜单末尾加「方案命名」，点击可给四个方案重命名；
  切换方案时键帽提示的文本也从序号文本改用重命名后的文本
- ✅ 配置模型 v15：`AppConfig.ProfileNames`（4 项，空串 = 默认序号）；`ProfileCount = 4` 常量
  收敛到 `AppConfig`（`MainViewModel.ProfileCount` 引用它，避免两处各写一个字面量）；
  归一化 `AppConfig.NormalizeProfileNames()`（补齐 / 截断到 4 项、去首尾空白、超长截断到
  `Constants.MaxProfileNameLength = 12`）——加载、导入、对话框确定三处共用同一实现
- ✅ VM 命名 API：`ProfileNames`（只读快照）/ `ProfileName(index)` / `ProfileDisplayName(index)`
  （自定义名优先，未命名回退序号 ①②③④）/ `ProfileTabText(index)`（未命名 = "方案①" 原样式，
  已命名 = 自定义名）/ `SetProfileNames(...)`（整体替换 → 归一化 → 落盘 → 通知刷新）
- ✅ 键帽提示文本改用命名：`CycleToNextProfile` 的 `ShowCycleKeycap(ProfileDisplayName(index))`
  （原为 `ProfileLabel(index)` 序号）；日志与"\...已被方案X的连发键占用"提示同步改用显示名
- ✅ 档位标签：`MainWindow.ApplyProfileNames()` 按 VM 刷新四个 RadioButton 的 `Content` 与悬停说明
  （未命名 = 方案①（默认）/ 方案②…，已命名 = "PVE" + "（自定义名称，档位 ①）"），
  初渲染 + `PropertyChanged(ProfileNames)`（命名确定 / 导入配置）双路驱动
- ✅ 新增模态框 `Views/ProfileNamingWindow`（模板对齐 VoiceSettingsWindow）：四行
  「序号 + 名称输入框」（MaxLength = 12）、恢复默认 / 取消 / 确定，回车即确定、Esc 取消、
  打开即聚焦第一档；「恢复默认」只清空输入框，仍以确定为准（避免误清）
- ✅ 菜单：「方案命名」置于「选项」**末尾**（检查更新之后），动作项 `Tag="action"` + `MenuSubStyle`，
  走 `RenameProfilesCommand` → `ProfileRenameRequested` 事件（与语音设置同一套"命令 + 事件"弹窗模式），
  悬停说明走注释区（MenuComments）
- ✅ 验证（探针实测，无副作用：未初始化 VM 实例 + 反射注入 `_config`，不触碰用户配置文件）：
  - 归一化：`"  PVE  "` → `PVE`；14 字名称 → 截断为 12 字；`null` → 空；只给 1 项 → 补齐 4 项
  - 显示名回退：`[只有一项, 空, 空, 空]` → 显示名 `只有一项 | ② | ③ | ④`、
    档位标签 `只有一项 | 方案② | 方案③ | 方案④`
  - JSON 往返 + Clone 一致（列表为独立实例，非共享引用）；配置版本 v15；
    `MainViewModel.ProfileCount == AppConfig.ProfileCount == 4`；默认序号 ①②③④
  - 对话框实机构造通过（`App.xaml` 全局样式解析通过）、现状回填正确、按钮为 恢复默认 / 取消 / 确定
  - 17 → 18 个 `MenuItem` 全部显式样式（lint）
- ✅ 构建 0 警告 0 错误（Release / Debug）
- ⚠️ 待用户实测：命名确定后档位标签与切换方案（热键）时的提示键帽是否显示自定义名；长名称（12 字）
  键帽会相应变宽（键帽宽度自适应，不截断）
- ℹ️ 实现提示：net48 无 `System.Range`/`System.Index`，切片 `s[..n]` 编译失败（CS0518）→
  统一用 `Substring(0, n)`

## 上次变更（成为衍天高手 / 语音设置 / 切换方案设置 的 Tooltip 移入注释区）
- 📋 用户需求：把「成为衍天高手」「语音设置」「切换方案设置」三个按钮的 Tooltip 提示文案
  移动到注释区展示（与全应用「悬停说明走注释区」的既有约定一致）
- ✅ 「成为衍天高手」「语音设置」：按钮加 `x:Name`（DivinerVoiceButton / VoiceSettingsButton）
  + `MouseEnter="OnBottomBarCommentEnter"` / `MouseLeave="OnCommentMouseLeave"`，
  ToolTip 移除；文案登记进 `BottomBarComments`（与状态提醒 / 全局开关热键 / 调整可视化位置同一通道）
- ✅ 「切换方案设置」：该区（StackPanel）本就经 `OnCycleKeyCommentEnter` 把说明送注释区，
  原有 `CycleKeyComment` 已完整覆盖按钮 Tooltip 的全部信息（①②③④ 顺序切换 / 点击进入录入 /
  左键或直接按键设置 / 右键取消）并多出 Esc 与空白处退出 → 直接移除按钮 ToolTip，文案零丢失
- ✅ 字典与注释同步说明「底栏按钮说明一律走注释区，不再用 ToolTip（同源避免两套文案不一致）」
- ✅ 核验：XAML 中三个 ToolTip 均已消失（余下 3 处为夜间模式 / 键帽透明度 / 音量图标钮，未在本次范围）；
  两个按钮的 `x:Name` 与 `BottomBarComments` 键一一对应；切换方案区仍挂 `OnCycleKeyCommentEnter`
- ✅ 构建 0 警告 0 错误（Release / Debug）
- ⚠️ 待用户实测：悬停三个按钮时注释区文案是否正确显示、移开后恢复默认公告

## 上次变更（门派主题：衍天高手 / 莫问高手）
- 📋 用户需求：「选项」菜单新增第一项「门派主题」，子菜单默认选中「衍天高手」、另可选「莫问高手」；
  选中莫问后：窗口标题图标改为门派图标、标题名「莫问高手」+ 版本号、开关模式 #2BE9F6、
  按压模式 #66BB4D、双宏第二配色 #31CBB6、注释区默认文本取自 Notice2.md、键帽配色切 Green、
  状态提醒常驻键帽 / 任务栏小图标 / 调整可视化位置键帽图标换成莫问图标
- 🔁 用户追加：**双宏第二配色与开关模式配色互换** → 开关模式 #31CBB6、双宏 #2BE9F6
  （只改 `Constants` 两个莫问常量取值，槽位映射与界面各处自动跟随；注释区文案同步）
- ✅ 抽象 `Helpers/SectThemes.cs`：门派主题 = 四类落点的唯一入口（标题名与图标 / 三个强调色槽位 /
  公告资源与远端地址 / 键帽配色预设），下标即持久化值，越界回退默认门派：
  - 衍天高手（0）：#836899 / #cea23a / #a364ea、app.ico、Notice.md、Pansy
  - 莫问高手（1）：#31CBB6 / #66BB4D / #2BE9F6、mowen.ico、Notice2.md、Green
- ✅ 换肤链路（强调色只有一处落点，故全界面自动跟随）：`App.ApplyTheme(isDark, sect)` 写
  AccentPrimary / AccentGold / ThemeRoundCheckFill，`KeySourceViewModel.UpdateTheme(isDark, sect)`
  重建键位图块刷子 → 开关 / 按压 / 双宏图块、模式分段条、档位标签、横幅、热键录入提示、
  圆形勾选框、悬浮层虚线框随之整体换色
- ✅ 图标：用户提供的 莫问10447.png（48×48）转制为多尺寸 `Resources/mowen.ico`（16/24/32/48）；
  窗口图标（= 任务栏按钮图标）、托盘图标（含开启态绿点合成）、状态提醒常驻键帽、
  调整模式虚拟键帽四处同源（`KeycapOverlayWindow.SetAppIconUri` + `RefreshAppIcon`）
- ✅ 标题栏与托盘提示随门派（「莫问高手 v1.7.5」）；切换门派时托盘图标重建（旧图标 Dispose，防句柄堆积）
- ✅ 注释区公告双源：Notice2.md 为用户提供（内容未改动）→ 内嵌 `TheCelestialDiviner.Notice2.md`
  + 远端 `Constants.Notice2RemoteUrl`；缓存各自独立（`NoticeContent` / `MoWenNoticeContent`），
  切换门派即换默认文本并后台同步该门派公告（期间再次切换则丢弃本次结果）
- ✅ 键帽配色预设：切莫问 → Green（记住原配色）；切回衍天且当前仍是 Green（用户未手改）→ 还原原配色，
  缺省回 Pansy；用户已手改则尊重用户选择
- ✅ 设置菜单：「门派主题」为「选项」第一项，两个叶子（Tag 0/1）走 `SetSectThemeCommand`；
  ● 选中标记纳入 `SyncSettingsMenu`（菜单展开 + VM 变更双路同步），悬停说明走注释区（MenuComments）
- ✅ 配置 v14：`SectTheme` + `MoWenNoticeContent`（Clone / 导入导出同步；字段有默认值，无需迁移动作）
- ✅ 验证（探针实测，非推断）：
  - 调真实 `App.ApplyTheme` 后读主题资源 + 键位图块静态刷子（反射）：衍天 日/夜 = #836899 / #cea23a
    （夜间圆形勾选框取金），莫问 日/夜 = #31CBB6 / #66BB4D，双宏 = #2BE9F6；键帽预设 Pansy ↔ Green 解析正确
  - 图标资源：两个 ico 各 4 帧、最大 48×48；`System.Drawing.Icon` 载入 + 32px 绿点合成（与
    `App.SetupTray` 同路径）成功
  - 内嵌公告：Notice.md 146 字 / Notice2.md 92 字，远端地址各自独立
  - 渲染探针（样式从主程序 XAML 现场抽取）：一级 7 行几何一致（行高 24 / 字号 11 / 同一文本起点），
    「门派主题」居首；二级选中态像素 = **#31CBB6 底 + 白字**（互换后门派色确实落进菜单选中态）
  - 配置往返：SectTheme / MoWenNoticeContent 序列化 + Clone 一致；默认门派 0；版本 v14
  - 17 个 `MenuItem` 全部显式样式（lint）
- ✅ 构建 0 警告 0 错误（Release / Debug）
- ⚠️ 待用户实测：切换门派后的整体观感（尤其夜间模式下青绿 / 翠绿 / 青碧三色的对比度）
- ℹ️ 本次未纳入（仍为衍天文案，需要的话下一轮一并跟随门派）：托盘右键菜单项文本、
  「成为衍天高手」按钮与「衍天高手启动」语音、消息框标题、README 与安装包品牌名

## 上次变更（导入 / 导出配置移入「选项」菜单）
- 📋 用户需求：把底栏的「导入配置」「导出配置」按钮移入「选项」菜单
- ✅ 底栏移除两个按钮（`ImportCommand` / `ExportCommand` 绑定原样迁移到菜单项），
  底栏左起变为 [☰选项][调整可视化位置][成为衍天高手][语音设置]；菜单键与「调整可视化位置」
  之间的 8px 间隙由 Menu 右边距天然保留（无新增间距代码）
- ✅ 菜单新增两个动作项（`Tag="action"`，与「检查更新」同款 → 折叠 ● 占位列，
  与上方分组项同为 14px 文本起点）：位置在「键帽可视化」分组项之后、「检查更新」之前
  （检查更新保持末位，其“检查中…”禁用态不受影响）；悬停说明走注释区（MenuComments 通道）
- ❌→✅ **样式遗漏（用户指出“样式应该和其他项统一”）**：首次提交两项漏写
  `Style="{StaticResource MenuSubStyle}"` → 落到系统默认 MenuItem 模板（行高 22 / 字号 12 /
  系统缩进与高亮，日间观感突兀、夜间模式不可用）。已补齐，并加一道全文件 lint：
  14 个 `MenuItem` 元素全部显式指定样式（属性写法或 `<MenuItem.Style>` 属性元素）
- ✅ 样式统一性验证（渲染探针：样式由脚本从 MainWindow.xaml **现场抽取**、非手抄 → 不会漂移）：
  - 逐行量测一级 6 行：行高均 24.0、字号均 11、前景均 ThemeFg #1F1F1F、文本起点一致；
    `导入配置 / 导出配置 / 检查更新` 三行动作项逐项同值（`●`/`▸` 均 Collapsed），
    与三个分组项（`▸` Visible）同文本起点 —— 参考项 `检查更新` 本就是该样式，故已完全统一
  - **对照实验**（同款动作项但不写 Style）：行高 22.0 / 字号 12 / 量不到模板内 `Bg`・
    `ContentPresenter`（系统模板）→ 证明该量度确实能识别此缺陷（修复前即此形态）
  - PNG 逐行像素对照（2x 渲染）：六行行内背景同为 ThemePanel `#F0F0F0`
    （鼠标恰好悬停的那一行 `#E8E8E8` = 动作项悬停色 ThemeButtonBg，顺带验证悬停态生效），
    文本起点横向像素一致；调色板含 ThemeBorder `#CCCCCC` / 文字 `#1F1F1F` / 选中紫 `#836899` /
    白字，二级「键盘注入」四行同为行高 24 且选中行紫底白字
- ✅ 注释口径顺带修正：`ImportFromJson` 整体替换 `_config`、`ExportToJson` 序列化整份
  AppConfig（方案档位 + 热键 + 各项设置），原文案“当前档位的连发键配置”不准确 ——
  底栏 ToolTip 随按钮移除，注释区新文案按实际语义写（导入 = 整份覆盖并立即生效）
- ✅ 注释同步：底栏占位注释、方案面板「导入 / 导出配置：已移至…」注释
- ✅ 底栏腾出约 136px；窗口 MinWidth 维持 980（不擅自放宽最小宽度限制，避免复现底栏拥挤）
- ✅ 构建 0 警告 0 错误（Release）；未改版本号
- ⚠️ 待用户实测：菜单项点击仍走原文件对话框路径（导入 JSON / 导出 JSON），行为不变

## 上次变更（「选项」菜单上拉 → 下拉）
- 📋 用户需求：界面左下角的「选项」菜单由向上弹出改为下拉
- ✅ `MenuPlacements.AboveTarget` → **`BelowTarget`**：首选左上角对齐菜单键按钮左下角
  （向下展开、左对齐，保留 2px 纵向呼吸间隙）；`MainWindow.xaml` 一级弹层 `x:Static`
  引用与注释同步（二级 `RightOfTarget` 未动 —— 横向路径的 0 间隙悬停语义不受影响）
- ✅ 屏幕下缘自动上翻（WPF 源码取证 + 探针实测，不靠猜）：
  - WPF `Popup.UpdatePosition()` 对 Custom 摆位的**多个候选**按「候选矩形与屏幕边界相交面积」
    评分择优（同分取靠前候选，候选完全可见即提前 break），并非只用第一个候选 ——
    故回调同时返回「下方（首选）+ 上方（备选）」两个候选
  - `Popup.GetScreenBounds()` 在 `TemplatedParent is MenuItem` 时取显示器**工作区 rcWork**
    （任务栏不计入可用高度）→ 窗口最大化 / 贴屏幕下缘时下拉放不下，自动翻到按钮上方，
    菜单不会落到屏幕外
- ✅ 实测 A（独立 harness 引用主程序集，复刻同款菜单模板 + 真实定位回调，2x DPI 屏）：
  窗口底部距工作区底边 300 DIP → 按钮底边 Y=1560 / 弹层顶 Y=1562 = **下拉**（2 DIP 间隙）
- ✅ 实测 B（同上，按钮底边距工作区底边 11 DIP = 最大化几何）：弹层翻到按钮上方
  （弹层底 Y=2085 ≈ 按钮顶 Y=2086）
- ✅ 实测 C：二级弹层（键盘注入）在 A / B 两场景下均仍无缝向右展开（分组项右边 X = 弹层左 X）
- ✅ 构建 0 警告 0 错误（Release）；未改版本号
- ⚠️ 待用户实测外观：下拉弹层自按钮下方展开，越过底栏、部分位于窗口外（菜单近屏幕底时自动上翻）

## 上次变更（连发间隔上限 100ms → 90000ms，发版 v1.7.5）
- 📋 用户需求：连发间隔时长上限从 100ms 提到 90000ms（一分半一发）
- ✅ `Constants.MaxIntervalMs` 100 → 90000（唯一落点，行内录入 / 配置加载 / 导入的钳位全部走此常量）
- ✅ 按压时长上限按用户规格修正：`MaxHoldMs` 200 → **100**（10~100），
  模型注释 / 归一化注释 / 点击超时注释（500ms 仍覆盖 100ms + 抖动）同步更新
- ✅ 间隔输入框 `MaxLength` 3 → 5（原 3 位装不下 90000），模型注释范围同步更新为 10~90000
- ✅ 时序框 Tooltip 文案**转移到左下注释区**：按压时长 / 连发间隔框移除 ToolTip，
  悬停时在注释区展示各自说明（范围 / 周期定义 / ±20% 抖动 / 编辑交互），移出后恢复默认公告（复用现有 ShowComment / RestoreComment 通道）
- ✅ **精确模式（用户需求）**：间隔 > 100ms（`Constants.TimingJitterCutoffMs`）的方案**取消抖动**——
  慢速连发场景不局限于按键辅助（如定时触发），按压与间隔均按设定值精确执行、触发时刻精确
  （`JitterMs` 增加 precise 参数短路返回原值；`TaskLoop` 与滚轮脉冲路径两处调用点同步传参）
- ✅ 注入节奏：`TaskLoop` 等待间隔按剩余时长**分块休眠**（长间隔 10ms 块、短间隔回落 1ms），
  90000ms 级间隔不逐毫秒空转；精确模式下间隔即为设定值（无抖动钳位），≤100ms 高频段保持原抖动
- ✅ 键帽可视化：脉冲保护窗口 600ms 语义为"覆盖单次脉冲按压（≤100ms）+ 抖动"（不再按最慢周期整周期保活）——长间隔下脉冲之间键帽正常走 1.2s 空闲宽限淡出，下次脉冲复用重建；杜绝 60s 间隔下键帽长期滞留屏幕
- ✅ 双宏辅键独立时序 / 可视化（用户需求）：双宏第二行新增辅键**按压时长 / 连发间隔编辑框 + eye 开关**——
  `SchemeRowViewModel` 新增 Second* 系列属性；`UpdateSchemeInterval/Hold` 支持按目标索引更新（双宏两键互不影响；
  调度器本已按 `Configs[phase]` 独立执行各键时序与精确模式，此前仅缺编辑入口）
- ✅ 双宏辅键可视化实现：`TargetKeyConfig.VisualEnabled`（目标级开关，随配置持久化，Clone 同步）；
  `SyncVisualizerRegistry` 额外登记辅键源（次键 eye → 键帽 / 脉冲显示）；
  `KeyVisualizerService.OnTargetFired` 按目标可视化开关过滤脉冲键帽（顺带修正一致性：
  eye 关闭后连发脉冲也不再显示，与 eye ToolTip"按键触发时是否显示键帽"语义对齐）
- ✅ 勾选框 Tooltip → 注释区（用户需求）：方案行首「启用 / 停用」与双宏「首键参与连发」
  两勾选框移除 Tooltip，悬停时在左下注释区展示各自说明（复用 ShowComment / RestoreComment）
- ✅ 标题栏 / 托盘（用户需求）：标题栏不再展示英文名 "The Celestial Diviner"，
  版本号显示到三位 v1.7.5（标题、托盘、更新提示共用新增 `UpdateService.DisplayVersion`，
  随 csproj Version 自动跟随，不再硬编码）
- ✅ 幽灵按住项回收：原"1 秒内不做判定"时间窗 → **连发任务激活态判定**（`_firing`，停发后回收），
  间隔放宽后脉冲间隙不再误判 / 漏判（移除 `_lastFireTick` 字段）
- ✅ 构建 0 警告 0 错误（Release）
- ✅ 发版 v1.7.5（2026-09-18）：提交 `f805061` + 标签 `v1.7.5`（3de5ea0）
  - **Gitee 已发布**：main / 标签已推送（顺带补齐历史 v1.6.0 / v1.7.0 / v1.7.1 标签）；
    `Notice.md` 同步远端公告源（raw/main/Notice.md）；
    Release v1.7.5 已创建并上传 `TheCelestialDiviner-1.7.5-win64.zip`（发布说明已核验与本地逐字一致）
  - **GitHub 已发布（走 REST API 绕过被阻断的 git 通道）**：github.com:443 在本机网络不可达
    （TCP 超时，非凭据问题；gh 登录态正常），改用 `api.github.com` / `uploads.github.com`：
    按 Git Data API 逐字节复刻提交（blob base64 → tree → commit，tree / 标签对象 SHA 与本地完全一致），
    main → `685d750`、`refs/tags/v1.7.5` → `3de5ea0`，Release 已创建并上传同一附件
    （id 391653565，说明与本地逐字一致）。脚本：`dist/gh-api-push.ps1`（幂等，可复用于后续发版）
- ✅ 注释区公告按上版惯例改为**单行** 1.7.5 说明（42 字，上版 1.7.4 为 35 字；不再叠加旧版行）：
  "点选项检查升级1.7.5版本：连发间隔提至 90 秒、双宏两键独立设置，稳定性提升。"

## 上次变更（语音设置模态框）
- 📋 用户需求：底栏「成为衍天高手」后新增「语音设置」按钮，点击弹出模态框，
  支持为全局开关开启 / 关闭、方案切换导入自定义音频，也可重置回默认音频
- ✅ 底栏「语音设置」按钮（`OpenVoiceSettingsCommand`）→ `VoiceSettingsWindow` 模态框
  （Owner = 主窗口 / CenterOwner / NoResize / SizeToContent=Height）：三行
  （开启语音 / 关闭语音 / 方案切换语音），每行「当前音频名 + 导入 / 试听 / 重置」；
  未设置自定义音频时重置按钮禁用
- ✅ 导入：OpenFileDialog（mp3 / wav / m4a / aac / wma）→ 复制到
  %APPDATA%\TheCelestialDiviner\sounds\（先复制后清理该用途旧文件，失败不破坏已有自定义音频）
  → 热应用 → 落盘；失败弹窗提示原因
- ✅ 重置：删除自定义文件 → 热应用 → 落盘；试听：不改配置直接播放
- ✅ SoundCueService 重构：内嵌默认音频解包 URI 与自定义文件 URI 分离，`ApplyCustom` 统一装载
  （文件缺失静默回退默认并留痕）；新增 `UnloadCue` 在导入 / 重置前释放 MediaPlayer 文件占用
  （否则覆盖 / 删除自定义音频会失败）；`PlayStart` 自定义开启音优先于「衍天高手」变体；
  `ImportCustom` 先复制后清理（复制失败旧音频原样保留）
- ✅ 配置模型 v13：`AppConfig.CustomSounds`（Start / Stop / Cycle 文件名，空 = 内嵌默认音频）；
  `Clone` 同步；导入配置后 `ApplyCustom` 还原（缺文件回退默认）
- ✅ 底栏容纳新按钮：窗口 MinWidth 900 → 980（避免内容溢出重叠）
- ✅ 构建 0 警告 0 错误
- ✅ 发版 v1.7.4（csproj Version/FileVersion 1.7.4.0；窗口标题仅主次版本仍为 v1.7）：
  Notice.md 新增 1.7.4 升级告示并同步 Gitee（远端公告源 raw/main/Notice.md）；
  发布包 dist/TheCelestialDiviner-1.7.4-win64.zip（12 文件，不含闭源 DD 驱动）；
  双端（GitHub / Gitee）创建 v1.7.4 Release 并上传同一附件

## 上次变更（底栏键帽透明度滑块）
- 📋 用户需求：底栏新增可视化键帽透明度设置按钮，图标用 lucide keyboard，
  点击像音量按钮一样弹出滑块调节
- ✅ 底栏「键帽透明度」按钮（`OpacityButton`，lucide keyboard 线稿图标，15×15）：
  点击向上弹出滑块弹层（`OpacityPopup`，与音量按钮同款 Placement=Top / StaysOpen=False，
  点弹层外自动关闭）；滑块 0~100 + 百分比数值，拖动实时生效
- ✅ 悬浮层透明度：`KeycapOverlayWindow.SetOpacity(percent)` 设置窗口整体 Opacity
  （只作用于窗口，键帽自身的淡出动画不受影响）；`KeyVisualizerService.SetOpacity`
  沿用 SetPosition 的启动时序暂存机制（VM 构造早于窗口创建，AttachOverlay 后应用）
- ✅ 配置模型 v12：`AppConfig.VisualizerOpacity`（0~100，默认 100 = 完全不透明）；
  Load / 导入均钳位；`Clone` 同步；VM 属性实时作用 + 防抖落盘（拖动不刷日志 / 不反复写盘）；
  导入配置随 `OnPropertyChanged` 还原滑块与悬浮窗
- ✅ 细节：透明度归零时按钮图标暗淡 0.4（对齐音量 0 的 volume-x 暗淡提示）
- ✅ 构建 0 警告 0 错误

## 上次变更（置顶 / 热键自动重复 / UI 阻塞修复）
- ❌ 现象（用户实测）：启动后有时键帽不浮在最上层，得切一下应用才出现；随后按总开关键
  （F9）没反应或有延迟
- 🔍 根因一（热键无按下沿判定）：低级钩子对键盘自动重复照单全收（InputHookService 每次
  KEYDOWN 都派发），而 MainViewModel.HandleHookDown 的总开关 / 切换方案热键分支没有像调度器
  `_downSources` 那样的去重——按住 F9 即反复翻转。本机日志实证：2026-09-04 20:23 连续 50+ 次
  「全局启用/停用」、后半段稳定 31~32ms 一次（正好是 Windows 自动重复最快档）；2026-09-09
  18:08:22 有 63ms 内 12 次、18:29:42 有 11ms 内 3 次的爆发。人手动点不可能这个节奏
- 🔍 根因二（翻转开销全在 UI 线程）：每次翻转都同步 `SaveConfig()`（写临时文件 + 覆盖拷贝 +
  删除，持静态锁）且关总开关时 `StopAllCore()` 最多自旋 500ms 等在途点击收尾——钩子排队的
  事件与悬浮层重绘被压后一次性倾泻，体感「没反应 / 有延迟」
- 🔍 根因三（悬浮层置顶未重声明）：HWND 懒创建（AttachOverlay 只 new 对象，首次 Show 发生在
  第一次按键、游戏已在前台时），且无键帽即 Hide、按键再 Show；WPF 只在窗口首次创建时应用
  Topmost，重新显示后可能掉到其他置顶窗口之下，切应用强制重排 Z 序才恢复
- ✅ 修复一：总开关 / 切换方案热键加按下沿标记（`_masterKeyHeld` / `_cycleKeyHeld`，抬起复位），
  按住自动重复只响应第一次；去掉热键路径多余的 `Dispatcher.BeginInvoke`（HandleHookDown 已在
  UI 线程，直接调用）
- ✅ 修复二：`SaveConfigDeferred()` 防抖落盘（500ms 合并写盘，退出时序仍同步落盘兜底）；
  `StopAllCore(waitForInFlight)` 新增参数，运行期总开关停用传 false 不再阻塞 UI（任务循环内
  的 Click 自身会按下→抬起释放按键；退出时序 StopAll 仍等待）
- ✅ 修复三：NativeMethods 新增 SetWindowPos / HWND_TOPMOST / SWP_*；悬浮层 `PreloadHandle()`
  启动即 EnsureHandle 预创建 HWND（不显示，置顶 / 穿透样式在本程序仍前台时建立）；
  所有显示路径统一走 `ShowOverlay()`（Show 后 SetWindowPos 重新声明置顶，SWP_NOACTIVATE
  不抢焦点）；ApplyClickThrough 改样式后补 SWP_FRAMECHANGED；调整模式窗口同样显式置顶；
  SnapIntoWorkArea 增加布局未就绪保护（预创建句柄后首次定位 ActualWidth 可能为 0/NaN）
- ✅ 构建 0 警告 0 错误；未改版本号（等验证通过再发版）
- ⚠️ 待用户实测确认：若游戏为独占全屏（或开启 Windows「全屏优化」），外部进程的置顶窗口
  仍无法覆盖，需把游戏设为窗口 / 无边框，或对游戏 exe 关闭全屏优化

## 上次变更（DD 驱动自动获取）
- 📋 用户需求：引入开源驱动替代 DD 免手动下载。调研结论：dengqizhou30/HIDDriver
  （Apache-2.0）驱动为**测试证书签名**，需 bcdedit testsigning + nointegritychecks +
  重启测试模式 + devcon 手动安装 + 自编译（仓库无预编译产物），对终端用户不可用；
  kmclass（AGPL-3.0）同样要求测试模式；Interception（LGPL）驱动已签名但安装需改
  键盘类过滤驱动 + 重启，HVCI 兼容性存疑。用户确认放弃开源驱动方案，
  转向"DD 驱动自动获取"
- ✅ DdDriverService.StartAutoFetch 后台单飞自动获取：DD 官方发布渠道
  （github.com/ddxoft = 作者自己的发布页，程序只做下载器、不二次分发闭源驱动）
  → GitHub API 查最新 Release 7z 资产（限流/失败退回内置直链）→ 下载（3 分钟超时）
  → 随包 7zr.exe（7-Zip 独立版，LGPL，仅解 .7z，602KB）解包 → 优先取官方包
  1.simple 变体 dd63330.dll → 安装到 %APPDATA%\TheCelestialDiviner\drivers
  （既有探测位置，用户目录可写无需管理员）；临时目录 finally 清理；全程 Logger 留痕
- ✅ VM 接入（冷启动 / 热切换两条路径）：DD 模式缺驱动时先回退普通模式（不影响使用）
  + 后台自动获取；完成回调 AutoFetchCompleted 封送 UI 线程重试 EnsureReady 成功后
  经 KeyboardMode 属性自动升回 DD 模式（同步模拟器 + 落盘 + 日志）；
  用户中途手动切换模式即取消挂起（_ddAutoActivate 标志）
- ✅ 细节：自动获取跳过条件只认可加载的 x64 版（exe / APPDATA 的 dd63330.dll），
  Change Box 的 DD64.dll（32 位时间锁版，x64 必加载失败）不算"已有驱动"——
  本机实测命中过该误判；首次 DD 初始化成功后把 DD 自释放到 %TEMP% 的 dd63330.sys
  转存 APPDATA（KeepKernelDriverSys），后续启动预置内核服务不受 %TEMP% 清理影响；
  ResolveDriverSysPath 增加 APPDATA 候选
- ✅ csproj：7zr.exe（drivers/7zr.exe，入库，LGPL 允许再分发）随构建 / 发布包分发
- ✅ 端到端实测（独立 harness 引用主程序集）：下载 3657KB → 解包 → 安装成功，
  安装的 dd63330.dll SHA-256 与官方包内文件逐字节一致；临时目录清理确认
- ✅ README / drivers\README 同步（DD 自动获取为主、手动下载为兜底；7-Zip LGPL 署名）；
  构建 0 警告 0 错误
- ✅ 稳定性终案（用户确认"镜像到 Release 与直接内置无异"）：发布 zip **重新附带**
  dd63330.dll / dd63330.sys（用户开箱即用，无需任何下载）；源码仓库仍不入库
  （drivers/ gitignore 不变，二次开发按官网自取）；自动获取保留为兜底
  （源码构建 / 驱动被杀软误删场景）；README DD 节改为"随包附带 + 自动获取兜底"
- ✅ 版本 1.7.2（csproj Version/FileVersion 1.7.2.0；窗口标题仅主次版本仍为 v1.7）
- ✅ 版权合规复盘（用户提出 DeepSeek 意见）：核实 ddxoft/master 仓库**无任何许可文件**，
  默认保留所有权利 → 发布产物携带闭源二进制构成未经授权复制/分发，DeepSeek 判断成立；
  **恢复引导式获取**：发布产物移除 dd63330.dll/.sys（12 文件），静默自动获取改为
  **用户确认后**的引导流程（DdDriverFetchConfirmRequested 事件 + View Yes/No 确认框，
  下载由用户发起、程序仅做官方渠道下载器，法律地位更干净）；冷启动路径在
  InitializeRuntime（窗口可见后）弹确认，热切换路径即时弹确认；拒绝保持普通模式并
  日志提示手动途径；v1.7.2 双端附件同版本替换为不含驱动的包并同步说明
- ✅ 引导强调核心依赖 + 失败重试（用户需求）：确认框文案告知"本软件的键盘连发功能
  完全依赖 DD 驱动运作"以传达获取重要性，并承诺"下载失败自动重试直至成功"；
  RunAutoFetch 重构为重试循环——线性退避 5s 起步上限 60s 直至成功，
  NoRetryException 标记重试无意义的硬性失败（缺 7zr.exe / 上游包内容变化）直接终止，
  网络类失败（解析 / 下载 / 解包损坏）均重试，重试前清理解包残留目录；
  热切换时若获取已在途（IsAutoFetchRunning）不再重复弹确认，改提示在途与自动启用；
  README 注意事项 / DD 节同步"完全依赖 + 重试直至成功"表述
- ✅ 引导文案整合开发者难处（用户需求）：确认框与 README 说明"DD 驱动虽由作者
  免费提供，但它是闭源软件，我们无权将其打包分发，所以这一步需要由您来完成"，
  向用户解释为何需要下载而非直接内置
- ✅ README 强调使用范围声明（用户需求）：开头新增 ⚠️ 声明块——本软件仅供剑网三
  PVE 环境下的按键辅助使用，请勿用于 PVP 等违规环境，违规后果自负；
  注意事项列表将"仅限剑网三 PVE 环境"列为首条
- ✅ 历史发行版清理（用户需求，发布说明不提历史版本以免误导）：双端删除
  v1.6.0 / v1.7.0 / v1.7.1 / v1.7.2 的 Release（其安装包内置过闭源 DD 驱动二进制）
  与对应旧标签（旧提交树含驱动，标签源码归档同样携带）；保留 v1.7.2 标签
  （树已无驱动，供 compare 链接）；仅保留全新的 v1.7.3 Release
- ✅ 版本 1.7.3（csproj Version/FileVersion 1.7.3.0；窗口标题仅主次版本仍为 v1.7）

## 上次变更（README 用户向重写 + DD 驱动出库（不入库 / 不入安装包））
- ✅ README 全面重写为**用户向**：下载运行、注意事项（管理员权限 / 杀软误报 / DD 驱动
  自行下载 / 游戏过滤 / 配置不丢失 / 联网点 / 托盘驻留）、功能一览、使用说明与触发方式
  速查表、更新方法、二次开发；移除开发者向的项目结构 / 构建细节；标题去掉版本号
- ✅ DD 驱动出库（闭源第三方组件，不再随仓库 / 安装包分发）：
  - git rm --cached drivers/dd63330.dll/.sys + .gitignore 忽略（本地保留供开发构建）
  - drivers/README.md 改为"从 ddxoft 官网 http://www.ddxoft.com/ 获取，放入 drivers/ 目录"
    的开发者指引（保留 sys 预置服务用途说明）
  - UpdateService 发版要求注释同步：安装包不含 DD 驱动，覆盖安装不删除已放置的驱动
  - v1.7.1 双端 Release 附件重打包替换（去除 dd63330.*，其余 11 个文件与布局不变），
    GitHub Release 说明与 Gitee Release 正文同步补充"不含 DD 驱动"提示
- ✅ 缺失驱动时程序行为不变：DD 模式自动回退普通注入并日志提示；已装用户升级后
  驱动文件保留（xcopy 覆盖不删除）

## 上次变更（切换方案热键语音 + 目标方案代号键帽）
- ✅ 切换方案热键触发并实际切换档位时播放“切换”提示语音：
  新增内嵌资源 Resources/Sounds/CycleVoice.mp3（源件 PR/切换.mp3，csproj
  Sounds\*.mp3 通配自动覆盖）；SoundCueService 新增独立 _cyclePlayer + PlayCycle()
  （独立播放器可与总开关语音叠加，MediaFailed 留痕）；UI 手动点档位不播（仅热键路径）
- ✅ 键帽可视化区域同步显示目标方案代号键帽（①②③④）：
  KeyVisualizerService.ShowCycleKeycap 走连发脉冲通道渲染（HoldPulse 80ms 一次
  完整按下弹起），重复触发复用同帽递增连击角标，停止触发 1.2s 后随活动巡检
  独立淡出移除（触发后键帽自然消失原则）
- ✅ 与状态提醒键帽同语义独立运行：无视键位可视化总开关（_globalEnabled）与
  模式过滤、不做物理按键快照登记——可视化总开关关闭时依然显示
- ✅ 其余档位均为空未切换时不播语音不显键帽（无目标代号可显示）；
  构建 0 警告 0 错误；反射核对 cyclevoice.mp3 已入 .g.resources
- ✅ 版本 1.7.1（csproj Version/FileVersion 1.7.1.0；窗口标题仅主次版本仍为 v1.7）
- ✅ 发版压缩包改为同名目录包裹（dist 与 Gitee Release 附件同步替换）：解压文件不再散落；
  自更新解包下钻逻辑天然兼容（payload 根级无 exe 且唯一子目录含 exe 时自动进入该目录）；
  旧版根级散文件 zip 布局同样兼容

## 上次变更（二级弹层间隙致慢速无法选入）
- ❌ 现象：左下角「选项」菜单的二级级联子菜单，只有极快划入才能选中，
  鼠标稍慢、划过分组项右侧时子菜单立即消失
- 🔍 根因（WPF 源码 + 最小复现实验双重定位）：二级弹层与分组项右侧有 2px 间隙，
  鼠标离开分组项、进入间隙期间仍悬停在本应用 Menu 上（未离开菜单捕获树），
  MenuItem.MouseLeaveInMenuMode → IsMouseOverSibling = true → 启动 MenuShowDelay
  关闭计时器；本机系统 MenuShowDelay = 0 → 反选、弹层立即关闭。快速划过时
  Leave→Enter 同帧，弹层 MouseEnter 会停掉关闭计时器方可幸免 —— 即“只有极快
  才能选中”。最小复现实验：复刻同款自绘菜单（Custom 定位 + 2px 间隙 +
  MenuShowDelay=0），程序化 1px/10ms 慢速右移，弹层在鼠标到达前约 1.5s 即关闭；
  间隙改 0 后同速穿越全程保持（对照验证）
- ✅ 修复：MenuPlacements.RightOfTarget 二级弹层定位 Gap 2 → 0（无缝贴齐分组项
  右侧）——鼠标离开分组项瞬间已命中弹层 HWND，IsMouseOverSibling 恒 false，
  关闭计时器不再启动；一级弹层（向上弹出，纵向路径鼠标离开按钮后立即离开
  Menu 区域，无此问题）保留 2px 呼吸间隙不变
- ✅ MainWindow.xaml 二级弹层注释同步；构建 0 警告 0 错误

## 上次变更（档位组上移 + 圆形勾选框）
- ✅ 方案档位标签组（方案①②③④）自面板底部移至最顶部：DockPanel 首个子元素 + Dock=Top，
  现自上而下 = 方案档位①②③④ → 连发时序档位（常规/极限） → 模式分段条 → 选择态提示 →
  方案列表 → 分区标签组（常规/轮转/双宏） → 「从左侧键盘管理方案」按钮；
  档位 Border 边距 0,6,0,0 → 0,0,0,8（顶部无需上边距）；分区标签组上边距移除、
  下边距 6 保留（直接贴列表），管理按钮注释同步更新
- ✅ 方案列表勾选框美化：App.xaml 新增 RoundCheckStyle 圆形勾选框模板（15×15），
  表头全选框 + 行启用勾选框 + 双宏首键勾选框三处共用（替换系统默认方框）：
  - 底色经新主题资源 ThemeRoundCheckFill 随主题切换（App.ApplyTheme 注入：
    日间 = 主题紫 #836899、夜间 = 主题金 #cea23a，色值唯一落点 Constants.cs）
  - 勾选符号恒白色圆头对勾；半选态（IsChecked=null，表头部分行启用）白色短横线
  - 悬停圆框描边提亮反馈；禁用整体 40% 透明；沿用 Focusable=False（不参与键盘焦点）
- ✅ 构建：0 警告 0 错误

## 上次变更（分区标签组移至管理按钮上方 + 方案列表表头底边框）
- ✅ 开关分区子标签组（常规/轮转/双宏，深灰选中 SectionTabStyle）自顶部堆栈（模式分段条与
  选择态提示条之间）移至方案面板底部堆栈：DockPanel 内声明在管理按钮之后 → 置于
  「从左侧键盘管理方案」按钮上方；底部堆栈自下而上 = 档位①②③④ → 管理按钮 → 分区标签组，
  间距统一 6px（管理按钮上边距 6 移除、分区标签组上下边距 6）
- ✅ 方案列表表头加底边框：表头 Grid 外包 Border（ThemeBorder 底边 1px，列表内容分隔线），
  表头文字与线之间留 3px，线与列表之间 2px
- ✅ 纯 XAML 布局调整，VM 零改动
- ✅ 构建通过（0 警告 0 错误）

## 上次变更（状态提醒按钮金色/常规色切换修复（缺 PropertyChanged））
- ❌ 现象：状态提醒按钮图标一直保持金色，关闭后不回落到常规按钮色
- 🔍 根因：MainViewModel.StatusReminderEnabled setter 未发 PropertyChanged —— 底栏图标的
  DataTrigger 绑定（StatusReminderEnabled → 金色/常规色）只在启动时读取一次初值
  （默认开启 → 金），此后点击切换 VM 状态变了但绑定不知情，触发器永不重评估
- ✅ 修复：setter 末尾补 OnPropertyChanged(nameof(StatusReminderEnabled))
  （导入配置路径此前已单独发通知，不受影响）
- ✅ 构建通过（0 警告 0 错误）

## 上次变更（导入/导出配置移至底栏 + 管理按钮移回方案面板底部）
- ✅ 导入配置 / 导出配置按钮自方案面板底部移至底栏，紧跟「选项」菜单键之后
  （[☰选项] [导入配置] [导出配置] [调整可视化位置]，IconButtonStyle 26px 高、
  8px 间距与底栏节奏一致）；作用于当前方案档位，命令绑定不变
- ✅ 「从左侧键盘管理方案」按钮自分区标签组上方移回方案面板底部——原导入/导出的位置
  （档位①②③④上方、ListBox 之下，Dock=Bottom + 上边距 6）；开关模式/按压模式切换区域
  恢复紧凑（模式分段条 → 分区子标签直接相接）
- ✅ 纯布局调整：ImportCommand / ExportCommand / StartPickingCommand 绑定与 VM 零改动
- ✅ 构建通过（0 警告 0 错误）

## 上次变更（键帽鼠标标签/图标 + 管理按钮移位 + 分区标签配色）
- ✅ 键帽可视化鼠标标签改版（仅悬浮层键帽；主界面图块与方案列表名称不变）：
  - 滚轮上/下 → 「↑滚」「↓滚」，侧键1/2 → 「X1」「X2」（纯文本，KeyVisualizerService
    CanonicalLabel + KeycapTargetName 双处同步——脉冲键帽与物理键帽标签必须同源）
  - 左键/中键/右键 → lucide 图标键帽：mouse-left / mouse-right / mouse（ISC 许可；
    mouse-left/right 为 0.573+ 新增，lucide-static@0.573.0 包未含、取自主分支）。
    键帽标识标签仍为「左键/中键/右键」（保证“同键触发连发”时脉冲与物理按键复用同帽），
    键帽面按标签查表（CapIcons）渲染 lucide 线稿描边（描边 = 配色文字色，线宽 2 随
    Stretch 等比缩放），修饰键组合目标（Ctrl+左键）仍为文本键帽
- ✅ 「从左侧键盘管理方案」按钮移至开关分区子标签组（常规/轮转/双宏）上方
- ✅ 分区子标签选中色紫 → 深灰：新增 SectionTabStyle（BasedOn ModeTabStyle 重写模板），
    选中 = ThemeButtonBg 底（同底栏按钮底色，不引入界面外新颜色）+ 主题色文字 + SemiBold，
    悬停保持 TabIdleBg；日/夜主题层次各自成阶梯：背景 < 悬停 < 选中（浅 #FAFAFA/#F0F0F0/#E8E8E8，
    深 #202020/#2B2B2B/#333333）。仅分区标签组换用，时序/档位/模式分段不受影响
- ✅ 构建通过（0 警告 0 错误）

## 上次变更（弹层定位修复（MenuDropAlignment）+ Tooltip 改注释区展示）
- ❌ 现象（截图取证）：主菜单弹层右对齐到「选项」按钮而非左对齐；二级弹层（键盘注入选项）
  翻到主菜单左侧展开
- 🔍 根因：系统“菜单右对齐”辅助选项 SystemParameters.MenuDropAlignment 被置位
  （部分中文软件会改此系统设置），WPF Popup 的 Top 摆位被镜像为右对齐、Right 摆位被
  翻转到左侧 —— 与此前“菜单向上/向右展开”的实机表现完全吻合
- ✅ 修复：弹层全部改用 Placement=Custom 显式坐标定位（Custom 不参与 MenuDropAlignment
  镜像），新增 Helpers/MenuPlacements.cs 静态回调：
  - AboveTarget：一级弹层左下角对齐按钮左上角（向上展开、左对齐，2px 间隙）
  - RightOfTarget：二级弹层左上角对齐分组项右上角（向右展开，2px 间隙）
  - XAML 经 x:Static 挂接（conv:MenuPlacements.*），任何系统设置下定位确定
- ✅ 菜单悬浮 Tooltip 改注释区展示：移除三个分组项的 ToolTip，悬停分组项 / 叶子项 /
  配色子项时在注释区（CommentText）展示所属分组说明（与分区标签悬停注释共用同一展示位
  与恢复逻辑 OnSectionTabMouseLeave）；分组项 XAML 接线，键盘注入/键帽可视化叶子项
  OnLoaded 循环接线，键帽配色子项经 ItemContainerStyle EventSetter；说明文案收录
  MenuComments 字典（键 = 分组标题，叶子项经 Parent 向上取）
- ✅ 构建通过（0 警告 0 错误）

## 上次变更（菜单对齐 / 悬停选中同款效果 / 底栏按钮统一间隙）
- ✅ 选项文本与菜单键按钮文本左对齐：二级菜单项左内边距 10 → 14（14 + ● 标记列 14 =
  选项文本起点 28px = 按钮文本起点 9 + menu 图标 13 + 间距 6）；一级弹层左缘与按钮左缘对齐
  （注：当时 Top 摆位实机受 MenuDropAlignment 镜像未生效，已在下一轮以 Custom 定位修复）
- ✅ 悬停效果升级为选中同款：悬停菜单选项 = 紫底白字（AccentPrimary），与选中态视觉一致；
  IsHighlighted（覆盖菜单展开路径高亮）与 IsMouseOver（纯命中测试，保证悬停必有反馈）
  双触发器同款视觉；选中（当前生效项）= 同款紫底白字 + 常驻白色 ● 区分
- ✅ 菜单键与「调整可视化位置」按钮之间补 8px 间隙（Menu 右边距，与底栏其余控件间距节奏一致）
- ✅ 子菜单向右展开（注：当时 Right 摆位实机受 MenuDropAlignment 镜像翻到左侧，
  已在下一轮以 Custom 定位修复）
- ✅ 构建通过（0 警告 0 错误）；注：上一轮编辑曾把触发器区块引号写成中文弯引号导致 XAML
  解析失败（MC3000），已修正

## 上次变更（菜单键按钮化 + 热键钮日间配色）
- ✅ 设置菜单键按钮化：顶层 MenuTopStyle 自绘模板改为按钮观感——lucide menu 图标（三条横线，
  v0.544，MenuIconPath）+ 文本「选项」（替换原「☰ 菜单」），配色对齐 IconButtonStyle
  （ThemeButtonBg 底 / ThemeBorder 边框 / 圆角 4 / ThemeFg 图标文字）
- ✅ 弹出方向：菜单位于窗口左下角 → 顶层子菜单 Placement=Top 向上弹出（弹层间隙移到底部）；
  二级子菜单保持 Placement=Right 向右展开
- ✅ 交互反馈：悬停 = TabIdleBg 灰底（原有）；新增选中态 = 当前生效项紫底白字 + 白色 ●
  （AccentPrimary，与全应用“选中 = 主题紫”语义一致）；触发器顺序置于悬停之后，
  当前项悬停时保持紫色不被灰底覆盖；菜单键展开（按下）态 = ThemeBorder，与 IconButtonStyle 按压一致
- ✅ 全局热键设置按钮（底栏）日间配色：原日间为近黑底（HotkeyBg #1F1F1F）白字，改为主题化
  HotkeySettingBg / HotkeySettingBorder / HotkeySettingFg —— 夜间沿用近黑底白字（#303030/#333333/White），
  日间复用普通按钮配色（= ThemeButtonBg / ThemeBorder / ThemeFg）；键鼠区总开关键图块的
  HotkeyBg 黑色高亮不受影响
- ✅ 构建通过（0 警告 0 错误）

## 上次变更（按钮去键盘焦点 + 底栏设置菜单）
- ✅ 全窗口按钮/单选钮/复选框不再参与键盘焦点（Focusable=False）：点击按钮后按 Space
  不再重复触发、方向键不再游走选中其他按钮（纯鼠标操作策略）：
  - IconButtonStyle（App.xaml 全局，覆盖底栏图标钮 / 方案行 eye·✕ / 导入导出 / 总开关键 /
    管理钮 / 调整模式确定钮等全部 Button）
  - ModeTabStyle / ModeSegmentStyle（模式·分区·时序·档位单选钮，ProfileTabStyle 继承）
  - 方案列表表头/行内三个 CheckBox + 方案列表 ListBox
  - TextBox（时序编辑）保留键盘输入；音量 Slider 未动
- ✅ 底栏左下角新增「☰ 菜单」设置菜单键（WPF Menu 级联）：键盘注入（普通/扫描码/消息/DD 驱动）、
  键帽配色（KeycapSchemes 全部预设动态生成）、键帽可视化（全部/修饰键和自定义键/自定义键，
  原「可视化」改名）；三个下拉框自底栏移除
- ✅ 菜单模板全部自绘主题化：系统默认 MenuItem 夜间模式为白底黑字不可用 →
  顶层 MenuTopStyle（点击开合，子菜单向下弹出）+ 二级 MenuSubStyle（叶子 / 级联组共用，
  Role 触发器画 ▸ 箭头，Popup 向右弹出自动避屏）；弹出层 ThemePanel/ThemeBorder；
  选中标记为自绘 ●（AccentPrimary，IsCheckable=False + 代码同步 IsChecked 实现单选，
  规避系统勾选符号硬编码配色）；菜单项 Focusable=False（悬停展开不受影响）
- ✅ VM 新增 SetKeyboardModeCommand / SetKeycapSchemeCommand / SetVisualizerModeCommand
  （参数沿用原下拉语义：索引 / 方案名）；KeycapSchemeName / VisualizerModeIndex setter
  补 OnPropertyChanged（此前不通知，菜单 ● 标记需要跟随刷新）
- ✅ MainWindow：移除 KeyboardModeBox 接线；SyncSettingsMenu 按 VM 状态回填 ● 标记
  （静态项直接遍历，键帽配色生成项经 ItemContainerGenerator 取容器），
  VM PropertyChanged（KeyboardMode / KeycapSchemeName / VisualizerModeIndex）+
  菜单展开 SubmenuOpened（覆盖按需生成的容器首展开时机）双路驱动同步
- ✅ 构建通过（0 警告 0 错误）

## 上次变更（调整区域可拖动 + 键帽恢复单行）
- ✅ 调整模式整个区域可点击拖动修复：WPF AllowsTransparency 窗口为分层窗口，OS 按
  Alpha 通道命中测试——纯透明（alpha=0）区域鼠标消息穿透到下层窗口，WPF
  MouseLeftButtonDown 收不到（此前只能拖住键帽本体 / 提示条，空白区拖不动）。
  修复：窗口 Background 垫 alpha=1 近透明画刷（#01000000，视觉不可见），整个 940×180
  区域均可按下 DragMove 拖拽，且不穿透到下层窗口
- ✅ 键帽布局恢复单行（用户偏好，撤销上一轮 WrapPanel 双行换行）：根容器 WrapPanel →
  StackPanel，键帽 Margin 底边距 8 → 0；调整模式右下 StackPanel 底边距保持 0，
  调整键帽与真实键帽同为窗口底边贴齐（顺带修正了原始实现 4px 的对齐偏差，逐像素对齐）
- ✅ 构建通过（0 警告 0 错误）

## 上次变更（可视化区域：防拖出屏幕 + 磁吸 + 禁最大化 + 双行换行）
- ✅ 新增 SnapIntoWorkArea(win, magnet) 静态辅助（KeycapOverlayWindow）：
  WinForms Screen.FromHandle 取所在屏幕工作区（物理像素）→ 按窗口当前 DPI（PerMonitorV2
  TransformToDevice）换算 DIP；magnet > 0 时四边独立磁吸（距边 ≤24px 拖拽结束贴齐），
  随后整窗（含宽高）强制钳回工作区（Compat.Clamp，防反向钳位取 Max 保护）；
  无 PresentationSource（未显示）时跳过，显示后首次定位再校正
- ✅ 调整模式窗口：拖拽结束（DragMove 返回后）磁吸 + 钳位 → 区域不可拖出屏幕
  （此前拖到屏幕边缘外时虚线框被屏幕边缘裁切，即"右下角显示残缺"的根因）；
  打开时也钳位一次（陈旧保存位置 / 显示器热插拔防御）
- ✅ 调整模式窗口 ResizeMode = NoResize：区域仅临时显示，禁用调整大小 / 最大化
  （含 Aero Snap / Win+↑），防窗口尺寸被改后虚线框 ≠ 真实区域 940×180 破坏 1:1 对齐
- ✅ 主悬浮层 PositionToSaved 也接入钳位（magnet = 0 静默）：保存位置越界时钳回工作区
- ✅ 键帽截断修复：悬浮层根容器 StackPanel → WrapPanel（窗口 940×180 本就为两行键帽预留，
  单行 ≈ 12 帽，超出一行自动换行到第二行而非截断）；键帽 Margin 加底边距 8 = 第二行行距
  （单行时键帽整体上移 8px，无功能影响）；调整模式右下 StackPanel 底边距 4 → 0，
  补齐原先 4px 的对齐偏差（现调整键帽与真实键帽同为窗口底边上方 8px，逐像素对齐）
- ✅ 虚线框内缩 1px → 2px（高分屏窗口边缘取整裁切防御）
- ✅ 构建通过（0 警告 0 错误）

## 上次变更（调整可视化位置模式）
- ✅ 调整模式虚拟键帽内容从"A"改为**应用图标**（复用提醒键帽的 LoadAppIcon，所见即所得）
- ✅ 确定按钮套用应用全局 IconButtonStyle（4px 圆角 + 主题配色 + 悬停反馈），
  局部 Padding 覆盖样式内边距不变
- ✅ 新增**可视化区域虚线边框**（Rectangle，AccentPrimary 主题色 SetResourceReference 随主题
  同步，1px 圆角虚线 4-3，内缩 1px 防描边被窗口边缘裁切）：标出悬浮窗 940×180 完整范围，
  键帽实际渲染在其右下角锚点；Transparent 填充使整个范围均可 DragMove 拖拽；
  提示文案同步改为「虚线框 = 键帽显示区域 · 拖动调整位置 · 确定保存 · Esc 取消」
- ✅ 构建通过（0 警告 0 错误）

## 上次变更（状态提醒按钮：金色激活态 + 默认激活）
- ✅ 按钮图标激活态改为**金色墨迹**（AccentGold，与夜间模式钮同源）；未激活恢复默认文字色
  （ThemeFg 满透明度），去掉原先的 0.4 暗淡 / 紫色高亮方案
- ✅ 修复上一版的触发器失效 bug：Stroke 原写在 Path 本地属性上，WPF 依赖属性优先级中
  本地值 > Style 触发器 → 激活态的颜色 Setter 从未生效（仅透明度变化）；现 Data / Stroke
  全部移入 Style Setter（与 eye / volume 图标"本地值会压过 DataTrigger"注释同源教训）
- ✅ 默认激活：AppConfig.StatusReminderEnabled 默认值 false → true；配置版本 v7 → v8，
  迁移把存量配置（含开发期落盘的关闭态）统一置为开启——迁移只执行一次（落盘 v8 后跳过），
  用户此后手动关闭不会被重置
- ✅ 构建通过（0 警告 0 错误）

## 上次变更（状态提醒键帽）
- ✅ 底部栏右侧新增“状态提醒”图标按钮（lucide message-square-dot，26×26 与夜间/音量钮同规格；
  激活 = 主题紫高亮，未激活 = 文字色 0.4 暗淡），开关随配置持久化（AppConfig v7 新增
  StatusReminderEnabled，缺省 false，Clone / 导入配置同步）
- ✅ 功能语义：按钮开启后，**应用总开关**（非键位可视化总开关）开启时，键帽悬浮区常驻一个
  键帽，内容为应用图标（Resources\app.ico 经 IconBitmapDecoder 取最大帧，加载失败退化为
  “启”字）；任一方案连发触发 → 键帽立即消失；全部连发停止 2.5s → 键帽恢复显示
  （即总开关正在开启的屏幕指示灯，连发时让位给真实连发键帽）
- ✅ TaskSchedulerService 新增 FiringChanged 事件：`_tasks.Any(t => t.Active)` 聚合态翻转时触发
  （去重，锁内调用）；HandleSourceDown / HandleSourceUp / SetMasterEnabled / ApplyConfig /
  StopAll 五个状态变更点末尾触发——任一任务 Active 即视为连发中（Toggle 被 Hold 暂停时
  Hold 必在连发，语义一致），因此连发开始于第一次注入前、恢复计时精确起于全部停止
- ✅ KeyVisualizerService 新增状态提醒状态机（SetMasterSwitch / SetReminderEnabled 入口，
  全部 UI 线程）：desired = 按钮开 && 总开关开 && 无连发 && 恢复倒计时未挂起；
  停发时 DispatcherTimer 2.5s 倒计时，期间再次连发则 Stop/Start 重置；悬浮层未创建前
  只记状态，AttachOverlay 末尾补应用（修复 VM 构造早于窗口创建的时序）
- ✅ KeycapOverlayWindow 新增 SetReminderVisible + 常驻 KeycapControl（isReminder: true）：
  - 不参与快照协调（UpdateKeys 两个分支跳过）、不参与空闲淡出（RetireIdleCaps 跳过），
    空快照 / 可视化总开关关闭均不清除它
  - 恒居键帽序列末位（普通键帽经 AddCap 插到它之前）：窗口右对齐 → 提醒键帽位置稳定不跳动
  - BeginAdjust 清场时一并移除，EndAdjust 后按期望态恢复（调整模式中状态变更只记 _reminderDesired）
- ✅ 导入配置后 StatusReminderEnabled 随导入还原（services 开关 + OnPropertyChanged 刷新按钮图标）
- ✅ 构建通过（0 警告 0 错误）

## 上次变更（悬停注释文案）
- ✅ MainWindow.xaml.cs SectionComments 文案更新（仅注释区显示文本，无逻辑改动）：
  - 轮转开关：「支持两个或多个轮转开关模式键位之间一键切换。」
  - 双宏开关：「首键位一键启停两个开关模式键位的轮流连发。」
- ✅ 第二勾选框说明并入双宏悬停注释（非功能改动，行为已有实现）：
  「取消勾选第二个勾选框，则首键位一键启停第二个开关模式键位的连发，不再轮转。」
  （行为：取消勾选 → Targets[0].Enabled=false，调度器过滤后仅剩次键连发）
- ✅ 构建通过（0 警告 0 错误）；注意运行入口：最新产物在
  src\TheCelestialDiviner\bin\Debug\net48\，dist\try-color 为旧拷贝（曾导致改完看不到变化）

## 上次变更（双宏页布局死循环修复）
- ❌ 现象：切到双宏开关分区后整机严重卡顿（UI 线程 100% 空转，截图/操作数秒才响应），
  方案列表每行只剩行首勾选框，键名 / 时序框 / eye / ✕ 全部不可见
- 🔍 根因（minidump 线程栈取证）：第二勾选框当时放在第二行的一个嵌套 Grid 里，且该嵌套
  Grid 既没有 Grid.Column（整体落进外层第 0 列）、首列又带 `SharedSizeGroup="SchemeColCheck"`
  —— 嵌套共享 + 星号列 + IsDual 行可见性切换三者叠加，触发 WPF 共享列宽（SharedSizeScope）
  无限布局循环：UI 线程在 PresentationCore 反复重排版（转储栈顶为 PresentationCore.ni 深层递归）；
  同时嵌套 Grid 的内容宽度（勾选框 + ⇄辅键名 ≈ 68px）被并进外层第 0 列的 Auto 宽度并经共享
  组放大，键名列（`*`）被挤瘦到裁掉 F11 尾字符，最坏时整行内容被推出面板外只剩勾选框
- ✅ 修复（仅 MainWindow.xaml 方案行模板）：删除嵌套 Grid，第二勾选框与 ⇄辅键名直接作为
  外层 Grid 第 1 行的单元格（列 0 = 勾选框列、列 1 = 键名列，天然与首行对齐，无需共享列宽），
  两者均绑定 IsDual → BoolToVis 控制显隐；方案行模板不再有任何嵌套共享尺寸参与者
- ✅ 实机回归：双宏页两行方案完整渲染（- + ⇄ = / F11 + ⇄ F12），UI 线程 CPU 由打满一核
  （2000ms/2s）降至 16ms/2s；常规 / 轮转 / 双宏三页切换均正常；第二勾选框取消 → 落盘
  Targets[0].Enabled=false、方案 Enabled 保持 true，复选后恢复（实测通过）

## 上次变更（双宏首键连发开关）
- ✅ 双宏方案列表勾选列新增第二个勾选框（辅键行首列，与首行启用勾选同列对齐）：
  - 勾选（默认）＝原行为：首键与次键 1-2-1-2 交替连发，首键启停
  - 取消＝首键仅作启停触发键：按首键启动 / 再按停止，次键单独连发不交替
    （等效于用首键位启停次键位的连发方案）
- ✅ 实现路径（模型零改动）：复用 TargetKeyConfig.Enabled（已随配置持久化），
  调度器 ApplyConfig 本就过滤未启用目标 —— 双宏任务仅剩次键时自动退化为
  单目标连发（phase 恒 0），首键触发源语义不变；首行启用勾选仍为方案级整体启停
- ✅ SchemeRowViewModel 新增 FirstKeyFiring 属性 + onFirstKeyFiringChanged 回调；
  MainViewModel 新增 SetDualFirstKeyFiring（写首目标键 Enabled → 重建任务 → 落盘 + 日志）
- ✅ MigrateLegacySchemes 不再强制双宏目标键 Enabled=true（否则重启覆盖用户取消的勾选）
- ✅ 新录入双宏方案两目标默认 Enabled=true，与勾选框默认态一致；构建通过（0 警告 0 错误）

## 上次变更（日/夜图标区分 + 键帽逐键帽独立淡出）
- ✅ 底栏主题按钮图标随模式切换：白天 = lucide sun（中心圆 + 八向光线，金色描边），
  夜间 = lucide eclipse（外圈大圆 + 月牙弧线）；DataTrigger 绑定 VM.NightMode，
  切换时 OnPropertyChanged(NightMode) 驱动图标刷新
- ✅ 键帽可视化“停止的键位不随时间消失”修复（根因：窗口级淡出计时器被发射中的
  脉冲键不断重置 —— HoldPulse 每次发射 Stop+Start，开关模式连发不停计时器永不触发，
  同屏已停止的键位只能陪跑到发射键也停止）：
  - 移除窗口级 _dismissTimer/BeginDismiss，改为 500ms 活动巡检（_activityTimer）
    + 逐键帽独立宽限/淡出：各键帽距最后活动（按压刷新 / 首次释放 MarkReleaseOnce 锚定 /
    最后一次脉冲 PulsePress 顺延）超过 2.5s 后各自 400ms 淡出移除；全部移除后隐藏窗口
  - IsRetirable 仅看最后活动时刻，不依赖释放标记（轮转开关自动停旧键无后续快照也能退出）；
    物理按住中 / 发射中（600ms 脉冲窗口）永不淘汰
  - 宽限内重按 / 重新发射（CancelRetire）取消淡出继续复用同名键帽，连击计数保留；
    已释放键帽不再立即从视觉树移除，改为抬起后进入宽限（快照中消失 → 宽限而非删除）
  - 效果：一个开关模式键位连发中，其它已停止键位 2.5s 后独立消失，互不影响
- ✅ 构建通过（0 警告 0 错误）

## 上次变更（角标修复 + 夜间模式）
- ✅ 键帽连击角标显示不全修复：角标向右上外凸 1/4 边长（6px）超出悬浮窗右缘被裁，
  Root 预留 0.25×size 右侧留白（Margin 方式，StackPanel 无 Padding）
- ✅ 连击角标计数上限 99：超过后停在 99 不再累加；角标 MinWidth 正圆 + Padding 2，
  两位数时宽度自适应呈横向药丸，数字不溢出圆边
- ✅ 底栏右下角新增夜间模式按钮（音量按钮左侧，26×26 与音量钮同规格）：
  图标 lucide eclipse（外圈大圆 + 月牙弧线，金色描边与主题金一致）；点击切换白天/夜间
- ✅ 主题切换链路：
  - AppConfig 新增 NightMode（缺省 false）与 ThemeFollowSystem（缺省 true 跟随系统）；
    Clone 深拷贝同步；手动切换一次后 ThemeFollowSystem = false（固定不再跟随系统）
  - MainViewModel.NightMode 属性：切换 → ApplyTheme + 落盘 + 日志；
    VM 构造时按配置应用主题（App.OnStartup 原无条件跟随系统的调用移除，避免覆盖）；
    导入配置后随配置还原主题
  - App.ApplyTheme 复用同一 ResourceDictionary 实例覆写键值（反复切换不再堆积字典）；
    同步 KeycapOverlayWindow.SetThemeDark（调整模式提示条深浅配色）
  - 夜间模式适配：紫（#836899）/ 金（#cea23a）不动，背景 #202020、面板 #2A2A2A、
    边框 #3D3D3D、按钮 #333333、文字 #E6E6E6；键位图块空态 #2F2F2F / 空字 #AAAAAA；
    已创建图块实例刷子切换时全量 RefreshTheme；调整模式提示条深底白字
- ✅ 夜间模式补适配：模式分段条轨道容器硬编码 Background="White" → 主题资源
  SegmentTrackBg（白天白轨道不变 / 夜间 #383838，比面板亮一档，保留紫/金胶囊与轨道层次）
- ✅ 构建通过（0 警告 0 错误）

## 上次变更（键帽校准收尾 + 档位归位 + 鼠标键简称）
- ✅ 对照 keyviz 源码（src/components/keycaps/lowprofile.tsx / press-count.tsx、
  src/components/settings/keycap.tsx、src/stores/key_style.ts）校准键帽复刻：
  - 边框色：不再固定黑色。keyviz 点选配色预设时执行 setBorderStyle({ color: scheme.secondary })，
    即边框 = 方案 secondary 色（Pansy → #4527a0）；KeycapScheme 增加 Border 属性（缺省 = Base），
    KeycapOverlayWindow.BorderColor 改用 scheme.Border
  - 连击角标：复刻 keyviz PressCount —— 0.75×size（24px）正圆、圆角 50%，
    底色 = 配色文字色、数字 = 配色面色（Pansy：金底紫字），显示裸数字（去掉 "×" 前缀），
    锚右上角向外凸出 1/4 边长（Margin -0.25×size）；角标弹跳动画保留
- ✅ 面板“档位”语义归位（修正前次误移）：
  - 方案①②③④是方案标签，放回面板最底部原位（导入/导出配置之下，
    Bottom 声明在先贴最底边，档位 Border 保持在导入导出 Grid 之前）
  - “档位标签” = 连发时序档位（常规档 26ms / 极限档 11ms），移到面板最顶部（模式分段条之上）
  - 面板顺序：时序档位 → 模式分段条 → 分区子标签 → 添加 → 方案列表 → 导入/导出 → 最底方案档位
- ✅ 键帽鼠标键简称：悬浮键帽“鼠标左键/鼠标中键/鼠标右键” → “左键/中键/右键”
  （KeyVisualizerService.CanonicalLabel 物理按下路径改简称 + 新增 KeycapTargetName 连发脉冲
  路径同源简称，保证“同键触发连发”时脉冲与物理按键复用同一键帽；
  滚轮/侧键保持原名，修饰键前缀顺序 Ctrl→Shift→Alt 不变）
- ✅ 构建通过（0 警告 0 错误）

## 上次变更（按压+间隔精简 + 方案④）
- ✅ 表头"按压+频率" → **"按压+间隔"**（tooltip 同步去掉"频率"措辞）
- ✅ 行内时序显示 "xxms + xxms" → **"xx + xxms"**：删去按压框后的 "ms" 文本列
  （SchemeColMs1 列整个移除，"+"、"ms"/eye/✕ 列号前移，表头 ColumnSpan 5 → 4），
  行模板与表头 SharedSizeGroup 同步对齐；周期语义不变（按压 + 间隔，日志与 tooltip 不变）
- ✅ 时序数字框（按压 / 间隔 TextBox）左右内边距收窄：Padding="-2,0"
  （App.xaml 全局 TextBox 模板 PART_ContentHost Margin=Padding，负值抵消 TextBoxView
  自带 ~2px 内缩，数字与虚影水印居中对齐、可用宽度更大）
- ✅ 方案档位 3 → 4 套（新增方案④）：
  - ProfileCount 3 → 4（ActiveProfile 钳位 / SyncProfileRuntime 补空档位自动跟随）；
    ProfileLabel 增加 2 → ③、默认 → ④
  - AppConfig.Profiles 初始化器补第 4 套空档位；ConfigService v3→v4 迁移补齐循环 < 3 → < 4
    （SyncProfileRuntime 加载后仍会补齐，纯一致性）；迁移日志"方案三档位" → "方案四档位"
  - MainWindow.xaml 档位 Border 三列 → 四列 + Profile4Button（Tag=3，Checked 回写 VM）；
    code-behind ProfileButtons 数组加入 Profile4Button
  - 配置无 schema 变更（Profiles 为列表，旧配置加载后自动补第 4 套空档位），版本号维持 v6

## 上次状态（方案管理模式：持续添加 / 右键取消 / 成对退出，2026-09-05）
- ✅ 按钮语义改造：面板按钮 "＋ 添加连发键" → **"从左侧键盘管理方案"**（空闲态）；
  点击进入方案管理模式（原一次性录入态），按钮变为 **"退出管理方案"**（仍可点击）
- ✅ 管理模式持续保持，不再点一个键就退出：
  - **左键单击键位 = 添加方案**（键盘按键直接录入保留；双宏分区仍为两步成对录入）
  - **右键单击键位 = 取消该键位方案**（新 RemovePickAt；键位自身无方案但为某双宏辅键时同样整对取消）
  - **Esc / 点击键鼠区空白处（含图块外任意区域与其它窗口）/ 点"退出管理方案"按钮 = 请求退出**
- ✅ 双宏成对录入配对语义：
  - 成对完成（偶数步）后管理模式保持，可继续成对添加下一组（提示条回到"选第 1 键"）
  - **偶数态才能退出**：成对录入中（已选第 1 键未选第 2 键）请求退出 → 先放弃暂存的第 1 键
    回到偶数态（AbandonDualFirstPick），再次请求才真正退出
  - **取消一个 = 取消一对**：右键双宏方案任一键位（开关键位或辅键）整对取消；
    DeleteScheme 检测双宏方案并在日志列出两个键位（方案列表 ✕ 删除同样生效）
  - 成对录入中右键暂存的第 1 键 = 放弃该键重新选择
- ✅ 双触发防线（钩子只观察不吞事件，同一次物理点击 / 按键会同时到达钩子路径与 WPF）：
  - 图块 WPF 左键 MouseBinding 改为**仅钩子未安装时兜底**（HandleSourceClicked 判 !HookInstalled），
    否则双宏两步录入会被同一次点击触发两次（第 1 键暂存后立刻又被当第 2 键校验报"两键相同"）
  - StartPicking / StartMasterKeyPicking 增加 **150ms 退出冷却**（_lastPickingExitTick）：
    "退出管理方案"点击先由钩子空白路径退出，同一物理点击随后作为陈旧 WPF 点击落回
    已恢复可用的按钮，冷却防止刚退出就被重进（同时修复旧版热键录入按钮的同类隐患）
  - View 增加 Esc 兜底（PreviewKeyDown，仅 !HookInstalled 时触发）：钩子失效时也能退出管理模式
- ✅ 提示文案：默认底部提示 / 提示条 / 日志全部更新为管理语义
  （s_defaultHint、s_managePrompt、s_dualManagePrompt；进入 / 退出 / 放弃第 1 键 / 无方案可取消 均有日志）
- ✅ 总开关键录入保持一次性录入语义不变（录完即退出，不受管理模式改造影响）

## 上次状态（方案列表表头 + 可视化开关迁位 + 档位仅影响新方案 + 按压时长时序，2026-09-05）
- ✅ 方案列表表头（面板 DockPanel 挂 IsSharedSizeScope，列宽经 SharedSizeGroup 与行模板对齐）：
  - 列 1 全选框：一键启用/停用全部连发键（SchemesAllEnabled 三态——全启用 true /
    全停用 false / 混合 null 半选；行内单行勾选联动刷新；空列表点击视觉回弹）
  - 列 2"按键"、列 3"按压+频率"（跨时序编辑框五列，tooltip 解释 周期=按压+间隔）
  - 列 4 可视化总开关：底栏 eye 按钮**迁入表头**（ToggleGlobalVisualCommand 复用，
    eye/eye-off 随 GlobalVisualEnabled 切换）；**底栏原按钮已删除**（音量按钮保留）
  - 列 5 删除勾选项（✕ 图标复用行删除按钮）：删除所有勾选（启用）的连发键，
    经 DeleteCheckedRequested 事件由 View 弹 MessageBox 确认（破坏性操作）后执行
- ✅ 时序档位逻辑修正：切换常规/极限**仅写入面板默认值**（之后添加的连发键使用），
  不再修改已有方案（tooltip / 日志同步更正）；行内编辑框仍可单键调整
- ✅ 连发时序重构：周期 = **按压时长 HoldMs（按下→弹起）+ 间隔 IntervalMs（弹起→下次按下）**；
  原实现按压 0ms（down/up 背靠背），逐帧轮询输入的游戏在帧窗口 ≥ 按压时长时
  按下状态采不到（丢发）、间隔过短时两次按压合并为一次持续按住
- ✅ 默认按压 26ms + 间隔 26ms（周期 52ms ≈ 19 发/秒）：按 40fps 最差帧窗口 25ms 校准
  （按压/间隔均须 ≥ 帧窗口才能零丢失），全 40~100 帧区间稳定登记；
  上限换算：100 帧 50 发/秒 > 任何游戏动作冷却，实际收益不受损
- ✅ 双独立 ±20% 抖动（Constants.TimingJitterPercent）：TaskLoop 每轮对按压与间隔
  各自独立取随机值（26ms → 20~31ms），消除恒定周期的机器指纹；SchedulerService
  新增 JitterMs（静态 Random + 锁，.NET Framework 无 Random.Shared）
- ✅ 注入层：InputSimulatorService.Click 增加 holdMs 参数（PressKey / ClickButton 在
  down/up 之间 Sleep，精度依赖 timeBeginPeriod(1)）；滚轮目标无按压语义不受影响
- ✅ 钩子线程保护：滚轮脉冲路径（Hold 模式滚轮源）原在钩子线程同步 Click，
  现含 Sleep 不得阻塞 → 投递 ThreadPool 并以配置实例为锁串行化（down/up 严格成对）
- ✅ 模型：TargetKeyConfig 新增 HoldMs（默认 26，钳位 10~200，缺失/非法 → 默认）；
  AppConfig 新增 DefaultHoldMs 面板默认值 + TimingPreset（0 常规/1 极限）；
  CurrentVersion 5 → 6（字段有默认值，仅推进版本号）；
  ConfigService.NormalizeIntervals → NormalizeTiming（间隔 + 按压一并归一化）
- ✅ UI：方案行新增"按压时长"编辑框（周期构成显示为 `[按压]ms + [间隔]ms`），
  复用间隔框的虚影水印 / 点击清空 / 回车失焦提交 / 点击外部自动提交全流程
  （SchemeRowViewModel.HoldEdit/CommitHoldEdit；MainWindow OnHoldBox* 处理器，
  GotMouseCapture 清空处理器更名 OnTimingBoxGotMouseCapture 共用）
- ✅ 理论依据：逐帧轮询输入的登记率上限 = 1 / 2×帧窗口（采样序列每 2 帧至多一次
  弹起→按下跳变），周期压破 2×帧窗口只会降低登记率，不存在"丢换快"的交易；
  极限档 11/11（帧率 ≥90 上限 ~45 发/秒，55 帧以下丢发、40 帧拍频混叠骤降 ~5 发/秒）
- ✅ 实测基线保留：EchoGuard 按 (vk, 方向) + 12ms 时间窗匹配，与按压时长无关，无需改动

## 上次状态（滑块几何协调 + 全局方框圆角化 + 行按钮间距，2026-09-05）
- ✅ 滑块几何协调：钮高 26 → 20（内容 Padding 0,2），衬底外扩 4px（圆角 14）；
  轨道 CornerRadius=16、Padding=4,6 → 衬底距容器边垂直 2px、水平 1px，不再超出容器；
  轮廓↔标签保持无缝（衬底图形），轮廓↔容器留 1~2px 呼吸
- ✅ 全局方框圆角化：IconButtonStyle 加 CornerRadius=4 模板（含悬停/按下/禁用反馈），
  覆盖导入/导出/热键/添加/调整位置/行内 eye/删除等全部按钮；新增 RoundedComboStyle
  圆角下拉模板（含弹层圆角与项高亮）应用于键盘注入 / 键帽配色下拉；TextBox 全局
  圆角模板（App.xaml）应用于间隔编辑框；键源图块圆角 3 → 4；横幅 / 底栏 / 日志面板
  补 CornerRadius=4 与外边距（10,6,10,4 / 10,0,10,6）
- ✅ 方案行可视化开关与删除按钮间距：-1px 重叠 → 4px 小间距（去掉 Panel.ZIndex）

## 上次状态（模式滑块轮廓改衬底图形 + 字体恒定加粗，2026-09-05）
- ✅ 字体恒定加粗：ModeSegmentStyle FontWeight=SemiBold（含未选中），去掉样式级
  IsChecked 触发器；悬停模板触发器的加粗 setter 一并移除（不再需要）
- ✅ 轮廓改绘制衬底：描边环（BorderThickness + 1px 间隙）有留白感，改为在钮背后
  垫灰色圆角矩形（Halo，负 Margin 外扩 5px、圆角 18），无缝紧贴钮边缘，
  视觉比 2px 描边大得多；颜色仍 TabIdleBg（与方案档位悬停同源）；
  透明 ↔ 灰切换零布局位移，外缘恰止于本列边界，不侵犯相邻钮

## 上次状态（模式滑块几何定稿，2026-09-05）
- ✅ 圆角拉满：钮高 26 / 圆角 13（胶囊），轨道 CornerRadius=16、Padding=3→5
- ✅ 钮缩小让位轮廓：钮 Margin=3,0（水平），内容 Padding 0,6 → 0,4
- ✅ 轮廓环改为负 Margin 外扩 3px（BorderThickness=2 + 间隙 1），环圆角 16 = 13 + 3：
  不占钮内部空间、外缘恰好止于本列边界，与相邻钮色底保持 3px 白隙，互不侵犯
- ✅ 轮廓颜色 #808080 → TabIdleBg（与方案①②③悬停色同源，深浅主题自适应）
- ✅ 环 BorderThickness 常驻占位、BorderBrush 透明 ↔ 灰切换，显隐零布局位移

## 上次状态（模式选择重做为滑块样式，2026-09-05）
- ✅ ModeSegmentStyle 重写：两枚独立 RadioButton（不再拼成一整条），
  底色仍常驻紫（AccentPrimary）/ 金（AccentGold），共同置于白色背景容器
  （CornerRadius=8 Padding=3）作滑块轨道；选中 / 悬停钮外围显灰色外轮廓
  （BorderThickness=1.5 常驻占位防布局位移，BorderBrush 透明 ↔ #808080 切换），
  轮廓圆角 7.5 = 内标签圆角 6 + 环厚 1.5；选中 / 悬停字体加粗，光标默认
- ✅ 删除 Helpers/SegmentCorner.cs（附加属性仅旧分段条使用，已无引用）

## 上次状态（方案档位移面板最底 + 底栏间距/等高统一，2026-09-05）
- ✅ 方案①②③档位标签移到右面板最底部（导入/导出配置之下、贴面板底边；
  DockPanel 内先声明的 Bottom 子元素贴最底边，档位 Border 声明在导入导出 Grid 之前）；
  面板顺序：模式分段条 → 分区子标签 → 添加 → 方案列表 → 导入/导出 → 最底方案档位
- ✅ 底栏"全局开关"文字改为"全局开关热键设置："
- ✅ 底栏间距统一 8px（注入下拉/配色下拉右侧 10px → 8px；eye 与音量间 10px → 8px；
  热键按钮右侧补 8px 间距，衔接最右 eye 组）
- ✅ 底栏等高：热键按钮、eye、音量、调整可视化位置统一 Height=26
  （eye/音量 22×20、24×20 → 26×26；热键 Padding 4,3 → 4,0 定高）

## 上次状态（分段条悬停加粗 + 底栏图标移最右，2026-09-05）
- ✅ ModeSegmentStyle：未选中半边悬停时字体加粗（模板触发器 IsMouseOver →
  TextElement.FontWeight；ContentPresenter 无 FontWeight 属性，须用 TextElement 附加属性），
  可点击性暗示；选中半边仍常驻加粗（样式级 IsChecked 触发器）
- ✅ 底栏重排：可视化总开关（eye）与音量按钮（volume）移到底栏最右边
  （新 StackPanel Dock=Right 声明在全局开关键组之前，贴最右边缘），
  底栏从左到右：键盘注入 → 键帽配色 → 调整位置 → 全局开关 → 最右 eye+音量

## 上次状态（金色定稿 #B39041 + 分段条字重 + 导入导出移底）
- ✅ 金色定稿 #B39041：AccentGold 资源 + 键盘区按压模式图块（KeySourceViewModel.s_holdBrush）
- ✅ ModeSegmentStyle 字重：未选中半边恢复正常（Normal），选中半边由
  Style.Triggers IsChecked 触发器常驻加粗（SemiBold）；底色仍常驻紫 / 金
- ✅ 导入 / 导出配置移到方案面板最底部：DockPanel 内声明于 ListBox 之前并
  Dock=Bottom（ListBox 为最后子元素占中间剩余空间），方案列表增长时按钮
  不会被推出面板；面板顺序：方案档位 → 模式分段条 → 分区子标签 → 添加 →
  方案列表 → 底部导入/导出
- ✅ 使用说明 v1.6.0 相应描述同步过时（布局描述），下次发版时更新

## 上次状态（金色统一 #FFCB00 + 分段条简化 + 方案列表去选中特效，2026-09-05）
- ✅ 金色统一改 #FFCB00：AccentGold 资源（App.ApplyTheme）+ 键盘区按压模式图块
  硬编码（KeySourceViewModel.s_holdBrush）；横幅深色文字 #1F1F1F 保留（亮金底对比度）
- ✅ ModeSegmentStyle 简化：两半文字统一白色加粗（去掉右侧深字 #1F1F1F）；
  去掉悬停加暗 / 按下内衬 / 选中暗化全部触发器；光标恢复默认（去掉 Hand）；
  选中态仅常驻加粗（底色本来就常驻紫 / 金）
- ✅ 方案列表 ListBox 加 ItemContainerStyle 重写模板（纯 ContentPresenter）：
  去掉 WPF 默认蓝色选中 / 悬停底色

## 上次状态（发版 v1.6.0，2026-09-05）
- ✅ 版本 1.6.0（csproj Version/FileVersion、窗口标题、托盘文本）
- ✅ 使用说明.txt 更新至 v1.6.0：方案面板布局调整（档位标签上移）、
  开关/按压分段条、键帽配色下拉、可视化悬浮窗位置调整、间隔编辑虚影水印
- ✅ Release 发布并打包：dist/TheCelestialDiviner-1.6.0-win64.zip（1.77 MB / 解压 6.44 MB），
  产物结构与 1.5.0 一致（exe + System.*.dll 依赖 + dd63330 驱动 + 使用说明）

## 上次状态（右面板布局 + 模式分段条）
- ✅ 方案①②③档位标签移到导入/导出配置按钮上方（原顺序：导入导出 → 档位 → 模式）
- ✅ 模式标签重做为分段条（ModeSegmentStyle）：开关/按压两半常驻底色拼成一整条
  （左半开关模式常驻主题紫 AccentPrimary，右半按压模式常驻金 AccentGold），
  选中半边加暗内衬（#26000000）+ 加粗呈按压选中态；未选中半边文字淡化 0.75、
  悬停轻微加暗；按下时更深内衬（#45000000）给出点击按压反馈；
  左右外侧圆角经新增 SegmentCorner.Rounding 附加属性逐半指定
  （Helpers/SegmentCorner.cs；模板内 RelativeSource=TemplatedParent 绑定读取）
- ✅ 金色底文字用 #1F1F1F（与全局开关横幅一致，保证对比度）

## 上次状态（v1.5.0 配色统一，2026-09-04）
- ✅ 全局配色统一：主题紫 #8A5CF5（模式标签选中 / 方案①②③选中 / 开关模式图块 /
  双宏图块 / 总开关键图块 / 横幅开启 / 录入提示条），金色 #D6A01D（按压模式图块 /
  横幅关闭）；硬编码色值提为主题资源（AccentPrimary / AccentGold / HotkeyBg / TabIdleBg），
  后续调色只改 ApplyTheme 一处
- ✅ 热键按钮与总开关键图块改黑色底白字（用户指定）
- ✅ 方案①②③ 内边距扩大呈正方形（ProfileTabStyle 26×26）
- ✅ 未选中模式标签 / 档位按钮增加灰色底（浅 #DFDFDF / 深 #3A3A3A），可点击性明确
- ✅ 行内可视化开关（eye）改用 IconButtonStyle（与删除按钮同款，视觉统一）
- ✅ 新增全局键位可视化总开关（音量右侧，eye 图标默认开启）：关闭时全部键位统一暂停显示
  （服务层闸门 + 列表 eye 图标整体暗淡禁用），各键位开关状态保留，恢复后延续；
  状态持久化（config v5 GlobalVisualEnabled 字段），导入配置同步还原
- ✅ 发版：使用说明更新至 v1.5.0（新增三档位 / 键位可视化说明），
  dist 打包 TheCelestialDiviner-1.5.0-win64.zip（1.76 MB / 解压 6.38 MB）

## 上次状态（v1.5.0 主体，GPL-3.0 许可证变更 + 键位可视化悬浮层）
- ✅ 许可证 MIT → GPL-3.0（键帽视觉复刻自 keyviz GPL 设计）
- ✅ 键帽可视化悬浮层（右下角点击穿透 + 置顶 + PBT 双层键帽 + 组合键并排 + 连击角标）
- ✅ 方案键位自动注册可视化，方案行 eye / eye-off 逐键开关（实时落盘）
- ✅ 修复：Geometry 资源类型不匹配 + 异常处理器弹窗重入渲染管线导致栈溢出闪退
  （Renderer 异常不再弹模态框，WER LocalDumps 已配置）

## 上次状态（v1.4.0，界面精简 + 方案三档位）
- ✅ 许可证 MIT → **GPL-3.0**（键帽视觉复刻自 keyviz 的 GPL 设计，衍生作品整体转 GPL；
  LICENSE 已替换为 GPL-3.0 全文，README 许可段落与出处声明同步）
- ✅ 新增键位可视化悬浮层（KeycapOverlayWindow）：屏幕右下角，点击穿透
  （WS_EX_TRANSPARENT + NOACTIVATE + TOOLWINDOW）+ 置顶 + 不抢焦点；
  键帽复刻 keyviz PBT 样式（外层深色底座 #1a1a1a + 内层浅色渐变按压面，
  按下内层下沉 0.15em / easeInOutExpo 100ms；组合键修饰键独立键帽并排；
  连击 ≥2 次右上角红色 ×N 角标弹跳显示）
- ✅ 显示语义：按下 → 显示并停在屏上（计时停止）；释放 → 2.5 秒后淡出隐藏；
  释放事件与当前显示组合匹配才抬键帽（防止按住 A 期间 B 的释放误抬 A）
- ✅ KeyVisualizerService：注册表（方案键位 → 显示开关）+ 钩子事件订阅 +
  UI Dispatcher 封送；修饰键状态用 GetAsyncKeyState 实时检测
- ✅ 有方案的键位自动注册可视化（默认全部开启）：启动 / 添加 / 删除 / 导入 /
  档位切换 / 总开关键迁移后 SyncVisualizerRegistry 全量同步
- ✅ 方案行新增可视化开关列（lucide eye / eye-off 图标，ISC 许可）：
  默认 eye；关闭后 eye-off 半透明；切换实时落盘（config v5 新增 VisualKeys 表，
  缺省视为开启），仅控制悬浮层显示、不影响连发本身
- ✅ 版本 1.5.0（csproj / 窗口标题 / 托盘文本）

## 上次状态（v1.4.0，界面精简 + 方案三档位）
- ✅ 删除顶部状态栏（定时器 / 权限 / 钩子 / 总开关状态文本行）；
  语音音量设置项移入右侧方案面板“全局开关热键”一行（热键后面，紧凑滑块）
- ✅ 底部栏“导出配置”后新增方案档位切换 ①②③（RadioButton，复用 ModeTabStyle 选中效果）：
  - 配置模型 v4：`AppConfig.Profiles`（固定 3 套方案字典）+ `ActiveProfile`（默认 0 = 方案①）
  - 运行期镜像约定：`Schemes` 与 `Profiles[ActiveProfile]` 同一实例，全部方案编辑直接落在活动档位
  - 切换档位：重建方案列表 + 刷新图块高亮 + 调度器 ApplyConfig + 实时落盘 + 日志
  - 按钮选中态由 code-behind 随 VM.ActiveProfile 同步（避开 RadioButton 组内互斥取消选中破坏绑定的 WPF 已知问题）
  - 迁移：v3 旧配置存量方案整体迁入档位①；v4 配置反序列化完整还原；导入配置后同步档位按钮选中态
- ✅ 小键盘句点图块改名：图标行显示省略的“Num”、名称行显示“.”（原“Num.”单行）
- ✅ 定时器 1ms 分辨率提升保留（连发间隔精度功能基础），仅去掉状态栏展示
- ✅ 版本 1.4.0（csproj Version/FileVersion 1.4.0.0 / 窗口标题 v1.4）

## 上次状态（v1.3.0 修订，连发间隔下限 10ms + 方案行内编辑）
- ✅ 连发间隔默认值与下限改为 **10ms**（Constants.DefaultIntervalMs = MinIntervalMs = 10，
  上限仍 100ms；TargetKeyConfig.IntervalMs 默认值同步 10ms）
- ✅ 配置兼容：ConfigService.NormalizeIntervals 在加载 / 导入时把 < 10ms 的存量间隔钳制到 10ms
- ✅ 删除方案面板顶部的全局"连发间隔"设置项（顶栏仅保留全局开关热键）
- ✅ 方案列表行内间隔改为可编辑文本框：新方案按 10ms 默认值生成，点击文本框可修改，
  回车 / 失焦提交，非法输入回退上次有效值，越界自动钳制到 10~100；
  修改后重建调度器任务实时生效并落盘（UpdateSchemeInterval）
- ✅ 录入流程不再读取全局间隔输入：新方案统一用 Constants.DefaultIntervalMs 生成

## 上次状态（v1.3.0，开关模式分区 + 顶栏合并）
- ✅ 方案面板顶部合并为一行：**全局开关热键 + 连发间隔**（热键在前、间隔在后）
  （v1.3.0 修订已移除间隔输入，顶栏仅保留热键）
- ✅ 开关模式新增三个分区子标签（常规开关 / 轮转开关 / 双宏开关；仅开关模式显示）：
  - **分区1 常规开关**：原来的正常开关模式，互不影响
  - **分区2 轮转开关**：Rotate 分区内互斥——开启某轮转键时自动停止该分区内正在运行的其他键
    （省去手动关闭；调度器 Activate 内遍历任务 Deactivate 其他 Rotate 激活任务）
  - **分区3 双宏开关**：两步连选两个键（头键 = 开关键位，仅头键行显示启用勾选框，两行一体）；
    按下头键 → 两键轮流触发（1-2-1-2…，间隔 = 录入的连发间隔），再按一次头键一起停止
- ✅ 模型：新增 `ToggleSection` 枚举（Normal/Rotate/Dual，JsonStringEnumConverter）+ `KeyScheme.Section`
  （默认 Normal，仅 Mode=Toggle 生效）；AppConfig.CurrentVersion 2 → 3；
  MigrateIfNeeded 改为 `fromVersion < 2` 才跑 v1→v2 逻辑（v2→v3 仅推进版本号）
- ✅ 调度器：RunningTask.Config 改 `Configs`（List<TargetKeyConfig>）+ Section；双宏 = 单任务双目标，
  TaskLoop 按 phase 轮流 Click，停止时 phase 归零（重启从第 1 键开始）；事件日志同步
- ✅ 录入流程：双宏两步录入（第 1 键暂存不落盘，提示条同步进度）；校验：滚轮拒、总开关占用拒、
  两键相同拒、任一方已注册方案拒、任一方已是其他双宏辅键拒；普通单键路径同样拒绝"已是双宏辅键"
- ✅ 列表：双宏行两行一体（第 1 行勾选 + 主键 + 间隔 + 删除，第 2 行仅辅键名占位对齐）；
  按模式 + 分区过滤；辅键图块在启用双宏方案下蓝色标记（"⇄ A · B"，KeySourceViewModel.MarkDualPartner）
- ✅ 版本 1.3.0（csproj Version/FileVersion 1.3.0.0 / 窗口标题 v1.3）

## 上次状态（v1.2.2，commit 见 git log）
- ✅ 键盘区按真实 ANSI 物理布局重排（KeyboardRowPanel 键宽单位自适应）
- ✅ 暗淡效果触发互换：总开关开启时控件暗淡、关闭时清晰 + 黄色横幅
- ✅ 修复构造阶段按钮晚于配置加载的初始暗淡状态不同步

## 上次状态（v1.2.1，commit b1743e5）
- ✅ 修复总开关语音无声：WPF MediaPlayer 不解析 pack://application 资源 URI
  （Media Foundation 管线静默失败），改为启动时解包 WAV 到 %TEMP%\TheCelestialDiviner\
  并以文件 URI 播放；挂 MediaFailed 日志钩子，失败必留痕
- ✅ 键盘注入默认模式改为 DD 驱动（AppConfig.DefaultKeyboardMode = 3）
- ✅ 实机验证：F9 注入 → 总开关切换日志正常，语音播放无 MediaFailed

## 上次状态（v1.2.0，commit 57a47b8）— .NET Framework 4.8 迁移
- ✅ 发布体积 147MB → 5.1MB（主 exe 0.4MB + System.Text.Json 等小依赖 + dd63330.dll 3.7MB）
- ✅ Win10 1903+ / Win11 零安装（4.8 系统内置；不用 4.8.1 因其覆盖面反而小）
- ✅ 新增 Helpers/CompilerShims.cs（init/required 特性垫片）、
  Helpers/Compatibility.cs（Compat.Clamp / CombineHashCodes / 集合扩展）
- ✅ 调用点替换：Math.Clamp → Compat.Clamp（7 处）、File.Move 三参 → Copy+Delete、
  HashCode.Combine → Compat.CombineHashCodes、KVP 解构扩展
- ✅ System.Text.Json 改 NuGet 包 8.0.5 + AutoGenerateBindingRedirects
- ⚠️ 分发为目录形式（exe + config + System.*.dll + dd 驱动），不再是单文件

## 全部文件清单
- 根：TheCelestialDiviner.sln / .gitignore / README.md / PROGRESS.md / tools/make_icon.py
- src/TheCelestialDiviner/：csproj / app.manifest / App.xaml(.cs)
- Helpers：Constants / Logger / NativeMethods / ThemeHelper / Converters / Compatibility /
  CompilerShims / KeyboardRowPanel（新）
- Models：AppConfig.cs（InputKind/MouseInput/TargetKind/TriggerMode/InputSource/
  TargetKeyConfig/KeyScheme/GlobalSwitchConfig+Clone/AppConfig）
- Services：InputHookService / InputSimulatorService / TaskSchedulerService / DdDriverService /
  ConfigService / TimerResolutionService / KeyRecorder / InputNameMapper / SoundCueService
- ViewModels：MainViewModel(partial: 核心 + Collections) / KeySourceViewModel（含 WidthUnits）/
  TargetKeyViewModel / RelayCommand
- Views：MainWindow.xaml(.cs) / SchemeDialog.xaml(.cs) / GlobalSwitchDialog.xaml(.cs)
- Resources：app.ico + Sounds/StartVoice.wav / StopVoice.wav（内嵌，播放时解包）

## 部署形态
- 目标框架：net48（.NET Framework 4.8，系统内置零安装）
- 发布：dotnet publish -c Release -o bin\Release\net48-publish（约 5.1MB 全包）
- 高分屏：app.manifest PerMonitorV2（Framework 下正统做法）
- 运行要求：管理员权限（全局钩子 + 注入）
