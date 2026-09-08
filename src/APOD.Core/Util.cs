using System.Reflection;
using System.Runtime.InteropServices;

namespace APOD.Core
{
    public static class Util
    {
        public static string GetVersion()
        {
            return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3);
        }

        internal static bool IsWindows() => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        internal static bool IsLinux() => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

        internal static bool IsOsx() => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    }
}