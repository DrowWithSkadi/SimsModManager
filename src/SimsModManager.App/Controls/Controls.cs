using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace SimsModManager.App.Controls;

/// <summary>Иконка Lucide: контур 24×24, масштабируется под размер элемента.</summary>
public sealed class IconView : Control
{
    static IconView()
    {
        FocusableProperty.OverrideMetadata(typeof(IconView), new FrameworkPropertyMetadata(false));
        IsHitTestVisibleProperty.OverrideMetadata(typeof(IconView), new FrameworkPropertyMetadata(false));
    }

    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(Geometry), typeof(IconView));

    public Geometry? Data
    {
        get => (Geometry?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(IconView), new PropertyMetadata(2.0));

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }
}

/// <summary>Дополнительные свойства кнопок: иконка слева от текста.</summary>
public static class Btn
{
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.RegisterAttached("Icon", typeof(Geometry), typeof(Btn), new PropertyMetadata(null));

    public static Geometry? GetIcon(DependencyObject d) => (Geometry?)d.GetValue(IconProperty);
    public static void SetIcon(DependencyObject d, Geometry? value) => d.SetValue(IconProperty, value);
}

/// <summary>Поле поиска с иконкой, подсказкой «Ctrl + F» и кнопкой очистки.</summary>
public sealed class SearchBox : TextBox
{
    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(nameof(Placeholder), typeof(string), typeof(SearchBox), new PropertyMetadata(""));

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("PART_Clear") is ButtonBase clear)
            clear.Click += (_, _) =>
            {
                Clear();
                Focus();
            };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && Text.Length > 0)
        {
            Clear();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }
}
