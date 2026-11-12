using System.Text.RegularExpressions;
using System.Xml.Serialization;

namespace GameMonitor
{
    [Serializable]
    public class GameItem
    {
        public string DisplayName { get; set; } = "新游戏";
        /// <summary>条目稳定标识与显示用名称（历史字段名保留，避免破坏旧配置与使用记录）.</summary>
        public string ProcessName { get; set; } = "";

        /// <summary>
        /// 匹配关键字列表：按顺序逐个匹配，先命中即停，任一命中即视为运行中。
        /// 旧配置为空时由 ProcessName 迁移而来；保存后始终写出。
        /// </summary>
        public List<string> Keywords { get; set; } = new List<string>();

        /// <summary>规范化关键字（去首尾空白、去空项、忽略大小写去重）；为空时回退到 ProcessName 单关键字。</summary>
        [XmlIgnore]
        public List<string> KeywordList
        {
            get
            {
                var list = new List<string>();
                if (Keywords != null)
                {
                    foreach (var raw in Keywords)
                    {
                        string k = (raw ?? "").Trim();
                        if (k.Length == 0) continue;
                        if (list.Any(x => x.Equals(k, StringComparison.OrdinalIgnoreCase))) continue;
                        list.Add(k);
                    }
                }
                if (list.Count == 0)
                {
                    string legacy = (ProcessName ?? "").Trim();
                    if (legacy.Length > 0)
                    {
                        foreach (var part in AppConfig.SplitKeywords(legacy))
                            if (!list.Any(x => x.Equals(part, StringComparison.OrdinalIgnoreCase)))
                                list.Add(part);
                    }
                }
                return list;
            }
        }
        public int RunMinutes { get; set; } = 60;
        public int DailyLimitMinutes { get; set; } = 0;
        public bool Enabled { get; set; } = true;
        /// <summary>匹配方式，默认“包含”，兼容旧配置。</summary>
        public MatchMode MatchMode { get; set; } = MatchMode.Contains;

        /// <summary>该条目自己的间隔时间（休息期）分钟数；&lt;=0 表示旧配置未设置，Load 时按旧全局值或 10 兜底。</summary>
        public int CooldownMinutes { get; set; } = -1;

        /// <summary>周末倍数：周六/周日同时作用于该条目的单次时长与每日配额（0.5~3.0）。</summary>
        public double WeekendMultiplier { get; set; } = 1.0;

        /// <summary>
        /// 按日期记录的“条目级”临时加时（额外配额），与 AppConfig.BonusRecords（旧全局加时）区分。
        /// 条目当日限额 = 每日配额（含周末倍数）+ 该条目的当日加时。
        /// </summary>
        public List<BonusRecord> BonusRecords { get; set; } = new List<BonusRecord>();

        public override string ToString()
        {
            string quota = DailyLimitMinutes > 0 ? $"{DailyLimitMinutes} 分" : "沿用总限额";
            string keywords = string.Join("；", KeywordList);
            return $"{DisplayName}  (关键字:{keywords})  -  单次 {RunMinutes} 分 / 今日配额 {quota} / 休息 {CooldownMinutes} 分";
        }
    }

    [Serializable]
    public class GameUsage
    {
        public string ProcessName { get; set; } = "";
        public DateTime Date { get; set; } = DateTime.Today;
        public int UsedSeconds { get; set; } = 0;
    }

    /// <summary>间隔时间（休息期）的持久化状态：程序重启后仍能识别“还在休息”。</summary>
    [Serializable]
    public class CooldownState
    {
        public string ProcessName { get; set; } = "";
        public DateTime EndTime { get; set; } = DateTime.MinValue;
    }

    /// <summary>
    /// 单次会话累计（按条目名持久化）：进程关闭/程序重启后继续累计，直到清零时机到来。
    /// Date 记录“本次会话开始那天”，用于判断跨天首次启动时清零。
    /// </summary>
    [Serializable]
    public class SessionRecord
    {
        public string ProcessName { get; set; } = "";
        public DateTime Date { get; set; } = DateTime.Today;
        /// <summary>本次会话已累计秒数（跨进程/跨窗口同名条目只算一份）。</summary>
        public int UsedSeconds { get; set; }
        /// <summary>记录时的“单次设定”分钟数；设定被修改时据此清零。</summary>
        public int RunMinutes { get; set; }

        /// <summary>
        /// 本轮单次可用时长的加时秒数（可正可负：负数表示本轮上限被缩短）。
        /// 随会话记录持久化：进程关闭/程序重启后本轮加时仍生效；本轮结束时归零。
        /// </summary>
        public int SessionBonusSeconds { get; set; }
    }

    /// <summary>按日期记录的临时加时（额外配额），跨天自动失效。</summary>
    [Serializable]
    public class BonusRecord
    {
        public DateTime Date { get; set; } = DateTime.Today;
        public int Minutes { get; set; }
    }

    /// <summary>时段规则类型：开放时段 / 禁止时段。</summary>
    public enum ScheduleRuleKind
    {
        Allow = 0,
        Block = 1
    }

    /// <summary>
    /// 一条时段规则。StartTime/EndTime 为 "HH:mm:ss"；StartTime 晚于 EndTime 表示跨零点，
    /// 星期按“规则开始的那一天”计算（跨零点后半夜属于前一天规则的延续）。
    /// </summary>
    [Serializable]
    public class ScheduleRule
    {
        public bool Enabled { get; set; } = true;
        public ScheduleRuleKind Kind { get; set; } = ScheduleRuleKind.Block;
        public string StartTime { get; set; } = "00:00:00";
        public string EndTime { get; set; } = "00:00:00";
        /// <summary>7 个星期，索引与 DayOfWeek 一致：0=周日，1=周一 … 6=周六。</summary>
        public bool[] DaysOfWeek { get; set; } = new bool[7];

        public ScheduleRule Clone()
        {
            return new ScheduleRule
            {
                Enabled = Enabled,
                Kind = Kind,
                StartTime = StartTime,
                EndTime = EndTime,
                DaysOfWeek = DaysOfWeek == null ? new bool[7] : (bool[])DaysOfWeek.Clone()
            };
        }
    }

    /// <summary>某一类提示的文案模板与字体样式。</summary>
    [Serializable]
    public class PromptStyleConfig
    {
        /// <summary>文案模板，支持 {game} {minutes} {seconds} {remaining} {clock}。</summary>
        public string Template { get; set; } = "";

        public string FontFamily { get; set; } = "Microsoft YaHei";
        /// <summary>像素字号；0 表示按浮层尺寸自动缩放（仅倒计时数字使用）。</summary>
        public int FontSize { get; set; } = 34;
        public bool Bold { get; set; } = true;
        /// <summary>文字阴影（提示文字专用）。</summary>
        public bool Shadow { get; set; } = true;
        public int TextColorR { get; set; } = 255;
        public int TextColorG { get; set; } = 255;
        public int TextColorB { get; set; } = 0;
        /// <summary>字体色不透明度百分比 0~100。</summary>
        public int TextAlphaPercent { get; set; } = 90;
        /// <summary>提示文字整块底色（不是整屏背景）；四类提示与倒计时数字块共用这组配置。</summary>
        public int BackColorR { get; set; } = 0;
        public int BackColorG { get; set; } = 0;
        public int BackColorB { get; set; } = 0;
        /// <summary>文字块底色不透明度百分比 0~100（0 = 完全透明）。</summary>
        public int BackgroundAlphaPercent { get; set; } = 0;

