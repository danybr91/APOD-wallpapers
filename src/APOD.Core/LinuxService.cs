using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Tmds.DBus.Protocol;

namespace APOD.Core
{
    // En Linux no hay un comando estándar para cambiar el fondo. Lo más parecido es el portal
    // org.freedesktop.portal.Wallpaper (xdg-desktop-portal), implementado por GNOME, KDE y
    // xapp (Cinnamon, MATE, Xfce), que pide permiso al usuario la primera vez. Se usa primero el portal
    // y solo si no está disponible (sin D-Bus o sin backend de fondos, p. ej. wlroots/Hyprland/LXQt)
    // se recurre al comando propio de cada escritorio. Si el usuario deniega o cancela no se usan
    // los comandos, para no saltarse su decisión.
    internal class LinuxService : IOsService
    {
        private const string PORTAL_SERVICE = "org.freedesktop.portal.Desktop";
        private const string PORTAL_PATH = "/org/freedesktop/portal/desktop";
        private static readonly TimeSpan PORTAL_TIMEOUT = TimeSpan.FromMinutes(2);

        public async Task SetWallpaperAsync(string path)
        {
            string desktop = Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP")?.ToLowerInvariant() ?? "";

            (bool portal_available, string portal_error) = await TrySetWithPortalAsync(path);
            if (portal_available)
            {
                if (portal_error == null) return;
                throw new InvalidOperationException(portal_error);
            }

            if (TrySetWithDesktopCommand(desktop, path)) return;

            string name = desktop.Length > 0 ? desktop : "desconocido";
            throw new PlatformNotSupportedException(
                $"No se pudo establecer el fondo de pantalla automáticamente en este escritorio ({name}). {portal_error}");
        }

        private static bool TrySetWithDesktopCommand(string desktop, string path)
        {
            string uri = new Uri(path).AbsoluteUri;

            // XDG_CURRENT_DESKTOP puede ser una lista ("ubuntu:GNOME"), se busca por contenido.
            if (desktop.Contains("cinnamon"))
                return Run("gsettings", "set", "org.cinnamon.desktop.background", "picture-uri", uri);
            if (desktop.Contains("mate"))
                return Run("gsettings", "set", "org.mate.background", "picture-filename", path);
            if (desktop.Contains("gnome") || desktop.Contains("unity") || desktop.Contains("budgie") || desktop.Contains("pantheon"))
            {
                bool ok = Run("gsettings", "set", "org.gnome.desktop.background", "picture-uri", uri);
                // GNOME 42+ usa otra clave con el tema oscuro; en versiones antiguas no existe.
                if (ok) Run("gsettings", "set", "org.gnome.desktop.background", "picture-uri-dark", uri);
                return ok;
            }
            if (desktop.Contains("kde"))
                return Run("plasma-apply-wallpaperimage", path);
            if (desktop.Contains("xfce"))
                return SetXfce(path);
            if (desktop.Contains("lxqt"))
                return Run("pcmanfm-qt", "--set-wallpaper", path);
            if (desktop.Contains("lxde"))
                return Run("pcmanfm", "--set-wallpaper", path);
            if (desktop.Contains("sway"))
                return Run("swaymsg", "output", "*", "bg", path, "fill");

            return false;
        }

        // Xfce guarda un fondo por monitor y espacio de trabajo (monitor0, monitorVirtual1, monitoreDP-1...),
        // así que se actualizan todas las propiedades last-image existentes.
        private static bool SetXfce(string path)
        {
            if (!Run("xfconf-query", out string output, "-c", "xfce4-desktop", "-l")) return false;

            bool any = false;
            foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (line.EndsWith("/last-image"))
                    any |= Run("xfconf-query", "-c", "xfce4-desktop", "-p", line, "-s", path);
            }
            return any;
        }

        private static bool Run(string file, params string[] args) => Run(file, out _, args);

        private static bool Run(string file, out string output, params string[] args)
        {
            output = "";
            var psi = new ProcessStartInfo(file)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (string arg in args) psi.ArgumentList.Add(arg);

            try
            {
                using var process = Process.Start(psi);
                if (process == null) return false;
                output = process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                process.WaitForExit();
                return process.ExitCode == 0;
            }
            catch (Win32Exception)
            {
                // El comando no está instalado.
                return false;
            }
        }

        private static MessageBuffer CreateSetWallpaperMessage(DBusConnection connection, string path, string token)
        {
            using MessageWriter writer = connection.GetMessageWriter();
            writer.WriteMethodCallHeader(PORTAL_SERVICE, PORTAL_PATH, "org.freedesktop.portal.Wallpaper",
                "SetWallpaperURI", "ssa{sv}", MessageFlags.None);
            writer.WriteString("");
            writer.WriteString(new Uri(path).AbsoluteUri);
            writer.WriteDictionary(new Dictionary<string, VariantValue>
            {
                ["handle_token"] = VariantValue.String(token),
                ["show-preview"] = VariantValue.Bool(false),
                ["set-on"] = VariantValue.String("background")
            });
            return writer.CreateMessage();
        }

        // available indica si el portal atendió la petición; error es null si se estableció el fondo.
        private static async Task<(bool available, string error)> TrySetWithPortalAsync(string path)
        {
            try
            {
                string address = DBusAddress.Session ?? throw new InvalidOperationException("no hay bus de sesión de D-Bus.");
                using var connection = new DBusConnection(address);
                await connection.ConnectAsync();

                // La respuesta llega por la señal Response del objeto Request, cuya ruta se conoce de antemano
                // gracias a handle_token. Hay que suscribirse antes de llamar para no perder la señal.
                string token = "apod" + Guid.NewGuid().ToString("N");
                string sender = connection.UniqueName.TrimStart(':').Replace('.', '_');
                string request_path = $"{PORTAL_PATH}/request/{sender}/{token}";

                var response = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
                var rule = new MatchRule
                {
                    Type = MessageType.Signal,
                    Interface = "org.freedesktop.portal.Request",
                    Member = "Response",
                    Path = request_path
                };
                using IDisposable subscription = await connection.AddMatchAsync(
                    rule,
                    (Message message, object _) => message.GetBodyReader().ReadUInt32(),
                    (Notification<uint> notification) =>
                    {
                        if (notification.HasValue) response.TrySetResult(notification.Value);
                        else if (notification.Exception != null) response.TrySetException(notification.Exception);
                    },
                    emitOnCapturedContext: false, ObserverFlags.None, null);

                try
                {
                    await connection.CallMethodAsync(CreateSetWallpaperMessage(connection, path, token));
                }
                catch (DBusErrorReplyException e)
                {
                    // Sin portal, o portal sin backend de fondos (el método no existe).
                    return (false, $"El portal de escritorio (xdg-desktop-portal) no está disponible: {e.Message}");
                }

                uint result;
                try
                {
                    result = await response.Task.WaitAsync(PORTAL_TIMEOUT);
                }
                catch (TimeoutException)
                {
                    return (true, "El sistema no respondió a la petición de cambiar el fondo.");
                }

                return result switch
                {
                    0 => (true, null),
                    1 => (true, "Se canceló el cambio de fondo."),
                    // Sin preview el portal pide permiso la primera vez; si se deniega (queda guardado)
                    // o no se responde en unos 25 s, devuelve este código.
                    _ => (true, "El sistema no permitió cambiar el fondo: el permiso se denegó o no se respondió a tiempo. " +
                                "Puedes cambiar el permiso en la configuración de privacidad del escritorio.")
                };
            }
            catch (Exception e)
            {
                return (false, $"El portal de escritorio (xdg-desktop-portal) no está disponible: {e.Message}");
            }
        }
    }
}
