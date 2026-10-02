using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace APOD.UI
{
    /// <summary>
    /// Comprueba si el control NativeWebView puede usarse en esta máquina.
    /// En Linux, Avalonia crea la vista de WebKitGTK desde un hilo secundario y algunas versiones
    /// de WebKitGTK (p. ej. 2.54) abortan el proceso (SIGABRT) en ese caso. Un abort nativo no se
    /// puede capturar desde .NET, así que la prueba se hace en un proceso hijo.
    /// </summary>
    public static class WebViewSupport
    {
        public const string ProbeArgument = "--probe-webview";

        private const int ProbeTimeoutMs = 15000;

        private static readonly string[] WebKitGtkLibraries = { "libwebkit2gtk-4.1.so.0", "libwebkit2gtk-4.0.so.37" };

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate bool GtkInitCheck(IntPtr argc, IntPtr argv);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr WebKitWebViewNew();

        private static readonly Lazy<bool> _isSupported = new Lazy<bool>(Detect);

        /// <summary>Resultado de la comprobación; se calcula una sola vez, la primera vez que se consulta.</summary>
        public static bool IsSupported => _isSupported.Value;

        private static bool Detect()
        {
            if (!OperatingSystem.IsLinux())
            {
                return true;
            }

            try
            {
                return RunProbeProcess();
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Punto de entrada del proceso hijo: crea una vista de WebKitGTK en un hilo secundario,
        /// igual que Avalonia. Si WebKitGTK no lo admite, el proceso termina abortado.
        /// </summary>
        public static int RunProbe()
        {
            int result = 1;
            var thread = new Thread(() => result = CreateGtkWebView() ? 0 : 1);
            thread.Start();
            thread.Join();
            return result;
        }

        private static bool CreateGtkWebView()
        {
            if (!NativeLibrary.TryLoad("libgtk-3.so.0", out IntPtr gtk))
            {
                return false;
            }

            IntPtr webkit = IntPtr.Zero;
            foreach (string library in WebKitGtkLibraries)
            {
                if (NativeLibrary.TryLoad(library, out webkit))
                {
                    break;
                }
            }
            if (webkit == IntPtr.Zero)
            {
                return false;
            }

            var gtkInitCheck = Marshal.GetDelegateForFunctionPointer<GtkInitCheck>(NativeLibrary.GetExport(gtk, "gtk_init_check"));
            var webViewNew = Marshal.GetDelegateForFunctionPointer<WebKitWebViewNew>(NativeLibrary.GetExport(webkit, "webkit_web_view_new"));

            return gtkInitCheck(IntPtr.Zero, IntPtr.Zero) && webViewNew() != IntPtr.Zero;
        }

        private static bool RunProbeProcess()
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            // Si se ejecuta como "dotnet apod.dll" hay que indicar el ensamblado al host.
            if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet")
            {
                startInfo.ArgumentList.Add(Environment.GetCommandLineArgs()[0]);
            }
            startInfo.ArgumentList.Add(ProbeArgument);

            using var process = Process.Start(startInfo);
            process.OutputDataReceived += (_, _) => { };
            process.ErrorDataReceived += (_, _) => { };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(ProbeTimeoutMs))
            {
                process.Kill();
                return false;
            }
            return process.ExitCode == 0;
        }
    }
}
