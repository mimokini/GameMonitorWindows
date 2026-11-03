using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace GameMonitor
{
    [DataContract]
    public class RemoteGameItem
    {
        [DataMember] public string DisplayName { get; set; }
        [DataMember] public string ProcessName { get; set; }
        [DataMember] public int RunMinutes { get; set; }
        [DataMember] public int DailyLimitMinutes { get; set; }
        [DataMember] public int UsedMinutesToday { get; set; }
        [DataMember] public bool IsRunning { get; set; }
        [DataMember] public bool Enabled { get; set; }
        [DataMember] public string MatchMode { get; set; }
    }

    [DataContract]
    public class RemoteConfigDto
    {
        [DataMember] public int ReminderMinutes { get; set; }
        [DataMember] public int CountdownSeconds { get; set; }
        [DataMember] public bool HasPassword { get; set; }
        [DataMember] public List<RemoteGameItem> Games { get; set; }
    }

    [DataContract]
    public class RemoteReq
    {
        [DataMember] public string ProcessName { get; set; }
        [DataMember] public string NewPass { get; set; }
    }

    [DataContract]
    public class RemoteAuthInfo
    {
        [DataMember] public bool Required { get; set; }
    }

    public class RemoteServer
    {
        private const int MaxHeaderBytes = 4096;         // 请求头最大 4KB
        private const int MaxBodyBytes = 64 * 1024;      // 请求体最大 64KB
        private const int ClientTimeoutSeconds = 5;      // 收发超时
        private const int RequestTimeoutSeconds = 10;    // 单请求总超时
        private const int MaxConcurrentClients = 8;      // 并发连接上限

        private readonly AppConfig _config;
        private readonly Control _uiControl;
        private readonly Action _onConfigUpdated;
        private readonly Func<string, bool> _onKillTarget;
        private readonly Action<string> _onForceCountdown;
        private readonly Action _onPasswordCleared;

        private readonly SemaphoreSlim _slots = new SemaphoreSlim(MaxConcurrentClients);

        private TcpListener _listener;
        private CancellationTokenSource _cts;
        private int _currentPort = -1;
        private bool _currentAllowLan = false;

        public RemoteServer(AppConfig config, Control uiControl, Action onConfigUpdated,
            Func<string, bool> onKillTarget, Action<string> onForceCountdown, Action onPasswordCleared)
        {
            _config = config;
            _uiControl = uiControl;
            _onConfigUpdated = onConfigUpdated;
            _onKillTarget = onKillTarget;
            _onForceCountdown = onForceCountdown;
            _onPasswordCleared = onPasswordCleared;
        }

        public bool IsRunning => _listener != null;

        /// <summary>启动监听；失败时返回 false 并通过 error 回传原因，供 UI 明确提示。</summary>
        public bool Start(int port, bool allowLan, out string error)
        {
            error = null;

            if (string.IsNullOrEmpty(_config.AdminPassword))
            {
                error = "未设置管理密码，远程控制台不会启动。请先在设置中设置管理密码。";
                return false;
            }

            if (_listener != null && _currentPort == port && _currentAllowLan == allowLan)
                return true;

            Stop();

            TcpListener listener = null;
            try
            {
                _cts = new CancellationTokenSource();
                IPAddress bindIp = allowLan ? IPAddress.Any : IPAddress.Loopback;
                listener = new TcpListener(bindIp, port);
                listener.Start();

                _listener = listener;
                _currentPort = port;
                _currentAllowLan = allowLan;

                var token = _cts.Token;
                Task.Run(() => AcceptLoopAsync(listener, token));
                AppLog.Info($"远程控制台已启动：{bindIp}:{port}");
                return true;
            }
            catch (Exception ex)
            {
                try { listener?.Stop(); } catch { }
                try { _cts?.Cancel(); } catch { }
                _listener = null;
                _currentPort = -1;
                error = $"远程控制台启动失败（端口 {port}）：{ex.Message}";
                AppLog.Error(error, ex);
                return false;
            }
        }

        public void Stop()
        {
            try
            {
                _cts?.Cancel();
                _listener?.Stop();
                _listener = null;
                _currentPort = -1;
                _currentAllowLan = false;
            }
            catch (Exception ex)
            {
                AppLog.Warn("停止远程控制台失败", ex);
            }
        }

        private async Task AcceptLoopAsync(TcpListener listener, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync();
                }
                catch (Exception ex)
                {
                    if (token.IsCancellationRequested) break;
                    AppLog.Warn("接受远程连接失败", ex);
                    await Task.Delay(200);
                    continue;
                }

                // 并发超限：直接关闭连接，避免线程被占满
                if (!_slots.Wait(0))
                {
                    try { client.Close(); } catch { }
                    AppLog.Warn($"远程连接数超过 {MaxConcurrentClients}，已丢弃新连接。");
                    continue;
                }

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await HandleClientAsync(client, token);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("处理远程请求失败", ex);
                    }
                    finally
                    {
                        _slots.Release();
                    }
                });
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken token)
        {
            using (client)
            {
                client.ReceiveTimeout = ClientTimeoutSeconds * 1000;
                client.SendTimeout = ClientTimeoutSeconds * 1000;

                using (var stream = client.GetStream())
                using (var requestCts = new CancellationTokenSource(TimeSpan.FromSeconds(RequestTimeoutSeconds)))
                {
                    try
                    {
                        var headerBytes = new List<byte>();
                        int last4 = 0;
                        while (true)
                        {
                            int b = stream.ReadByte();
                            if (b == -1) return;
                            headerBytes.Add((byte)b);

                            // 请求头上限，防止逐字节无限增长导致内存耗尽
                            if (headerBytes.Count > MaxHeaderBytes)
                            {
                                await WriteResponseAsync(stream, 431, "text/plain; charset=utf-8", "请求头过大");
                                return;
                            }

                            last4 = (last4 << 8) | b;
                            if (last4 == 0x0D0A0D0A) break;
                        }

                        if (headerBytes.Count == 0) return;

                        string headerText = Encoding.UTF8.GetString(headerBytes.ToArray());
                        string[] headerLines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                        if (headerLines.Length == 0) return;

                        string[] parts = headerLines[0].Split(' ');
                        if (parts.Length < 3) return;

                        string method = parts[0];
                        string path = parts[1].Split('?')[0];

                        string clientPass = "";
                        foreach (var line in headerLines)
                        {
                            if (line.StartsWith("X-Admin-Password:", StringComparison.OrdinalIgnoreCase))
                            {
                                clientPass = line.Substring(17).Trim();
                                break;
                            }
                        }

                        int contentLength = 0;
                        bool hasContentLength = false;
                        bool contentLengthValid = true;
                        foreach (var line in headerLines)
                        {
                            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                            {
                                hasContentLength = true;
                                contentLengthValid = int.TryParse(line.Substring(15).Trim(), out contentLength);
                                break;
                            }
                        }

                        if (hasContentLength && !contentLengthValid)
                        {
                            await WriteResponseAsync(stream, 400, "text/plain; charset=utf-8", "Content-Length 格式不正确");
                            return;
                        }

                        if (hasContentLength && (contentLength < 0 || contentLength > MaxBodyBytes))
                        {
                            await WriteResponseAsync(stream, 413, "text/plain; charset=utf-8", "请求体过大");
                            return;
                        }

                        string body = "";
                        if (contentLength > 0)
                        {
                            byte[] bodyBytes = new byte[contentLength];
                            int read = 0;
                            while (read < contentLength)
                            {
                                int r = await stream.ReadAsync(bodyBytes, read, contentLength - read, requestCts.Token);
                                if (r == 0) break;
                                read += r;
                            }
                            body = Encoding.UTF8.GetString(bodyBytes, 0, read);
                        }

                        bool authed = IsPasswordValid(clientPass);

                        string responseBody = "";
                        string contentType = "text/plain; charset=utf-8";
                        int statusCode = 200;

                        if (method == "GET" && path == "/")
                        {
                            responseBody = GetHtmlPage();
                            contentType = "text/html; charset=utf-8";
                        }
                        else if (method == "GET" && path == "/api/auth")
                        {
                            // 登录所需的最低限度信息，不泄露任何业务数据
                            responseBody = SerializeJson(new RemoteAuthInfo
                            {
                                Required = !string.IsNullOrEmpty(_config.AdminPassword)
                            });
                            contentType = "application/json; charset=utf-8";
                        }
                        else if (method == "GET" && path == "/api/status")
                        {
                            if (!authed)
                            {
                                statusCode = 401;
                                responseBody = "{\"success\":false,\"error\":\"密码错误，请重新登录\"}";
                            }
                            else
                            {
                                responseBody = SerializeJson(BuildStatusDto());
                                contentType = "application/json; charset=utf-8";
                            }
                        }
                        else if (method == "POST")
                        {
                            if (!authed)
                            {
                                statusCode = 401;
                                responseBody = "{\"success\":false,\"error\":\"密码错误，请重新输入\"}";
                            }
                            else
                            {
                                switch (path)
                                {
                                    case "/api/save":
                                        {
                                            var newCfg = TryDeserialize<RemoteConfigDto>(body);
                                            var result = RunOnUi(() => ApplyRemoteConfig(newCfg));
                                            statusCode = result.Status;
                                            responseBody = result.Body;
                                            break;
                                        }
                                    case "/api/reset_usage":
                                        {
                                            var req = TryDeserialize<RemoteReq>(body);
                                            if (req == null || string.IsNullOrWhiteSpace(req.ProcessName))
                                            {
                                                statusCode = 400;
                                                responseBody = "{\"success\":false,\"error\":\"缺少 ProcessName\"}";
                                            }
                                            else
                                            {
                                                var result = RunOnUi(() => ResetUsage(req.ProcessName));
                                                statusCode = result.Status;
                                                responseBody = result.Body;
                                            }
                                            break;
                                        }
                                    case "/api/kill":
                                        {
                                            var req = TryDeserialize<RemoteReq>(body);
                                            if (req == null || string.IsNullOrWhiteSpace(req.ProcessName))
                                            {
                                                statusCode = 400;
                                                responseBody = "{\"success\":false,\"error\":\"缺少 ProcessName\"}";
                                            }
                                            else
                                            {
                                                // 只对 GameMonitor 已匹配到的本轮快照执行结束，不再按关键字重新扫描
                                                bool killed = RunOnUi(() => _onKillTarget != null && _onKillTarget(req.ProcessName));
                                                responseBody = killed
                                                    ? "{\"success\":true}"
                                                    : "{\"success\":false,\"error\":\"未找到运行中的匹配进程\"}";
                                            }
                                            break;
                                        }
                                    case "/api/force_countdown":
                                        {
                                            var req = TryDeserialize<RemoteReq>(body);
                                            if (req == null || string.IsNullOrWhiteSpace(req.ProcessName))
                                            {
                                                statusCode = 400;
                                                responseBody = "{\"success\":false,\"error\":\"缺少 ProcessName\"}";
                                            }
                                            else
                                            {
                                                RunOnUi(() =>
                                                {
                                                    _onForceCountdown?.Invoke(req.ProcessName);
                                                    return true;
                                                });
                                                responseBody = "{\"success\":true}";
                                            }
                                            break;
                                        }
                                    case "/api/change_password":
                                        {
                                            var req = TryDeserialize<RemoteReq>(body);
                                            if (req == null)
                                            {
                                                statusCode = 400;
                                                responseBody = "{\"success\":false,\"error\":\"请求体格式不正确\"}";
                                            }
                                            else if (string.IsNullOrWhiteSpace(req.NewPass))
                                            {
                                                statusCode = 400;
                                                responseBody = "{\"success\":false,\"error\":\"新密码不能为空，若要取消密码请使用清除按钮\"}";
                                            }
                                            else
                                            {
                                                RunOnUi(() =>
                                                {
                                                    _config.AdminPassword = req.NewPass;
                                                    _config.SaveQuiet();
                                                    return true;
                                                });
                                                responseBody = "{\"success\":true}";
                                            }
                                            break;
                                        }
                                    case "/api/clear_password":
                                        {
                                            // 清除密码后远程服务必须立即关闭，避免出现无密码的匿名访问
                                            RunOnUi(() =>
                                            {
                                                _config.AdminPassword = "";
                                                _config.EnableRemote = false;
                                                _config.SaveQuiet();
                                                return true;
                                            });
                                            responseBody = "{\"success\":true,\"message\":\"密码已清除，远程控制台已关闭\"}";
                                            break;
                                        }
                                    default:
                                        statusCode = 404;
                                        responseBody = "{\"success\":false,\"error\":\"Not Found\"}";
                                        break;
                                }
                                contentType = "application/json; charset=utf-8";
                            }
                        }
                        else
                        {
                            statusCode = 404;
                            responseBody = "Not Found";
                        }

                        await WriteResponseAsync(stream, statusCode, contentType, responseBody);

                        if (method == "POST" && path == "/api/clear_password" && authed)
                            _onPasswordCleared?.Invoke();
                    }
                    catch (OperationCanceledException)
                    {
                        AppLog.Warn("远程请求超时，已关闭连接。");
                    }
                    catch (IOException)
                    {
                        // 客户端提前断开，属于正常情况
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("远程请求处理异常", ex);
                    }
                }
            }
        }

        private T RunOnUi<T>(Func<T> func)
        {
            if (_uiControl == null || !_uiControl.IsHandleCreated)
                return func();

            try
            {
                return (T)_uiControl.Invoke(func);
            }
            catch (Exception ex)
            {
                AppLog.Error("封送到 UI 线程失败", ex);
                throw;
            }
        }

        private bool IsPasswordValid(string supplied)
        {
            string expected = _config.AdminPassword ?? "";
            if (expected.Length == 0) return false;

            byte[] a = Encoding.UTF8.GetBytes(expected);
            byte[] b = Encoding.UTF8.GetBytes(supplied ?? "");
            try
            {
                return CryptographicOperations.FixedTimeEquals(a, b);
            }
            catch
            {
                return false;
            }
        }

        // ================= 配置读写（全部在 UI 线程执行） =================

        private RemoteConfigDto BuildStatusDto()
        {
            // 配置/用量快照必须在 UI 线程读取，进程枚举放到后台线程，避免阻塞 UI
            var dto = RunOnUi(BuildConfigSnapshot);
            var runningNames = GetRunningProcessNames();
            foreach (var item in dto.Games)
                item.IsRunning = IsRunningNow(item.ProcessName, item.MatchMode, runningNames);
            return dto;
        }

        private RemoteConfigDto BuildConfigSnapshot()
        {
            lock (_config.SyncRoot)
            {
                DateTime today = DateTime.Today;
                var list = new List<RemoteGameItem>();

                foreach (var g in _config.Games)
                {
                    var usage = _config.Usage.FirstOrDefault(x =>
                        x.ProcessName.Equals(g.ProcessName, StringComparison.OrdinalIgnoreCase));
                    int usedMinutes = (usage != null && usage.Date.Date == today) ? usage.UsedSeconds / 60 : 0;

                    list.Add(new RemoteGameItem
                    {
                        DisplayName = g.DisplayName,
                        ProcessName = g.ProcessName,
                        RunMinutes = g.RunMinutes,
                        DailyLimitMinutes = g.DailyLimitMinutes,
                        UsedMinutesToday = usedMinutes,
                        Enabled = g.Enabled,
                        MatchMode = g.MatchMode.ToString()
                    });
                }

                return new RemoteConfigDto
                {
                    ReminderMinutes = _config.ReminderMinutes,
                    CountdownSeconds = _config.CountdownSeconds,
                    HasPassword = !string.IsNullOrEmpty(_config.AdminPassword),
                    Games = list
                };
            }
        }

        /// <summary>枚举进程名并在使用后立即释放，避免句柄累积。</summary>
        private static List<string> GetRunningProcessNames()
        {
            var names = new List<string>();
            var procs = Process.GetProcesses();
            try
            {
                foreach (var p in procs)
                {
                    try { names.Add(p.ProcessName ?? ""); } catch { }
                }
            }
            finally
            {
                foreach (var p in procs)
                {
                    try { p.Dispose(); } catch { }
                }
            }
            return names;
        }

        private static bool IsRunningNow(string keyword, string matchMode, List<string> runningNames)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return false;

            MatchMode mode = MatchMode.Contains;
            if (!string.IsNullOrEmpty(matchMode))
            {
                try { mode = (MatchMode)Enum.Parse(typeof(MatchMode), matchMode, true); }
                catch { mode = MatchMode.Contains; }
            }

            if (mode == MatchMode.ExactProcess)
                return runningNames.Any(x => x.Equals(keyword, StringComparison.OrdinalIgnoreCase));

            if (mode == MatchMode.ExactWindowTitle)
                return false; // 仅按进程名快照判断，无法可靠拿到标题

            return runningNames.Any(x => x.ToLowerInvariant().Contains(keyword.ToLowerInvariant()));
        }

        private RemoteOpResult ApplyRemoteConfig(RemoteConfigDto newCfg)
        {
            if (newCfg == null)
                return new RemoteOpResult(400, "{\"success\":false,\"error\":\"请求体格式不正确\"}");

            if (newCfg.ReminderMinutes < 1 || newCfg.ReminderMinutes > 120)
                return new RemoteOpResult(400, "{\"success\":false,\"error\":\"提醒宽限必须在 1~120 分钟\"}");
            if (newCfg.CountdownSeconds < 5 || newCfg.CountdownSeconds > 600)
                return new RemoteOpResult(400, "{\"success\":false,\"error\":\"倒计时必须在 5~600 秒\"}");

            var games = newCfg.Games ?? new List<RemoteGameItem>();

            // 先整体校验，任何一条不合法都不落盘，避免出现半套配置
            var normalized = new List<GameItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var g in games)
            {
                if (g == null) continue;

                string display = (g.DisplayName ?? "").Trim();
                string keyword = (g.ProcessName ?? "").Trim();

                if (display.Length == 0)
                    return new RemoteOpResult(400, "{\"success\":false,\"error\":\"显示名称不能为空\"}");
                if (keyword.Length < GameMonitor.MinKeywordLength)
                    return new RemoteOpResult(400,
                        $"{{\"success\":false,\"error\":\"匹配关键字至少需要 {GameMonitor.MinKeywordLength} 个字符\"}}");
                if (g.RunMinutes < 1 || g.RunMinutes > 1440)
                    return new RemoteOpResult(400, "{\"success\":false,\"error\":\"单次时长必须在 1~1440 分钟\"}");
                if (g.DailyLimitMinutes < 0 || g.DailyLimitMinutes > 1440)
                    return new RemoteOpResult(400, "{\"success\":false,\"error\":\"每日配额必须在 0~1440 分钟\"}");
                if (!seen.Add(keyword))
                    return new RemoteOpResult(400, "{\"success\":false,\"error\":\"匹配关键字不能重复\"}");

                MatchMode mode = MatchMode.Contains;
                if (!string.IsNullOrEmpty(g.MatchMode))
                {
                    try { mode = (MatchMode)Enum.Parse(typeof(MatchMode), g.MatchMode, true); }
                    catch { mode = MatchMode.Contains; }
                }

                normalized.Add(new GameItem
                {
                    DisplayName = display,
                    ProcessName = keyword.ToLowerInvariant(),
                    RunMinutes = g.RunMinutes,
                    DailyLimitMinutes = g.DailyLimitMinutes,
                    MatchMode = mode,
                    Enabled = true // 新游戏默认启用，已有游戏在下面按关键字保留原 Enabled
                });
            }

            lock (_config.SyncRoot)
            {
                // 保留原有 Enabled / MatchMode（网页未显式发送时）
                foreach (var g in normalized)
                {
                    var old = _config.Games.FirstOrDefault(x =>
                        x.ProcessName.Equals(g.ProcessName, StringComparison.OrdinalIgnoreCase));
                    if (old != null)
                    {
                        g.Enabled = old.Enabled;
                        if (string.IsNullOrEmpty(GetIncomingMatchMode(newCfg, g.ProcessName)))
                            g.MatchMode = old.MatchMode;
                    }
                }

                _config.ReminderMinutes = newCfg.ReminderMinutes;
                _config.CountdownSeconds = newCfg.CountdownSeconds;

                _config.Games.Clear();
                _config.Games.AddRange(normalized);

                _config.Usage.RemoveAll(u =>
                    u.ProcessName == null ||
                    !_config.Games.Exists(g =>
                        g.ProcessName != null &&
                        g.ProcessName.Equals(u.ProcessName, StringComparison.OrdinalIgnoreCase)));

                _config.SaveQuiet();
            }

            _onConfigUpdated?.Invoke();
            return new RemoteOpResult(200, "{\"success\":true}");
        }

        private static string GetIncomingMatchMode(RemoteConfigDto cfg, string keyword)
        {
            var item = cfg.Games?.FirstOrDefault(g =>
                g != null && g.ProcessName != null &&
                g.ProcessName.Trim().Equals(keyword, StringComparison.OrdinalIgnoreCase));
            return item?.MatchMode;
        }

        private RemoteOpResult ResetUsage(string processName)
        {
            if (processName == null || processName.Trim().Length == 0)
                return new RemoteOpResult(400, "{\"success\":false,\"error\":\"缺少 ProcessName\"}");

            lock (_config.SyncRoot)
            {
                var u = _config.Usage.FirstOrDefault(x =>
                    x.ProcessName != null &&
                    x.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase));
                if (u != null)
                {
                    u.UsedSeconds = 0;
                    u.Date = DateTime.Today;
                }
                else
                {
                    _config.Usage.Add(new GameUsage
                    {
                        ProcessName = processName,
                        Date = DateTime.Today,
                        UsedSeconds = 0
                    });
                }
                _config.SaveQuiet();
            }

            _onConfigUpdated?.Invoke();
            return new RemoteOpResult(200, "{\"success\":true}");
        }

        private class RemoteOpResult
        {
            public int Status { get; }
            public string Body { get; }

            public RemoteOpResult(int status, string body)
            {
                Status = status;
                Body = body;
            }
        }

        // ================= HTTP 输出 =================

        private static string ReasonPhrase(int code)
        {
            switch (code)
            {
                case 200: return "OK";
                case 400: return "Bad Request";
                case 401: return "Unauthorized";
                case 403: return "Forbidden";
                case 404: return "Not Found";
                case 405: return "Method Not Allowed";
                case 413: return "Payload Too Large";
                case 431: return "Request Header Fields Too Large";
                case 500: return "Internal Server Error";
                case 503: return "Service Unavailable";
                default: return "Unknown";
            }
        }

        private static async Task WriteResponseAsync(Stream stream, int statusCode, string contentType, string responseBody)
        {
            byte[] bodyBytesOut = Encoding.UTF8.GetBytes(responseBody ?? "");
            string header = $"HTTP/1.1 {statusCode} {ReasonPhrase(statusCode)}\r\n" +
                            $"Content-Type: {contentType}\r\n" +
                            $"Content-Length: {bodyBytesOut.Length}\r\n" +
                            "Cache-Control: no-store\r\n" +
                            "X-Content-Type-Options: nosniff\r\n" +
                            "Connection: close\r\n\r\n";
            byte[] headerBytesOut = Encoding.UTF8.GetBytes(header);

            await stream.WriteAsync(headerBytesOut, 0, headerBytesOut.Length);
            if (bodyBytesOut.Length > 0)
                await stream.WriteAsync(bodyBytesOut, 0, bodyBytesOut.Length);
        }

        private string GetHtmlPage()
        {
            return @"
<!DOCTYPE html>
<html>
<head>
<meta charset='utf-8'>
<meta name='viewport' content='width=device-width, initial-scale=1'>
<title>游戏监控 - 远程控制台</title>
<style>
:root { --primary: #6366f1; --primary-hover: #4f46e5; --bg: #f3f4f6; --card: #ffffff; --text: #1f2937; --text-muted: #6b7280; --border: #e5e7eb; }
body { font-family: 'Segoe UI', 'Microsoft YaHei', sans-serif; background: linear-gradient(135deg, #e0e7ff 0%, #f3f4f6 100%); margin: 0; padding: 30px 15px; color: var(--text); font-size: 14px; min-height: 100vh; }
.container { max-width: 1200px; margin: 0 auto; background: var(--card); padding: 30px; border-radius: 16px; box-shadow: 0 10px 25px -5px rgba(0, 0, 0, 0.1), 0 8px 10px -6px rgba(0, 0, 0, 0.1); }
h2 { text-align: center; font-size: 28px; font-weight: 800; background: linear-gradient(to right, #4f46e5, #9333ea); -webkit-background-clip: text; -webkit-text-fill-color: transparent; margin-top: 0; margin-bottom: 30px; }
h3 { font-size: 18px; font-weight: 600; color: var(--text); margin-top: 30px; margin-bottom: 15px; display: flex; align-items: center; }
h3::before { content: ''; display: inline-block; width: 4px; height: 18px; background: var(--primary); border-radius: 2px; margin-right: 10px; }
.form-group { margin-bottom: 15px; display: inline-block; margin-right: 20px; }
.form-group label { display: block; margin-bottom: 8px; font-weight: 500; color: var(--text-muted); font-size: 13px; }
.form-control { padding: 10px 14px; border: 1px solid var(--border); border-radius: 8px; font-size: 14px; transition: all 0.2s; background: #f9fafb; }
.form-control:focus { border-color: var(--primary); outline: none; box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.2); background: #fff; }
.table-wrapper { overflow-x: auto; margin-top: 15px; border-radius: 8px; border: 1px solid var(--border); }
table { width: 100%; border-collapse: collapse; text-align: left; white-space: nowrap; }
th, td { padding: 14px 15px; border-bottom: 1px solid var(--border); vertical-align: middle; }
th { background-color: #f9fafb; color: var(--text-muted); font-weight: 600; font-size: 13px; text-transform: uppercase; letter-spacing: 0.05em; }
tr:last-child td { border-bottom: none; }
tr:hover { background-color: #f9fafb; }
.table-input { width: 100%; border: 1px solid transparent; background: transparent; padding: 8px; border-radius: 4px; font-family: inherit; font-size: 14px; color: var(--text); transition: all 0.2s; }
.table-input:hover { border-color: var(--border); background: #fff; }
.table-input:focus { border-color: var(--primary); outline: none; background: #fff; box-shadow: 0 0 0 2px rgba(99, 102, 241, 0.2); }
.number-input { width: 70px; text-align: center; }
.status-tag { display: inline-flex; align-items: center; padding: 4px 10px; border-radius: 20px; font-size: 12px; font-weight: 600; }
.status-tag::before { content: ''; width: 6px; height: 6px; border-radius: 50%; margin-right: 6px; }
.status-running { background-color: #ecfdf5; color: #059669; border: 1px solid #a7f3d0; }
.status-running::before { background-color: #10b981; }
.status-stopped { background-color: #f3f4f6; color: #6b7280; border: 1px solid #e5e7eb; }
.status-stopped::before { background-color: #9ca3af; }
.btn { color: white; border: none; padding: 8px 14px; border-radius: 6px; cursor: pointer; font-size: 13px; font-weight: 500; transition: all 0.2s; display: inline-block; margin-right: 6px; box-shadow: 0 1px 2px rgba(0,0,0,0.05); }
.btn:last-child { margin-right: 0; }
.btn:hover { transform: translateY(-1px); box-shadow: 0 4px 6px rgba(0,0,0,0.1); }
.btn-primary { background: var(--primary); }
.btn-warning { background: #f59e0b; }
.btn-danger { background: #ef4444; }
.btn-dark { background: #4b5563; }
.btn-secondary { background: #e5e7eb; color: #374151; padding: 10px 16px; font-size: 14px; }
.btn-save { background: var(--primary); padding: 10px 20px; font-size: 14px; }
.alert { display: none; padding: 12px 16px; margin-bottom: 20px; border-radius: 8px; text-align: center; font-weight: 500; }
.alert-success { background: #ecfdf5; color: #065f46; border: 1px solid #a7f3d0; }
.alert-error { background: #fef2f2; color: #991b1b; border: 1px solid #fecaca; }
#loginMask { display: none; position: fixed; top:0; left:0; width:100%; height:100%; background:rgba(17,24,39,0.6); backdrop-filter: blur(4px); z-index:999; align-items:center; justify-content:center; }
#loginMask.show { display: flex; }
.login-box { background:#fff; padding:40px; border-radius:16px; text-align:center; width:340px; box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.1); }
.login-box h3 { justify-content: center; margin-top: 0; }
.login-error { color:#dc2626; font-size:13px; min-height:18px; margin-bottom:10px; }
</style>
</head>
<body>

<div id='loginMask'>
    <div class='login-box'>
        <h3>安全验证</h3>
        <div id='loginError' class='login-error'></div>
        <input type='password' id='loginPass' class='form-control' style='width:100%; margin-bottom:20px; box-sizing:border-box;' placeholder='请输入管理密码'>
        <button class='btn btn-primary' style='width:100%; padding:12px; font-size:15px;' onclick='doLogin()'>进入控制台</button>
    </div>
</div>

<div class='container'>
  <h2>游戏监控 - 远程控制台</h2>

  <div id='alertBox' class='alert'></div>

  <h3>全局时间设置</h3>
  <div class='form-group'>
    <label>提醒后宽限 (分钟)</label>
    <input type='number' id='reminder' class='form-control' min='1' max='120' style='width:120px;'>
  </div>
  <div class='form-group'>
    <label>倒计时 (秒)</label>
    <input type='number' id='countdown' class='form-control' min='5' max='600' style='width:120px;'>
  </div>

  <h3>监控列表 <span style='font-size:13px; color:#6b7280; font-weight:normal; margin-left:10px;'>状态每 5 秒自动刷新</span></h3>

  <div class='table-wrapper'>
    <table>
      <thead>
        <tr>
          <th width='100'>运行状态</th>
          <th>显示名称</th>
          <th>匹配关键字</th>
          <th width='100'>单次 (分)</th>
          <th width='100'>配额 (分)</th>
          <th width='130'>今日已用</th>
          <th width='320'>操作</th>
        </tr>
      </thead>
      <tbody id='gameList'>
      </tbody>
    </table>
  </div>

  <div style='margin-top: 25px; display: flex; justify-content: space-between;'>
    <button class='btn btn-secondary' onclick='addGame()'>+ 添加新游戏</button>
    <button class='btn btn-save' onclick='saveConfig()'>保存并应用修改</button>
  </div>

  <h3>安全设置 (修改密码)</h3>
  <div class='form-group'>
    <label>新密码</label>
    <input type='password' id='newPass' class='form-control' style='width:200px;'>
  </div>
  <div class='form-group'>
    <label>确认新密码</label>
    <input type='password' id='confirmPass' class='form-control' style='width:200px;'>
  </div>
  <div style='margin-top: 10px;'>
    <button class='btn btn-primary' onclick='changePassword()' style='padding: 10px 20px;'>更新密码</button>
    <button class='btn btn-danger' onclick='clearPassword()' style='padding: 10px 20px;'>清除密码（会关闭控制台）</button>
  </div>
</div>

<script>
  let configData = { Games: [], ReminderMinutes: 5, CountdownSeconds: 60, HasPassword: true };
  let adminPass = sessionStorage.getItem('adminPass') || '';
  let isEditing = false;
  let hasUnsavedChanges = false;
  let hasUnsavedGlobalChanges = false;
  let authed = false;

  function showLoginError(msg) {
      document.getElementById('loginError').textContent = msg || '';
  }

  function showMask() {
      document.getElementById('loginMask').classList.add('show');
  }

  function hideMask() {
      document.getElementById('loginMask').classList.remove('show');
  }

  async function boot() {
      try {
          const auth = await fetch('/api/auth').then(r => r.json());
          if (!auth.Required) {
              hideMask();
              await loadStatus();
              authed = true;
              return;
          }
          if (adminPass) {
              const ok = await login(adminPass, true);
              if (ok) { hideMask(); return; }
              sessionStorage.removeItem('adminPass');
              adminPass = '';
          }
          showMask();
      } catch (e) {
          showLoginError('无法连接服务：' + e.message);
          showMask();
      }
  }

  async function login(pass, silent) {
      const res = await fetch('/api/status', { headers: { 'X-Admin-Password': pass } });
      if (res.status === 401) {
          if (!silent) showLoginError('密码错误，请重新输入');
          return false;
      }
      if (!res.ok) {
          showLoginError('服务异常：' + res.status);
          return false;
      }
      const data = await res.json();
      sessionStorage.setItem('adminPass', pass);
      adminPass = pass;
      authed = true;
      applyData(data);
      return true;
  }

  async function doLogin() {
      const p = document.getElementById('loginPass').value;
      if (!p) { showLoginError('请输入管理密码'); return; }
      showLoginError('');
      const ok = await login(p, false);
      if (ok) hideMask();
  }

  function applyData(data) {
      configData = data;
      if (!isEditing && !hasUnsavedGlobalChanges) {
          document.getElementById('reminder').value = data.ReminderMinutes;
          document.getElementById('countdown').value = data.CountdownSeconds;
      }
      renderGames();
      updateStatusOnly(data.Games);
  }

  document.getElementById('reminder').addEventListener('change', () => { hasUnsavedGlobalChanges = true; });
  document.getElementById('countdown').addEventListener('change', () => { hasUnsavedGlobalChanges = true; });

  document.addEventListener('focusin', (e) => { if (e.target.tagName === 'INPUT') isEditing = true; });
  document.addEventListener('focusout', (e) => { if (e.target.tagName === 'INPUT') isEditing = false; });

  function makeInput(type, value, field, index, numeric) {
      const input = document.createElement('input');
      input.type = type;
      input.className = 'table-input' + (numeric ? ' number-input' : '');
      input.value = value;
      input.dataset.field = field;
      input.dataset.index = index;
      input.addEventListener('change', () => {
          const idx = parseInt(input.dataset.index);
          const val = numeric ? (parseInt(input.value) || 0) : input.value;
          configData.Games[idx][field] = val;
          hasUnsavedChanges = true;
      });
      return input;
  }

  function makeButton(text, cls, handler) {
      const btn = document.createElement('button');
      btn.className = 'btn ' + cls;
      btn.textContent = text;
      btn.addEventListener('click', handler);
      return btn;
  }

  function renderGames() {
      const tbody = document.getElementById('gameList');
      tbody.textContent = '';

      configData.Games.forEach((game, index) => {
          const tr = document.createElement('tr');

          const tdStatus = document.createElement('td');
          const tag = document.createElement('span');
          tag.className = 'status-tag ' + (game.IsRunning ? 'status-running' : 'status-stopped');
          tag.dataset.proc = game.ProcessName || '';
          tag.textContent = game.IsRunning ? '运行中' : '未运行';
          tdStatus.appendChild(tag);
          tr.appendChild(tdStatus);

          const tdName = document.createElement('td');
          tdName.appendChild(makeInput('text', game.DisplayName || '', 'DisplayName', index, false));
          tr.appendChild(tdName);

          const tdProc = document.createElement('td');
          tdProc.appendChild(makeInput('text', game.ProcessName || '', 'ProcessName', index, false));
          tr.appendChild(tdProc);

          const tdRun = document.createElement('td');
          tdRun.appendChild(makeInput('number', game.RunMinutes || 60, 'RunMinutes', index, true));
          tdRun.appendChild(document.createTextNode(' 分'));
          tr.appendChild(tdRun);

          const tdLimit = document.createElement('td');
          tdLimit.appendChild(makeInput('number', game.DailyLimitMinutes || 0, 'DailyLimitMinutes', index, true));
          tdLimit.appendChild(document.createTextNode(' 分'));
          tr.appendChild(tdLimit);

          const tdUsed = document.createElement('td');
          const usedSpan = document.createElement('span');
          usedSpan.className = 'used-text';
          usedSpan.dataset.proc = game.ProcessName || '';
          const usedB = document.createElement('b');
          usedB.textContent = String(game.UsedMinutesToday || 0);
          usedSpan.appendChild(usedB);
          const limitStr = game.DailyLimitMinutes === 0 ? '不限' : String(game.DailyLimitMinutes);
          usedSpan.appendChild(document.createTextNode(' / ' + limitStr + ' 分'));
          tdUsed.appendChild(usedSpan);
          tr.appendChild(tdUsed);

          const tdOps = document.createElement('td');
          tdOps.appendChild(makeButton('重置', 'btn-warning', () => resetGame(index)));
          tdOps.appendChild(makeButton('倒计时', 'btn-primary', () => forceCountdown(index)));
          tdOps.appendChild(makeButton('结束', 'btn-dark', () => killGame(index)));
          tdOps.appendChild(makeButton('删除', 'btn-danger', () => removeGame(index)));
          tr.appendChild(tdOps);

          tbody.appendChild(tr);
      });
  }

  async function fetchStatus() {
      if (!authed) return;
      try {
          const res = await fetch('/api/status', { headers: { 'X-Admin-Password': adminPass } });
          if (res.status === 401) { logout(); return; }
          if (!res.ok) return;

          const data = await res.json();
          if (configData.HasPassword !== data.HasPassword) { location.reload(); }

          if (!isEditing && !hasUnsavedGlobalChanges) {
              document.getElementById('reminder').value = data.ReminderMinutes;
              document.getElementById('countdown').value = data.CountdownSeconds;
          }

          if (!hasUnsavedChanges && !isEditing) {
              const structureChanged = configData.Games.length !== data.Games.length ||
                  data.Games.some((g, i) => {
                      const old = configData.Games[i];
                      return !old || old.DisplayName !== g.DisplayName ||
                          old.ProcessName !== g.ProcessName ||
                          old.RunMinutes !== g.RunMinutes ||
                          old.DailyLimitMinutes !== g.DailyLimitMinutes;
                  });
              configData.Games = data.Games;
              if (structureChanged) renderGames();
          } else {
              configData.Games = data.Games;
          }
          updateStatusOnly(data.Games);
      } catch (e) { }
  }

  function updateStatusOnly(newGames) {
      document.querySelectorAll('.status-tag').forEach(el => {
          const proc = el.dataset.proc;
          if (!proc) return;
          const match = newGames.find(x => (x.ProcessName || '').toLowerCase() === proc.toLowerCase());
          if (!match) return;
          el.textContent = match.IsRunning ? '运行中' : '未运行';
          el.className = 'status-tag ' + (match.IsRunning ? 'status-running' : 'status-stopped');
      });

      document.querySelectorAll('.used-text').forEach(el => {
          const proc = el.dataset.proc;
          if (!proc) return;
          const match = newGames.find(x => (x.ProcessName || '').toLowerCase() === proc.toLowerCase());
          if (!match) return;
          const limitStr = match.DailyLimitMinutes === 0 ? '不限' : String(match.DailyLimitMinutes);
          el.textContent = '';
          const b = document.createElement('b');
          b.textContent = String(match.UsedMinutesToday);
          el.appendChild(b);
          el.appendChild(document.createTextNode(' / ' + limitStr + ' 分'));
      });
  }

  function addGame() {
      configData.Games.push({
          DisplayName: '新游戏',
          ProcessName: 'newgame',
          RunMinutes: 60,
          DailyLimitMinutes: 0,
          UsedMinutesToday: 0,
          IsRunning: false,
          Enabled: true,
          MatchMode: 'Contains'
      });
      hasUnsavedChanges = true;
      renderGames();
  }

  function removeGame(index) {
      if (!confirm('确定要删除此游戏吗？')) return;
      configData.Games.splice(index, 1);
      hasUnsavedChanges = true;
      renderGames();
  }

  function showAlert(msg, isSuccess) {
      const box = document.getElementById('alertBox');
      box.className = 'alert ' + (isSuccess ? 'alert-success' : 'alert-error');
      box.textContent = msg;
      box.style.display = 'block';
      setTimeout(() => { box.style.display = 'none'; }, 3000);
  }

  function logout() {
      sessionStorage.removeItem('adminPass');
      authed = false;
      adminPass = '';
      showLoginError('登录已失效，请重新输入密码');
      showMask();
  }

  async function postJson(url, data) {
      const res = await fetch(url, {
          method: 'POST',
          headers: {
              'Content-Type': 'application/json',
              'X-Admin-Password': adminPass
          },
          body: JSON.stringify(data)
      });
      if (res.status === 401) { logout(); return null; }
      return res;
  }

  async function resetGame(index) {
      if (!confirm('确定要重置该游戏的今日已用时间吗？')) return;
      const processName = configData.Games[index].ProcessName;
      try {
          const res = await postJson('/api/reset_usage', { ProcessName: processName });
          if (res && res.ok) {
              showAlert('已重置该游戏今日用时！', true);
              hasUnsavedChanges = false;
              isEditing = false;
              fetchStatus();
          } else if (res) {
              const data = await res.json().catch(() => ({}));
              showAlert(data.error || '重置失败。', false);
          }
      } catch (e) { showAlert('网络错误：' + e.message, false); }
  }

  async function forceCountdown(index) {
      if (!confirm('确定要强制让该游戏进入倒计时吗？')) return;
      const processName = configData.Games[index].ProcessName;
      try {
          const res = await postJson('/api/force_countdown', { ProcessName: processName });
          if (res && res.ok) {
              showAlert('已发送强制倒计时指令！', true);
              hasUnsavedChanges = false;
              isEditing = false;
              fetchStatus();
          } else if (res) {
              const data = await res.json().catch(() => ({}));
              showAlert(data.error || '操作失败', false);
          }
      } catch (e) { showAlert('网络错误：' + e.message, false); }
  }

  async function killGame(index) {
      if (!confirm('确定要立即结束该游戏的进程吗？')) return;
      const processName = configData.Games[index].ProcessName;
      try {
          const res = await postJson('/api/kill', { ProcessName: processName });
          if (res && res.ok) {
              const data = await res.json().catch(() => ({}));
              if (data.success) showAlert('已结束匹配进程！', true);
              else showAlert(data.error || '结束失败', false);
              hasUnsavedChanges = false;
              isEditing = false;
              fetchStatus();
          }
      } catch (e) { showAlert('网络错误：' + e.message, false); }
  }

  async function saveConfig() {
      const rows = document.querySelectorAll('#gameList tr');
      rows.forEach((row, index) => {
          if (index >= configData.Games.length) return;
          row.querySelectorAll('input').forEach(input => {
              const field = input.dataset.field;
              if (!field) return;
              if (field === 'DisplayName' || field === 'ProcessName') {
                  configData.Games[index][field] = input.value.trim();
              } else {
                  configData.Games[index][field] = parseInt(input.value) || 0;
              }
          });
      });

      configData.ReminderMinutes = parseInt(document.getElementById('reminder').value) || 5;
      configData.CountdownSeconds = parseInt(document.getElementById('countdown').value) || 60;

      try {
          const res = await postJson('/api/save', configData);
          if (res && res.ok) {
              showAlert('配置已保存并应用！', true);
              isEditing = false;
              hasUnsavedChanges = false;
              hasUnsavedGlobalChanges = false;
              fetchStatus();
          } else if (res) {
              const data = await res.json().catch(() => ({}));
              showAlert(data.error || '保存失败。', false);
          }
      } catch (e) { showAlert('网络错误：' + e.message, false); }
  }

  async function changePassword() {
      const newP = document.getElementById('newPass').value;
      const confirmP = document.getElementById('confirmPass').value;

      if (!newP) { alert('新密码不能为空！若要取消密码请点击“清除密码”按钮。'); return; }
      if (newP !== confirmP) { alert('两次输入的新密码不一致！'); return; }
      if (!confirm('确定要修改密码吗？修改后需要重新登录。')) return;

      try {
          const res = await postJson('/api/change_password', { NewPass: newP });
          if (res && res.ok) {
              const data = await res.json();
              if (data.success) {
                  showAlert('密码修改成功，请重新登录！', true);
                  sessionStorage.removeItem('adminPass');
                  setTimeout(() => location.reload(), 1200);
              } else {
                  showAlert(data.error || '修改失败', false);
              }
          }
      } catch (e) { showAlert('网络错误：' + e.message, false); }
  }

  async function clearPassword() {
      if (!confirm('确定要彻底清除密码吗？清除后远程控制台会立即关闭，需在桌面端重新设置密码才能再开启。')) return;

      try {
          const res = await postJson('/api/clear_password', {});
          if (res && res.ok) {
              showAlert('密码已清除，远程控制台已关闭。', true);
              setTimeout(() => location.reload(), 1500);
          }
      } catch (e) { showAlert('网络错误：' + e.message, false); }
  }

  boot();
  setInterval(fetchStatus, 5000);
</script>
</body>
</html>";
        }

        private string SerializeJson(object obj)
        {
            var serializer = new DataContractJsonSerializer(obj.GetType());
            using (var ms = new MemoryStream())
            {
                serializer.WriteObject(ms, obj);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private T DeserializeJson<T>(string json)
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json ?? "")))
            {
                return (T)serializer.ReadObject(ms);
            }
        }

        private T TryDeserialize<T>(string json)
        {
            try
            {
                return DeserializeJson<T>(json);
            }
            catch (Exception ex)
            {
                AppLog.Warn("远程请求体解析失败", ex);
                return default(T);
            }
        }
    }
}
