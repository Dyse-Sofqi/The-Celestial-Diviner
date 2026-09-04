using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;
using TheCelestialDiviner.Helpers;
using TheCelestialDiviner.Models;
using TheCelestialDiviner.Views;

namespace TheCelestialDiviner.Services;

/// <summary>
/// 键位可视化服务：持有可可视化键位注册表（含每键开关），
/// 订阅全局钩子的按下 / 释放事件，把命中注册表的按键封送到 UI 线程驱动悬浮层。
/// 注册表由主视图模型随方案变更同步（默认全量注册 + 默认开启）。
/// </summary>
public sealed class KeyVisualizerService
{
    /// <summary>可视化键位状态：是否注册 + 是否显示（用户可关闭单个键位的可视化）。</summary>
    public readonly struct VisualKeyState
    {
        public VisualKeyState(bool visible) => Visible = visible;
        public bool Visible { get; }
    }

    private readonly object _gate = new();
    private readonly Dictionary<InputSource, VisualKeyState> _registry = new();
    private bool _globalEnabled = true;   // 可视化总闸门（不影响各键开关状态的记忆）

    private readonly InputHookService _hooks;
    private KeycapOverlayWindow? _overlay;
    private Dispatcher? _dispatcher;

    public KeyVisualizerService(InputHookService hooks)
    {
        _hooks = hooks;
        hooks.SourceDown += OnSourceDown;
        hooks.SourceUp += OnSourceUp;
    }

    /// <summary>初始化悬浮层（UI 线程调用；重复调用安全）。</summary>
    public void AttachOverlay()
    {
        if (_overlay is not null) return;
        _dispatcher = Application.Current?.Dispatcher;
        _overlay = new KeycapOverlayWindow();
    }

    /// <summary>替换注册表（方案变更 / 配置导入后由 VM 全量重建）。未列出的键位视为未注册（不显示）。</summary>
    public void ReplaceRegistry(IEnumerable<KeyValuePair<string, bool>> entries)
    {
        lock (_gate)
        {
            _registry.Clear();
            foreach (var entry in entries)
            {
                var source = TaskSchedulerService.ParseSourceKey(entry.Key);
                if (source is null) continue;
                _registry[source] = new VisualKeyState(entry.Value);
            }
        }
    }

    /// <summary>更新单个键位的显示开关（方案行 eye 图标切换）。</summary>
    public void SetVisible(string key, bool visible)
    {
        var source = TaskSchedulerService.ParseSourceKey(key);
        if (source is null) return;
        lock (_gate)
        {
            // 未注册的键位忽略（eye 只对已注册方案的行有意义）。
            if (!_registry.ContainsKey(source)) return;
            _registry[source] = new VisualKeyState(visible);
        }
    }

    /// <summary>
    /// 全局可视化闸门（总开关）：关闭时所有键位统一暂停显示（各键开关状态保留，恢复后延续）；
    /// 配置加载后调用一次以还原持久化状态。
    /// </summary>
    public void SetGlobalEnabled(bool enabled)
    {
        lock (_gate)
        {
            _globalEnabled = enabled;
        }
    }

    private void OnSourceDown(InputSource source)
    {
        lock (_gate)
        {
            if (_globalEnabled) {
                if (!_registry.TryGetValue(source, out var state) || !state.Visible) return;
            }
            else
            {
                return; // 全局禁用：所有键位暂停显示。
            }
        }
        var labels = BuildComboLabels(source);
        var overlay = _overlay;
        var dispatcher = _dispatcher;
        dispatcher?.BeginInvoke(() => overlay?.ShowKeys(labels, pressed: true));

        // 滚轮无释放事件（钩子只产生 down）：安排延时虚拟释放，否则键帽会永久停留。
        if (source.Kind == InputKind.Mouse && source.Mouse is MouseInput.WheelUp or MouseInput.WheelDown)
        {
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(250);
                dispatcher?.BeginInvoke(() => overlay?.ShowKeys(labels, pressed: false));
            });
        }
    }

    private void OnSourceUp(InputSource source)
    {
        lock (_gate)
        {
            if (_globalEnabled) {
                if (!_registry.TryGetValue(source, out var state) || !state.Visible) return;
            }
            else
            {
                return;
            }
        }
        var labels = BuildComboLabels(source);
        _dispatcher?.BeginInvoke(() => _overlay?.ShowKeys(labels, pressed: false));
    }

    /// <summary>组合键标签列表（可视化用）：按住中的修饰键 + 该键，各自独立键帽（如 ["Ctrl","F6"]）。</summary>
    private static List<string> BuildComboLabels(InputSource source)
    {
        var parts = new List<string>();
        if ((NativeMethods.GetAsyncKeyState(0xA2) & 0x8000) != 0) parts.Add("Ctrl");
        if ((NativeMethods.GetAsyncKeyState(0xA0) & 0x8000) != 0) parts.Add("Shift");
        if ((NativeMethods.GetAsyncKeyState(0xA4) & 0x8000) != 0) parts.Add("Alt");
        parts.Add(InputNameMapper.GetSourceName(source));
        return parts;
    }
}
