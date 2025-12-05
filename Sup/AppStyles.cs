using Avalonia;
using Avalonia.Media;

namespace Sup
{
    public static class AppStyles
    {
        // Кисти для цветовой схемы
        public static SolidColorBrush AppBackgroundBrush => new SolidColorBrush(Color.FromRgb(245, 247, 250));
        public static SolidColorBrush SurfaceBrush => new SolidColorBrush(Color.FromRgb(255, 255, 255));
        public static SolidColorBrush SurfaceAltBrush => new SolidColorBrush(Color.FromRgb(248, 250, 252));
        public static SolidColorBrush TextPrimaryBrush => new SolidColorBrush(Color.FromRgb(33, 37, 41));
        public static SolidColorBrush TextSecondaryBrush => new SolidColorBrush(Color.FromRgb(108, 117, 125));

        // Радиусы скругления
        public static CornerRadius CardCornerRadius => new CornerRadius(16);
        public static CornerRadius ControlCornerRadius => new CornerRadius(8);

        // Эффекты
        public static DropShadowEffect SoftShadowEffect => new DropShadowEffect
        {
            OffsetX = 0,
            OffsetY = 4,
            BlurRadius = 12,
            Color = Color.FromArgb(40, 0, 0, 0)
        };
    }
}