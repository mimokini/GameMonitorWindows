using System;
using System.Drawing;
using System.Windows.Forms;

namespace GameMonitor
{
    /// <summary>
    /// 程序图标加载：从自身 exe 的内嵌图标资源提取（编译期由 csproj 的 ApplicationIcon 写入 exe），
    /// 运行时不再依赖 exe 同目录的 app.ico 文件。
    /// 托盘图标、设置窗口、密码框、关于页都通过这里取图标；结果缓存单实例，避免重复提取。
    /// 提取失败时回退系统图标，保证不会出现无图标窗口或崩溃。
    /// </summary>
    public static class AppIcon
    {
        private static readonly object CacheLock = new object();
        private static Icon _cachedIcon;

        public static Icon Resolve()
        {
            lock (CacheLock)
            {
                if (_cachedIcon != null) return _cachedIcon;

                _cachedIcon = TryExtractFromExe();
                if (_cachedIcon == null)
                {
                    AppLog.Warn("未能从 exe 内嵌资源提取图标，回退系统默认图标。");
                    _cachedIcon = SystemIcons.Shield;
                }
                return _cachedIcon;
            }
        }

        /// <summary>
        /// 从当前 exe 的 Win32 图标资源取图（即 csproj 中 ApplicationIcon 编译进 exe 的那份，不是外部文件）。
        /// </summary>
        private static Icon TryExtractFromExe()
        {
            try
            {
                string exePath = Application.ExecutablePath;
                if (string.IsNullOrEmpty(exePath)) return null;

                var icon = Icon.ExtractAssociatedIcon(exePath);
                if (icon != null && icon.Width > 0) return icon;

                icon?.Dispose();
            }
            catch (Exception ex)
            {
                AppLog.Warn("从 exe 提取图标失败", ex);
            }
            return null;
        }
    }
}
