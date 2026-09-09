using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using TheCelestialDiviner.Helpers;
using TheCelestialDiviner.Models;
using TheCelestialDiviner.Services;
using TheCelestialDiviner.ViewModels;

namespace TheCelestialDiviner.Views;

/// <summary>
/// 主窗口：状态栏/横幅/鼠标区/键盘区/底部栏/日志面板；
/// 钩子事件 Dispatcher 封送、音量滑块、关闭到托盘、方案与全局开关对话框。
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly App _app;

    public MainWindow(MainViewModel vm, App app)
    {
        InitializeComponent();
        _vm = vm;
        _app = app;
        DataContext = _vm;

        // 点击编辑框之外任意区域时自动提交时序编辑（按压时长 / 连发间隔）：
        // 仅当焦点确实处于方案行的时序编辑框时才介入（同步提交并移出焦点），
        // 不做全局焦点转移 —— 避免打断 ComboBox 下拉 / 列表 / 按钮自身的焦点行为。
        PreviewMouseDown += (_, _) =>
        {
            if (Keyboard.FocusedElement is not TextBox tb ||
                tb.DataContext is not SchemeRowViewModel row)
                return;
            if (tb.IsMouseOver) return; // 点击编辑框自身：正常编辑，不触发提交
            row.CommitHoldEdit();
            row.CommitIntervalEdit();
            Keyboard.ClearFocus();      // 非可聚焦目标也能退出编辑态；可聚焦目标随后自行接管焦点
        };

        // 钩子未安装时的兜底退出：Esc 退出键位选择态。钩子正常时由钩子路径处理，
        // 此处不触发，避免同一次按键双重执行（双宏成对录入中会“先放弃第 1 键又立即退出”）。
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || !_vm.PickingActive || _vm.HookInstalled) return;
            _vm.TryExitPicking();
            e.Handled = true;
        };

        Loaded += OnLoaded;
    }

    /// <summary>底部栏方案档位按钮（①②③④，选中态由 VM.ActiveProfile 驱动同步）。</summary>
    private RadioButton[] ProfileButtons => new[] { Profile1Button, Profile2Button, Profile3Button, Profile4Button };

    /// <summary>方案列表按压时长框：回车提交（提交绑定并清除焦点）。</summary>
    private void OnHoldBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (sender is TextBox tb)
        {
            CommitHoldBox(tb);
            Keyboard.ClearFocus();
        }
        e.Handled = true;
    }

    /// <summary>方案列表间隔框：回车提交（提交绑定并清除焦点）。</summary>
    private void OnIntervalBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (sender is TextBox tb)
        {
            CommitIntervalBox(tb);
            Keyboard.ClearFocus();
        }
        e.Handled = true;
    }

    /// <summary>
    /// 时序编辑框（按压时长 / 连发间隔）获得鼠标捕获（点击进入）时清空文本只留虚影水印
    /// （HoldMs / IntervalMs），直接键入数值而不必先手动删除。
    /// </summary>
    private void OnTimingBoxGotMouseCapture(object sender, MouseEventArgs e)
    {
        if (sender is TextBox tb)
        {
            tb.Clear();
            tb.CaretIndex = 0;
        }
    }

    /// <summary>按压时长框失焦：提交编辑（非法 / 空值回退上次有效值 = 虚影值）。</summary>
    private void OnHoldBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
            CommitHoldBox(tb);
    }

    /// <summary>间隔框失焦：提交编辑（非法 / 空值回退上次有效值 = 虚影值）。</summary>
    private void OnIntervalBoxLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
            CommitIntervalBox(tb);
    }

    /// <summary>把按压时长框的编辑文本提交给行 VM（VM 负责回退逻辑并刷新虚影）。</summary>
    private static void CommitHoldBox(TextBox tb)
    {
        if (tb.DataContext is SchemeRowViewModel row)
            row.CommitHoldEdit();
    }

    /// <summary>把间隔框的编辑文本提交给行 VM（VM 负责回退逻辑并刷新虚影）。</summary>
    private static void CommitIntervalBox(TextBox tb)
    {
        if (tb.DataContext is SchemeRowViewModel row)
            row.CommitIntervalEdit();
    }

    // ---------- 初始化 ----------
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _app.SetupTray(this);

        // 底栏设置菜单：加载时回填 ● 选中标记（键帽配色子项容器按需生成，
        // 展开时经 SubmenuOpened 事件再同步）；后续变更由下方 VM PropertyChanged 订阅驱动。
        SyncSettingsMenu();
        // 键盘注入 / 键帽可视化叶子项：悬停注释联动（分组项经 XAML 接线，配色子项经样式 EventSetter）。
        foreach (var item in KeyboardModeMenu.Items.OfType<MenuItem>())
        {
            item.MouseEnter += OnMenuCommentEnter;
            item.MouseLeave += OnMenuCommentLeave;
        }
        foreach (var item in VisualizerModeMenu.Items.OfType<MenuItem>())
        {
            item.MouseEnter += OnMenuCommentEnter;
            item.MouseLeave += OnMenuCommentLeave;
        }

        // 连发时序档位单选钮：加载时按 VM 回填，导入配置后同步。
        SyncTimingPresetRadios(_vm.TimingPreset);
        _vm.TimingPresetChanged += SyncTimingPresetRadios;

        // 音量滑块初值回填（绑定双向，拖动后写 VM 并防抖落盘）。
        SoundVolumeSlider.Value = _vm.SoundVolume;
        // 音量按钮 → 向上弹出滑块弹层；点击弹层外自动关闭（StaysOpen=False）。
        VolumeButton.Click += (_, _) => VolumePopup.IsOpen = !VolumePopup.IsOpen;
        // 键帽透明度按钮 → 向上弹出滑块弹层（同音量交互；点击弹层外自动关闭）。
        OpacityButton.Click += (_, _) => OpacityPopup.IsOpen = !OpacityPopup.IsOpen;

        // 夜间模式按钮：切换白天 / 夜间主题（VM 落盘并重注主题资源）。
        NightModeButton.Click += (_, _) => _vm.NightMode = !_vm.NightMode;

        // 方案档位按钮：初值回填；后续随 VM.ActiveProfile 变更同步选中态。
        // 不用 IsChecked 双向绑定：同组互斥取消选中会破坏绑定（WPF 已知问题）。
        // 设置菜单选中标记：键盘注入 / 键帽配色 / 键帽可视化 随 VM 状态同步
        //（菜单点击 / 导入配置 / DD 就绪检查回退）。
        // 注释区公告：初渲染（内嵌兜底或已同步的远端缓存）；随 VM.NoticeContent 更新刷新。
        RenderDefaultComment(_vm.NoticeContent);
        // 方案档位按钮：初值回填；后续随 VM.ActiveProfile 变更同步选中态。
        // 不用 IsChecked 双向绑定：同组互斥取消选中会破坏绑定（WPF 已知问题）。
        // 设置菜单选中标记：键盘注入 / 键帽配色 / 键帽可视化 随 VM 状态同步
        //（菜单点击 / 导入配置 / DD 就绪检查回退）。
        SyncProfileButtons(_vm.ActiveProfile);
        _vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.ActiveProfile))
                Dispatcher.BeginInvoke(() => SyncProfileButtons(_vm.ActiveProfile));
            else if (args.PropertyName == nameof(MainViewModel.PickingKind))
                Dispatcher.BeginInvoke(UpdateCycleKeyPickingComment);   // 进入/退出切换方案热键录入 → 注释区联动
            else if (args.PropertyName == nameof(MainViewModel.NoticeContent))
                Dispatcher.BeginInvoke(() => RenderDefaultComment(_vm.NoticeContent));   // 公告远端更新 → 默认内容刷新
            else if (args.PropertyName is nameof(MainViewModel.KeyboardMode)
                     or nameof(MainViewModel.KeycapSchemeName)
                     or nameof(MainViewModel.VisualizerModeIndex))
                SyncSettingsMenu();
        };

        // 键位可视化悬浮层（钩子事件 → 悬浮层；UI 线程窗口创建后接线）。
        _vm.Visualizer.AttachOverlay();
        // 位置调整模式：底栏按钮 → 悬浮窗虚拟键帽拖拽 → 确认回调保存落盘。
        _vm.AdjustVisualizerRequested += onConfirm =>
            _vm.Visualizer.BeginAdjust(onConfirm);
        // 语音设置：底栏按钮 → 模态框（导入自定义提示音 / 试听 / 重置回默认）。
        _vm.VoiceSettingsRequested += ShowVoiceSettings;

        // 钩子事件 → UI 封送（调度器本身线程安全，但日志与 UI 属性需封送）。
        _app.Hooks.SourceDown += src =>
            Dispatcher.BeginInvoke(() => _vm.HandleHookDown(src));
        _app.Hooks.SourceUp += src =>
            Dispatcher.BeginInvoke(() => _vm.HandleHookUp(src));

        // VM 弹窗请求 + 键位录入选择态的鼠标命中测试。
        _vm.ImportRequested += ImportConfig;
        _vm.ExportRequested += ExportConfig;
        _vm.PickingMouseReceived += OnPickingMouseReceived;

        // 批量删除勾选连发键：确认框（破坏性操作，不可撤销）。
        _vm.DeleteCheckedRequested += count =>
        {
            if (MessageBox.Show(this, $"确定删除勾选的 {count} 个连发键？此操作不可撤销。",
                    "衍天高手", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                != MessageBoxResult.Yes)
                return;
            _vm.DeleteCheckedSchemes();
        };

        // 检查更新：确认框（列出远端版本与发布说明）→ 结果消息框（无图标无提示音）→ 重启退出。
        _vm.UpdateConfirmRequested += check =>
        {
            var notes = check.Notes?.Trim() ?? "";
            if (notes.Length > 600) notes = notes.Substring(0, 600) + "…";
            return MessageBox.Show(this,
                $"发现新版本 {check.TagName}（当前 v{UpdateService.CurrentVersion.ToString(3)}），是否下载并安装？"
                + "\n\n确认后程序将自动下载更新包并重启完成安装。"
                + (notes.Length > 0 ? "\n\n—— 发布说明 ——\n" + notes : ""),
                "检查更新 — 衍天高手", MessageBoxButton.YesNo, MessageBoxImage.Question)
                == MessageBoxResult.Yes;
        };
        // 未发现新版本 / 检查失败等结果提示：不带图标的普通消息框（Information 图标会响提示音）。
        _vm.UpdateMessageRequested += (title, message) =>
            MessageBox.Show(this, message, title, MessageBoxButton.OK);
        _vm.UpdateRestartRequested += () => _app.ExitForUpdateRestart();

        // DD 驱动引导获取：确认框（用户发起官方渠道下载，程序只做下载器不分发闭源驱动）。
        _vm.DdDriverFetchConfirmRequested += message =>
            MessageBox.Show(this, message, "DD 驱动 — 衍天高手",
                    MessageBoxButton.YesNo, MessageBoxImage.Question)
            == MessageBoxResult.Yes;

        // 运行时初始化（定时器分辨率提升 + 钩子安装 + 权限状态回填）。
        _vm.InitializeRuntime(IsElevated());
    }

    // ---------- 注释区域（原日志区）：悬停标签显示说明，默认显示链接 ----------

    /// <summary>标签悬停 → 注释文本映射（常规 / 轮转 / 双宏 / 开关模式 / 按压模式）。</summary>
    private static readonly Dictionary<string, string> SectionComments = new()
    {
        ["常规开关"] = "按一下开启该键位的自动连发，再按一下则停止。",
        ["轮转开关"] = "支持两个或多个轮转开关模式键位之间一键切换。",
        ["双宏开关"] = "首键位一键启停两个开关模式键位的轮流连发。取消勾选第二个勾选框，则首键位一键启停第二个开关模式键位的连发，不再轮转。",
        ["开关模式"] = "按一下开启该键位的自动连发，再按一下则停止。",
        ["按压模式"] = "按住该键位则自动连发，松开自动停止。",
        ["常规档 26ms"] = "40~100 帧全区间零丢失，约 19 发/秒（推荐默认）。",
        ["极限档 11ms"] = "帧率 ≥90 时上限约 45 发/秒；低帧率会丢发，40 帧附近骤降。",
    };

    /// <summary>悬停进入：注释区域显示对应说明文本。</summary>
    private void OnSectionTabMouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is RadioButton rb &&
            rb.Content is string content &&
            SectionComments.TryGetValue(content, out var text))
        {
            ShowComment(text);
        }
    }

    /// <summary>悬停离开：恢复默认链接内容。</summary>
    private void OnSectionTabMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        => RestoreComment();

    /// <summary>注释区统一展示：显示说明文本并隐藏默认内容（诗句 + 链接），避免两段文本重叠。</summary>
    private void ShowComment(string text)
    {
        CommentText.Text = text;
        CommentText.Visibility = Visibility.Visible;
        DefaultComment.Visibility = Visibility.Collapsed;
    }

    /// <summary>恢复注释区默认内容（诗句 + 链接）。切换方案热键录入期间保持录入说明（其他悬停说明离开后也回到录入说明）。</summary>
    private void RestoreComment()
    {
        if (_vm.PickingKind == PickKind.SetCycleKey)
        {
            ShowComment(CycleKeyComment);
            return;
        }
        CommentText.Text = "";
        CommentText.Visibility = Visibility.Collapsed;
        DefaultComment.Visibility = Visibility.Visible;
    }

    // ---------- 切换方案热键 → 注释区联动 ----------
    /// <summary>切换方案热键的注释区说明（悬停底栏"切换方案设置"区或进入其录入态时显示）。</summary>
    private const string CycleKeyComment =
        "切换方案热键：按下已设置的键位，按 ①→②→③→④ 顺序切换到下一个非空方案档位（空档位自动跳过）。" +
        "点击按钮进入录入：左键点击左侧键位或直接按键完成设置；右键已设置的键位取消，恢复未设置；Esc 或点击空白处退出录入。";

    /// <summary>悬停底栏"切换方案设置"区（标签或按钮）：注释区显示说明。</summary>
    private void OnCycleKeyCommentEnter(object sender, System.Windows.Input.MouseEventArgs e)
        => ShowComment(CycleKeyComment);

    /// <summary>离开底栏"切换方案设置"区：恢复注释区默认内容。</summary>
    private void OnCommentMouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        => RestoreComment();

    /// <summary>
    /// 录入态切换联动注释区（订阅 VM.PickingKind 通知）：进入切换方案热键录入显示说明，
    /// 退出后恢复默认内容；注释区已被其他说明占用时不覆盖。
    /// </summary>
    private void UpdateCycleKeyPickingComment()
    {
        if (_vm.PickingKind == PickKind.SetCycleKey)
            ShowComment(CycleKeyComment);
        else if (CommentText.Text == CycleKeyComment)
            RestoreComment();
    }

    // ---------- 设置菜单 → 注释区联动 ----------
    /// <summary>菜单分组悬停说明（键 = 分组标题；替代悬浮 Tooltip，与分区标签注释共用展示位）。</summary>
    private static readonly Dictionary<string, string> MenuComments = new()
    {
        ["键盘注入"] = "游戏内键盘连发不生效时逐个尝试：扫描码模式 → 消息模式 → DD 驱动模式。消息模式仅游戏在前台时有效；DD 驱动模式为物理级注入（需 dd63330.dll + 管理员权限）。",
        ["键帽配色"] = "可视化键帽配色方案（复刻自 keyviz 预设），切换实时生效并落盘。",
        ["键帽可视化"] = "键帽可视化方案：全部 = 所有键位；修饰键和自定义键 = 修饰键与方案内键位；自定义键 = 仅方案内键位（均受单键开关约束）。",
        ["检查更新"] = "查询 Gitee 上的最新 Release：发现新版本时经确认自动下载安装并重启；没有更新或网络不可用会以普通消息提示。",
    };

    /// <summary>悬停菜单项：在注释区展示所属分组的说明文本（分组项取自身标题，叶子项取父分组标题）。</summary>
    private void OnMenuCommentEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not MenuItem item) return;
        var text = MenuComments.TryGetValue(item.Header?.ToString() ?? "", out var own) ? own : null;
        if (text is null && item.Parent is MenuItem group)
            MenuComments.TryGetValue(group.Header?.ToString() ?? "", out text);
        if (text is null || text.Length == 0) return;
        ShowComment(text);
    }

    /// <summary>离开菜单项：恢复注释区默认内容（复用分区标签的离开逻辑）。</summary>
    private void OnMenuCommentLeave(object sender, System.Windows.Input.MouseEventArgs e)
        => OnSectionTabMouseLeave(sender, e);

    // ---------- 底栏元素 → 注释区联动 ----------
    /// <summary>底栏元素悬停说明（键 = 元素 x:Name；替代悬浮 Tooltip，与菜单注释共用展示位）。</summary>
    private static readonly Dictionary<string, string> BottomBarComments = new()
    {
        ["StatusReminderButton"] = "状态提醒：总开关开启时在键帽悬浮区常驻应用图标键帽；任一方案连发时自动隐藏，全部连发停止 2.5 秒后恢复（不受键位可视化总开关约束）。",
        ["MasterKeySettingPanel"] = "全局开关热键：点击按钮进入录入，左键点击左侧键位或直接按键即完成录入（默认 F9）。",
        ["AdjustVisualizerButton"] = "调整键帽显示位置：拖动应用图标键帽到目标处，虚线框为键帽可显示区域，点击确定保存。",
    };

    /// <summary>悬停底栏元素（状态提醒 / 全局开关热键设置 / 调整可视化位置）：注释区显示对应说明。</summary>
    private void OnBottomBarCommentEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is FrameworkElement { Name: { Length: > 0 } name } &&
            BottomBarComments.TryGetValue(name, out var text))
            ShowComment(text);
    }

    /// <summary>注释区域链接点击：用系统默认浏览器打开。</summary>
    private void OnCommentLinkClick(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch
        {
            // 浏览器启动失败时静默（不影响主功能）。
        }
        e.Handled = true;
    }

    // ---------- 注释区公告渲染（Notice.md：内嵌兜底 + Gitee 远端更新） ----------
    /// <summary>行内链接标记 [文本](URL) 识别。</summary>
    private static readonly Regex s_noticeLinkRegex = new(@"\[([^]\r\n]+)]\(([^)\r\n]+)\)");

    /// <summary>裸 URL 识别（中文全角标点/括号收尾不算 URL 的一部分）。</summary>
    private static readonly Regex s_noticeUrlRegex = new(@"https?://[^\s（）【】<>，。；、]+");

    /// <summary>把公告文本渲染进注释区默认内容（按行拆分；链接标记与裸 URL 渲染为可点击超链接）。</summary>
    private void RenderDefaultComment(string content)
    {
        DefaultComment.Inlines.Clear();
        if (string.IsNullOrEmpty(content)) return;
        var lines = content.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0) DefaultComment.Inlines.Add(new LineBreak());
            AppendNoticeLine(lines[i]);
        }
    }

    /// <summary>渲染一行公告：链接标记之外的部分再扫描裸 URL，其余按纯文本输出。</summary>
    private void AppendNoticeLine(string line)
    {
        var index = 0;
        foreach (Match link in s_noticeLinkRegex.Matches(line))
        {
            AppendPlainTextWithUrls(line.Substring(index, link.Index - index));
            AppendHyperlink(link.Groups[1].Value, link.Groups[2].Value);
            index = link.Index + link.Length;
        }
        AppendPlainTextWithUrls(line.Substring(index));
    }

    /// <summary>纯文本中再识别裸 URL（其余直接输出；空片段跳过）。</summary>
    private void AppendPlainTextWithUrls(string text)
    {
        var index = 0;
        foreach (Match url in s_noticeUrlRegex.Matches(text))
        {
            if (url.Index > index) DefaultComment.Inlines.Add(new Run(text.Substring(index, url.Index - index)));
            AppendHyperlink(url.Value, url.Value);
            index = url.Index + url.Length;
        }
        if (index < text.Length) DefaultComment.Inlines.Add(new Run(text.Substring(index)));
    }

    /// <summary>插入可点击超链接（主题紫随日/夜切换；非法 URL 退化为纯文本）。</summary>
    private void AppendHyperlink(string text, string url)
    {
        if (text.Length == 0) return;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            DefaultComment.Inlines.Add(new Run(text));
            return;
        }
        var link = new Hyperlink { NavigateUri = uri };
        link.Inlines.Add(new Run(text));
        link.SetResourceReference(Hyperlink.ForegroundProperty, "AccentPrimary");
        link.RequestNavigate += OnCommentLinkClick;
        DefaultComment.Inlines.Add(link);
    }

    /// <summary>方案档位按钮选中态同步（仅驱动选中项，不回写 VM）。</summary>
    private void SyncProfileButtons(int active)
    {
        for (var i = 0; i < ProfileButtons.Length; i++)
        {
            var target = i == active;
            if (ProfileButtons[i].IsChecked != target)
                ProfileButtons[i].IsChecked = target;
        }
    }

    /// <summary>当前进程是否以管理员身份运行。</summary>
    private static bool IsElevated()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(identity)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    // ---------- 键位录入选择态 ----------
    /// <summary>模式标签切换（XAML 解析期 IsChecked 即触发，此时 VM 未注入，需判空）。</summary>
    private void OnModeTabChecked(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        if (sender is RadioButton { Tag: string tag })
            _vm.SelectedMode = tag == "Hold" ? TriggerMode.Hold : TriggerMode.Toggle;
    }

    /// <summary>开关分区子标签切换（常规 / 轮转 / 双宏；同样需判空防 XAML 解析期触发）。</summary>
    private void OnSectionTabChecked(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        if (sender is RadioButton { Tag: string tag } &&
            Enum.TryParse<ToggleSection>(tag, out var section))
            _vm.SelectedSection = section;
    }

    /// <summary>
    /// 底部栏方案档位切换（①②③④；XAML 解析期 IsChecked 即触发，需判空）。
    /// 同组 RadioButton 互斥，点击选中项只触发一次 Checked；VM 侧同步重建方案列表、
    /// 调度器任务并实时落盘。
    /// </summary>
    private void OnProfileChecked(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        if (sender is RadioButton { Tag: string tag } &&
            int.TryParse(tag, out var index))
            _vm.ActiveProfile = index;
    }

    /// <summary>连发时序档位切换（常规 0 / 极限 1；XAML 解析期 IsChecked 即触发，需判空）。</summary>
    private void OnTimingPresetChecked(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        if (sender is RadioButton { Tag: string tag } &&
            int.TryParse(tag, out var preset))
            _vm.TimingPreset = preset;
    }

    /// <summary>按 VM 当前档位回填时序单选钮（加载 / 导入后调用；赋值触发的 Checked
    /// 事件写回相同值，VM 侧等值判断不会重复落盘）。</summary>
    private void SyncTimingPresetRadios(int preset)
    {
        TimingRegularRadio.IsChecked = preset == 0;
        TimingExtremeRadio.IsChecked = preset == 1;
    }

    // ---------- 底栏设置菜单 ----------
    /// <summary>
    /// 按 VM 当前状态回填设置菜单的 ● 选中标记（键盘注入 / 键帽配色 / 键帽可视化）。
    /// 键帽配色子项容器在首次展开时才生成，故除加载 / 状态变更外，
    /// 菜单展开（SubmenuOpened）时也会再同步一次。
    /// </summary>
    private void SyncSettingsMenu()
    {
        SetMenuCheck(KeyboardModeMenu.Items, _vm.KeyboardMode);
        SetMenuCheck(VisualizerModeMenu.Items, _vm.VisualizerModeIndex);
        for (var i = 0; i < KeycapSchemeMenu.Items.Count; i++)
        {
            if (KeycapSchemeMenu.ItemContainerGenerator.ContainerFromIndex(i) is not MenuItem item) continue;
            item.IsChecked = string.Equals(item.Header?.ToString(), _vm.KeycapSchemeName, StringComparison.Ordinal);
        }
    }

    /// <summary>按 Tag 索引回填一组菜单项的选中标记（XAML 中 Tag 为字符串索引）。</summary>
    private static void SetMenuCheck(ItemCollection items, int current)
    {
        foreach (var item in items.OfType<MenuItem>())
            item.IsChecked = int.TryParse(item.Tag?.ToString(), out var index) && index == current;
    }

    /// <summary>设置菜单任一级展开：子项容器此时已生成，补同步 ● 选中标记。</summary>
    private void OnSettingsMenuSubmenuOpened(object sender, RoutedEventArgs e) => SyncSettingsMenu();

    /// <summary>
    /// 管理模式中的鼠标按下（钩子路径）：光标落在键鼠图块上 → 右键取消该键方案（双宏整对取消）/
    /// 其余鼠标键录入该键（双宏按成对录入处理）；落在键鼠区外（含本窗口其余区域与其他窗口）→
    /// 请求退出（双宏成对录入中先放弃第 1 键，偶数态才真正退出）。
    /// </summary>
    private void OnPickingMouseReceived(InputSource pressed)
    {
        var screen = System.Windows.Forms.Cursor.Position;
        var source = FindTileAt(KeyboardArea, screen) ?? FindTileAt(MouseArea, screen);
        if (source is null)
        {
            _vm.TryExitPicking();
            return;
        }
        if (pressed.Kind == InputKind.Mouse && pressed.Mouse == MouseInput.Right)
            _vm.RemovePickAt(source);
        else
            _vm.CompletePick(source);
    }

    /// <summary>命中测试容器内指定屏幕坐标处的键鼠图块，返回其输入源（占位空白忽略）。</summary>
    private static InputSource? FindTileAt(UIElement container, System.Drawing.Point screen)
    {
        var rel = container.PointFromScreen(new Point(screen.X, screen.Y));
        if (container.InputHitTest(rel) is not DependencyObject hit) return null;

        var d = hit;
        while (d is not null)
        {
            if (d is FrameworkElement { DataContext: KeySourceViewModel tile }
                && !tile.IsSpacer && tile.Source is not null
                && !(tile.Source.Kind == InputKind.Keyboard && tile.Source.VirtualKey == 0))
            {
                return tile.Source;
            }
            d = System.Windows.Media.VisualTreeHelper.GetParent(d);
        }
        return null;
    }

    // ---------- 导入 / 导出 ----------
    private void ImportConfig()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入配置",
            Filter = "配置文件 (*.json)|*.json|所有文件 (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var json = System.IO.File.ReadAllText(dlg.FileName);
            _vm.ImportFromJson(json);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"读取文件失败：{ex.Message}", "衍天高手",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportConfig()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出配置",
            Filter = "配置文件 (*.json)|*.json",
            FileName = "TheCelestialDiviner-config.json"
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            System.IO.File.WriteAllText(dlg.FileName, _vm.ExportToJson());
            _vm.AddLog($"配置已导出到 {dlg.FileName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"写入文件失败：{ex.Message}", "衍天高手",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>打开语音设置模态框（自定义总开关开启 / 关闭与方案切换的提示音）。</summary>
    private void ShowVoiceSettings()
    {
        var dlg = new VoiceSettingsWindow(_vm) { Owner = this };
        dlg.ShowDialog();
    }

    // ---------- 交互 ----------
    // “全部停止”按钮已取消：其职责与全局总开关重叠，停止能力由总开关承担。

    // ---------- 托盘 ----------
    /// <summary>隐藏到托盘。</summary>
    public void HideToTray()
    {
        Hide();
        WindowState = WindowState.Minimized;
        ShowInTaskbar = false;
    }

    /// <summary>从托盘恢复显示。</summary>
    public void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        ShowInTaskbar = true;
        Activate();
    }

    /// <summary>关闭 → 隐藏到托盘（不退出）；托盘菜单退出才真正退出。</summary>
    private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_app.IsExiting)
        {
            e.Cancel = true;
            _app.RequestExitFromClose(this);
        }
    }
}