        public PromptStyleConfig Clone()
        {
            return new PromptStyleConfig
            {
                Template = Template,
                FontFamily = FontFamily,
                FontSize = FontSize,
                Bold = Bold,
                Shadow = Shadow,
                TextColorR = TextColorR,
                TextColorG = TextColorG,
                TextColorB = TextColorB,
                TextAlphaPercent = TextAlphaPercent,
                BackColorR = BackColorR,
                BackColorG = BackColorG,
                BackColorB = BackColorB,
                BackgroundAlphaPercent = BackgroundAlphaPercent
            };
        }
    }

    /// <summary>四类提示的样式集合；旧配置缺失时按默认值工作。</summary>
    [Serializable]
    public class OverlayStyleConfig
    {
        public PromptStyleConfig Reminding { get; set; } = CreateReminding();
        public PromptStyleConfig CountingDown { get; set; } = CreateCountdown();
        public PromptStyleConfig Cooldown { get; set; } = CreateCooldown();
        public PromptStyleConfig QuotaExhausted { get; set; } = CreateQuota();
        /// <summary>命中禁止时段、被强制关闭时的提示样式（旧配置缺失时补默认）。</summary>
        public PromptStyleConfig BlockedBySchedule { get; set; } = CreateBlockedBySchedule();

        public static PromptStyleConfig CreateReminding() => new PromptStyleConfig
        {
            Template = "「{game}」请在 {minutes} 分钟内退出",
            FontFamily = "Microsoft YaHei",
            FontSize = 34,
            Bold = true,
            Shadow = true,
            TextColorR = 255,
            TextColorG = 255,
            TextColorB = 0,
            TextAlphaPercent = 90,
            BackColorR = 0,
            BackColorG = 0,
            BackColorB = 0,
            BackgroundAlphaPercent = 0
        };

        public static PromptStyleConfig CreateCountdown() => new PromptStyleConfig
        {
            Template = "{seconds}",
            FontFamily = "Microsoft YaHei",
            FontSize = 0, // 自动缩放
            Bold = true,
            Shadow = false,
            TextColorR = 255,
            TextColorG = 30,
            TextColorB = 30,
            TextAlphaPercent = 80,
            BackColorR = 0,
            BackColorG = 0,
            BackColorB = 0,
            BackgroundAlphaPercent = 0
        };

        public static PromptStyleConfig CreateCooldown() => new PromptStyleConfig
        {
            Template = "「{game}」还需休息 {minutes} 分钟（{time} 才可以运行哦）",
            FontFamily = "Microsoft YaHei",
            FontSize = 34,
            Bold = true,
            Shadow = false,
            TextColorR = 255,
            TextColorG = 128,
            TextColorB = 0,
            TextAlphaPercent = 80,
            BackColorR = 0,
            BackColorG = 0,
            BackColorB = 0,
            BackgroundAlphaPercent = 0
        };

        /// <summary>上一版休息提示的默认文案（{clock} 是剩余时长）；仅用于把“默认文案”升级为 {time}。</summary>
        public const string LegacyCooldownTemplate = "「{game}」还需休息 {minutes} 分钟（{clock} 后可再次开启）";

        public static PromptStyleConfig CreateQuota() => new PromptStyleConfig
        {
            Template = "「{game}」今日已无法启动，明天 0 点自动重置",
            FontFamily = "Microsoft YaHei",
            FontSize = 34,
            Bold = true,
            Shadow = false,
            TextColorR = 128,
            TextColorG = 0,
            TextColorB = 255,
            TextAlphaPercent = 80,
            BackColorR = 0,
            BackColorG = 0,
            BackColorB = 0,
            BackgroundAlphaPercent = 0
        };

        public static PromptStyleConfig CreateBlockedBySchedule() => new PromptStyleConfig
        {
            Template = "「{game}」当前处于禁止时段，已强制关闭",
            FontFamily = "Microsoft YaHei",
            FontSize = 34,
            Bold = true,
            Shadow = false,
            TextColorR = 255,
            TextColorG = 0,
            TextColorB = 0,
            TextAlphaPercent = 80,
            BackColorR = 0,
            BackColorG = 0,
            BackColorB = 0,
            BackgroundAlphaPercent = 0
        };
    }

    /// <summary>单个 RGB 颜色（各分量 0~255）。</summary>
    [Serializable]
    public class ColorSetting
    {
        public int R { get; set; }
        public int G { get; set; }
        public int B { get; set; }

        public ColorSetting() { }

        public ColorSetting(int r, int g, int b)
        {
            R = r;
            G = g;
            B = b;
        }
    }

    /// <summary>悬浮窗状态圆点的逐状态颜色。</summary>
    [Serializable]
    public class DotColorConfig
    {
        public ColorSetting Idle { get; set; } = new ColorSetting(154, 163, 175);
        public ColorSetting Running { get; set; } = new ColorSetting(34, 197, 94);
        public ColorSetting Reminding { get; set; } = new ColorSetting(245, 158, 11);
        public ColorSetting Cooldown { get; set; } = new ColorSetting(255, 193, 7);
        public ColorSetting Quota { get; set; } = new ColorSetting(239, 68, 68);
    }

    [Serializable]
    public class AppConfig
    {
        public List<GameItem> Games { get; set; } = new List<GameItem>();
        public List<GameUsage> Usage { get; set; } = new List<GameUsage>();

        /// <summary>旧版全局间隔时间。只读入、不再写出：旧配置里存在时作为每个条目的缺省值。</summary>
        [XmlElement("CooldownMinutes")]
        public int LegacyCooldownMinutes { get; set; } = -1;

        public bool ShouldSerializeLegacyCooldownMinutes() => false;

        /// <summary>间隔期状态（与进程名对应），用于重启后继续生效。</summary>
        public List<CooldownState> Cooldowns { get; set; } = new List<CooldownState>();

        /// <summary>暂停监控的截止时间（为空/过去表示未暂停）。持久化，重启不可绕过。</summary>
        public DateTime PauseUntil { get; set; } = DateTime.MinValue;

        /// <summary>
        /// 暂停监控的默认时长（分钟），设置页输入框与托盘菜单共用。
        /// 老配置没有该字段时反序列化保留此默认值 30，与旧版菜单/设置页写死的 30 分钟一致，
        /// 避免老用户升级后输入框变成 0 导致暂停无效。
        /// </summary>
        public int PauseMinutes { get; set; } = 30;

        /// <summary>按日期记录的临时加时分钟数（旧全局字段：保留兼容读取，UI 不再写入）。</summary>
        public List<BonusRecord> BonusRecords { get; set; } = new List<BonusRecord>();

        /// <summary>
        /// 单次会话累计记录（按条目名 + 会话开始日期）：进程关闭、程序重启后继续累计，
        /// 只有间隔期结束、跨天首次启动、手动重置今日用量、修改单次设定时才清零。
        /// </summary>
        public List<SessionRecord> SessionRecords { get; set; } = new List<SessionRecord>();

        /// <summary>时段规则（开放/禁止）；旧配置没有该字段时为空列表，表示不启用时段管控。</summary>
        public List<ScheduleRule> ScheduleRules { get; set; } = new List<ScheduleRule>();

        /// <summary>旧全局加时是否已迁移为条目级加时，避免重复迁移。</summary>
        public bool BonusMigratedToEntries { get; set; } = false;

