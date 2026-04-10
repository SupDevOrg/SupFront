using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Sup
{
    public partial class App : Application
    {

        // ÄËß ËÎÊÀËÜÍÛÕ ÒÅÑÒÎÂ ÇÀÌÅÍÈÒÜ ApiBaseUrl ÍÀ "http://localhost:8080/api/v1/"
        public static string ApiBaseUrl { get; } = "https://ample-determination-dev.up.railway.app/api/v1/";
        public static string WsBaseUrl { get; } = "ws://ample-determination-dev.up.railway.app/api/v1/message/ws";

        // Ñèãíàëüíûé ñåğâåğ äëÿ WebRTC
        public static string SignalingBaseUrl { get; } = "wss://signalservice-dev.up.railway.app";
        public static string SignalingUsername { get; } = "7093a2c122d9710f49d53b9f";
        public static string SignalingPassword { get; } = "ePGIsuBqmM5VrSo";
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