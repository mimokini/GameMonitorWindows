using System.Runtime.InteropServices;
using System.Text;

namespace GameMonitor
{
    /// <summary>
    /// 用 Windows DPAPI（CryptProtectData / CryptUnprotectData）加密管理密码。
    /// 加密结果绑定当前 Windows 用户：换用户或换机器无法解密，属预期行为。
    /// 加密后的值带 "enc:v1:" 前缀，便于识别旧配置里的明文并做一次性迁移。
    /// </summary>
    public static class PasswordProtector
    {
        public const string Prefix = "enc:v1:";

        private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        private struct DATA_BLOB
        {
            public int cbData;
            public IntPtr pbData;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string szDataDescr,
            IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags,
            out DATA_BLOB pDataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, IntPtr ppszDataDescr,
            IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags,
            out DATA_BLOB pDataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);

        /// <summary>加密明文密码；空字符串返回空；DPAPI 不可用时回退明文并写日志。</summary>
        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";

            byte[] input = Encoding.UTF8.GetBytes(plain);
            IntPtr inPtr = Marshal.AllocHGlobal(input.Length);
            DATA_BLOB inBlob = new DATA_BLOB { cbData = input.Length, pbData = inPtr };
            DATA_BLOB outBlob = new DATA_BLOB();
            try
            {
                Marshal.Copy(input, 0, inPtr, input.Length);
                if (CryptProtectData(ref inBlob, "GameMonitor.AdminPassword", IntPtr.Zero, IntPtr.Zero,
                        IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, out outBlob))
                {
                    byte[] cipher = new byte[outBlob.cbData];
                    Marshal.Copy(outBlob.pbData, cipher, 0, outBlob.cbData);
                    return Prefix + Convert.ToBase64String(cipher);
                }

                AppLog.Warn("DPAPI 加密管理密码失败（Win32 错误码 " + Marshal.GetLastWin32Error() + "），暂以明文保存。");
                return plain;
            }
            catch (Exception ex)
            {
                AppLog.Warn("DPAPI 加密管理密码异常，暂以明文保存。", ex);
                return plain;
            }
            finally
            {
                Marshal.FreeHGlobal(inPtr);
                if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
            }
        }

        /// <summary>
        /// 解密配置中的密码。
        /// 返回值：true 表示识别成功（wasPlaintext=true 表示这是旧配置的明文，需要迁移加密）；
        /// false 表示带前缀但无法解密（换了 Windows 用户/机器），调用方应按空密码处理。
        /// </summary>
        public static bool TryUnprotect(string stored, out string plain, out bool wasPlaintext)
        {
            plain = "";
            wasPlaintext = true;
            if (string.IsNullOrEmpty(stored)) return true;

            if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
            {
                // 旧配置：没有前缀就是明文
                plain = stored;
                return true;
            }

            wasPlaintext = false;
            byte[] cipher;
            try
            {
                cipher = Convert.FromBase64String(stored.Substring(Prefix.Length));
            }
            catch
            {
                return false;
            }

            if (cipher.Length == 0) return true;

            IntPtr inPtr = Marshal.AllocHGlobal(cipher.Length);
            DATA_BLOB inBlob = new DATA_BLOB { cbData = cipher.Length, pbData = inPtr };
            DATA_BLOB outBlob = new DATA_BLOB();
            try
            {
                Marshal.Copy(cipher, 0, inPtr, cipher.Length);
                if (!CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
                        IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, out outBlob))
                    return false;

                if (outBlob.cbData <= 0) return true;
                byte[] bytes = new byte[outBlob.cbData];
                Marshal.Copy(outBlob.pbData, bytes, 0, outBlob.cbData);
                plain = Encoding.UTF8.GetString(bytes);
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Warn("DPAPI 解密管理密码异常。", ex);
                return false;
            }
            finally
            {
                Marshal.FreeHGlobal(inPtr);
                if (outBlob.pbData != IntPtr.Zero) LocalFree(outBlob.pbData);
            }
        }
    }
}