        /// <summary>每日总限额（分钟，跨条目的当日已用时间之和）；0=不启用。</summary>
        public int DailyTotalLimitMinutes { get; set; } = 0;

        // ===== 提醒声音（按事件开关） =====
        public bool SoundOnGrace { get; set; } = true;
        public bool SoundOnCountdown { get; set; } = true;
        public bool SoundOnCooldown { get; set; } = true;
        /// <summary>配额用完提示音；老配置缺字段时默认开启，与其它提示音一致。</summary>
        public bool SoundOnQuota { get; set; } = true;
        /// <summary>禁止时段提示音；老配置缺字段时默认开启，与其它提示音一致。</summary>
        public bool SoundOnBlockedBySchedule { get; set; } = true;
        /// <summary>自定义音频文件（.wav）；空字符串表示使用系统提示音。</summary>
        public string SoundGraceFile { get; set; } = "";
        public string SoundCountdownFile { get; set; } = "";
        public string SoundCooldownFile { get; set; } = "";
        public string SoundQuotaFile { get; set; } = "";
        public string SoundBlockedByScheduleFile { get; set; } = "";

        /// <summary>四类提示的文案与字体样式；旧配置缺失时为 null，Load 时补默认值。</summary>
        public OverlayStyleConfig OverlayStyle { get; set; }

        // ===== 周配额已移除：改为每个条目的周末倍数 =====

        public int ReminderMinutes { get; set; } = 5;
        public int CountdownSeconds { get; set; } = 60;
        /// <summary>提示文字在屏幕上的停留秒数（宽限/休息期/配额用完/禁止时段提示）；1~60，默认 5。</summary>
        public int NoticeStaySeconds { get; set; } = 5;
        public uint HotkeyModifiers { get; set; } = 0x0003;
        public int HotkeyVk { get; set; } = 0x47;
        public bool StartWithWindows { get; set; } = false;
        public bool HideTrayIcon { get; set; } = false;
        public bool MouseTransparent { get; set; } = false;
        public bool EnableDaemon { get; set; } = false;

        // 管理密码：内存中始终是明文（比较、提示都用它）；写入 config 时用 DPAPI 加密。
        // XML 元素名保持 AdminPassword，旧配置的明文能被原样读入后再迁移加密。
        private string _adminPassword = "";

        [XmlElement("AdminPassword")]
        public string AdminPasswordStored { get; set; } = "";

        [XmlIgnore]
        public string AdminPassword
        {
            get { return _adminPassword; }
            set
            {
                _adminPassword = value ?? "";
                AdminPasswordStored = PasswordProtector.Protect(_adminPassword);
            }
        }

        // ===== 远程调参 =====
        public bool EnableRemote { get; set; } = false;
        public int RemotePort { get; set; } = 8080;
        public bool RemoteAllowLan { get; set; } = false;

        // ===== 桌面悬浮框 =====
        public bool StatusOverlayEnabled { get; set; } = true;
        public int StatusOverlayX { get; set; } = -9999; // 改为 -9999 防止负坐标屏幕误判
        public int StatusOverlayY { get; set; } = -9999;
        public int StatusOverlayOpacity { get; set; } = 200;
        /// <summary>悬浮窗整体不透明度百分比 0~100；旧配置缺失时由 StatusOverlayOpacity 迁移。</summary>
        public int StatusOverlayOpacityPercent { get; set; } = -1;
        public bool ShouldSerializeStatusOverlayOpacity() => false;
        /// <summary>悬浮窗状态圆点的逐状态颜色。</summary>
        public DotColorConfig DotColors { get; set; } = new DotColorConfig();
        public bool StatusOverlaySnap { get; set; } = true;
        public bool StatusOverlayAutoHide { get; set; } = false;
        public int StatusOverlayAutoHideSeconds { get; set; } = 5;
        public bool StatusOverlayTopMost { get; set; } = true;
        /// <summary>贴边隐藏：悬浮窗贴住屏幕边缘且无操作时，只保留 3px 窄条。</summary>
        public bool StatusOverlayEdgeHide { get; set; } = false;
        /// <summary>贴边隐藏的独立延迟（秒）；老配置缺字段时默认 5，与“无操作自动淡显”一致。</summary>
        public int StatusOverlayEdgeHideSeconds { get; set; } = 5;
        /// <summary>悬浮窗顶部说明文字模板，支持 {game} {used} {limit}；留空使用默认“今日游戏时长”。</summary>
        public string StatusOverlayTitleText { get; set; } = "";
        /// <summary>
        /// 悬浮窗圆角半径（逻辑像素）：0=方角、4=小圆角、8=中圆角、14=大圆角。
        /// 默认 0，与旧版实际观感一致（旧版整块矩形填色，四角为方角）；
        /// 老配置没有该字段时保留此默认值，不会因 0 以外的脏值把悬浮窗画成圆形或不可见。
        /// </summary>
        public int StatusOverlayCornerRadius { get; set; } = 0;
        /// <summary>
        /// 悬浮窗水平吸附边：0=自由（不吸附）、1=左、2=右。
        /// 老配置没有该字段时保持 0，加载后由当前坐标按原有贴边阈值判定一次并回写，
        /// 因此历史位置不会被强行拽到屏幕另一侧。
        /// </summary>
        public int StatusOverlayAnchorX { get; set; } = 0;
        /// <summary>悬浮窗垂直吸附边：0=自由、1=上、2=下。</summary>
        public int StatusOverlayAnchorY { get; set; } = 0;
        /// <summary>
        /// 吸附偏移（像素）：与 StatusOverlayAnchorX 配合，右侧吸附时表示“工作区右边界到窗口右边缘”的距离。
        /// 只在对应的吸附边不为 0 时有效。
        /// </summary>
        public int StatusOverlayOffsetX { get; set; } = 0;
        /// <summary>吸附偏移（像素）：与 StatusOverlayAnchorY 配合，下方吸附时表示“工作区下边界到窗口下边缘”的距离。</summary>
        public int StatusOverlayOffsetY { get; set; } = 0;

        // ===== 倒计时字体 =====
        // 旧版全局倒计时样式字段：只读取以兼容旧配置，不再回写（样式已统一到 OverlayStyle.*）。
        public string CountdownFontFamily { get; set; } = "Microsoft YaHei";
        public int CountdownFontColorR { get; set; } = 255;
        public int CountdownFontColorG { get; set; } = 30;
        public int CountdownFontColorB { get; set; } = 30;
        public int CountdownFontAlpha { get; set; } = 235;
        public int CountdownHeightPercent { get; set; } = 75;
        public int CountdownWidthPercent { get; set; } = 90;
        public bool CountdownShadow { get; set; } = true;

        // ===== 倒计时背景 =====
        public int CountdownBackColorR { get; set; } = 0;
        public int CountdownBackColorG { get; set; } = 0;
        public int CountdownBackColorB { get; set; } = 0;
        public int CountdownBackAlpha { get; set; } = 0;

        public bool ShouldSerializeCountdownFontFamily() => false;
        public bool ShouldSerializeCountdownFontColorR() => false;
        public bool ShouldSerializeCountdownFontColorG() => false;
        public bool ShouldSerializeCountdownFontColorB() => false;
        public bool ShouldSerializeCountdownFontAlpha() => false;
        public bool ShouldSerializeCountdownShadow() => false;
        public bool ShouldSerializeCountdownBackColorR() => false;
        public bool ShouldSerializeCountdownBackColorG() => false;
        public bool ShouldSerializeCountdownBackColorB() => false;
        public bool ShouldSerializeCountdownBackAlpha() => false;

