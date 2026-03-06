using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Sup
{
    public partial class App : Application
    {
        // ƒÀﬂ ÀŒ ¿À‹Õ€’ “≈—“Œ¬ «¿Ã≈Õ»“‹ ApiBaseUrl Õ¿ "http://localhost:8080/api/v1/"
        public static string ApiBaseUrl { get; } = "https://ample-determination-dev.up.railway.app/api/v1/";
        public static string WsBaseUrl { get; } = "ws://ample-determination-dev.up.railway.app/api/v1/message/ws";
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}