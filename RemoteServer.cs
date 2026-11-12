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
        /// <summary>条目标识（= Keywords[0]），用于重置/加时/结束本次等操作。</summary>
        [DataMember] public string ProcessName { get; set; }
        /// <summary>匹配关键字列表（按顺序匹配，命中即停）；Web 用同一个输入框以“；”分隔。</summary>
        [DataMember] public List<string> Keywords { get; set; }
        [DataMember] public int RunMinutes { get; set; }
        [DataMember] public int DailyLimitMinutes { get; set; }
        [DataMember] public int UsedMinutesToday { get; set; }
        [DataMember] public bool IsRunning { get; set; }
        [DataMember] public bool Enabled { get; set; }
        [DataMember] public string MatchMode { get; set; }
        /// <summary>Idle / Running / Reminding / CountingDown / Cooldown / QuotaExhausted</summary>
        [DataMember] public string Phase { get; set; }
        [DataMember] public int CooldownRemainingSeconds { get; set; }
        /// <summary>宽限期剩余秒数（Reminding 阶段；其余为 0）</summary>
        [DataMember] public int RemindRemainingSeconds { get; set; }
        /// <summary>倒计时剩余秒数（CountingDown 阶段；其余为 0）</summary>
        [DataMember] public int CountdownRemainingSeconds { get; set; }
        /// <summary>今日实际配额（周末倍数 + 该条目加时；0 表示沿用总限额且总限额也未启用）</summary>
        [DataMember] public int EffectiveLimitMinutes { get; set; }
        /// <summary>该条目今日加时分钟数（旧每日加时，仅兼容读取）</summary>
        [DataMember] public int BonusMinutes { get; set; }
        /// <summary>本轮单次加时秒数（可负）</summary>
        [DataMember] public int SessionBonusSeconds { get; set; }
        /// <summary>本轮可用总时长（分钟，含加时）</summary>
        [DataMember] public int RoundLimitMinutes { get; set; }
        /// <summary>本轮剩余可用分钟数</summary>
        [DataMember] public int RoundRemainingMinutes { get; set; }
        /// <summary>该条目自己的间隔时间（分钟）。</summary>
        [DataMember] public int CooldownMinutes { get; set; }
        /// <summary>周末倍数（周六/周日生效）。</summary>
        [DataMember] public double WeekendMultiplier { get; set; }
        /// <summary>今天实际生效的倍数（周末取配置值，工作日为 1.0）。</summary>
        [DataMember] public double WeekendMultiplierToday { get; set; }
        /// <summary>今日生效的单次时长（含周末倍数）。</summary>
        [DataMember] public int EffectiveRunMinutes { get; set; }
        /// <summary>单次时长是否已被“没收”。</summary>
        [DataMember] public bool SessionForced { get; set; }
    }

    [DataContract]
    public class RemoteScheduleRule
    {
        [DataMember] public bool Enabled { get; set; }
        /// <summary>Block / Allow</summary>
        [DataMember] public string Kind { get; set; }
        [DataMember] public string StartTime { get; set; }
        [DataMember] public string EndTime { get; set; }
        /// <summary>7 个星期，索引 0=周日 … 6=周六。</summary>
        [DataMember] public List<bool> DaysOfWeek { get; set; }
    }

    [DataContract]
    public class RemoteConfigDto
    {
        [DataMember] public int ReminderMinutes { get; set; }
        [DataMember] public int CountdownSeconds { get; set; }
        [DataMember] public bool SoundOnGrace { get; set; }
        [DataMember] public bool SoundOnCountdown { get; set; }
        [DataMember] public bool SoundOnCooldown { get; set; }
        [DataMember] public bool SoundOnQuota { get; set; }
        [DataMember] public bool SoundOnBlockedBySchedule { get; set; }
        /// <summary>以下音频文件路径仅回显（Web 不提供上传）。</summary>
        [DataMember] public string SoundGraceFile { get; set; }
        [DataMember] public string SoundCountdownFile { get; set; }
        [DataMember] public string SoundCooldownFile { get; set; }
        /// <summary>配额用完提示音文件路径：只由设置页维护，仅在此回显；Web 保存时不回写（保留磁盘现值）。</summary>
        [DataMember] public string SoundQuotaFile { get; set; }
        /// <summary>禁止时段提示音文件路径：只由设置页维护，仅在此回显；Web 保存时不回写（保留磁盘现值）。</summary>
        [DataMember] public string SoundBlockedByScheduleFile { get; set; }
        [DataMember] public bool IsPaused { get; set; }
        [DataMember] public int PauseRemainingSeconds { get; set; }
        [DataMember] public bool HasPassword { get; set; }
        [DataMember] public List<RemoteGameItem> Games { get; set; }
        /// <summary>跨条目重复关键字时，前端确认后再次提交置 true。</summary>
        [DataMember] public bool ConfirmDuplicates { get; set; }
        /// <summary>每日总限额（分钟，0=不启用；条目 DailyLimit=0 时沿用）。</summary>
        [DataMember] public int DailyTotalLimitMinutes { get; set; }
        /// <summary>今日全部监控条目已用分钟之和（只读回显）。</summary>
        [DataMember] public int TotalUsedMinutes { get; set; }
        /// <summary>今日总限额实际生效值（含今日全部条目加时；0=不启用）。</summary>
        [DataMember] public int EffectiveTotalLimitMinutes { get; set; }
        /// <summary>今日总时长是否已用完。</summary>
        [DataMember] public bool TotalLimitExceeded { get; set; }
        /// <summary>时段规则（开放/禁止），Web 可编辑保存。</summary>
        [DataMember] public List<RemoteScheduleRule> ScheduleRules { get; set; }
        /// <summary>当前是否命中禁止时段。</summary>
        [DataMember] public bool InBlockedPeriod { get; set; }
        /// <summary>当前是否处于开放时段（未配置开放规则时为 true）。</summary>
        [DataMember] public bool InAllowedPeriod { get; set; }
        /// <summary>当前命中规则摘要（只读）。</summary>
        [DataMember] public string ScheduleSummary { get; set; }
        /// <summary>本次运行期间是否检测到系统时间跳变。</summary>
        [DataMember] public bool ClockJumpDetected { get; set; }
    }

    [DataContract]
    public class RemoteHistoryDay
    {
        [DataMember] public string Date { get; set; }
        /// <summary>当天首次启动时间（HH:mm:ss）；旧数据为空，Web 显示“—”。</summary>
        [DataMember] public string FirstStartTime { get; set; }
        /// <summary>当天末次关闭时间（HH:mm:ss）；旧数据为空，Web 显示“—”。</summary>
        [DataMember] public string LastEndTime { get; set; }
        [DataMember] public int UsedMinutes { get; set; }
        [DataMember] public int LaunchCount { get; set; }
        [DataMember] public string LastEndReason { get; set; }
        [DataMember] public bool WasInCooldown { get; set; }
        [DataMember] public int BonusMinutes { get; set; }
    }

    [DataContract]
    public class RemoteHistoryDto
    {
        [DataMember] public int Days { get; set; }
        [DataMember] public List<RemoteHistoryDay> Items { get; set; }
        /// <summary>当日 0~23 时的小时用量（分钟），Web 柱状图使用。</summary>
        [DataMember] public List<int> HourlyMinutes { get; set; }
        /// <summary>当日是否有小时桶数据；旧版本记录的这一天为 false。</summary>
        [DataMember] public bool HasHourlyData { get; set; }
    }

    [DataContract]
    public class RemoteReq
    {
        [DataMember] public string ProcessName { get; set; }
        [DataMember] public string NewPass { get; set; }
        [DataMember] public int Minutes { get; set; }
        [DataMember] public int Days { get; set; }
    }

    /// <summary>写操作统一响应：全部经 DataContractJsonSerializer 序列化，条目名含控制字符也不会破坏 JSON。</summary>
    [DataContract]
    public class RemoteOpResponse
    {
        [DataMember] public bool success { get; set; }
        [DataMember] public string error { get; set; }
        [DataMember] public string message { get; set; }
        [DataMember] public int sessionBonusSeconds { get; set; }
        [DataMember] public int removed { get; set; }
        /// <summary>重复关键字需要前端 confirm 时返回 true。</summary>
        [DataMember] public bool needConfirm { get; set; }
        [DataMember] public string confirmText { get; set; }
        [DataMember] public List<string> duplicates { get; set; }
        /// <summary>保存成功时的非阻断提示（如「关键字较短」），前端只做提示，不影响保存结果。</summary>
        [DataMember] public string notice { get; set; }
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
        private readonly Func<string, bool> _onForceExhaustSession;
        private readonly Action _onPasswordCleared;
        private readonly Func<List<GameStatus>> _statusProvider;
        private readonly Func<int, bool> _onPause;
        private readonly Func<bool> _onResume;
        /// <summary>本轮单次加时/减时：(匹配关键字, 分钟) ⇒ 结果（成功/错误/反馈文案）。</summary>
        private readonly Func<string, int, BonusAdjustResult> _onEntryBonus;
        /// <summary>清空今天以前的使用记录，返回删除的历史行数。</summary>
        private readonly Func<int> _onClearUsage;
        /// <summary>重置某条目今日用量时，同时清零监控运行态的本轮累计。</summary>
        private readonly Action<string> _onResetEntrySession;

        private readonly SemaphoreSlim _slots = new SemaphoreSlim(MaxConcurrentClients);

        private TcpListener _listener;
        private CancellationTokenSource _cts;
        private int _currentPort = -1;
        private bool _currentAllowLan = false;

        public RemoteServer(AppConfig config, Control uiControl, Action onConfigUpdated,
            Func<string, bool> onKillTarget, Func<string, bool> onForceExhaustSession, Action onPasswordCleared,
            Func<List<GameStatus>> statusProvider = null,
            Func<int, bool> onPause = null, Func<bool> onResume = null,
            Func<string, int, BonusAdjustResult> onEntryBonus = null,
            Func<int> onClearUsage = null, Action<string> onResetEntrySession = null)
        {
            _config = config;
            _uiControl = uiControl;
            _onConfigUpdated = onConfigUpdated;
            _onKillTarget = onKillTarget;
            _onForceExhaustSession = onForceExhaustSession;
            _onPasswordCleared = onPasswordCleared;
            _statusProvider = statusProvider;
            _onPause = onPause;
            _onResume = onResume;
            _onEntryBonus = onEntryBonus;
            _onClearUsage = onClearUsage;
            _onResetEntrySession = onResetEntrySession;
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
                        string target = parts[1];
                        int queryIndex = target.IndexOf('?');
                        string path = queryIndex >= 0 ? target.Substring(0, queryIndex) : target;
                        string query = queryIndex >= 0 ? target.Substring(queryIndex + 1) : "";

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
                        else if (method == "GET" && path == "/api/history")
                        {
                            if (!authed)
                            {
                                statusCode = 401;
                                responseBody = "{\"success\":false,\"error\":\"密码错误，请重新登录\"}";
                            }
                            else
                            {
                                int days = ParseQueryInt(query, "days", 14);
                                if (days < 1) days = 1;
                                if (days > UsageHistoryStore.RetentionDays) days = UsageHistoryStore.RetentionDays;
                                responseBody = SerializeJson(BuildHistoryDto(days));
                                contentType = "application/json; charset=utf-8";
                            }
                        }
                        else if (method == "POST")
                        {
                            // /api/bonus（临时加时/减时）与 /api/clear_usage（清空今天以前的历史）按需求不再要求密码；其它写接口仍校验。
                            if (!authed && path != "/api/bonus" && path != "/api/clear_usage")
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
                                    case "/api/clear_usage":
                                        {
                                            // 与设置页“清空”完全同一后端路径：只删除今天以前的历史行
                                            int removed = RunOnUi(() => _onClearUsage != null ? _onClearUsage() : 0);
                                            AppLog.Info($"网页端清空今天以前的使用记录：删除 {removed} 条历史行（{DateTime.Now:yyyy-MM-dd HH:mm:ss}）。");
                                            responseBody = SerializeJson(new RemoteOpResponse { success = true, removed = removed });
                                            contentType = "application/json; charset=utf-8";
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
                                    case "/api/exhaust_session":
                                        {
                                            var req = TryDeserialize<RemoteReq>(body);
                                            if (req == null || string.IsNullOrWhiteSpace(req.ProcessName))
                                            {
                                                statusCode = 400;
                                                responseBody = "{\"success\":false,\"error\":\"缺少 ProcessName\"}";
                                            }
                                            else
                                            {
                                                bool ok = RunOnUi(() =>
                                                    _onForceExhaustSession != null && _onForceExhaustSession(req.ProcessName));
                                                responseBody = ok
                                                    ? "{\"success\":true}"
                                                    : "{\"success\":false,\"error\":\"未找到该条目，或它正处于结束流程/今日已用完，无法结束本次\"}";
                                            }
                                            break;
                                        }
                                    case "/api/pause":
                                        {
                                            var req = TryDeserialize<RemoteReq>(body);
                                            int minutes = req == null ? 0 : req.Minutes;
                                            if (minutes < 1 || minutes > 1440)
                                            {
                                                statusCode = 400;
                                                responseBody = "{\"success\":false,\"error\":\"暂停分钟数必须在 1~1440\"}";
                                            }
                                            else
                                            {
                                                RunOnUi(() =>
                                                {
                                                    _onPause?.Invoke(minutes);
                                                    return true;
                                                });
                                                AppLog.Info($"网页端暂停监控 {minutes} 分钟。");
                                                responseBody = "{\"success\":true}";
                                            }
                                            break;
                                        }
                                    case "/api/resume":
                                        {
                                            RunOnUi(() =>
                                            {
                                                _onResume?.Invoke();
                                                return true;
                                            });
                                            AppLog.Info("网页端取消暂停。");
                                            responseBody = "{\"success\":true}";
                                            break;
                                        }
                                    case "/api/bonus":
                                        {
                                            var req = TryDeserialize<RemoteReq>(body);
                                            int minutes = req == null ? 0 : req.Minutes;
                                            if (req == null || string.IsNullOrWhiteSpace(req.ProcessName))
                                            {
                                                statusCode = 400;
                                                responseBody = "{\"success\":false,\"error\":\"缺少 ProcessName（加时/减时只作用于选中条目）\"}";
                                            }
                                            else if (minutes == 0 || Math.Abs(minutes) > GameMonitor.MaxBonusMinutes)
                                            {
                                                statusCode = 400;
                                                responseBody = $"{{\"success\":false,\"error\":\"加时/减时必须在 1~{GameMonitor.MaxBonusMinutes} 分钟\"}}";
                                            }
                                            else
                                            {
                                                // 与托盘/设置页同一入口：本轮单次加时/减时
                                                BonusAdjustResult opResult = null;
                                                RunOnUi(() =>
                                                {
                                                    if (_onEntryBonus != null)
                                                        opResult = _onEntryBonus(req.ProcessName, minutes);
                                                    return true;
                                                });
                                                if (opResult == null)
                                                {
                                                    statusCode = 400;
                                                    responseBody = SerializeJson(new RemoteOpResponse
                                                    {
                                                        success = false,
                                                        error = "加时/减时功能不可用"
                                                    });
                                                }
                                                else if (!opResult.Success)
                                                {
                                                    statusCode = 400;
                                                    responseBody = SerializeJson(new RemoteOpResponse
                                                    {
                                                        success = false,
                                                        error = opResult.Error ?? "加时失败"
                                                    });
                                                }
                                                else
                                                {
                                                    AppLog.Info(minutes > 0
                                                        ? $"网页端为「{req.ProcessName}」本轮加时 {minutes} 分钟（本轮加时累计 {opResult.SessionBonusSeconds} 秒）。"
                                                        : $"网页端为「{req.ProcessName}」本轮减时 {-minutes} 分钟（本轮加时累计 {opResult.SessionBonusSeconds} 秒）。");
                                                    responseBody = SerializeJson(new RemoteOpResponse
                                                    {
                                                        success = true,
                                                        message = opResult.Message ?? "",
                                                        sessionBonusSeconds = opResult.SessionBonusSeconds
                                                    });
                                                }
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
            // 配置/用量/监控阶段都在 UI 线程取快照；进程与窗口标题匹配走统一实现（后台线程枚举）
            var dto = RunOnUi(BuildConfigSnapshot);
            var statuses = new List<GameStatus>();
            if (_statusProvider != null)
            {
                try { statuses = RunOnUi(() => _statusProvider()) ?? new List<GameStatus>(); }
                catch (Exception ex) { AppLog.Warn("读取监控状态失败", ex); }
            }

            var matcherItems = new List<GameItem>();
            foreach (var item in dto.Games)
            {
                matcherItems.Add(new GameItem
                {
                    DisplayName = item.DisplayName,
                    ProcessName = item.ProcessName,
                    MatchMode = ParseMatchMode(item.MatchMode)
                });
            }

            bool[] running = ProcessMatcher.QueryRunning(matcherItems);
            for (int i = 0; i < dto.Games.Count; i++)
            {
                var item = dto.Games[i];
                item.IsRunning = running.Length > i && running[i];

                var st = statuses.FirstOrDefault(s =>
                    s.Game != null && s.Game.ProcessName != null && item.ProcessName != null &&
                    s.Game.ProcessName.Equals(item.ProcessName, StringComparison.OrdinalIgnoreCase));

                if (st != null)
                {
                    item.Phase = st.Phase.ToString();
                    item.CooldownRemainingSeconds = st.Phase == MonitorPhase.Cooldown
                        ? st.CooldownRemainingSeconds
                        : 0;
                    item.RemindRemainingSeconds = st.Phase == MonitorPhase.Reminding
                        ? st.RemindRemainingSeconds
                        : 0;
                    item.CountdownRemainingSeconds = st.Phase == MonitorPhase.CountingDown
                        ? st.CountdownRemainingSeconds
                        : 0;
                    item.EffectiveLimitMinutes = st.EffectiveLimitMinutes;
                    item.BonusMinutes = st.BonusMinutes;
                    item.SessionBonusSeconds = st.SessionBonusSeconds;
                    item.RoundLimitMinutes = st.RoundLimitMinutes;
                    item.RoundRemainingMinutes = st.RoundRemainingMinutes;
                    item.CooldownMinutes = st.Game.CooldownMinutes;
                    item.WeekendMultiplierToday = st.WeekendMultiplier;
                    item.EffectiveRunMinutes = st.EffectiveRunMinutes;
                    item.SessionForced = st.SessionForced;
                }
                else
                {
                    item.Phase = item.IsRunning ? MonitorPhase.Running.ToString() : MonitorPhase.Idle.ToString();
                    item.CooldownRemainingSeconds = 0;
                    item.RemindRemainingSeconds = 0;
                    item.CountdownRemainingSeconds = 0;
                    // EffectiveLimitMinutes 保留快照中按“条目配额/总限额 + 周末倍数 + 加时”算出的值
                    item.EffectiveRunMinutes = item.RunMinutes;
                    item.RoundLimitMinutes = item.RunMinutes;
                    item.RoundRemainingMinutes = item.RunMinutes;
                    item.WeekendMultiplierToday = 1.0;
                    item.SessionForced = false;
                }
            }

            var firstStatus = statuses.FirstOrDefault();
            dto.InBlockedPeriod = firstStatus?.InBlockedPeriod ?? false;
            dto.InAllowedPeriod = firstStatus?.InAllowedPeriod ?? true;
            dto.ScheduleSummary = firstStatus?.ScheduleSummary ?? "";
            dto.ClockJumpDetected = firstStatus?.ClockJumpDetected ?? false;

            return dto;
        }

        private RemoteConfigDto BuildConfigSnapshot()
        {
            lock (_config.SyncRoot)
            {
                DateTime today = DateTime.Today;
                var list = new List<RemoteGameItem>();
                int totalUsedSeconds = 0;
                int configuredTotal = Math.Max(0, _config.DailyTotalLimitMinutes);

                foreach (var g in _config.Games)
                {
                    var usage = _config.Usage.FirstOrDefault(x =>
                        x.ProcessName != null && g.ProcessName != null &&
                        x.ProcessName.Equals(g.ProcessName, StringComparison.OrdinalIgnoreCase));
                    int usedMinutes = (usage != null && usage.Date.Date == today) ? usage.UsedSeconds / 60 : 0;
                    if (usage != null && usage.Date.Date == today)
                        totalUsedSeconds += Math.Max(0, usage.UsedSeconds);

                    int bonus = _config.EntryBonusMinutesFor(g, today);
                    int effectiveLimit;
                    if (g.DailyLimitMinutes > 0)
                    {
                        double multiplier = GameMonitor.IsWeekend(today) ? g.WeekendMultiplier : 1.0;
                        effectiveLimit = Math.Max(1, (int)Math.Round(g.DailyLimitMinutes * multiplier, MidpointRounding.AwayFromZero)) + bonus;
                    }
                    else
                    {
                        effectiveLimit = configuredTotal > 0 ? configuredTotal + bonus : 0;
                    }

                    list.Add(new RemoteGameItem
                    {
                        DisplayName = g.DisplayName,
                        ProcessName = g.ProcessName,
                        Keywords = new List<string>(g.KeywordList),
                        RunMinutes = g.RunMinutes,
                        DailyLimitMinutes = g.DailyLimitMinutes,
                        UsedMinutesToday = usedMinutes,
                        Enabled = g.Enabled,
                        MatchMode = g.MatchMode.ToString(),
                        CooldownMinutes = g.CooldownMinutes,
                        WeekendMultiplier = g.WeekendMultiplier,
                        BonusMinutes = bonus,
                        EffectiveLimitMinutes = effectiveLimit
                    });
                }

                int totalBonus = _config.TotalEntryBonusMinutes(today);
                var scheduleRules = new List<RemoteScheduleRule>();
                foreach (var rule in _config.ScheduleRules ?? new List<ScheduleRule>())
                {
                    if (rule == null) continue;
                    var days = new List<bool>(new bool[7]);
                    for (int d = 0; d < 7 && d < (rule.DaysOfWeek?.Length ?? 0); d++)
                        days[d] = rule.DaysOfWeek[d];
                    scheduleRules.Add(new RemoteScheduleRule
                    {
                        Enabled = rule.Enabled,
                        Kind = rule.Kind == ScheduleRuleKind.Allow ? "Allow" : "Block",
                        StartTime = ScheduleRules.NormalizeTime(rule.StartTime),
                        EndTime = ScheduleRules.NormalizeTime(rule.EndTime),
                        DaysOfWeek = days
                    });
                }

                return new RemoteConfigDto
                {
                    ReminderMinutes = _config.ReminderMinutes,
                    CountdownSeconds = _config.CountdownSeconds,
                    SoundOnGrace = _config.SoundOnGrace,
                    SoundOnCountdown = _config.SoundOnCountdown,
                    SoundOnCooldown = _config.SoundOnCooldown,
                    SoundOnQuota = _config.SoundOnQuota,
                    SoundOnBlockedBySchedule = _config.SoundOnBlockedBySchedule,
                    SoundGraceFile = _config.SoundGraceFile ?? "",
                    SoundCountdownFile = _config.SoundCountdownFile ?? "",
                    SoundCooldownFile = _config.SoundCooldownFile ?? "",
                    SoundQuotaFile = _config.SoundQuotaFile ?? "",
                    SoundBlockedByScheduleFile = _config.SoundBlockedByScheduleFile ?? "",
                    IsPaused = _config.PauseUntil > DateTime.Now,
                    PauseRemainingSeconds = _config.PauseUntil > DateTime.Now
                        ? Math.Max(0, (int)Math.Ceiling((_config.PauseUntil - DateTime.Now).TotalSeconds))
                        : 0,
                    HasPassword = !string.IsNullOrEmpty(_config.AdminPassword),
                    Games = list,
                    DailyTotalLimitMinutes = configuredTotal,
                    TotalUsedMinutes = totalUsedSeconds / 60,
                    EffectiveTotalLimitMinutes = configuredTotal > 0 ? configuredTotal + totalBonus : 0,
                    TotalLimitExceeded = configuredTotal > 0 &&
                                         totalUsedSeconds >= (configuredTotal + totalBonus) * 60,
                    ScheduleRules = scheduleRules
                };
            }
        }

        private static MatchMode ParseMatchMode(string matchMode)
        {
            if (string.IsNullOrEmpty(matchMode)) return MatchMode.Contains;
            try { return (MatchMode)Enum.Parse(typeof(MatchMode), matchMode, true); }
            catch { return MatchMode.Contains; }
        }

        private static int ParseQueryInt(string query, string key, int fallback)
        {
            if (string.IsNullOrEmpty(query)) return fallback;
            foreach (var pair in query.Split('&'))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                if (!pair.Substring(0, eq).Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
                if (int.TryParse(pair.Substring(eq + 1), out int value)) return value;
            }
            return fallback;
        }

        private RemoteHistoryDto BuildHistoryDto(int days)
        {
            var dto = new RemoteHistoryDto { Days = days, Items = new List<RemoteHistoryDay>() };
            var summary = UsageHistoryStore.GetDailySummary(days);
            foreach (var day in summary)
            {
                // 无数据的日期不占行：与设置页一致，清空后只剩有数据的今天
                if (!day.HasData) continue;
                dto.Items.Add(new RemoteHistoryDay
                {
                    Date = day.Date.ToString("yyyy-MM-dd"),
                    FirstStartTime = string.IsNullOrEmpty(day.FirstStartTime) ? "" : day.FirstStartTime,
                    LastEndTime = string.IsNullOrEmpty(day.LastEndTime) ? "" : day.LastEndTime,
                    UsedMinutes = day.UsedMinutes,
                    LaunchCount = day.LaunchCount,
                    LastEndReason = day.LastEndReason ?? "",
                    WasInCooldown = day.WasInCooldown,
                    BonusMinutes = day.BonusMinutes
                });
            }

            var today = summary.FirstOrDefault();
            // 存储是“秒桶”，接口/图表统一用分钟。
            dto.HourlyMinutes = today == null
                ? new List<int>(new int[24])
                : today.HourlySeconds.Select(sec => (int)Math.Round(Math.Max(0, sec) / 60.0, MidpointRounding.AwayFromZero)).ToList();
            dto.HasHourlyData = today?.HasHourlyData ?? false;
            return dto;
        }

        private RemoteOpResult ErrorResult(string message)
            => new RemoteOpResult(400, SerializeJson(new RemoteOpResponse { success = false, error = message }));

        private RemoteOpResult ApplyRemoteConfig(RemoteConfigDto newCfg)
        {
            if (newCfg == null)
                return new RemoteOpResult(400, "{\"success\":false,\"error\":\"请求体格式不正确\"}");

            if (newCfg.ReminderMinutes < 1 || newCfg.ReminderMinutes > 120)
                return new RemoteOpResult(400, "{\"success\":false,\"error\":\"提醒宽限必须在 1~120 分钟\"}");
            if (newCfg.CountdownSeconds < 5 || newCfg.CountdownSeconds > 600)
                return new RemoteOpResult(400, "{\"success\":false,\"error\":\"倒计时必须在 5~600 秒\"}");
            if (newCfg.DailyTotalLimitMinutes < 0 || newCfg.DailyTotalLimitMinutes > 1440)
                return new RemoteOpResult(400, "{\"success\":false,\"error\":\"每日总限额必须在 0~1440 分钟（0=不启用）\"}");

            var games = newCfg.Games ?? new List<RemoteGameItem>();

            // 条目级加时按匹配关键字保留：Web 保存整表重建时不能丢掉 BonusRecords
            var previousBonuses = new Dictionary<string, List<BonusRecord>>(StringComparer.OrdinalIgnoreCase);
            lock (_config.SyncRoot)
            {
                foreach (var g in _config.Games)
                {
                    if (g?.ProcessName != null)
                        previousBonuses[g.ProcessName] = g.BonusRecords ?? new List<BonusRecord>();
                }
            }

            // 先整体校验，任何一条不合法都不落盘，避免出现半套配置
            var normalized = new List<GameItem>();

            foreach (var g in games)
            {
                if (g == null) continue;

                string display = (g.DisplayName ?? "").Trim();
                var keywords = AppConfig.SplitKeywords(
                    g.Keywords != null && g.Keywords.Count > 0
                        ? string.Join(";", g.Keywords)
                        : (g.ProcessName ?? ""));

                if (display.Length == 0)
                    return ErrorResult("显示名称不能为空");
                if (keywords.Count == 0)
                    return ErrorResult("匹配关键字不能为空（多个关键字用；分隔）");
                foreach (var keyword in keywords)
                {
                    if (keyword.Length < GameMonitor.MinKeywordLength)
                        return ErrorResult($"「{display}」的关键字「{keyword}」至少需要 {GameMonitor.MinKeywordLength} 个字符");
                    if (ProcessMatcher.IsProtectedProcess(keyword))
                        return ErrorResult($"「{display}」的关键字「{keyword}」命中内置保护名单，不能用于监控");
                }
                if (g.RunMinutes < 1 || g.RunMinutes > 1440)
                    return ErrorResult("单次时长必须在 1~1440 分钟");
                if (g.DailyLimitMinutes < 0 || g.DailyLimitMinutes > 1440)
                    return ErrorResult("每日配额必须在 0~1440 分钟");
                if (g.CooldownMinutes < 1 || g.CooldownMinutes > 180)
                    return ErrorResult($"「{display}」的间隔时间必须在 1~180 分钟");
                if (g.WeekendMultiplier < 0.5 || g.WeekendMultiplier > 3.0)
                    return ErrorResult($"「{display}」的周末倍数必须在 0.5~3.0");

                // 条目内重复输入：去重后保存，只记 INFO
                string rawKeywords = g.Keywords != null && g.Keywords.Count > 0
                    ? string.Join(";", g.Keywords)
                    : (g.ProcessName ?? "");
                var intra = AppConfig.FindIntraDuplicates(rawKeywords);
                if (intra.Count > 0)
                    AppLog.Info($"网页端：条目「{display}」的关键字存在条目内重复（{string.Join("、", intra)}），已去重后保存。");

                // 条目标识 = 首个关键字（与历史用量/会话记录一致）
                string entryId = keywords[0];
                normalized.Add(new GameItem
                {
                    DisplayName = display,
                    ProcessName = entryId,
                    Keywords = keywords,
                    RunMinutes = g.RunMinutes,
                    DailyLimitMinutes = g.DailyLimitMinutes,
                    MatchMode = ParseMatchMode(g.MatchMode),
                    Enabled = g.Enabled,
                    CooldownMinutes = g.CooldownMinutes,
                    WeekendMultiplier = Math.Round(g.WeekendMultiplier * 2) / 2,
                    BonusRecords = previousBonuses.TryGetValue(entryId, out var bonus) && bonus != null
                        ? bonus
                        : new List<BonusRecord>()
                });
            }

            // 跨条目重复关键字：不再拒绝，确认后允许保存（归属按列表顺序先者独占）
            var validation = AppConfig.ValidateGames(normalized);
            if (validation.Error != null)
                return ErrorResult(validation.Error);
            if (validation.NeedsConfirm && !newCfg.ConfirmDuplicates)
            {
                return new RemoteOpResult(409, SerializeJson(new RemoteOpResponse
                {
                    success = false,
                    needConfirm = true,
                    confirmText = validation.ConfirmText,
                    duplicates = validation.Duplicates
                }));
            }
            if (validation.NeedsConfirm)
                AppLog.Info($"网页端保存时存在重复关键字：{string.Join("、", validation.Duplicates)}（用户已确认）");

            // 重复关键字的条目可能共享首个关键字标识：确认后给后者追加 #2/#3，保证内部键唯一
            AppConfig.AssignUniqueEntryIds(normalized);

            // 时段规则校验（时间格式 + 类型），任一非法则整体不落盘
            var scheduleRules = new List<ScheduleRule>();
            if (newCfg.ScheduleRules != null)
            {
                foreach (var r in newCfg.ScheduleRules)
                {
                    if (r == null) continue;
                    if (!ScheduleRules.IsValidTime(r.StartTime) || !ScheduleRules.IsValidTime(r.EndTime))
                        return new RemoteOpResult(400,
                            "{\"success\":false,\"error\":\"时段规则的开始/结束时间格式应为 HH:mm:ss\"}");

                    ScheduleRuleKind kind;
                    if ("Allow".Equals(r.Kind, StringComparison.OrdinalIgnoreCase))
                        kind = ScheduleRuleKind.Allow;
                    else if ("Block".Equals(r.Kind, StringComparison.OrdinalIgnoreCase))
                        kind = ScheduleRuleKind.Block;
                    else
                        return new RemoteOpResult(400,
                            "{\"success\":false,\"error\":\"时段规则类型必须是 Allow 或 Block\"}");

                    var days = new bool[7];
                    for (int d = 0; d < 7 && r.DaysOfWeek != null && d < r.DaysOfWeek.Count; d++)
                        days[d] = r.DaysOfWeek[d];

                    scheduleRules.Add(new ScheduleRule
                    {
                        Enabled = r.Enabled,
                        Kind = kind,
                        StartTime = ScheduleRules.NormalizeTime(r.StartTime),
                        EndTime = ScheduleRules.NormalizeTime(r.EndTime),
                        DaysOfWeek = days
                    });
                }
            }

            lock (_config.SyncRoot)
            {
                _config.ReminderMinutes = newCfg.ReminderMinutes;
                _config.CountdownSeconds = newCfg.CountdownSeconds;
                _config.SoundOnGrace = newCfg.SoundOnGrace;
                _config.SoundOnCountdown = newCfg.SoundOnCountdown;
                _config.SoundOnCooldown = newCfg.SoundOnCooldown;
                _config.SoundOnQuota = newCfg.SoundOnQuota;
                _config.SoundOnBlockedBySchedule = newCfg.SoundOnBlockedBySchedule;
                // 配额用完/禁止时段两个提示音文件路径只由设置页维护，Web 保存时不回写，保持原值
                _config.DailyTotalLimitMinutes = newCfg.DailyTotalLimitMinutes;
                _config.ScheduleRules = scheduleRules;

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

            // 两字关键字不拦截保存，只在响应里带一条非阻断提示（前端拼在“已保存并应用”后面）
            var shortKeywords = AppConfig.FindShortKeywords(normalized);
            string notice = shortKeywords.Count > 0
                ? $"关键字「{string.Join("、", shortKeywords)}」较短，可能匹配到多个窗口/进程，建议写得更具体。"
                : null;
            if (notice != null)
                AppLog.Info($"网页端保存：存在较短关键字（{string.Join("、", shortKeywords)}），已返回非阻断提示。");
            return new RemoteOpResult(200, SerializeJson(new RemoteOpResponse { success = true, notice = notice }));
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

            UsageHistoryStore.ResetUsed(processName);
            UsageHistoryStore.SaveIfDirty();
            _onResetEntrySession?.Invoke(processName);
            AppLog.Info($"网页端重置「{processName}」的今日用量。");

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
                case 409: return "Conflict";
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
:root { --primary: #6366f1; --primary-hover: #4f46e5; --bg: #f3f4f6; --card: #ffffff; --text: #1f2937; --text-muted: #6b7280; --border: #e5e7eb; --divider: #eceff3; }
body { font-family: 'Segoe UI', 'Microsoft YaHei', sans-serif; background: linear-gradient(135deg, #e0e7ff 0%, #f3f4f6 100%); margin: 0; padding: 30px 15px 100px; color: var(--text); font-size: 14px; min-height: 100vh; }
.container { max-width: 1200px; margin: 0 auto; background: var(--card); padding: 30px; border-radius: 16px; box-shadow: 0 10px 25px -5px rgba(0, 0, 0, 0.1), 0 8px 10px -6px rgba(0, 0, 0, 0.1); }
h2 { text-align: center; font-size: 28px; font-weight: 800; background: linear-gradient(to right, #4f46e5, #9333ea); -webkit-background-clip: text; -webkit-text-fill-color: transparent; margin-top: 0; margin-bottom: 30px; }
h3 { font-size: 18px; font-weight: 600; color: var(--text); margin-top: 30px; margin-bottom: 15px; display: flex; align-items: center; }
h3::before { content: ''; display: inline-block; width: 4px; height: 18px; background: var(--primary); border-radius: 2px; margin-right: 10px; }
.form-group { margin-bottom: 15px; display: inline-block; margin-right: 20px; }
.form-group label { display: block; margin-bottom: 8px; font-weight: 500; color: var(--text-muted); font-size: 13px; }
.form-control { padding: 10px 14px; border: 1px solid var(--border); border-radius: 8px; font-size: 14px; transition: all 0.2s; background: #f9fafb; }
.form-control:focus { border-color: var(--primary); outline: none; box-shadow: 0 0 0 3px rgba(99, 102, 241, 0.2); background: #fff; }
.table-wrapper { overflow-x: auto; margin-top: 15px; border-radius: 8px; border: 1px solid var(--divider); }
.collapse-header { margin-top: 12px; width: 100%; display: flex; justify-content: space-between; align-items: center; gap: 12px; padding: 12px 14px; background: #fafbfc; border: 1px solid var(--divider); border-radius: 8px; font-family: inherit; font-size: 14px; font-weight: 600; color: var(--text); cursor: pointer; transition: background 0.2s, border-color 0.2s; }
.collapse-header:hover { background: #f3f4f6; border-color: #d9dde3; }
.collapse-header span:last-child { color: var(--text-muted); font-size: 12px; font-weight: 500; }
table { width: 100%; border-collapse: collapse; text-align: left; white-space: nowrap; }
th, td { padding: 11px 12px; border-bottom: 1px solid var(--divider); vertical-align: middle; }
th { background-color: #fafbfc; color: var(--text-muted); font-weight: 600; font-size: 13px; text-transform: uppercase; letter-spacing: 0.05em; }
tr:last-child td { border-bottom: none; }
tr:hover { background-color: #fafbfc; }
.table-input { width: 100%; border: 1px solid transparent; background: transparent; padding: 8px; border-radius: 4px; font-family: inherit; font-size: 14px; color: var(--text); transition: all 0.2s; }
.table-input:hover { border-color: var(--border); background: #fff; }
.table-input:focus { border-color: var(--primary); outline: none; background: #fff; box-shadow: 0 0 0 2px rgba(99, 102, 241, 0.2); }
.number-input { width: 56px; text-align: center; }
.status-tag { display: inline-flex; align-items: center; padding: 4px 10px; border-radius: 20px; font-size: 12px; font-weight: 600; }
.status-tag::before { content: ''; width: 6px; height: 6px; border-radius: 50%; margin-right: 6px; }
.status-running { background-color: #ecfdf5; color: #059669; border: 1px solid #a7f3d0; }
.status-running::before { background-color: #10b981; }
.status-stopped { background-color: #f3f4f6; color: #6b7280; border: 1px solid #e5e7eb; }
.status-stopped::before { background-color: #9ca3af; }
.status-cooldown { background-color: #fff7ed; color: #b45309; border: 1px solid #fed7aa; }
.status-cooldown::before { background-color: #f59e0b; }
.status-exhausted { background-color: #fef2f2; color: #b91c1c; border: 1px solid #fecaca; }
.status-exhausted::before { background-color: #ef4444; }
.table-select { padding: 8px; border: 1px solid transparent; background: transparent; border-radius: 4px; font-family: inherit; font-size: 14px; color: var(--text); }
.table-select:hover { border-color: var(--border); background: #fff; }
.table-check { width: 18px; height: 18px; cursor: pointer; }
tr.row-selected { background-color: #eef2ff; }
tr.row-selected:hover { background-color: #e0e7ff; }
.btn:disabled { opacity: 0.45; cursor: not-allowed; transform: none; box-shadow: none; }
.btn { color: white; border: none; padding: 8px 14px; border-radius: 6px; cursor: pointer; font-size: 13px; font-weight: 500; transition: all 0.2s; display: inline-block; margin-right: 6px; box-shadow: 0 1px 2px rgba(0,0,0,0.05); }
.btn:last-child { margin-right: 0; }
.btn:hover { transform: translateY(-1px); box-shadow: 0 4px 6px rgba(0,0,0,0.1); }
.btn-primary { background: var(--primary); }
.btn-warning { background: #f59e0b; }
.btn-danger { background: #ef4444; }
.btn-dark { background: #4b5563; }
.btn-secondary { background: #e5e7eb; color: #374151; padding: 10px 16px; font-size: 14px; }
.btn-save { background: var(--primary); padding: 10px 20px; font-size: 14px; }
/* 底部浮动操作栏：右侧与页面容器内容区对齐，半透明毛玻璃 + 细分隔线，风格与卡片一致 */
.save-bar { position: fixed; left: 0; right: 0; bottom: 0; z-index: 900; background: rgba(255, 255, 255, 0.94); -webkit-backdrop-filter: blur(10px); backdrop-filter: blur(10px); border-top: 1px solid var(--divider); box-shadow: 0 -6px 16px -8px rgba(16, 24, 40, 0.18); }
.save-bar-inner { max-width: 1200px; margin: 0 auto; padding: 12px 30px; box-sizing: border-box; display: flex; align-items: center; gap: 10px; }
/* 按钮组始终靠右：用 margin-left:auto 定位，不依赖前面的提示元素占位，提示显隐不会让按钮横向位移 */
.save-bar-actions { margin-left: auto; display: flex; align-items: center; gap: 10px; flex: 0 0 auto; }
/* 状态区固定在按钮左侧：占满剩余宽度并裁剪，文字长短变化不会推动右侧按钮 */
.save-bar-status { flex: 1 1 0; min-width: 0; min-height: 18px; display: flex; align-items: center; gap: 6px; font-size: 12px; line-height: 18px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
.save-bar-status::before { content: ''; width: 6px; height: 6px; border-radius: 50%; flex: 0 0 auto; background: transparent; }
.save-bar-status[data-state='dirty'] { color: #b45309; }
.save-bar-status[data-state='dirty']::before { background: #f59e0b; }
.save-bar-status[data-state='success'] { color: #047857; }
.save-bar-status[data-state='success']::before { background: #10b981; }
.save-bar-status[data-state='error'] { color: #b91c1c; }
.save-bar-status[data-state='error']::before { background: #ef4444; }
.save-bar .btn { margin-right: 0; }
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

  <h3>全局设置 <span id='pauseState' style='font-size:13px; color:#6b7280; font-weight:normal; margin-left:10px;'></span></h3>
  <div style='display:flex; gap:16px; flex-wrap:wrap; align-items:flex-end; margin-bottom:10px;'>
    <div class='form-group' style='margin:0;'>
      <label>提醒后宽限 (分钟)</label>
      <input type='number' id='reminder' class='form-control' min='1' max='120' style='width:96px;'>
    </div>
    <div class='form-group' style='margin:0;'>
      <label>倒计时 (秒)</label>
      <input type='number' id='countdown' class='form-control' min='5' max='600' style='width:96px;'>
    </div>
    <div class='form-group' style='margin:0;'>
      <label>每日总限额 (分钟，0=不启用)</label>
      <input type='number' id='dailyTotalLimit' class='form-control' min='0' max='1440' style='width:120px;'>
    </div>
    <!-- 暂停监控：输入框与“每日总限额”同一行，上方同款字段说明标签 -->
    <div class='form-group' style='margin:0;'>
      <label>暂停监控 (分钟，1~1440)</label>
      <input type='number' id='pauseMinutes' class='form-control' min='1' max='1440' value='30' style='width:120px;'>
    </div>
    <div style='display:flex; gap:8px; padding-bottom:1px;'>
      <button class='btn btn-secondary' onclick='pauseMonitor()'>暂停监控</button>
      <button class='btn btn-secondary' onclick='resumeMonitor()'>取消暂停</button>
    </div>
  </div>

  <h3>监控列表</h3>
  <div style='display:flex; gap:10px; flex-wrap:wrap; margin-bottom:10px; align-items:flex-end;'>
    <div class='form-group' style='margin:0;'>
      <label>加时时间（分）</label>
      <input type='number' id='selMinutes' class='form-control' min='1' max='120' value='10' style='width:80px;'>
    </div>
    <button class='btn btn-primary' id='selBonusPlus' onclick='adjustSelectedBonus(1)' disabled
            title='请先在列表中点行或勾选条目'>加时（选中条目）</button>
    <button class='btn btn-warning' id='selBonusMinus' onclick='adjustSelectedBonus(-1)' disabled
            title='请先在列表中点行或勾选条目'>减时（选中条目）</button>
    <!-- 今日总用时：单行右对齐，替代原先独立的状态行 -->
    <span id='totalUsageText' style='margin-left:auto; font-weight:600; font-size:13px;
          color:#1f2937; white-space:nowrap;'>今日总用时 0 分，总限额未启用</span>
  </div>

  <div class='table-wrapper'>
    <table>
      <thead>
        <tr>
          <th width='44'>选择</th>
          <th width='120'>运行状态</th>
          <th width='56'>启用</th>
          <th>显示名称</th>
          <th width='132'>匹配方式</th>
          <th>匹配关键字</th>
          <th width='76'>单次(分)</th>
          <th width='76'>配额(分)</th>
          <th width='76'>间隔(分)</th>
          <th width='92'>周末倍数</th>
          <th width='110'>今日已用(分)</th>
          <th width='320'>操作</th>
        </tr>
      </thead>
      <tbody id='gameList'>
      </tbody>
    </table>
  </div>

  <div style='margin-top: 25px;'>
    <button class='btn btn-secondary' onclick='addGame()'>+ 添加新游戏</button>
  </div>

  <h3>时段管控</h3>
  <div id='scheduleState' style='font-size:12px; color:#6b7280; margin-bottom:8px; line-height:1.7;'></div>
  <div class='table-wrapper' style='margin-top:0;'>
    <table>
      <thead>
        <tr>
          <th width='56'>启用</th>
          <th width='110'>类型</th>
          <th width='120'>开始</th>
          <th width='120'>结束</th>
          <th width='330'>星期</th>
          <th width='70'>操作</th>
        </tr>
      </thead>
      <tbody id='ruleList'></tbody>
    </table>
  </div>
  <div style='margin-top:10px; display:flex; gap:10px; align-items:center; flex-wrap:wrap;'>
    <button class='btn btn-secondary' onclick='addRule()'>+ 新增时段规则</button>
    <span style='font-size:12px; color:#6b7280; line-height:1.8;'>
      禁止时段优先于开放时段；开始晚于结束视为跨零点（后半夜按开始那天的星期生效）；开放时段内照常计时，但不弹窗、不强杀。
    </span>
  </div>

  <h3>使用记录</h3>
  <div id='hourlyNote' style='display:none; font-size:12px; color:#6b7280; margin-bottom:6px;'>此前版本无小时数据，本日按 0 显示。</div>
  <!-- 小时图：宽度随容器缩放，24 根柱子均分整行（grid 1fr），窄屏不溢出 -->
  <div id='historyChart' style='display:grid; grid-template-columns:repeat(24, minmax(0, 1fr)); gap:2px;
       align-items:end; height:130px; padding:8px 6px 4px 6px; background:#fafbfc;
       border:1px solid var(--divider); border-radius:8px; overflow:hidden;'></div>
  <!-- 明细表默认折叠，标题栏点击展开（仅本次会话内保持状态） -->
  <button class='collapse-header' id='historyToggle' onclick='toggleHistoryDetail()'>
    <span>使用记录明细（按天）</span>
    <span id='historyToggleIcon'>展开 ▾</span>
  </button>
  <div id='historyDetailBody' style='display:none; margin-top:8px;'>
    <div class='table-wrapper' style='margin-top:0;'>
      <table>
        <thead>
          <tr>
            <th width='110'>日期</th>
            <th width='96'>首次开启</th>
            <th width='96'>末次关闭</th>
            <th width='96'>已用(分)</th>
            <th width='92'>启动次数</th>
            <th width='96'>加时(分)</th>
            <th>最后结束原因</th>
            <th width='130'>进入过间隔期</th>
          </tr>
        </thead>
        <tbody id='historyList'></tbody>
      </table>
    </div>
  </div>
  <div style='margin-top:10px; font-size:12px; color:#6b7280; line-height:1.8;'>
    <div style='display:flex; align-items:center; gap:8px;'>
      <div style='flex:1;'>柱状图：每小时使用时长（当日 0~23 时）。</div>
      <button class='btn btn-save' style='margin-left:auto;' onclick='clearUsage()'>清空记录</button>
    </div>
    <div>表格按天汇总。</div>
  </div>

  <h3>提醒声音</h3>
  <div class='form-group' style='display:flex; gap:20px; flex-wrap:wrap;'>
    <label style='display:flex; align-items:center; gap:6px;'>
      <input type='checkbox' id='soundGrace'> 宽限开始时播放
    </label>
    <label style='display:flex; align-items:center; gap:6px;'>
      <input type='checkbox' id='soundCountdown'> 倒计时开始时播放
    </label>
    <label style='display:flex; align-items:center; gap:6px;'>
      <input type='checkbox' id='soundCooldown'> 休息期提示时播放
    </label>
    <label style='display:flex; align-items:center; gap:6px;'>
      <input type='checkbox' id='soundQuota'> 配额用完时播放
    </label>
    <label style='display:flex; align-items:center; gap:6px;'>
      <input type='checkbox' id='soundBlocked'> 禁止时段提示时播放
    </label>
  </div>
  <div id='soundFiles' style='font-size:12px; margin-bottom:10px;'></div>

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

<div class='save-bar'>
  <div class='save-bar-inner'>
    <span id='saveBarStatus' class='save-bar-status' data-state='none' role='status' aria-live='polite'></span>
    <div class='save-bar-actions'>
      <button class='btn btn-save' id='saveBtn' onclick='saveConfig()' disabled>保存并应用</button>
      <button class='btn btn-secondary' id='cancelBtn' onclick='cancelChanges()' disabled>取消更改</button>
    </div>
  </div>
</div>

<script>
  let configData = { Games: [], ScheduleRules: [], ReminderMinutes: 5, CountdownSeconds: 60, DailyTotalLimitMinutes: 0, HasPassword: true };
  let adminPass = sessionStorage.getItem('adminPass') || '';
  let isEditing = false;
  let hasUnsavedChanges = false;
  let hasUnsavedGlobalChanges = false;
  let authed = false;

  // 底部浮动操作栏：任一未保存改动（列表/规则/全局设置）都算脏，两个按钮同步启用
  function isDirty() { return hasUnsavedChanges || hasUnsavedGlobalChanges; }

  let _barStatusTimer = null; // 成功提示的自动淡出计时器

  // 状态区状态机：dirty（有未保存）/ success（几秒后自动消失）/ error（常驻到下次操作）/ none（空占位）
  function setBarStatus(kind, text, autoHideMs) {
      const el = document.getElementById('saveBarStatus');
      if (!el) return;
      if (_barStatusTimer) { clearTimeout(_barStatusTimer); _barStatusTimer = null; }
      el.dataset.state = kind;
      el.textContent = text || '';
      if (autoHideMs > 0) {
          _barStatusTimer = setTimeout(() => {
              _barStatusTimer = null;
              if (isDirty()) setBarStatus('dirty', '有未保存的修改');
              else setBarStatus('none', '');
          }, autoHideMs);
      }
  }

  function updateSaveBar() {
      const dirty = isDirty();
      const saveBtn = document.getElementById('saveBtn');
      const cancelBtn = document.getElementById('cancelBtn');
      if (saveBtn) saveBtn.disabled = !dirty;
      if (cancelBtn) cancelBtn.disabled = !dirty;

      const el = document.getElementById('saveBarStatus');
      if (!el) return;
      if (_barStatusTimer) return;               // 成功提示展示期内不覆盖
      if (el.dataset.state === 'error') return;  // 失败原因常驻，等下次保存/取消/编辑
      setBarStatus(dirty ? 'dirty' : 'none', dirty ? '有未保存的修改' : '');
  }

  // 列表/时段规则的改动（编辑本身算一次新操作，清掉上一次失败提示）
  function markRowsDirty() { hasUnsavedChanges = true; setBarStatus('none', ''); updateSaveBar(); }
  // 全局设置（提醒/倒计时/总限额/声音）的改动
  function markGlobalDirty() { hasUnsavedGlobalChanges = true; setBarStatus('none', ''); updateSaveBar(); }

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
              const statusRes = await fetch('/api/status');
              if (statusRes.ok) applyData(await statusRes.json());
              authed = true;
              loadHistory();
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
      loadHistory();
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
          document.getElementById('dailyTotalLimit').value = data.DailyTotalLimitMinutes || 0;
          document.getElementById('soundGrace').checked = !!data.SoundOnGrace;
          document.getElementById('soundCountdown').checked = !!data.SoundOnCountdown;
          document.getElementById('soundCooldown').checked = !!data.SoundOnCooldown;
          document.getElementById('soundQuota').checked = !!data.SoundOnQuota;
          document.getElementById('soundBlocked').checked = !!data.SoundOnBlockedBySchedule;
      }
      renderSoundFiles(data);
      // 规则表正在编辑时不覆盖（登录后首次渲染不受输入焦点影响）
      const ruleBody = document.getElementById('ruleList');
      if (!ruleBody.contains(document.activeElement)) renderRules(data.ScheduleRules || []);
      updateQuickState();
      renderGames();
      updateStatusOnly(data.Games);
      updateSaveBar();
  }

  function updateQuickState() {
      const ps = document.getElementById('pauseState');
      if (configData.IsPaused) {
          const m = Math.max(1, Math.ceil((configData.PauseRemainingSeconds || 0) / 60));
          ps.textContent = '暂停中（剩余约 ' + m + ' 分钟）';
      } else {
          ps.textContent = '';
      }
      const totalUsed = configData.TotalUsedMinutes || 0;
      const totalLimit = configData.EffectiveTotalLimitMinutes || 0;
      const text = document.getElementById('totalUsageText');
      text.textContent = '今日总用时 ' + totalUsed + ' 分，' +
          (totalLimit > 0 ? '总限额 ' + totalLimit + ' 分' : '总限额未启用');
      text.style.color = (totalLimit > 0 && totalUsed >= totalLimit) ? '#b91c1c' : '#1f2937';

      const sched = document.getElementById('scheduleState');
      if (sched) {
          const summary = configData.ScheduleSummary || '';
          if (configData.InBlockedPeriod) {
              sched.textContent = '禁止时段生效中' + (summary ? '（' + summary + '）' : '') +
                  '：匹配程序会立即提示并关闭；倒计时中的会在倒计时结束后关闭。';
              sched.style.color = '#b91c1c';
          } else if (hasAllowRules() && configData.InAllowedPeriod) {
              sched.textContent = '开放时段中' + (summary ? '（' + summary + '）' : '') +
                  '：照常计时与记录用量，但不弹窗、不强杀。';
              sched.style.color = '#047857';
          } else if (hasAllowRules()) {
              sched.textContent = '当前不在开放时段：照常计时，超限也不弹窗、不强杀。';
              sched.style.color = '#6b7280';
          } else {
              sched.textContent = '未配置时段规则（不启用时段管控）。';
              sched.style.color = '#6b7280';
          }
          if (configData.ClockJumpDetected) {
              sched.textContent += ' ｜检测到系统时间跳变（已记录 WARN 日志）。';
              sched.style.color = '#b45309';
          }
      }
  }

  document.getElementById('reminder').addEventListener('change', markGlobalDirty);
  document.getElementById('countdown').addEventListener('change', markGlobalDirty);
  document.getElementById('dailyTotalLimit').addEventListener('change', markGlobalDirty);
  ['soundGrace', 'soundCountdown', 'soundCooldown', 'soundQuota', 'soundBlocked'].forEach(function (id) {
      document.getElementById(id).addEventListener('change', markGlobalDirty);
  });

  document.addEventListener('focusin', (e) => { if (e.target.tagName === 'INPUT') isEditing = true; });
  document.addEventListener('focusout', (e) => { if (e.target.tagName === 'INPUT') isEditing = false; });

  function hasAllowRules() {
      return (configData.ScheduleRules || []).some(r => r && r.Enabled && r.Kind === 'Allow');
  }

  function renderRules(rules) {
      configData.ScheduleRules = rules || [];
      const tbody = document.getElementById('ruleList');
      tbody.textContent = '';
      if (configData.ScheduleRules.length === 0) {
          const tr = document.createElement('tr');
          const td = document.createElement('td');
          td.colSpan = 6;
          td.style.cssText = 'color:#9ca3af; text-align:center; padding:14px;';
          td.textContent = '暂无时段规则（不启用时段管控）';
          tr.appendChild(td);
          tbody.appendChild(tr);
          return;
      }

      configData.ScheduleRules.forEach((rule, index) => {
          if (!rule.DaysOfWeek || rule.DaysOfWeek.length !== 7)
              rule.DaysOfWeek = [true, true, true, true, true, true, true];

          const tr = document.createElement('tr');

          const tdEnabled = document.createElement('td');
          const enabled = document.createElement('input');
          enabled.type = 'checkbox';
          enabled.className = 'table-check';
          enabled.checked = !!rule.Enabled;
          enabled.addEventListener('change', () => { rule.Enabled = enabled.checked; markRowsDirty(); });
          tdEnabled.appendChild(enabled);
          tr.appendChild(tdEnabled);

          const tdKind = document.createElement('td');
          const kind = document.createElement('select');
          kind.className = 'table-select';
          [['Block', '禁止时段'], ['Allow', '开放时段']].forEach(pair => {
              const opt = document.createElement('option');
              opt.value = pair[0];
              opt.textContent = pair[1];
              kind.appendChild(opt);
          });
          kind.value = rule.Kind === 'Allow' ? 'Allow' : 'Block';
          kind.addEventListener('change', () => { rule.Kind = kind.value; updateQuickState(); markRowsDirty(); });
          tdKind.appendChild(kind);
          tr.appendChild(tdKind);

          const tdStart = document.createElement('td');
          const start = document.createElement('input');
          start.type = 'time';
          start.step = '1';
          start.className = 'table-input';
          start.value = (rule.StartTime || '00:00:00');
          start.addEventListener('change', () => { rule.StartTime = start.value || '00:00:00'; markRowsDirty(); });
          tdStart.appendChild(start);
          tr.appendChild(tdStart);

          const tdEnd = document.createElement('td');
          const end = document.createElement('input');
          end.type = 'time';
          end.step = '1';
          end.className = 'table-input';
          end.value = (rule.EndTime || '00:00:00');
          end.addEventListener('change', () => { rule.EndTime = end.value || '00:00:00'; markRowsDirty(); });
          tdEnd.appendChild(end);
          tr.appendChild(tdEnd);

          const tdDays = document.createElement('td');
          tdDays.style.cssText = 'white-space:nowrap; font-size:12px;';
          [[1, '一'], [2, '二'], [3, '三'], [4, '四'], [5, '五'], [6, '六'], [0, '日']].forEach(pair => {
              const label = document.createElement('label');
              label.style.cssText = 'display:inline-flex; align-items:center; gap:2px; margin-right:7px;';
              const box = document.createElement('input');
              box.type = 'checkbox';
              box.className = 'table-check';
              box.checked = !!rule.DaysOfWeek[pair[0]];
              box.addEventListener('change', () => { rule.DaysOfWeek[pair[0]] = box.checked; markRowsDirty(); });
              label.appendChild(box);
              label.appendChild(document.createTextNode(pair[1]));
              tdDays.appendChild(label);
          });
          tr.appendChild(tdDays);

          const tdDelete = document.createElement('td');
          const del = makeButton('删除', 'btn-danger', () => {
              configData.ScheduleRules.splice(index, 1);
              renderRules(configData.ScheduleRules);
              markRowsDirty();
          });
          tdDelete.appendChild(del);
          tr.appendChild(tdDelete);

          tbody.appendChild(tr);
      });
  }

  function addRule() {
      configData.ScheduleRules.push({
          Enabled: true,
          Kind: 'Block',
          StartTime: '22:00:00',
          EndTime: '08:00:00',
          DaysOfWeek: [true, true, true, true, true, true, true]
      });
      renderRules(configData.ScheduleRules);
      markRowsDirty();
  }

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
          markRowsDirty();
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

  function statusInfo(game) {
      const phase = game.Phase || '';
      const tag = configData.InBlockedPeriod ? '·禁止时段'
          : (hasAllowRules() && configData.InAllowedPeriod ? '·开放时段' : '');
      const withTag = t => t + tag;
      if (configData.IsPaused) {
          return { text: '暂停中', cls: 'status-cooldown' };
      }
      if (phase === 'BlockedBySchedule') {
          return { text: '禁止时段', cls: 'status-exhausted' };
      }
      if (phase === 'Cooldown') {
          // 用词与桌面端 PhaseText 对齐：不足 60 秒按秒，其余按分钟向上取整
          const s = Math.max(0, game.CooldownRemainingSeconds || 0);
          if (s < 60) {
              return { text: withTag('休息 ' + Math.max(1, s) + ' 秒'), cls: 'status-cooldown' };
          }
          const cap = game.CooldownMinutes || 0;
          const m = Math.max(1, Math.ceil(s / 60));
          return { text: withTag('休息 ' + (cap > 0 ? Math.min(cap, m) : m) + ' 分'), cls: 'status-cooldown' };
      }
      if (phase === 'QuotaExhausted') {
          return { text: '禁止运行', cls: 'status-exhausted' };
      }
      if (phase === 'Reminding') {
          // 宽限期：不能再显示「还剩 0 分」
          const gs = Math.max(0, game.RemindRemainingSeconds || 0);
          if (gs <= 0) return { text: withTag('宽限期'), cls: 'status-running' };
          if (gs < 60) return { text: withTag('宽限期 ' + gs + ' 秒'), cls: 'status-running' };
          return { text: withTag('宽限期 ' + Math.ceil(gs / 60) + ' 分'), cls: 'status-running' };
      }
      if (phase === 'CountingDown') {
          if ((game.RoundRemainingMinutes || 0) > 0) {
              return { text: withTag('还剩 ' + game.RoundRemainingMinutes + ' 分'), cls: 'status-running' };
          }
          const cs = Math.max(0, game.CountdownRemainingSeconds || 0);
          return { text: withTag(cs > 0 ? '倒计时 ' + cs + ' 秒' : '倒计时'), cls: 'status-running' };
      }
      if (game.IsRunning) {
          return { text: withTag('还剩 ' + Math.max(0, game.RoundRemainingMinutes || 0) + ' 分'), cls: 'status-running' };
      }
      return { text: '未运行', cls: 'status-stopped' };
  }

  function makeSelect(value, field, index) {
      const sel = document.createElement('select');
      sel.className = 'table-select';
      sel.dataset.field = field;
      sel.dataset.index = index;
      const options = [
          ['Contains', '包含'],
          ['ExactProcess', '精确进程名'],
          ['ExactWindowTitle', '精确窗口名']
      ];
      options.forEach(o => {
          const opt = document.createElement('option');
          opt.value = o[0];
          opt.textContent = o[1];
          sel.appendChild(opt);
      });
      sel.value = value || 'Contains';
      sel.addEventListener('change', () => {
          configData.Games[parseInt(sel.dataset.index)].MatchMode = sel.value;
          markRowsDirty();
      });
      return sel;
  }

  function makeCheck(checked, field, index) {
      const box = document.createElement('input');
      box.type = 'checkbox';
      box.className = 'table-check';
      box.checked = !!checked;
      box.dataset.field = field;
      box.dataset.index = index;
      box.addEventListener('change', () => {
          configData.Games[parseInt(box.dataset.index)][field] = box.checked;
          markRowsDirty();
      });
      return box;
  }

  let selectedProcs = new Set();

  function procKey(name) { return (name || '').trim().toLowerCase(); }

  function isProcSelected(name) {
      const key = procKey(name);
      return key.length > 0 && selectedProcs.has(key);
  }

  function setProcSelected(name, on) {
      const key = procKey(name);
      if (!key) return;
      if (on) selectedProcs.add(key); else selectedProcs.delete(key);
      refreshSelectionUi();
  }

  function refreshSelectionUi() {
      document.querySelectorAll('#gameList tr').forEach(tr => {
          const on = isProcSelected(tr.dataset.proc);
          if (on) tr.classList.add('row-selected'); else tr.classList.remove('row-selected');
          const box = tr.querySelector('td input[type=checkbox].row-select');
          if (box) box.checked = on;
      });
      const has = selectedProcs.size > 0;
      const plus = document.getElementById('selBonusPlus');
      const minus = document.getElementById('selBonusMinus');
      if (plus) plus.disabled = !has;
      if (minus) minus.disabled = !has;
  }

  function makeRowCheck(game) {
      const box = document.createElement('input');
      box.type = 'checkbox';
      box.className = 'table-check row-select';
      box.checked = isProcSelected(game.ProcessName);
      box.title = '选中后可用上方“加时/减时”';
      box.addEventListener('click', e => e.stopPropagation());
      box.addEventListener('change', () => setProcSelected(game.ProcessName, box.checked));
      return box;
  }

  function renderGames() {
      const tbody = document.getElementById('gameList');
      tbody.textContent = '';

      configData.Games.forEach((game, index) => {
          const tr = document.createElement('tr');
          tr.dataset.proc = game.ProcessName || '';
          if (isProcSelected(game.ProcessName)) tr.classList.add('row-selected');
          tr.addEventListener('click', (e) => {
              if (e.target.closest('input,select,button')) return;
              setProcSelected(game.ProcessName, !isProcSelected(game.ProcessName));
          });

          const tdSelect = document.createElement('td');
          tdSelect.style.textAlign = 'center';
          tdSelect.appendChild(makeRowCheck(game));
          tr.appendChild(tdSelect);

          const tdStatus = document.createElement('td');
          const info = statusInfo(game);
          const tag = document.createElement('span');
          tag.className = 'status-tag ' + info.cls;
          tag.dataset.proc = game.ProcessName || '';
          tag.textContent = info.text;
          tdStatus.appendChild(tag);
          tr.appendChild(tdStatus);

          const tdEnabled = document.createElement('td');
          tdEnabled.appendChild(makeCheck(game.Enabled, 'Enabled', index));
          tr.appendChild(tdEnabled);

          const tdName = document.createElement('td');
          tdName.appendChild(makeInput('text', game.DisplayName || '', 'DisplayName', index, false));
          tr.appendChild(tdName);

          const tdMode = document.createElement('td');
          tdMode.appendChild(makeSelect(game.MatchMode, 'MatchMode', index));
          tr.appendChild(tdMode);

          const tdProc = document.createElement('td');
          const kwValue = (game.Keywords && game.Keywords.length ? game.Keywords.join('；') : (game.ProcessName || ''));
          const kwInput = makeInput('text', kwValue, 'Keywords', index, false);
          kwInput.placeholder = '多个关键字用；分隔，按顺序匹配';
          tdProc.appendChild(kwInput);
          tr.appendChild(tdProc);

          const tdRun = document.createElement('td');
          tdRun.appendChild(makeInput('number', game.RunMinutes || 60, 'RunMinutes', index, true));
          tr.appendChild(tdRun);

          const tdLimit = document.createElement('td');
          tdLimit.appendChild(makeInput('number', game.DailyLimitMinutes || 0, 'DailyLimitMinutes', index, true));
          tr.appendChild(tdLimit);

          const tdCooldown = document.createElement('td');
          tdCooldown.appendChild(makeInput('number', game.CooldownMinutes || 10, 'CooldownMinutes', index, true));
          tr.appendChild(tdCooldown);

          const tdMultiplier = document.createElement('td');
          const multInput = makeInput('number', (game.WeekendMultiplier || 1.0), 'WeekendMultiplier', index, false);
          multInput.step = '0.5';
          multInput.min = '0.5';
          multInput.max = '3.0';
          multInput.addEventListener('change', () => {
              configData.Games[index].WeekendMultiplier = parseFloat(multInput.value) || 1.0;
              markRowsDirty();
          });
          tdMultiplier.appendChild(multInput);
          tr.appendChild(tdMultiplier);

          const tdUsed = document.createElement('td');
          const usedSpan = document.createElement('span');
          usedSpan.className = 'used-text';
          usedSpan.dataset.proc = game.ProcessName || '';
          const usedB = document.createElement('b');
          usedB.textContent = String(game.UsedMinutesToday || 0);
          usedSpan.appendChild(usedB);
          const effective = (game.EffectiveLimitMinutes === undefined || game.EffectiveLimitMinutes === null)
              ? (game.DailyLimitMinutes || 0) : game.EffectiveLimitMinutes;
          const limitStr = effective === 0 ? '不限' : String(effective);
          usedSpan.appendChild(document.createTextNode(' / ' + limitStr));
          if ((game.BonusMinutes || 0) > 0) {
              usedSpan.appendChild(document.createTextNode('（含旧每日加时 ' + game.BonusMinutes + '）'));
          }
          if (game.Phase !== undefined && game.Phase !== null) {
              const bonusMin = Math.round((game.SessionBonusSeconds || 0) / 60);
              const roundTxt = ' 本轮剩余 ' + (game.RoundRemainingMinutes || 0) + '/' +
                  (game.RoundLimitMinutes || game.EffectiveRunMinutes || game.RunMinutes || 0) + ' 分' +
                  (bonusMin !== 0 ? '（本轮加时 ' + (bonusMin > 0 ? '+' : '') + bonusMin + ' 分）' : '');
              usedSpan.appendChild(document.createTextNode(roundTxt));
          }
          if ((game.WeekendMultiplierToday || 1) > 1.0 && game.Phase !== undefined) {
              usedSpan.appendChild(document.createTextNode(' 周末×' + game.WeekendMultiplierToday));
          }
          tdUsed.appendChild(usedSpan);
          tr.appendChild(tdUsed);

          const tdOps = document.createElement('td');
          tdOps.appendChild(makeButton('重置', 'btn-warning', () => resetGame(index)));
          tdOps.appendChild(makeButton('结束本次', 'btn-primary', () => exhaustSession(index)));
          tdOps.appendChild(makeButton('强制关闭', 'btn-dark', () => killGame(index)));
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
          const hadPassword = configData.HasPassword;
          configData.IsPaused = data.IsPaused;
          configData.PauseRemainingSeconds = data.PauseRemainingSeconds;
          configData.HasPassword = data.HasPassword;
          configData.DailyTotalLimitMinutes = data.DailyTotalLimitMinutes || 0;
          configData.TotalUsedMinutes = data.TotalUsedMinutes || 0;
          configData.EffectiveTotalLimitMinutes = data.EffectiveTotalLimitMinutes || 0;
          configData.TotalLimitExceeded = !!data.TotalLimitExceeded;
          configData.InBlockedPeriod = !!data.InBlockedPeriod;
          configData.InAllowedPeriod = data.InAllowedPeriod !== false;
          configData.ScheduleSummary = data.ScheduleSummary || '';
          configData.ClockJumpDetected = !!data.ClockJumpDetected;
          // 规则结构只在 applyData 时渲染；这里保留本地未保存的编辑
          if (!configData.ScheduleRules) configData.ScheduleRules = data.ScheduleRules || [];
          if (hadPassword !== data.HasPassword) { location.reload(); }

          if (!isEditing && !hasUnsavedGlobalChanges) {
              document.getElementById('reminder').value = data.ReminderMinutes;
              document.getElementById('countdown').value = data.CountdownSeconds;
              document.getElementById('dailyTotalLimit').value = data.DailyTotalLimitMinutes || 0;
              document.getElementById('soundGrace').checked = !!data.SoundOnGrace;
              document.getElementById('soundCountdown').checked = !!data.SoundOnCountdown;
              document.getElementById('soundCooldown').checked = !!data.SoundOnCooldown;
              document.getElementById('soundQuota').checked = !!data.SoundOnQuota;
              document.getElementById('soundBlocked').checked = !!data.SoundOnBlockedBySchedule;
          }
          renderSoundFiles(data);
          updateQuickState();

          if (!hasUnsavedChanges && !isEditing) {
              const structureChanged = configData.Games.length !== data.Games.length ||
                  data.Games.some((g, i) => {
                      const old = configData.Games[i];
                      return !old || old.DisplayName !== g.DisplayName ||
                          old.ProcessName !== g.ProcessName ||
                          old.RunMinutes !== g.RunMinutes ||
                          old.DailyLimitMinutes !== g.DailyLimitMinutes ||
                          old.CooldownMinutes !== g.CooldownMinutes ||
                          old.WeekendMultiplier !== g.WeekendMultiplier ||
                          old.Enabled !== g.Enabled ||
                          (old.MatchMode || 'Contains') !== (g.MatchMode || 'Contains');
                  });
              configData.Games = data.Games;
              if (structureChanged) renderGames();
          } else {
              // 有未保存修改时绝不能整表替换，否则新增行会被服务器快照顶掉（结构丢失 → 保存无效）
              mergeStatusIntoGames(data.Games);
          }
          updateStatusOnly(data.Games);
      } catch (e) { }
  }

  // 只把服务器返回的“状态派生字段”同步进本地列表，保留本地结构（含未保存的新增行）
  function mergeStatusIntoGames(serverGames) {
      (serverGames || []).forEach(sg => {
          const key = procKey(sg.ProcessName);
          const local = configData.Games.find(g => procKey(g.ProcessName) === key);
          if (!local) return;
          local.UsedMinutesToday = sg.UsedMinutesToday;
          local.IsRunning = sg.IsRunning;
          local.Phase = sg.Phase;
          local.CooldownRemainingSeconds = sg.CooldownRemainingSeconds;
          local.RemindRemainingSeconds = sg.RemindRemainingSeconds;
          local.CountdownRemainingSeconds = sg.CountdownRemainingSeconds;
          local.EffectiveLimitMinutes = sg.EffectiveLimitMinutes;
          local.BonusMinutes = sg.BonusMinutes;
          local.SessionBonusSeconds = sg.SessionBonusSeconds;
          local.RoundLimitMinutes = sg.RoundLimitMinutes;
          local.RoundRemainingMinutes = sg.RoundRemainingMinutes;
          local.WeekendMultiplierToday = sg.WeekendMultiplierToday;
          local.EffectiveRunMinutes = sg.EffectiveRunMinutes;
          local.SessionForced = sg.SessionForced;
      });
  }

  function updateStatusOnly(newGames) {
      document.querySelectorAll('.status-tag').forEach(el => {
          const proc = el.dataset.proc;
          if (!proc) return;
          const match = newGames.find(x => (x.ProcessName || '').toLowerCase() === proc.toLowerCase());
          if (!match) return;
          const info = statusInfo(match);
          el.textContent = info.text;
          el.className = 'status-tag ' + info.cls;
      });

      document.querySelectorAll('.used-text').forEach(el => {
          const proc = el.dataset.proc;
          if (!proc) return;
          const match = newGames.find(x => (x.ProcessName || '').toLowerCase() === proc.toLowerCase());
          if (!match) return;
          const effective = (match.EffectiveLimitMinutes === undefined || match.EffectiveLimitMinutes === null)
              ? (match.DailyLimitMinutes || 0) : match.EffectiveLimitMinutes;
          const limitStr = effective === 0 ? '不限' : String(effective);
          el.textContent = '';
          const b = document.createElement('b');
          b.textContent = String(match.UsedMinutesToday);
          el.appendChild(b);
          el.appendChild(document.createTextNode(' / ' + limitStr));
          if ((match.BonusMinutes || 0) > 0) {
              el.appendChild(document.createTextNode('（含旧每日加时 ' + match.BonusMinutes + '）'));
          }
          if (match.Phase !== undefined && match.Phase !== null) {
              const bonusMin = Math.round((match.SessionBonusSeconds || 0) / 60);
              el.appendChild(document.createTextNode(' 本轮剩余 ' + (match.RoundRemainingMinutes || 0) + '/' +
                  (match.RoundLimitMinutes || match.EffectiveRunMinutes || 0) + ' 分' +
                  (bonusMin !== 0 ? '（本轮加时 ' + (bonusMin > 0 ? '+' : '') + bonusMin + ' 分）' : '')));
          }
          if ((match.WeekendMultiplierToday || 1) > 1.0) {
              el.appendChild(document.createTextNode(' 周末×' + match.WeekendMultiplierToday));
          }
      });
  }


  function renderSoundFiles(data) {
      const el = document.getElementById('soundFiles');
      if (!el) return;
      const parts = [];
      if (data.SoundGraceFile) parts.push('宽限：' + data.SoundGraceFile);
      if (data.SoundCountdownFile) parts.push('倒计时：' + data.SoundCountdownFile);
      if (data.SoundCooldownFile) parts.push('休息期：' + data.SoundCooldownFile);
      el.textContent = parts.join(' ｜ ');
  }

  function addGame() {
      configData.Games.push({
          DisplayName: '新游戏',
          ProcessName: 'newgame',
          Keywords: ['newgame'],
          RunMinutes: 60,
          DailyLimitMinutes: 0,
          CooldownMinutes: 10,
          WeekendMultiplier: 1.0,
          UsedMinutesToday: 0,
          IsRunning: false,
          Enabled: true,
          MatchMode: 'Contains'
      });
      markRowsDirty();
      renderGames();
  }

  function removeGame(index) {
      if (!confirm('确定要删除此游戏吗？')) return;
      configData.Games.splice(index, 1);
      markRowsDirty();
      renderGames();
  }

  // 所有反馈都进浮动栏左侧状态区：成功几秒后淡出，失败常驻到下次操作（不再占用页面顶部，避免挤压布局）
  function showAlert(msg, isSuccess) {
      setBarStatus(isSuccess ? 'success' : 'error', msg, isSuccess ? 4000 : 0);
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

  async function clearUsage() {
      if (!confirm('将删除今天以前的历史记录。')) return;
      try {
          const res = await postJson('/api/clear_usage', {});
          if (res && res.ok) {
              const data = await res.json().catch(() => ({}));
              showAlert('已删除今天以前的历史记录' + (data.removed ? '（' + data.removed + ' 条）' : ''), true);
              hasUnsavedChanges = false;
              isEditing = false;
              updateSaveBar();
              fetchStatus();
              loadHistory();
          } else if (res) {
              const data = await res.json().catch(() => ({}));
              showAlert(data.error || '清空失败。', false);
          }
      } catch (e) { showAlert('网络错误：' + e.message, false); }
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
              updateSaveBar();
              fetchStatus();
          } else if (res) {
              const data = await res.json().catch(() => ({}));
              showAlert(data.error || '重置失败。', false);
          }
      } catch (e) { showAlert('网络错误：' + e.message, false); }
  }

  async function exhaustSession(index) {
      if (!confirm('「结束本次」会把本轮已用时长直接置为单次上限，立即进入提醒→倒计时→强制关闭。确定继续吗？')) return;
      const processName = configData.Games[index].ProcessName;
      try {
          const res = await postJson('/api/exhaust_session', { ProcessName: processName });
          if (res && res.ok) {
              const data = await res.json().catch(() => ({}));
              showAlert(data.success ? '已结束本次，已进入提醒/倒计时流程。' : (data.error || '操作失败'), !!data.success);
              hasUnsavedChanges = false;
              isEditing = false;
              updateSaveBar();
              fetchStatus();
          } else if (res) {
              const data = await res.json().catch(() => ({}));
              showAlert(data.error || '操作失败', false);
          }
      } catch (e) { showAlert('网络错误：' + e.message, false); }
  }

  async function killGame(index) {
      if (!confirm('「强制关闭」会立即终止该游戏的所有匹配进程（不经过提醒和倒计时）。确定继续吗？')) return;
      const processName = configData.Games[index].ProcessName;
      try {
          const res = await postJson('/api/kill', { ProcessName: processName });
          if (res && res.ok) {
              const data = await res.json().catch(() => ({}));
              if (data.success) showAlert('已强制关闭匹配进程！', true);
              else showAlert(data.error || '强制关闭失败', false);
              hasUnsavedChanges = false;
              isEditing = false;
              updateSaveBar();
              fetchStatus();
          }
      } catch (e) { showAlert('网络错误：' + e.message, false); }
  }

  // 取消更改：丢弃全部未保存修改（新增行/删除行/编辑值/未提交输入），从服务端重新拉取并重建界面
  async function cancelChanges() {
      if (!isDirty()) return;
      const scrollY = window.scrollY;
      // 先让正在编辑的输入框失焦，避免未提交内容在重建时残留
      if (document.activeElement && document.activeElement.blur) document.activeElement.blur();
      const prevRows = hasUnsavedChanges;
      const prevGlobal = hasUnsavedGlobalChanges;
      // 先清脏标记，applyData 才会用服务端值覆盖全局设置与规则表
      hasUnsavedChanges = false;
      hasUnsavedGlobalChanges = false;
      isEditing = false;
      updateSaveBar();
      try {
          const res = await fetch('/api/status', { headers: { 'X-Admin-Password': adminPass } });
          if (res.status === 401) { logout(); return; }
          if (!res.ok) throw new Error('HTTP ' + res.status);
          const data = await res.json();
          applyData(data);
          // 界面高度基本不变，直接回到原来的滚动位置，避免跳回顶部
          const restoreScroll = () => window.scrollTo(0, scrollY);
          restoreScroll();
          requestAnimationFrame(restoreScroll);
          showAlert('已取消所有未保存的修改。', true);
      } catch (e) {
          hasUnsavedChanges = prevRows;
          hasUnsavedGlobalChanges = prevGlobal;
          updateSaveBar();
          showAlert('取消更改失败：' + e.message, false);
      }
  }

  async function saveConfig() {
      const rows = document.querySelectorAll('#gameList tr');
      const collected = [];
      rows.forEach((row, index) => {
          // 以 DOM 行为准重建条目：新增行的索引可能超出本地数组，不能按索引取旧对象（否则被静默丢弃）
          const game = Object.assign({}, configData.Games[index] || {});
          row.querySelectorAll('input,select').forEach(el => {
              const field = el.dataset.field;
              if (!field) return;
              if (el.type === 'checkbox') { game[field] = el.checked; return; }
              if (el.tagName === 'SELECT') { game[field] = el.value; return; }
              if (field === 'DisplayName' || field === 'ProcessName') {
                  game[field] = el.value.trim();
              } else if (field === 'Keywords') {
                  const parts = el.value.replace(/；/g, ';').split(';')
                      .map(v => v.trim()).filter(v => v.length > 0);
                  const unique = [];
                  parts.forEach(v => { if (!unique.some(x => x.toLowerCase() === v.toLowerCase())) unique.push(v); });
                  game.Keywords = unique;
                  game.ProcessName = unique.length ? unique[0] : '';
              } else if (field === 'WeekendMultiplier') {
                  game[field] = parseFloat(el.value) || 1.0;
              } else {
                  game[field] = parseInt(el.value) || 0;
              }
          });
          collected.push(game);
      });
      configData.Games = collected;

      configData.ReminderMinutes = parseInt(document.getElementById('reminder').value) || 5;
      configData.CountdownSeconds = parseInt(document.getElementById('countdown').value) || 60;
      configData.DailyTotalLimitMinutes = parseInt(document.getElementById('dailyTotalLimit').value) || 0;
      configData.SoundOnGrace = document.getElementById('soundGrace').checked;
      configData.SoundOnCountdown = document.getElementById('soundCountdown').checked;
      configData.SoundOnCooldown = document.getElementById('soundCooldown').checked;
      configData.SoundOnQuota = document.getElementById('soundQuota').checked;
      configData.SoundOnBlockedBySchedule = document.getElementById('soundBlocked').checked;

      try {
          const res = await postJson('/api/save', configData);
          if (res && res.ok) {
              const saved = await res.json().catch(() => ({}));
              showAlert('已保存并应用' + (saved.notice ? '；' + saved.notice : ''), true);
              isEditing = false;
              hasUnsavedChanges = false;
              hasUnsavedGlobalChanges = false;
              updateSaveBar();
              fetchStatus();
              loadHistory();
          } else if (res) {
              const data = await res.json().catch(() => ({}));
              if (data.needConfirm) {
                  if (!confirm(data.confirmText + '\n\n（保存后仍按列表顺序归属靠前的条目）')) {
                      showAlert('已取消保存，配置未写入。', false);
                      return;
                  }
                  configData.ConfirmDuplicates = true;
                  const retry = await postJson('/api/save', configData);
                  const retryData = retry ? await retry.json().catch(() => ({})) : {};
                  if (retry && retry.ok) {
                      configData.ConfirmDuplicates = false;
                      showAlert('已保存并应用' + (retryData.notice ? '；' + retryData.notice : ''), true);
                      isEditing = false;
                      hasUnsavedChanges = false;
                      hasUnsavedGlobalChanges = false;
                      updateSaveBar();
                      fetchStatus();
                      loadHistory();
                      return;
                  }
                  configData.ConfirmDuplicates = false;
                  showAlert(retryData.error || '保存失败。', false);
                  return;
              }
              showAlert(data.error || ('保存失败（HTTP ' + res.status + '）。'), false);
          }
      } catch (e) { showAlert('网络错误：' + e.message, false); }
  }

  async function pauseMonitor() {
      const el = document.getElementById('pauseMinutes');
      const raw = el && el.value ? el.value.trim() : '';
      const minutes = raw === '' ? 30 : parseInt(raw);
      if (!Number.isInteger(minutes) || minutes < 1 || minutes > 1440) {
          showAlert('请输入 1~1440 之间的暂停分钟数（留空按 30 分钟）。', false);
          return;
      }
      try {
          const res = await postJson('/api/pause', { Minutes: minutes });
          if (res && res.ok) {
              showAlert('已暂停监控 ' + minutes + ' 分钟。', true);
              fetchStatus();
          } else if (res) {
              const data = await res.json().catch(() => ({}));
              showAlert(data.error || '暂停失败。', false);
          }
      } catch (e) { showAlert('网络错误：' + e.message, false); }
  }

  async function resumeMonitor() {
      try {
          const res = await postJson('/api/resume', {});
          if (res && res.ok) {
              showAlert('已取消暂停。', true);
              fetchStatus();
          } else if (res) {
              const data = await res.json().catch(() => ({}));
              showAlert(data.error || '操作失败。', false);
          }
      } catch (e) { showAlert('网络错误：' + e.message, false); }
  }

  function readSelMinutes() {
      const el = document.getElementById('selMinutes');
      const raw = el && el.value ? el.value.trim() : '';
      const n = raw === '' ? 10 : parseInt(raw);
      if (!Number.isInteger(n) || n < 1 || n > 120) {
          showAlert('请输入 1~120 之间的分钟数（留空按 10 分钟）。', false);
          return null;
      }
      return n;
  }

  async function adjustSelectedBonus(sign) {
      if (selectedProcs.size === 0) {
          showAlert('请先在列表中点行或勾选条目，再执行加时/减时。', false);
          return;
      }
      const minutes = readSelMinutes();
      if (minutes === null) return;
      const action = sign > 0 ? '加时' : '减时';
      if (sign < 0 && !confirm('确定要为选中的 ' + selectedProcs.size + ' 个条目各减时 ' + minutes +
          ' 分钟吗？\n只影响选中条目本轮的单次可用时长；若减后低于本轮已用时长，该条目会被拒绝。')) return;

      const procs = Array.from(selectedProcs);
      let applied = 0;
      let firstError = '';
      let lastMessage = '';
      for (const proc of procs) {
          try {
              const res = await postJson('/api/bonus', { ProcessName: proc, Minutes: sign > 0 ? minutes : -minutes });
              const data = await res.json().catch(() => ({}));
              if (res && res.ok && data.success) {
                  applied++;
                  if (data.message) lastMessage = data.message;
              } else if (!firstError) {
                  firstError = data.error || ('HTTP ' + (res ? res.status : '?'));
              }
          } catch (e) {
              if (!firstError) firstError = '网络错误：' + e.message;
          }
      }

      if (applied > 0) {
          const summary = lastMessage || ('已对选中的 ' + applied + ' 个条目' + action + ' ' + minutes + ' 分钟。');
          showAlert(summary + (firstError ? '（另有 ' + (procs.length - applied) + ' 个未成功：' + firstError + '）' : ''), true);
          fetchStatus();
          loadHistory();
      } else {
          showAlert(firstError || (action + '失败。'), false);
      }
  }

  async function loadHistory() {
      if (!authed) return;
      try {
          const res = await fetch('/api/history?days=14', { headers: { 'X-Admin-Password': adminPass } });
          if (!res.ok) return;
          const data = await res.json();
          renderHistory(data || {});
      } catch (e) { }
  }

  // 明细表折叠：默认折叠，标题栏点击展开；状态只保存在当前页面会话内
  function toggleHistoryDetail() {
      const body = document.getElementById('historyDetailBody');
      const icon = document.getElementById('historyToggleIcon');
      const expanded = body.style.display !== 'none';
      body.style.display = expanded ? 'none' : 'block';
      icon.textContent = expanded ? '展开 ▾' : '收起 ▴';
  }

  function renderHistory(data) {
      const items = data.Items || [];
      const chart = document.getElementById('historyChart');
      const note = document.getElementById('hourlyNote');
      const tbody = document.getElementById('historyList');
      chart.textContent = '';
      tbody.textContent = '';

      note.style.display = data.HasHourlyData ? 'none' : 'block';

      const hours = Array.isArray(data.HourlyMinutes) && data.HourlyMinutes.length === 24
          ? data.HourlyMinutes : new Array(24).fill(0);
      const max = Math.max(1, ...hours.map(v => v || 0));
      hours.forEach((minutes, hour) => {
          // grid 每列 1fr，柱子随容器等分缩放；窄屏也不会横向溢出
          const wrap = document.createElement('div');
          wrap.style.cssText = 'display:flex; flex-direction:column; justify-content:flex-end; align-items:center; height:100%; min-width:0;';
          const bar = document.createElement('div');
          const h = Math.round(((minutes || 0) / max) * 88);
          bar.style.cssText = 'width:100%; max-width:100%; height:' + (minutes > 0 ? Math.max(2, h) : 0) + 'px; background:#4f46e5; border-radius:2px;';
          bar.title = hour + ' 时：' + (minutes || 0) + ' 分钟';
          const label = document.createElement('div');
          label.style.cssText = 'font-size:10px; color:#6b7280; margin-top:4px; line-height:1;';
          label.textContent = hour;
          wrap.appendChild(bar);
          wrap.appendChild(label);
          chart.appendChild(wrap);
      });

      items.forEach(item => {
          const tr = document.createElement('tr');
          const cells = [
              item.Date || '',
              item.FirstStartTime || '—',
              item.LastEndTime || '—',
              String(item.UsedMinutes || 0),
              String(item.LaunchCount || 0),
              String(item.BonusMinutes || 0),
              item.LastEndReason || '',
              item.WasInCooldown ? '是' : ''
          ];
          cells.forEach(text => {
              const td = document.createElement('td');
              td.textContent = text;
              tr.appendChild(td);
          });
          tbody.appendChild(tr);
      });
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
  updateSaveBar();
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