        // ===== 悬浮框字体 =====
        public string StatusOverlayFontFamily { get; set; } = "Microsoft YaHei";
        public int StatusOverlayFontSize { get; set; } = 10;

        // ===== 悬浮框文字颜色 =====
        public int StatusOverlayTextColorR { get; set; } = 210;
        public int StatusOverlayTextColorG { get; set; } = 210;
        public int StatusOverlayTextColorB { get; set; } = 210;

        // ===== 悬浮框背景颜色 =====
        public int StatusOverlayBackColorR { get; set; } = 32;
        public int StatusOverlayBackColorG { get; set; } = 32;
        public int StatusOverlayBackColorB { get; set; } = 32;

        /// <summary>
        /// 配置读写锁。远程线程不再直接改配置，但仍然统一用它保护
        /// “加载 / 保存 / 跨线程读改写”这类临界区。
        /// </summary>
        [XmlIgnore]
        public object SyncRoot { get; } = new object();

        private static string ConfigPath
            => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.xml");

        private static readonly object IoLock = new object();

        public static AppConfig Load()
        {
            lock (IoLock)
            {
                if (!File.Exists(ConfigPath))
                    return Prepare(new AppConfig(), true); // 首次运行也要归一化，否则 OverlayStyle/DotColors 为 null

                try
                {
                    using (var fs = File.OpenRead(ConfigPath))
                    {
                        var ser = new XmlSerializer(typeof(AppConfig));
                        var cfg = ser.Deserialize(fs) as AppConfig;
                        if (cfg != null)
                        {
                            return Prepare(cfg);
                        }
                    }
                    throw new InvalidDataException("配置文件内容为空或格式不正确");
                }
                catch (Exception ex)
                {
                    AppConfig repaired;
                    string detail;
                    if (TryLoadSanitized(out repaired, out detail))
                    {
                        AppLog.Warn($"配置含非法值（{detail}），已按默认值修正后加载。");
                        return repaired;
                    }

                    // 不再静默回落默认值：把坏文件改名保留证据，并写日志
                    string corruptPath = BackupCorruptConfig();
                    AppLog.Error($"读取配置失败，已备份为 {corruptPath}", ex);
                    return new AppConfig();
                }
            }
        }

        /// <summary>反序列化后的统一兜底与迁移。isNew=true 表示没有历史配置，不做旧字段迁移。</summary>
        private static AppConfig Prepare(AppConfig cfg, bool isNew = false)
        {
            if (cfg.Games == null) cfg.Games = new List<GameItem>();
            if (cfg.Usage == null) cfg.Usage = new List<GameUsage>();
            if (cfg.Cooldowns == null) cfg.Cooldowns = new List<CooldownState>();
            if (cfg.BonusRecords == null) cfg.BonusRecords = new List<BonusRecord>();
            if (cfg.SessionRecords == null) cfg.SessionRecords = new List<SessionRecord>();
            if (cfg.ScheduleRules == null) cfg.ScheduleRules = new List<ScheduleRule>();
            if (cfg.SoundGraceFile == null) cfg.SoundGraceFile = "";
            if (cfg.SoundCountdownFile == null) cfg.SoundCountdownFile = "";
            if (cfg.SoundCooldownFile == null) cfg.SoundCooldownFile = "";
            if (cfg.SoundQuotaFile == null) cfg.SoundQuotaFile = "";
            if (cfg.SoundBlockedByScheduleFile == null) cfg.SoundBlockedByScheduleFile = "";
            cfg.NormalizeStyle(isNew);
            cfg.NormalizeGames();
            cfg.NormalizeScheduleRules();
            cfg.MigrateGlobalBonusToEntries();
            cfg.PruneSessionRecords();
            if (cfg.DailyTotalLimitMinutes < 0) cfg.DailyTotalLimitMinutes = 0;
            if (cfg.DailyTotalLimitMinutes > 1440) cfg.DailyTotalLimitMinutes = 1440;
            if (cfg.NoticeStaySeconds <= 0) cfg.NoticeStaySeconds = 5;
            cfg.NoticeStaySeconds = Math.Max(1, Math.Min(60, cfg.NoticeStaySeconds));
            // 暂停时长：老配置缺字段时保留默认 30；脏数据夹到 1~1440，杜绝 0 分钟导致暂停无效
            if (cfg.PauseMinutes <= 0) cfg.PauseMinutes = 30;
            cfg.PauseMinutes = Math.Max(1, Math.Min(1440, cfg.PauseMinutes));
            cfg.RestorePassword();
            return cfg;
        }

        /// <summary>旧配置可能出现 "True"/空布尔或空数值，反序列化会整体失败；这里先修正已知字段再重试一次。</summary>
        private static bool TryLoadSanitized(out AppConfig cfg, out string detail)
        {
            cfg = null;
            detail = "";
            try
            {
                string raw = File.ReadAllText(ConfigPath);
                var notes = new List<string>();
                string fixedXml = SanitizeXml(raw, notes);
                if (notes.Count == 0) return false;

                var ser = new XmlSerializer(typeof(AppConfig));
                using (var sr = new StringReader(fixedXml))
                {
                    cfg = ser.Deserialize(sr) as AppConfig;
                }
                if (cfg == null) return false;
                cfg = Prepare(cfg);
                detail = string.Join("；", notes.Take(8));
                return true;
            }
            catch
            {
                cfg = null;
                return false;
            }
        }

