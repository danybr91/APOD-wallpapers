using System;
using System.Runtime.InteropServices;
using APOD.Core;

namespace APOD.UI
{
    public class UiLogger : ILog
    {
        #region P/Invoke declarations

        [DllImport("User32", CharSet = CharSet.Auto)]
        private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

        #endregion

        private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        private const uint MB_OK = 0x00000000;
        private const uint MB_ICONINFORMATION = 0x00000040;

        // El host de la ventana registra aquí cómo escribir en la barra de estado.
        public Action<string> StatusWriter { get; set; }

        public void Info(string message)
        {
            if (StatusWriter != null)
            {
                StatusWriter(message);
            }
            else if (!IsWindows)
            {
                Console.Error.WriteLine($"INFO\t=> {message}");
            }
        }

        public void Error(string message)
        {
            if (StatusWriter != null)
            {
                StatusWriter($"ERROR: {message}");
            }
            else if (!IsWindows)
            {
                Console.Error.WriteLine($"ERROR\t=> {message}");
            }
        }

        public void Line(string message)
        {
            // El MsgBox se usa únicamente para la ayuda.
            if (IsWindows)
            {
                MessageBox(IntPtr.Zero, message, "APOD Wallpapers", MB_OK | MB_ICONINFORMATION);
            }
            else
            {
                Console.WriteLine(message);
            }
        }
    }
}
