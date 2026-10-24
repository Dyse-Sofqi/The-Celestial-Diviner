using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using TheCelestialDiviner.Helpers;
using TheCelestialDiviner.Models;

namespace TheCelestialDiviner.Services;

/// <summary>
/// 键帽 Web 叠加源（OBS「浏览器源」）：本机 127.0.0.1 的 HTTP + WebSocket 服务。
/// 键帽状态以 JSON 推给内嵌网页（<see cref="WebOverlayPage.Html"/>），OBS 浏览器源（CEF）
/// 原生支持透明背景渲染——键帽在录制/直播画面里透明浮动，无需窗口捕获，屏幕观感完全不变。
/// 仅绑定回环地址；URL 携带令牌防本机其它进程 / 网页误连（浏览器源 URL 一次粘贴、随配置持久）。
/// 通知方法可在任意线程调用（钩子 / 调度器 / 悬浮窗线程），内部加锁；广播为 fire-and-forget，
/// 单连接发送经每连接信号量串行化（WebSocket 不允许并发 SendAsync）。
/// </summary>
public sealed class WebOverlayService
{
    /// <summary>默认监听端口（127.0.0.1；配置可改）。</summary>
    public const int DefaultPort = 43117;

    private readonly object _gate = new();
    private readonly List<Client> _clients = new();
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;

    // 状态镜像（WS 连接建立时 hello 重放，客户端无需等待下一次变更）
    private IReadOnlyList<string> _lastKeys = Array.Empty<string>();
    private string _face = "#6d28d9";
    private string _base = "#4c1d95";
    private string _text = "#fbbf24";
    private string _border = "#4c1d95";
    private double _opacity = 1;
    private bool _reminder;
    private string _iconUri = "pack://application:,,,/Resources/app.ico";
    private int _iconRev;
    private byte[]? _iconPng;       // 已渲染 PNG（按 rev 缓存）
    private int _iconPngRev = -1;

    /// <summary>单客户端：WebSocket + 发送串行锁。</summary>
    private sealed class Client
    {
        public Client(WebSocket socket) => Socket = socket;
        public readonly WebSocket Socket;
        public readonly SemaphoreSlim SendLock = new(1, 1);
    }

    /// <summary>服务是否在运行。</summary>
    public bool IsRunning { get; private set; }

    /// <summary>当前在线客户端数（ClientCountChanged 事件外读取用快照语义）。</summary>
    public int ClientCount
    {
        get { lock (_gate) return _clients.Count; }
    }

    /// <summary>客户端连接 / 断开后触发（ThreadPool 线程；订阅方自行封送）。
    /// 桌面悬浮层静音联动（KeyVisualizerService.SetDesktopMuted）的数据源。</summary>
    public event Action? ClientCountChanged;

    /// <summary>构造源 URL（OBS 浏览器源粘贴用；端口按配置钳位，与 Start 一致）。</summary>
    public static string BuildUrl(int port, string token) =>
        $"http://127.0.0.1:{Compat.Clamp(port, 1024, 65535)}/overlay?token={Uri.EscapeDataString(token)}";

    /// <summary>
    /// 启动监听（重复调用安全）。返回 null = 成功；否则错误描述（如端口被占用）。
    /// </summary>
    public string? Start(int port, string token)
    {
        lock (_gate)
        {
            if (IsRunning) return null;
            port = Compat.Clamp(port, 1024, 65535);
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            // 同时绑 localhost：部分解析器把 localhost 命中 ::1（IPv6 回环），只绑 127.0.0.1 会连不上。
            listener.Prefixes.Add($"http://localhost:{port}/");
            try
            {
                listener.Start();
            }
            catch (Exception ex)
            {
                return $"端口 {port} 监听失败（可能被其它程序占用）：{ex.Message}";
            }
            _listener = listener;
            _token = token;
            _cts = new CancellationTokenSource();
            IsRunning = true;
            _ = Task.Run(() => AcceptLoopAsync(_cts!.Token));
            return null;
        }
    }

    private string _token = "";

    /// <summary>停止服务并断开全部客户端（未运行时空操作）。</summary>
    public void Stop()
    {
        List<Client> clients;
        lock (_gate)
        {
            if (!IsRunning) return;
            IsRunning = false;
            clients = new List<Client>(_clients);
            _clients.Clear();
            try { _cts?.Cancel(); } catch { /* 已取消：忽略 */ }
            try { _listener?.Stop(); } catch { /* 已停止：忽略 */ }
            _listener = null;
        }
        foreach (var client in clients)
        {
            try
            {
                _ = client.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            }
            catch { /* 已断开：忽略 */ }
        }
        ClientCountChanged?.Invoke();
    }