        private static string SanitizeXml(string xml, List<string> notes)
        {
            // 布尔：空节点 / True / False / 1 / 0 / 其它非法文本
            xml = Regex.Replace(xml, @"<Enabled\s*/>|<Enabled>\s*</Enabled>", m =>
            {
                notes.Add("Enabled 空值→true");
                return "<Enabled>true</Enabled>";
            });
            xml = Regex.Replace(xml, @"<Enabled>([^<]*)</Enabled>", m =>
            {
                string v = m.Groups[1].Value.Trim();
                string canonical = null;
                if (v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1")
                    canonical = "<Enabled>true</Enabled>";
                else if (v.Equals("false", StringComparison.OrdinalIgnoreCase) || v == "0")
                    canonical = "<Enabled>false</Enabled>";

                if (canonical == null)
                {
                    notes.Add($"Enabled \"{v}\"→true");
                    return "<Enabled>true</Enabled>";
                }

                // 文本合法但不是规范写法（如 True/0）也要记录，否则会被误判为“无需修正”而放弃整份配置
                if (!string.Equals(m.Value, canonical, StringComparison.Ordinal))
                    notes.Add($"Enabled \"{v}\"→规范化");
                return canonical;
            });

            // 整数：空节点或非数字 → 删除节点走默认值
            string[] ints = { "RunMinutes", "DailyLimitMinutes", "CooldownMinutes", "ReminderMinutes",
                              "CountdownSeconds", "RemotePort", "HotkeyVk", "HotkeyModifiers", "PauseMinutes",
                              "StatusOverlayOpacityPercent", "StatusOverlayCornerRadius", "TextAlphaPercent",
                              "StatusOverlayAnchorX", "StatusOverlayAnchorY",
                              "StatusOverlayOffsetX", "StatusOverlayOffsetY",
                              "BackColorR", "BackColorG", "BackColorB" };
            foreach (string name in ints)
            {
                string n = Regex.Escape(name);
                xml = Regex.Replace(xml, $@"<{n}\s*/>|<{n}>\s*</{n}>", m =>
                {
                    notes.Add($"{name} 空值→默认");
                    return "";
                });
                xml = Regex.Replace(xml, $@"<{n}>([^<]*)</{n}>", m =>
                {
                    if (int.TryParse(m.Groups[1].Value.Trim(), out _)) return m.Value;
                    notes.Add($"{name} \"{m.Groups[1].Value.Trim()}\"→默认");
                    return "";
                });
            }

            // 浮点：空节点或非数字 → 删除节点走默认值
            xml = Regex.Replace(xml, @"<WeekendMultiplier\s*/>|<WeekendMultiplier>\s*</WeekendMultiplier>", m =>
            {
                notes.Add("WeekendMultiplier 空值→默认");
                return "";
            });
            xml = Regex.Replace(xml, @"<WeekendMultiplier>([^<]*)</WeekendMultiplier>", m =>
            {
                if (double.TryParse(m.Groups[1].Value.Trim(), out _)) return m.Value;
                notes.Add($"WeekendMultiplier \"{m.Groups[1].Value.Trim()}\"→默认");
                return "";
            });

            return xml;
        }

        /// <summary>旧配置可能没有 OverlayStyle；缺失时用旧倒计时字体字段迁移，其余用默认模板。</summary>
        private void NormalizeStyle(bool isNew = false)
        {
            if (OverlayStyle == null)
            {
                OverlayStyle = new OverlayStyleConfig();

                // 首次运行没有历史配置：直接用内置默认样式，不套用旧倒计时字段
                if (!isNew)
                {
                    var cd = OverlayStyle.CountingDown;
                    if (!string.IsNullOrWhiteSpace(CountdownFontFamily)) cd.FontFamily = CountdownFontFamily.Trim();
                    cd.TextColorR = CountdownFontColorR;
                    cd.TextColorG = CountdownFontColorG;
                    cd.TextColorB = CountdownFontColorB;
                    cd.BackgroundAlphaPercent = (int)Math.Round(Math.Max(0, Math.Min(255, CountdownBackAlpha)) / 255.0 * 100);
                    cd.TextAlphaPercent = (int)Math.Round(Math.Max(0, Math.Min(255, CountdownFontAlpha)) / 255.0 * 100);
                    cd.Shadow = CountdownShadow;
                    cd.BackColorR = CountdownBackColorR;
                    cd.BackColorG = CountdownBackColorG;
                    cd.BackColorB = CountdownBackColorB;
                    AppLog.Info("旧配置缺少提示样式，已按默认模板与旧倒计时字体迁移。");
                }
            }

            if (OverlayStyle.Reminding == null) OverlayStyle.Reminding = OverlayStyleConfig.CreateReminding();
            if (OverlayStyle.CountingDown == null) OverlayStyle.CountingDown = OverlayStyleConfig.CreateCountdown();
            if (OverlayStyle.Cooldown == null) OverlayStyle.Cooldown = OverlayStyleConfig.CreateCooldown();
            if (OverlayStyle.QuotaExhausted == null) OverlayStyle.QuotaExhausted = OverlayStyleConfig.CreateQuota();
            if (OverlayStyle.BlockedBySchedule == null) OverlayStyle.BlockedBySchedule = OverlayStyleConfig.CreateBlockedBySchedule();

            NormalizePrompt(OverlayStyle.Reminding, OverlayStyleConfig.CreateReminding().Template);
            NormalizePrompt(OverlayStyle.CountingDown, OverlayStyleConfig.CreateCountdown().Template);
            NormalizePrompt(OverlayStyle.Cooldown, OverlayStyleConfig.CreateCooldown().Template);
            NormalizePrompt(OverlayStyle.QuotaExhausted, OverlayStyleConfig.CreateQuota().Template);
            NormalizePrompt(OverlayStyle.BlockedBySchedule, OverlayStyleConfig.CreateBlockedBySchedule().Template);

            // 旧默认休息文案用的是“剩余时长”{clock}；原样保留会让老用户看不到绝对时刻，这里只升级“仍是默认文案”的配置，
            // 用户自己改过的模板一律不动（{clock}/{minutes}/{remaining} 占位符仍按原语义工作）。
            if (string.Equals((OverlayStyle.Cooldown.Template ?? "").Trim(),
                    OverlayStyleConfig.LegacyCooldownTemplate, StringComparison.Ordinal))
            {
                OverlayStyle.Cooldown.Template = OverlayStyleConfig.CreateCooldown().Template;
                AppLog.Info("旧默认休息提示文案已升级：括号内改为显示可再次运行的绝对时刻（{time}）。");
            }

            NormalizeOverlay();
        }

        /// <summary>时段规则兜底：空列表保持为空；脏数据（null 项、非法时间、星期长度）就地修正，不报错。</summary>
        public void NormalizeScheduleRules()
        {
            if (ScheduleRules == null) ScheduleRules = new List<ScheduleRule>();
            ScheduleRules.RemoveAll(r => r == null);
            foreach (var rule in ScheduleRules)
            {
                if (rule.DaysOfWeek == null || rule.DaysOfWeek.Length != 7)
                {
                    bool[] days = new bool[7];
                    if (rule.DaysOfWeek != null)
                    {
                        int copy = Math.Min(7, rule.DaysOfWeek.Length);
                        for (int i = 0; i < copy; i++) days[i] = rule.DaysOfWeek[i];
                    }
                    rule.DaysOfWeek = days;
                }

                if (!global::GameMonitor.ScheduleRules.IsValidTime(rule.StartTime)) rule.StartTime = "00:00:00";
                if (!global::GameMonitor.ScheduleRules.IsValidTime(rule.EndTime)) rule.EndTime = "00:00:00";
                rule.StartTime = global::GameMonitor.ScheduleRules.NormalizeTime(rule.StartTime);
                rule.EndTime = global::GameMonitor.ScheduleRules.NormalizeTime(rule.EndTime);
            }
        }

        /// <summary>悬浮窗整体透明度与圆点颜色的兜底、旧字段迁移。</summary>
        private void NormalizeOverlay()
        {
            if (DotColors == null) DotColors = new DotColorConfig();
            NormalizeDotColor(DotColors.Idle);
            NormalizeDotColor(DotColors.Running);
            NormalizeDotColor(DotColors.Reminding);
            NormalizeDotColor(DotColors.Cooldown);
            NormalizeDotColor(DotColors.Quota);

            if (StatusOverlayOpacityPercent < 0)
                StatusOverlayOpacityPercent = (int)Math.Round(
                    Math.Max(0, Math.Min(255, StatusOverlayOpacity)) / 255.0 * 100);
            StatusOverlayOpacityPercent = Math.Max(0, Math.Min(100, StatusOverlayOpacityPercent));
            // 圆角只接受设置页四档 0/4/8/14；其他值取最接近的一档，避免脏配置把窗口画圆或画废
            StatusOverlayCornerRadius = NormalizeCornerRadius(StatusOverlayCornerRadius);
        }

        private static void NormalizeDotColor(ColorSetting color)
        {
            if (color == null) return;
            color.R = Math.Max(0, Math.Min(255, color.R));
            color.G = Math.Max(0, Math.Min(255, color.G));
            color.B = Math.Max(0, Math.Min(255, color.B));
        }

        /// <summary>
        /// 悬浮窗圆角四档（逻辑像素）：0 方角 / 4 小 / 8 中 / 14 大。
        /// 4 用于轻微柔化，8 接近 Win11 卡片圆角，14 明显圆润但仍是矩形；上限由绘制端按“短边一半”收敛，
        /// 所以贴边隐藏的窄条也不会被画成胶囊或消失。设置页与配置校验共用这份档位。
        /// </summary>
        public static readonly int[] CornerRadiusOptions = { 0, 4, 8, 14 };

        private static int NormalizeCornerRadius(int radius)
        {
            int best = 0;
            int bestDiff = int.MaxValue;
            foreach (int option in CornerRadiusOptions)
            {
                int diff = Math.Abs(option - radius);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    best = option;
                }
            }
            return best;
        }

