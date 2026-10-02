using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace APOD.Core
{
    internal class MacService : IOsService
    {
        public async Task SetWallpaperAsync(string path)
        {
            // Forma de System Events (la de Finder quedó obsoleta): compatible con macOS moderno
            // y sin depender de Xcode Command Line Tools. Requiere permiso de Automatización (una vez).
            var psi = new ProcessStartInfo("osascript")
            {
                UseShellExecute = false
            };
            psi.ArgumentList.Add("-e");
            psi.ArgumentList.Add($"tell application \"System Events\" to tell every desktop to set picture to \"{path}\"");
            using var process = Process.Start(psi);
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    "No se pudo establecer el fondo de pantalla. Verifica el permiso de Automatización (Ajustes del Sistema > Privacidad y seguridad > Automatización).");
            }
        }
    }
}