    /// <summary>按住键位快照更新（keys = 修饰键在前按按下顺序；bumps = 本轮各键连击增量）。
    /// t = 发送时刻（Environment.TickCount）：页面按 hello 校准时钟后实测端到端延迟（标题显示）。</summary>
    public void SetSnapshot(IReadOnlyList<string> keys, IReadOnlyDictionary<string, int> bumps)
    {
        lock (_gate) _lastKeys = keys.ToList();
        Broadcast(JsonSerializer.Serialize(new { type = "snapshot", keys, bumps, t = Environment.TickCount }));
    }

    /// <summary>连发脉冲 / 方案切换键帽（label、按压时长、合并次数）。</summary>
    public void BroadcastPulse(string label, int holdMs, int repeat) =>
        Broadcast(JsonSerializer.Serialize(new { type = "pulse", label, holdMs, count = repeat, t = Environment.TickCount }));

    /// <summary>状态提醒键帽显隐。</summary>
    public void SetReminder(bool visible)
    {
        lock (_gate) _reminder = visible;
        Broadcast(JsonSerializer.Serialize(new { type = "reminder", visible }));
    }

    /// <summary>键帽配色（hex 值；档位外观 / 配色切换时调用）。</summary>
    public void SetScheme(KeycapScheme scheme)
    {
        lock (_gate)
        {
            _face = scheme.Face;
            _base = scheme.Base;
            _text = scheme.Text;
            _border = scheme.Border;
        }
        Broadcast(JsonSerializer.Serialize(new
        {
            type = "scheme",
            face = scheme.Face,
            @base = scheme.Base,
            text = scheme.Text,
            border = scheme.Border
        }));
    }

    /// <summary>键帽整体透明度（0~100 百分比）。</summary>
    public void SetOpacity(double percent)
    {
        lock (_gate) _opacity = Compat.Clamp(percent, 0, 100) / 100.0;
        BroadcastConfig();
    }

    /// <summary>应用图标（状态提醒键帽内容；档位外观切换时调用，内部去重 + 版本号递增）。</summary>
    public void SetAppIcon(string iconUri)
    {
        lock (_gate)
        {
            if (string.Equals(_iconUri, iconUri, StringComparison.Ordinal)) return;
            _iconUri = iconUri;
            _iconRev++;
            _iconPng = null;   // 下次请求按新 rev 重渲染
        }
        BroadcastConfig();
    }

    /// <summary>config 消息（透明度 + 图标版本号；客户端按版本号刷新提醒键帽图标缓存）。</summary>
    private void BroadcastConfig()
    {
        double opacity;
        int iconRev;
        lock (_gate)
        {
            opacity = _opacity;
            iconRev = _iconRev;
        }
        Broadcast(JsonSerializer.Serialize(new { type = "config", opacity, iconRev }));
    }

    /// <summary>WS 连接建立时的全量状态重放。</summary>
    private string HelloJson()
    {
        lock (_gate)
        {
            return JsonSerializer.Serialize(new
            {
                type = "hello",
                t = Environment.TickCount,   // 页面据此校准时钟偏移（后续消息实测端到端延迟）
                scheme = new { face = _face, @base = _base, text = _text, border = _border },
                opacity = _opacity,
                reminder = _reminder,
                iconRev = _iconRev,
                keys = _lastKeys,
            });
        }
    }