        /// <summary>
        /// 模板空值统一回退到该类型默认文案；旧标记 TemplateExplicitlyEmpty 只读兼容，不再生效。
        /// </summary>
        private static void NormalizePrompt(PromptStyleConfig style, string defaultTemplate)
        {
            if (string.IsNullOrWhiteSpace(style.FontFamily)) style.FontFamily = "Microsoft YaHei";
            style.FontSize = Math.Max(0, Math.Min(200, style.FontSize));
            style.TextColorR = Math.Max(0, Math.Min(255, style.TextColorR));
            style.TextColorG = Math.Max(0, Math.Min(255, style.TextColorG));
            style.TextColorB = Math.Max(0, Math.Min(255, style.TextColorB));
            style.TextAlphaPercent = Math.Max(0, Math.Min(100, style.TextAlphaPercent));
            style.BackColorR = Math.Max(0, Math.Min(255, style.BackColorR));
            style.BackColorG = Math.Max(0, Math.Min(255, style.BackColorG));
            style.BackColorB = Math.Max(0, Math.Min(255, style.BackColorB));
            style.BackgroundAlphaPercent = Math.Max(0, Math.Min(100, style.BackgroundAlphaPercent));

            if (string.IsNullOrWhiteSpace(style.Template))
                style.Template = defaultTemplate ?? "";
        }

        /// <summary>旧配置没有条目级间隔时间/周末倍数时补默认值，不报错。</summary>
        private void NormalizeGames()
        {
            int legacyCooldown = LegacyCooldownMinutes > 0
                ? Math.Max(1, Math.Min(180, LegacyCooldownMinutes))
                : 10;

            foreach (var g in Games)
            {
                if (g == null) continue;
                if (g.Keywords == null) g.Keywords = new List<string>();
                // 旧配置只写了单关键字（ProcessName）→ 迁移为 Keywords=[ProcessName]；
                // 已有的多关键字配置保持原顺序不动。
                if (g.Keywords.Count == 0)
                {
                    var legacy = SplitKeywords(g.ProcessName ?? "");
                    g.Keywords.AddRange(legacy);
                }
                if (g.CooldownMinutes <= 0) g.CooldownMinutes = legacyCooldown;
                else g.CooldownMinutes = Math.Max(1, Math.Min(180, g.CooldownMinutes));

                if (double.IsNaN(g.WeekendMultiplier) || double.IsInfinity(g.WeekendMultiplier) ||
                    g.WeekendMultiplier < 0.5 || g.WeekendMultiplier > 3.0)
                    g.WeekendMultiplier = 1.0;
            }
        }

        /// <summary>内存里的配置已被改动、还没落盘（供 5 分钟定时器判断是否需要写）。</summary>
        private volatile bool _dirty;

        /// <summary>标记配置内容已变化，等待定时落盘或下一次显式保存。</summary>
        public void MarkDirty() => _dirty = true;

        /// <summary>是否还有未落盘的改动。</summary>
        public bool IsDirty => _dirty;

        /// <summary>
        /// 定时落盘用：只有内容真正变化过才写盘，空转期间完全不碰磁盘。
        /// </summary>
        public void SaveIfDirty()
        {
            if (!_dirty) return;
            SaveQuiet();
        }

        /// <summary>显式保存：失败时弹窗提示（设置页保存时用）。</summary>
        public void Save() => SaveCore(true);

        /// <summary>静默保存：失败只写日志（定时落盘/后台线程用）。</summary>
        public void SaveQuiet() => SaveCore(false);

