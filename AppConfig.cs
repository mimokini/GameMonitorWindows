using System.Xml.Serialization;

namespace GameMonitor
{
    [Serializable]
    public class GameItem
    {
        public string DisplayName { get; set; } = "新游戏";
        public string ProcessName { get; set; } = "";
        public int RunMinutes { get; set; } = 60;
        public int DailyLimitMinutes { get; set; } = 0;
        public bool Enabled { get; set; } = true;
        /// <summary>匹配方式，默认“包含”，兼容旧配置。</summary>
        public MatchMode MatchMode { get; set; } = MatchMode.Contains;

        public override string ToString()
        {
            string quota = DailyLimitMinutes > 0 ? $"{DailyLimitMinutes} 分" : "不限";
            return $"{DisplayName}  (关键字:{ProcessName})  -  单次 {RunMinutes} 分 / 今日配额 {quota}";
        }
    }

    [Serializable]
    public class GameUsage
    {
        public string ProcessName { get; set; } = "";
        public DateTime Date { get; set; } = DateTime.Today;
        public int UsedSeconds { get; set; } = 0;
    }

    [Serializable]
    public class AppConfig
    {
        public List<GameItem> Games { get; set; } = new List<GameItem>();
        public List<GameUsage> Usage { get; set; } = new List<GameUsage>();

        public int ReminderMinutes { get; set; } = 5;
        public int CountdownSeconds { get; set; } = 60;
        public uint HotkeyModifiers { get; set; } = 0x0003;
        public int HotkeyVk { get; set; } = 0x47;
        public bool StartWithWindows { get; set; } = false;
        public bool HideTrayIcon { get; set; } = false;
        public bool MouseTransparent { get; set; } = false;
        public bool EnableDaemon { get; set; } = false;
        public string AdminPassword { get; set; } = "";

        // ===== 远程调参 =====
        public bool EnableRemote { get; set; } = false;
        public int RemotePort { get; set; } = 8080;
        public bool RemoteAllowLan { get; set; } = false;

        // ===== 桌面悬浮框 =====
        public bool StatusOverlayEnabled { get; set; } = true;
        public int StatusOverlayX { get; set; } = -9999; // 改为 -9999 防止负坐标屏幕误判
        public int StatusOverlayY { get; set; } = -9999;
        public int StatusOverlayOpacity { get; set; } = 200;
        public bool StatusOverlaySnap { get; set; } = true;
        public bool StatusOverlayAutoHide { get; set; } = false;
        public int StatusOverlayAutoHideSeconds { get; set; } = 5;
        public bool StatusOverlayTopMost { get; set; } = true;

        // ===== 倒计时字体 =====
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
                    return new AppConfig();

                try
                {
                    using (var fs = File.OpenRead(ConfigPath))
                    {
                        var ser = new XmlSerializer(typeof(AppConfig));
                        var cfg = ser.Deserialize(fs) as AppConfig;
                        if (cfg != null)
                        {
                            if (cfg.Games == null) cfg.Games = new List<GameItem>();
                            if (cfg.Usage == null) cfg.Usage = new List<GameUsage>();
                            return cfg;
                        }
                    }
                    throw new InvalidDataException("配置文件内容为空或格式不正确");
                }
                catch (Exception ex)
                {
                    // 不再静默回落默认值：把坏文件改名保留证据，并写日志
                    string corruptPath = BackupCorruptConfig();
                    AppLog.Error($"读取配置失败，已备份为 {corruptPath}", ex);
                    return new AppConfig();
                }
            }
        }

        public void Save()
        {
            lock (IoLock)
            {
                try
                {
                    WriteAtomically();
                }
                catch (Exception ex)
                {
                    AppLog.Error("保存配置失败", ex);
                    MessageBox.Show("保存配置失败：" + ex.Message, "游戏监控",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        public void SaveQuiet()
        {
            lock (IoLock)
            {
                try
                {
                    WriteAtomically();
                }
                catch (Exception ex)
                {
                    AppLog.Error("保存配置失败", ex);
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