    // ---------- HTTP / WebSocket 基础 ----------

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener!.GetContextAsync();
            }
            catch
            {
                break;   // Stop / 句柄关闭：退出接受循环
            }
            _ = Task.Run(() => HandleAsync(ctx, ct));
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        try
        {
            var path = ctx.Request.Url?.AbsolutePath ?? "";
            if (ctx.Request.QueryString["token"] != _token)
            {
                ctx.Response.StatusCode = 404;   // 令牌不符：不区分 401/404，不向探测方泄露路径语义
                ctx.Response.Close();
                return;
            }
            switch (path)
            {
                case "/overlay":
                    await WriteBytesAsync(ctx, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(WebOverlayPage.Html), ct);
                    break;
                case "/icon.png":
                    var png = IconPng();
                    if (png is null) { ctx.Response.StatusCode = 404; ctx.Response.Close(); break; }
                    await WriteBytesAsync(ctx, "image/png", png, ct);
                    break;
                case "/ws":
                    await HandleWebSocketAsync(ctx);
                    break;
                default:
                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                    break;
            }
        }
        catch
        {
            try { ctx.Response.Close(); } catch { /* 已关闭：忽略 */ }
        }
    }

    private static async Task WriteBytesAsync(HttpListenerContext ctx, string contentType, byte[] bytes, CancellationToken ct)
    {
        ctx.Response.ContentType = contentType;
        // 页面 / 图标禁缓存：升级修复后 OBS 浏览器源刷新缓存即可拿到新版页面，
        // 否则 CEF 可能一直用内存里的旧页（旧 JS 不带图标令牌 → 图标永远文字降级）。
        ctx.Response.Headers[HttpResponseHeader.CacheControl] = "no-store";
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length, ct);
        ctx.Response.Close();
    }

    private async Task HandleWebSocketAsync(HttpListenerContext ctx)
    {
        WebSocketContext wsCtx;
        try
        {
            wsCtx = await ctx.AcceptWebSocketAsync(null);
        }
        catch
        {
            try { ctx.Response.Close(); } catch { /* 已关闭：忽略 */ }
            return;
        }
        var client = new Client(wsCtx.WebSocket);
        lock (_gate) _clients.Add(client);
        Logger.Info($"Web 叠加源客户端已连接（当前 {CountClients()} 个）。");
        ClientCountChanged?.Invoke();
        await SendAsync(client, HelloJson());

        var buffer = new byte[1024];
        try
        {
            while (client.Socket.State == WebSocketState.Open)
            {
                var result = await client.Socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await client.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                    break;
                }
                // 客户端消息一律忽略（单向广播协议）：收到即按保持连接处理。
            }
        }
        catch { /* 断开：由 finally 统一清理 */ }
        finally
        {
            lock (_gate) _clients.Remove(client);
            client.Socket.Dispose();
            Logger.Info($"Web 叠加源客户端已断开（当前 {CountClients()} 个）。");
            ClientCountChanged?.Invoke();
        }
    }

    private int CountClients()
    {
        lock (_gate) return _clients.Count;
    }

    /// <summary>向所有在线客户端广播（fire-and-forget；失败连接由接收循环清理）。</summary>
    private void Broadcast(string json)
    {
        List<Client> targets;
        lock (_gate) targets = _clients.ToList();
        if (targets.Count == 0) return;
        foreach (var client in targets)
            _ = Task.Run(() => SendAsync(client, json));
    }

    private static async Task SendAsync(Client client, string json)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            await client.SendLock.WaitAsync();
            try
            {
                if (client.Socket.State != WebSocketState.Open) return;
                var sw = System.Diagnostics.Stopwatch.StartNew();   // 发送耗时探针：定位延迟在服务端发送还是接收端
                await client.Socket.SendAsync(new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text, true, CancellationToken.None);
                sw.Stop();
                if (sw.ElapsedMilliseconds > 100 &&
                    unchecked(Environment.TickCount - _lastSlowSendLogTick) > 2000)
                {
                    _lastSlowSendLogTick = Environment.TickCount;
                    Logger.Warn($"Web 叠加源发送耗时 {sw.ElapsedMilliseconds}ms（接收端读取慢或系统负载高）。");
                }
            }
            finally
            {
                client.SendLock.Release();
            }
        }
        catch
        {
            // 发送失败（对端断开 / 关闭中）：连接由接收循环清理，此处吞掉保广播线程安全。
        }
    }

    private static int _lastSlowSendLogTick;   // 发送耗时告警限频

    /// <summary>应用图标 → PNG 字节（按图标版本缓存；失败返回 null，客户端退化显示文字键帽）。</summary>
    private byte[]? IconPng()
    {
        string iconUri;
        int rev;
        lock (_gate)
        {
            if (_iconPng is not null && _iconPngRev == _iconRev) return _iconPng;
            iconUri = _iconUri;
            rev = _iconRev;
        }
        byte[]? png = null;
        try
        {
            var decoder = System.Windows.Media.Imaging.IconBitmapDecoder.Create(
                new Uri(iconUri),
                System.Windows.Media.Imaging.BitmapCreateOptions.None,
                System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(frame));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            png = ms.ToArray();
        }
        catch (Exception ex)
        {
            Logger.Warn($"Web 叠加源应用图标渲染失败（{iconUri}）：{ex.Message}");
        }
        lock (_gate)
        {
            _iconPng = png;
            _iconPngRev = rev;
        }
        return png;
    }
}
