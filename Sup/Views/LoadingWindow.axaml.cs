using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using System;

namespace Sup.Views
{
    public partial class LoadingWindow : Window
    {
        private DispatcherTimer? _timer;
        private double _angle = 0;

        public LoadingWindow()
        {
            InitializeComponent();

            // Явно устанавливаем RenderTransformOrigin для SpinnerArc
            SpinnerArc.RenderTransformOrigin = new Avalonia.RelativePoint(0.5, 0.5, Avalonia.RelativeUnit.Relative);
            SpinnerArc.RenderTransform = new RotateTransform(0);

            // Таймер ~60 fps — 6° за тик = 1 оборот в секунду
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _timer.Tick += (s, e) =>
            {
                _angle = (_angle + 6) % 360;
                SpinnerArc.RenderTransform = new RotateTransform(_angle);
            };
            _timer.Start();

            this.Closed += (s, e) => _timer?.Stop();
        }
    }
}
