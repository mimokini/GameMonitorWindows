namespace GameMonitor
{
    public enum PromptKind
    {
        Reminding,
        Countdown,
        Cooldown,
        QuotaExhausted,
        BlockedBySchedule
    }

    /// <summary>
    /// 提示文案模板的安全替换：未知占位符原样保留；空模板回退到该类型的默认文案，
    /// 运行时不再出现空字符串，也绝不抛异常。
    /// </summary>
    public static class PromptText
    {
        /// <summary>某一类提示的内置默认文案（空模板时回退用）。</summary>
        public static string DefaultTemplate(PromptKind kind)
        {
            switch (kind)
            {
                case PromptKind.Countdown: return OverlayStyleConfig.CreateCountdown().Template;
                case PromptKind.Cooldown: return OverlayStyleConfig.CreateCooldown().Template;
                case PromptKind.QuotaExhausted: return OverlayStyleConfig.CreateQuota().Template;
                case PromptKind.BlockedBySchedule: return OverlayStyleConfig.CreateBlockedBySchedule().Template;
                default: return OverlayStyleConfig.CreateReminding().Template;
            }
        }

        public static string Format(string template, string gameName, int minutes, int seconds,
            string clock, string remaining, PromptKind kind = PromptKind.Reminding, string clockAt = "")
        {
            if (string.IsNullOrWhiteSpace(template))
                template = DefaultTemplate(kind);

            try
            {
                return template
                    .Replace("{game}", gameName ?? "")
                    .Replace("{minutes}", Math.Max(0, minutes).ToString())
                    .Replace("{seconds}", Math.Max(0, seconds).ToString())
                    .Replace("{clock}", clock ?? "")
                    .Replace("{remaining}", remaining ?? "")
                    // 新增的“绝对时刻”占位符：默认空串，只有休息期提示会给出真实时刻；
                    // 旧模板不含 {time}，因此升级后仍按原样渲染，不会出现空值或占位符原文。
                    .Replace("{time}", clockAt ?? "");
            }
            catch
            {
                // 模板只是普通字符串，理论上不会抛异常；兜底返回原文
                return template;
            }
        }
    }
}
