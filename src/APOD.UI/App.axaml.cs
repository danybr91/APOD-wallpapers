using Avalonia;
using APOD.Core;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace APOD.UI
{
    public class App : Application
    {
        private readonly ApodService _service;
        private readonly UiLogger _logger;

        public App(ApodService service, UiLogger logger)
        {
            _service = service;
            _logger = logger;
        }

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow(_service, _logger);
            }
            base.OnFrameworkInitializationCompleted();
        }
    }
}
