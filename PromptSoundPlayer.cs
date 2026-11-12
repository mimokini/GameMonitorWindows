using System.Media;

namespace GameMonitor
{
    public enum PromptSoundKind
    {
        Grace,
        Countdown,
        Cooldown,
        Quota,
        BlockedBySchedule
    }

    /// <summary>
    /// 提示音播放：优先使用用户配置的 .wav；文件缺失/格式不支持/播放失败时回退系统音并写 WARN。
    /// 播放放在后台线程，避免阻塞 UI；任何异常都不会抛到调用方。
    /// </summary>
    public static class PromptSoundPlayer
    {
        public static void Play(AppConfig config, PromptSoundKind kind)
        {
            if (config == null) return;

            bool enabled;
            string file;
            switch (kind)
            {
                case PromptSoundKind.Grace:
                    enabled = config.SoundOnGrace;
                    file = config.SoundGraceFile;
                    break;
                case PromptSoundKind.Countdown:
                    enabled = config.SoundOnCountdown;
                    file = config.SoundCountdownFile;
                    break;
                case PromptSoundKind.Quota:
                    enabled = config.SoundOnQuota;
                    file = config.SoundQuotaFile;
                    break;
                case PromptSoundKind.BlockedBySchedule:
                    enabled = config.SoundOnBlockedBySchedule;
                    file = config.SoundBlockedByScheduleFile;
                    break;
                default:
                    enabled = config.SoundOnCooldown;
                    file = config.SoundCooldownFile;
                    break;
            }

            if (!enabled) return;

            if (!TryPlayFile(file, kind)) PlaySystem(kind);
        }

        /// <summary>设置页「试听」：不读开关、不需要先保存，直接播放输入框里当前选中的音频文件。</summary>
        public static void Preview(string file)
        {
            if (!TryPlayFile(file, PromptSoundKind.Grace)) PlaySystem(PromptSoundKind.Grace);
        }

        /// <summary>播放自定义 .wav；文件缺失/扩展名不对/启动失败时返回 false（由调用方回退系统音）。</summary>
        private static bool TryPlayFile(string file, PromptSoundKind kind)
        {
            file = (file ?? "").Trim();
            if (file.Length == 0) return false;

            if (!File.Exists(file))
            {
                AppLog.Warn($"自定义提示音文件不存在，已回退系统音：{file}");
                return false;
            }
            if (!string.Equals(Path.GetExtension(file), ".wav", StringComparison.OrdinalIgnoreCase))
            {
                AppLog.Warn($"自定义提示音不是 .wav，已回退系统音：{file}");
                return false;
            }

            try
            {
                string path = file;
                Task.Run(() =>
                {
                    try
                    {
                        using (var player = new SoundPlayer(path))
                            player.PlaySync();
                    }
                    catch (Exception ex)
                    {
                        AppLog.Warn($"播放自定义提示音失败，已回退系统音：{path}", ex);
                        PlaySystem(kind);
                    }
                });
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Warn("启动自定义提示音播放失败，已回退系统音。", ex);
                return false;
            }
        }

        private static void PlaySystem(PromptSoundKind kind)
        {
            try
            {
                if (kind == PromptSoundKind.Countdown)
                {
                    // 倒计时用短促蜂鸣，其余用系统感叹号提示
                    Console.Beep(880, 220);
                }
                else
                {
                    SystemSounds.Exclamation.Play();
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn("播放系统提示音失败。", ex);
            }
        }
    }
}
