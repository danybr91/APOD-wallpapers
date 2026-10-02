using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace APOD.Core
{
    internal partial class WindowsService : IOsService
    {
        private const int SPI_SETDESKWALLPAPER = 0x0014;
        private const uint SPIF_UPDATEINIFILE = 0x0001;
        private const uint SPIF_SENDCHANGE = 0x0002;

        // LibraryImport no prueba los sufijos A/W como DllImport: se indica la variante Unicode explícitamente.
        [LibraryImport("User32", EntryPoint = "SystemParametersInfoW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        private static partial int SystemParametersInfo(int uiAction, int uiParam, string pvParam, uint fWinIni);

        public Task SetWallpaperAsync(string path)
        {
            if (SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, path, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE) == 0)
            {
                throw new InvalidOperationException(
                    $"No se pudo establecer el fondo de pantalla (error {Marshal.GetLastWin32Error()}).");
            }
            return Task.CompletedTask;
        }
    }
}
