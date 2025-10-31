using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Sup
{
    public partial class App : Application
    {
        // ƒÀﬂ ÀŒ ¿À‹Õ€’ “≈—“Œ¬ «¿Ã≈Õ»“‹ ApiBaseUrl Õ¿ "http://localhost:8080/api/v1/"
        public static string ApiBaseUrl { get; } = "http://81.200.152.249:80/api/v1/";
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