        private void SaveCore(bool notifyOnError)
        {
            lock (IoLock)
            {
                _dirty = false;
                try
                {
                    WriteAtomically();
                }
                catch (Exception ex)
                {
                    _dirty = true; // 写失败保留脏标记，下次定时器或退出前重试
                    AppLog.Error("保存配置失败", ex);
                    if (notifyOnError)
                        MessageBox.Show("保存配置失败：" + ex.Message, "游戏监控",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void WriteAtomically()
        {
            string tmpPath = ConfigPath + ".tmp";
            using (var fs = File.Create(tmpPath))
                new XmlSerializer(typeof(AppConfig)).Serialize(fs, this);

            if (File.Exists(ConfigPath))
                File.Replace(tmpPath, ConfigPath, ConfigPath + ".bak", true);
            else
                File.Move(tmpPath, ConfigPath);
        }

        /// <summary>把配置里（可能是加密的）密码还原成内存明文；旧明文会顺便迁移为 DPAPI 加密。</summary>
        private void RestorePassword()
        {
            _adminPassword = "";

            string stored = AdminPasswordStored;
            if (string.IsNullOrEmpty(stored)) return;

            if (!PasswordProtector.TryUnprotect(stored, out string plain, out bool wasPlaintext))
            {
                AppLog.Warn("管理密码无法解密（可能换了 Windows 用户或机器），已按空密码处理。");
                return;
            }

            _adminPassword = plain ?? "";
            if (wasPlaintext)
            {
                // 旧配置是明文：立即在内存里换成密文，下次保存自动落盘
                AdminPasswordStored = PasswordProtector.Protect(_adminPassword);
                AppLog.Info("检测到明文管理密码，已迁移为 DPAPI 加密（下次保存写入 config.xml）。");
            }
        }

        /// <summary>指定日期的额外加时分钟数（按日期记录，跨天自然失效）。</summary>
        public int BonusMinutesFor(DateTime date)
        {
            if (BonusRecords == null) return 0;
            int sum = 0;
            foreach (var b in BonusRecords)
            {
                if (b != null && b.Date.Date == date.Date)
                    sum += Math.Max(0, b.Minutes);
            }
            return sum;
        }

        /// <summary>给今日增加额外配额，返回今日加时后的总额。</summary>
        public int AddBonus(int minutes)
        {
            if (minutes <= 0) return BonusMinutesFor(DateTime.Today);
            if (BonusRecords == null) BonusRecords = new List<BonusRecord>();

            var today = DateTime.Today;
            BonusRecords.RemoveAll(b => b == null || b.Date.Date != today);

            var rec = BonusRecords.FirstOrDefault(b => b.Date.Date == today);
            if (rec == null)
            {
                rec = new BonusRecord { Date = today, Minutes = 0 };
                BonusRecords.Add(rec);
            }
            rec.Minutes += minutes;
            return rec.Minutes;
        }

        /// <summary>
        /// 旧“条目级每日加时”一次性折算：剩余总量并入今日总限额（AppConfig.BonusRecords），
        /// 清空条目级 BonusRecords。之后加时只走本轮 SessionBonusSeconds，避免旧每日加时与新本轮加时双重宽松。
        /// 幂等：条目级记录清空后再调用返回 0。返回本次折算的分钟数。
        /// </summary>
        public int MigrateLegacyEntryBonusToTotalLimit()
        {
            int legacy = 0;
            foreach (var g in Games)
            {
                if (g == null || g.BonusRecords == null) continue;
                legacy += EntryBonusMinutesFor(g, DateTime.Today);
                g.BonusRecords.RemoveAll(b => b == null || b.Date.Date == DateTime.Today);
            }
            if (legacy <= 0) return 0;

            int totalAfter = AddBonus(legacy);
            _dirty = true;
            AppLog.Info($"旧条目级加时 {legacy} 分钟已折算进今日总限额（今日总加时 {totalAfter} 分钟），条目级加时记录已清空；后续加时只增加本轮单次时长。");
            return legacy;
        }

        /// <summary>把“；/;”分隔的输入拆成规范化关键字（去空白、去空项、忽略大小写去重，保持顺序）。</summary>
        public static List<string> SplitKeywords(string raw)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(raw)) return list;
            foreach (var part in raw.Replace('；', ';').Split(';'))
            {
                string k = part.Trim();
                if (k.Length == 0) continue;
                if (list.Any(x => x.Equals(k, StringComparison.OrdinalIgnoreCase))) continue;
                list.Add(k);
            }
            return list;
        }

        /// <summary>多个关键字以“；”连接（用于表格/输入框回显）。</summary>
        public static string JoinKeywords(IEnumerable<string> keywords)
        {
            if (keywords == null) return "";
            var list = new List<string>();
            foreach (var raw in keywords)
            {
                string k = (raw ?? "").Trim();
                if (k.Length == 0) continue;
                if (list.Any(x => x.Equals(k, StringComparison.OrdinalIgnoreCase))) continue;
                list.Add(k);
            }
            return string.Join("；", list);
        }

        /// <summary>保存校验结果：Error 非空表示必须拒绝；ConfirmText 非空表示“重复关键字”需要用户确认。</summary>
        public sealed class GameValidationResult
        {
            public string Error;
            public string ConfirmText;
            public List<string> Duplicates = new List<string>();
            public bool NeedsConfirm => !string.IsNullOrEmpty(ConfirmText);
        }

        /// <summary>
        /// 保存前的统一校验（设置页与 Web 共用）：
        /// 硬错误 = 关键字过短 / 命中保护名单 / 条目内无关键字 / 条目标识重复；
        /// 跨条目重复关键字不算错误，返回确认文案（用户确认后仍可保存，归属按列表顺序先者独占）。
        /// </summary>
        public static GameValidationResult ValidateGames(IList<GameItem> games)
        {
            var result = new GameValidationResult();
            if (games == null) return result;

            var keywordOwner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var idOwner = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string duplicateIdText = null;

            for (int i = 0; i < games.Count; i++)
            {
                var g = games[i];
                if (g == null) continue;
                string label = string.IsNullOrWhiteSpace(g.DisplayName)
                    ? $"第 {i + 1} 个条目"
                    : $"「{g.DisplayName}」";

                string id = (g.ProcessName ?? "").Trim();
                if (id.Length == 0)
                {
                    result.Error = $"第 {i + 1} 个条目缺少条目标识（ProcessName）。";
                    return result;
                }

                // 条目标识重复不再直接拒绝（标识默认取首个关键字，重复关键字是允许“确认后保存”的），
                // 保存时由 AssignUniqueEntryIds 给重复标识追加 #2/#3 兜底，避免状态/用量键冲突。
                if (idOwner.TryGetValue(id, out var idPrev))
                {
                    // 通常此时关键字也重复，由下面的关键字分支给出精确文案；这里只兜底记录。
                    if (duplicateIdText == null)
                        duplicateIdText =
                            $"条目{idPrev}与{label}使用了相同的条目标识「{id}」。\n" +
                            "保存后仍按列表顺序归属靠前的条目。是否仍然保存？";
                }
                else
                {
                    idOwner[id] = label;
                }

                var keywords = g.KeywordList;
                if (keywords.Count == 0)
                {
                    result.Error = $"{label}没有配置匹配关键字。";
                    return result;
                }

                foreach (var keyword in keywords)
                {
                    if (keyword.Length < GameMonitor.MinKeywordLength)
                    {
                        result.Error = $"{label}的关键字「{keyword}」太短：至少需要 {GameMonitor.MinKeywordLength} 个字符。";
                        return result;
                    }
                    if (ProcessMatcher.IsProtectedProcess(keyword))
                    {
                        result.Error = $"{label}的关键字「{keyword}」命中内置保护名单，不能用于监控。";
                        return result;
                    }
                    if (keywordOwner.TryGetValue(keyword, out var prev))
                    {
                        result.Duplicates.Add($"{prev}/{keyword}");
                        if (result.ConfirmText == null)
                            result.ConfirmText =
                                $"条目{prev}与{label}使用了相同关键字「{keyword}」。\n" +
                                "同一进程将归属列表中靠前的条目，另一条目不会计时。是否仍然保存？";
                    }
                    else
                    {
                        keywordOwner[keyword] = label;
                    }
                }
            }
            if (result.ConfirmText == null && duplicateIdText != null)
                result.ConfirmText = duplicateIdText;
            result.Duplicates = result.Duplicates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return result;
        }

        /// <summary>
        /// 保存前给重复的条目标识追加 #2/#3…，保证内部键（运行态/用量/会话/加时）唯一。
        /// 只在用户确认“重复关键字仍保存”之后调用；不改变关键字本身。
        /// </summary>
        public static void AssignUniqueEntryIds(IList<GameItem> games)
        {
            if (games == null) return;
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in games)
            {
                if (g == null) continue;
                string baseId = (g.ProcessName ?? "").Trim();
                if (baseId.Length == 0) baseId = g.KeywordList.FirstOrDefault() ?? "item";

                string id = baseId;
                if (!used.Add(id))
                {
                    int n = 2;
                    do { id = $"{baseId}#{n++}"; } while (!used.Add(id));
                }
                g.ProcessName = id;
            }
        }

        /// <summary>同一条目内重复输入的关键字（用于去重后的 INFO 提示）。</summary>
        public static List<string> FindIntraDuplicates(GameItem game)
            => FindIntraDuplicates(game?.Keywords);

        /// <summary>从“；/;”分隔的原始输入中找出条目内重复出现的关键字（保留重复项信息用于提示）。</summary>
        public static List<string> FindIntraDuplicates(string raw)
        {
            var duplicates = new List<string>();
            if (string.IsNullOrWhiteSpace(raw)) return duplicates;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in raw.Replace('；', ';').Split(';'))
            {
                string k = part.Trim();
                if (k.Length == 0) continue;
                if (!seen.Add(k)) duplicates.Add(k);
            }
            return duplicates;
        }

        private static List<string> FindIntraDuplicates(IEnumerable<string> keywords)
        {
            var duplicates = new List<string>();
            if (keywords == null) return duplicates;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in keywords)
            {
                string k = (raw ?? "").Trim();
                if (k.Length == 0) continue;
                if (!seen.Add(k)) duplicates.Add(k);
            }
            return duplicates;
        }

