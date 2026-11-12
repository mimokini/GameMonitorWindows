using System.Globalization;

namespace GameMonitor
{
    /// <summary>一次时段判定的结果。优先级：禁止 &gt; 开放 &gt; 无规则（默认监控）。</summary>
    public sealed class ScheduleDecision
    {
        public bool Blocked;
        public bool HasAllowRules;
        public bool AllowHit;
        public string BlockSummary = "";
        public string AllowSummary = "";

        /// <summary>是否落在开放时段内；没有配置任何开放规则时恒为 true。</summary>
        public bool InAllowedPeriod => !HasAllowRules || AllowHit;

        /// <summary>供状态展示的当前命中规则摘要。</summary>
        public string Summary
        {
            get
            {
                if (Blocked) return BlockSummary;
                if (HasAllowRules) return AllowHit ? AllowSummary : "非开放时段";
                return "";
            }
        }
    }

    /// <summary>
    /// 时段规则统一判定入口：所有“禁止/开放”判断都必须走这里，避免各处口径不一致。
    /// 跨零点规则按“开始那一天”的星期生效，后半夜视为前一天规则的延续。
    /// </summary>
    public static class ScheduleRules
    {
        private static readonly string[] DayNames = { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };

        public static bool TryParseTime(string text, out TimeSpan value)
        {
            value = TimeSpan.Zero;
            if (string.IsNullOrWhiteSpace(text)) return false;

            var parts = text.Trim().Split(':');
            if (parts.Length < 2 || parts.Length > 3) return false;
            if (!int.TryParse(parts[0], out int hour)) return false;
            if (!int.TryParse(parts[1], out int minute)) return false;
            int second = 0;
            if (parts.Length == 3 && !int.TryParse(parts[2], out second)) return false;
            if (hour < 0 || hour > 23 || minute < 0 || minute > 59 || second < 0 || second > 59)
                return false;

            value = new TimeSpan(hour, minute, second);
            return true;
        }

        public static bool IsValidTime(string text) => TryParseTime(text, out _);

        public static string NormalizeTime(string text)
        {
            return TryParseTime(text, out TimeSpan value)
                ? $"{value.Hours:D2}:{value.Minutes:D2}:{value.Seconds:D2}"
                : "00:00:00";
        }

        public static bool DayEnabled(bool[] days, int dayOfWeek)
        {
            if (days == null || days.Length < 7) return false;
            int index = ((dayOfWeek % 7) + 7) % 7;
            return days[index];
        }

        /// <summary>单条规则在给定时刻是否命中（[Start, End) 半开区间；Start==End 视为未设置，不命中）。</summary>
        public static bool IsHit(ScheduleRule rule, DateTime now)
        {
            if (rule == null || !rule.Enabled) return false;
            if (!TryParseTime(rule.StartTime, out TimeSpan start)) return false;
            if (!TryParseTime(rule.EndTime, out TimeSpan end)) return false;
            if (start == end) return false;

            TimeSpan t = now.TimeOfDay;
            int today = (int)now.DayOfWeek;
            int yesterday = today == 0 ? 6 : today - 1;

            if (start < end)
                return DayEnabled(rule.DaysOfWeek, today) && t >= start && t < end;

            // 跨零点：Start 之后属于开始日，End 之前属于前一天规则的延续
            if (t >= start) return DayEnabled(rule.DaysOfWeek, today);
            if (t < end) return DayEnabled(rule.DaysOfWeek, yesterday);
            return false;
        }

        public static ScheduleDecision Evaluate(AppConfig config, DateTime now)
        {
            return config == null ? new ScheduleDecision() : Evaluate(config.ScheduleRules, now);
        }

        /// <summary>统一判定：禁止命中即 Blocked；禁止优先于开放；同类型多条规则取并集。</summary>
        public static ScheduleDecision Evaluate(IEnumerable<ScheduleRule> rules, DateTime now)
        {
            var decision = new ScheduleDecision();
            if (rules == null) return decision;

            foreach (var rule in rules)
            {
                if (rule == null || !rule.Enabled) continue;
                bool allow = rule.Kind == ScheduleRuleKind.Allow;
                if (allow) decision.HasAllowRules = true;
                if (!IsHit(rule, now)) continue;

                if (allow)
                {
                    decision.AllowHit = true;
                    if (string.IsNullOrEmpty(decision.AllowSummary)) decision.AllowSummary = Describe(rule);
                }
                else
                {
                    decision.Blocked = true;
                    if (string.IsNullOrEmpty(decision.BlockSummary)) decision.BlockSummary = Describe(rule);
                }
            }

            return decision;
        }

        /// <summary>规则摘要，用于悬浮窗/Web/托盘与设置页提示。</summary>
        public static string Describe(ScheduleRule rule)
        {
            if (rule == null) return "";
            string kind = rule.Kind == ScheduleRuleKind.Block ? "禁止" : "开放";
            TimeSpan start = TryParseTime(rule.StartTime, out TimeSpan s) ? s : TimeSpan.Zero;
            TimeSpan end = TryParseTime(rule.EndTime, out TimeSpan e) ? e : TimeSpan.Zero;
            string span = $"{start.Hours:D2}:{start.Minutes:D2}-{end.Hours:D2}:{end.Minutes:D2}";
            if (start > end) span += "（跨天）";
            return $"{kind} {span} {DescribeDays(rule.DaysOfWeek)}";
        }

        private static string DescribeDays(bool[] days)
        {
            if (days == null || days.Length < 7) return "未选星期";
            int count = 0;
            var names = new List<string>();
            for (int i = 0; i < 7; i++)
            {
                if (!days[i]) continue;
                count++;
                names.Add(DayNames[i]);
            }
            if (count == 0) return "未选星期";
            if (count == 7) return "每天";
            return string.Join("、", names);
        }
    }
}
