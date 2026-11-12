using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SimsModManager.App.Overlay;

public partial class OverlayWindow : Window
{
    public OverlayWindow()
    {
        InitializeComponent();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            CloseRequested?.Invoke();
        };
        // Alt+F4 тоже просто прячет оверлей — окно живёт всё время работы программы
        Closing += (_, e) =>
        {
            if (_allowClose) return;
            e.Cancel = true;
            CloseRequested?.Invoke();
        };

        var pulse = new DoubleAnimation(1, 0.35, TimeSpan.FromMilliseconds(700))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        BusyDot.BeginAnimation(OpacityProperty, pulse);
    }

    private bool _allowClose;

    public event Action? CloseRequested;

    public void CloseForReal()
    {
        _allowClose = true;
        Close();
    }

    /// <summary>Короткое появление: 140 мс, только прозрачность и сдвиг — это дёшево для видеокарты.</summary>
    public void PlayAppear()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(140);
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = ease });
        if (Root.RenderTransform is TranslateTransform move)
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, duration) { EasingFunction = ease });
        NameBox.Focus();
    }

    private void OnNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not OverlayViewModel vm) return;
        e.Handled = true;
        if (vm.MakeCheckpointCommand.CanExecute(null)) vm.MakeCheckpointCommand.Execute(null);
    }
}

public sealed class InverseVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