        /// <summary>
        /// 列出“刚好达到最小长度”的短关键字（两字），仅用于保存后的非阻断提示；
        /// 短关键字仍允许保存，由用户自行判断是否需要写得更具体。
        /// </summary>
        public static List<string> FindShortKeywords(IList<GameItem> games)
        {
            var shortList = new List<string>();
            if (games == null) return shortList;
            foreach (var g in games)
            {
                if (g == null) continue;
                foreach (var k in g.KeywordList)
                {
                    if (k.Length > GameMonitor.MinKeywordLength) continue;
                    if (shortList.Any(x => x.Equals(k, StringComparison.OrdinalIgnoreCase))) continue;
                    shortList.Add(k);
                }
            }
            return shortList;
        }

        /// <summary>清掉非今日的加时记录，避免配置无限增长。</summary>
        public void PruneBonusRecords()
        {
            if (BonusRecords == null) BonusRecords = new List<BonusRecord>();
            int removed = BonusRecords.RemoveAll(b => b == null || b.Date.Date != DateTime.Today);
            foreach (var g in Games)
            {
                if (g == null || g.BonusRecords == null) continue;
                removed += g.BonusRecords.RemoveAll(b => b == null || b.Date.Date != DateTime.Today);
            }
            if (removed > 0) AppLog.Info($"清理过期加时记录 {removed} 条。");
        }

        /// <summary>指定条目在指定日期的加时分钟数。</summary>
        public int EntryBonusMinutesFor(GameItem game, DateTime date)
        {
            if (game == null || game.BonusRecords == null) return 0;
            int sum = 0;
            foreach (var b in game.BonusRecords)
            {
                if (b != null && b.Date.Date == date.Date)
                    sum += Math.Max(0, b.Minutes);
            }
            return sum;
        }

        /// <summary>给指定条目的今日加时（分钟，正数）；返回该条目今日加时总额。</summary>
        public int AddEntryBonus(GameItem game, int minutes)
        {
            if (game == null) return 0;
            if (game.BonusRecords == null) game.BonusRecords = new List<BonusRecord>();
            if (minutes <= 0) return EntryBonusMinutesFor(game, DateTime.Today);

            var today = DateTime.Today;
            game.BonusRecords.RemoveAll(b => b == null || b.Date.Date != today);
            var rec = game.BonusRecords.FirstOrDefault(b => b.Date.Date == today);
            if (rec == null)
            {
                rec = new BonusRecord { Date = today, Minutes = 0 };
                game.BonusRecords.Add(rec);
            }
            rec.Minutes += minutes;
            return rec.Minutes;
        }

        /// <summary>今日所有条目的加时合计（用于总限额口径与展示）。</summary>
        public int TotalEntryBonusMinutes(DateTime date)
        {
            int sum = 0;
            foreach (var g in Games)
                sum += EntryBonusMinutesFor(g, date);
            return sum;
        }

        // ===== 单次会话累计（按条目名持久化） =====

        /// <summary>按条目名取会话记录（同名多进程/多窗口共用同一条）。</summary>
        public SessionRecord GetSessionRecord(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return null;
            if (SessionRecords == null) SessionRecords = new List<SessionRecord>();
            string key = processName.Trim();
            return SessionRecords.FirstOrDefault(r =>
                r != null && r.ProcessName != null &&
                r.ProcessName.Equals(key, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 写入会话已用秒数（每 tick 由监控线程调用，随 5 分钟定时保存落盘）。
        /// 新建记录的 Date 取今天；继续累计时不改 Date，跨天判定才有意义。
        /// </summary>
        public void SetSessionUsage(string processName, int usedSeconds, int runMinutes, int bonusSeconds)
        {
            if (string.IsNullOrWhiteSpace(processName)) return;
            if (SessionRecords == null) SessionRecords = new List<SessionRecord>();

            var rec = GetSessionRecord(processName);
            if (rec == null)
            {
                rec = new SessionRecord { ProcessName = processName.Trim(), Date = DateTime.Today };
                SessionRecords.Add(rec);
            }
            rec.UsedSeconds = Math.Max(0, usedSeconds);
            rec.RunMinutes = Math.Max(1, runMinutes);
            rec.SessionBonusSeconds = bonusSeconds;
            _dirty = true; // 本次会话累计变了，等定时器或显式保存落盘
        }

        /// <summary>
        /// 清零会话累计并把记账日期改为今天（间隔期结束/跨天/手动重置/改单次设定时调用）。
        /// resetBonus=false 用于“休息期结束、进入下一轮”：保留休息期里加的“下一次单次”加时。
        /// </summary>
        public void ResetSessionRecord(string processName, int runMinutes, bool resetBonus = true)
        {
            if (string.IsNullOrWhiteSpace(processName)) return;
            if (SessionRecords == null) SessionRecords = new List<SessionRecord>();

            var rec = GetSessionRecord(processName);
            if (rec == null)
            {
                rec = new SessionRecord { ProcessName = processName.Trim() };
                SessionRecords.Add(rec);
            }
            rec.Date = DateTime.Today;
            rec.UsedSeconds = 0;
            rec.RunMinutes = Math.Max(1, runMinutes);
            if (resetBonus) rec.SessionBonusSeconds = 0;
            _dirty = true;
        }

        /// <summary>清理会话记录：只保留今天与昨天的，避免 config 无限增长。</summary>
        public void PruneSessionRecords()
        {
            if (SessionRecords == null) SessionRecords = new List<SessionRecord>();
            DateTime cutoff = DateTime.Today.AddDays(-1);
            int removed = SessionRecords.RemoveAll(r =>
                r == null || string.IsNullOrWhiteSpace(r.ProcessName) || r.Date.Date < cutoff);
            if (removed > 0)
            {
                _dirty = true;
                AppLog.Info($"清理过期会话累计记录 {removed} 条。");
            }
        }

        /// <summary>
        /// 旧全局加时迁移：旧配置里的全局 BonusRecords 一次性均分给所有带关键字的条目（余数给第一条），
        /// 写日志后清空旧字段并置迁移标记，避免重复累加。无法分配（无条目）时也置标记。
        /// </summary>
        private void MigrateGlobalBonusToEntries()
        {
            foreach (var g in Games)
                if (g != null && g.BonusRecords == null) g.BonusRecords = new List<BonusRecord>();

            if (BonusMigratedToEntries)
            {
                if (BonusRecords != null) BonusRecords.Clear();
                return;
            }

            int total = BonusMinutesFor(DateTime.Today);
            var targets = Games.Where(g => g != null && !string.IsNullOrWhiteSpace(g.ProcessName)).ToList();
            BonusMigratedToEntries = true;

            if (total <= 0 || targets.Count == 0)
            {
                if (BonusRecords != null) BonusRecords.Clear();
                return;
            }

            int share = total / targets.Count;
            int rest = total % targets.Count;
            for (int i = 0; i < targets.Count; i++)
                AddEntryBonus(targets[i], share + (i == 0 ? rest : 0));

            BonusRecords.Clear();
            AppLog.Info($"旧全局加时 {total} 分钟已迁移为条目级加时：{targets.Count} 个条目均分，每条 {share} 分钟，余数 {rest} 分钟给了第一个条目。");
        }

        private static string BackupCorruptConfig()
        {
            try
            {
                string dir = Path.GetDirectoryName(ConfigPath) ?? AppDomain.CurrentDomain.BaseDirectory;
                string name = $"config.corrupt.{DateTime.Now:yyyyMMdd_HHmmss}.xml";
                string dest = Path.Combine(dir, name);
                File.Move(ConfigPath, dest);
                return dest;
            }
            catch (Exception ex)
            {
                AppLog.Error("备份损坏配置失败", ex);
                return ConfigPath;
            }
        }
    }
}
