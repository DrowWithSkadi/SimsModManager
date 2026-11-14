using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using SimsModManager.Core;

namespace SimsModManager.App.Localization;

/// <summary>
/// Переведённый текст в XAML: Text="{l:Tr 'Библиотека'}". Русский текст — это ключ,
/// перевод берётся из Core/Localization/en.json. При смене языка текст меняется сразу, без перезапуска.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension() { }

    public TrExtension(string text) => Text = text;

    [ConstructorArgument("text")]
    public string Text { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        // Обычное свойство (не DependencyProperty) нельзя привязать — отдаём готовую строку
        if (serviceProvider.GetService(typeof(IProvideValueTarget)) is IProvideValueTarget target
            && target.TargetObject is not Setter
            && target.TargetProperty is System.Reflection.PropertyInfo)
            return L.T(Text);

        var binding = new Binding(nameof(LanguageSource.Version))
        {
            Source = LanguageSource.Instance,
            Mode = BindingMode.OneWay,
            Converter = TrConverter.Instance,
            ConverterParameter = Text,
        };
        return binding.ProvideValue(serviceProvider);
    }
}

/// <summary>Источник для привязок: «версия» языка растёт при каждой смене — привязки перечитывают текст.</summary>
public sealed class LanguageSource : INotifyPropertyChanged
{
    public static LanguageSource Instance { get; } = new();

    private int _version;

    private LanguageSource() => L.Changed += () =>
    {
        Interlocked.Increment(ref _version);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Version)));
    };

    public int Version => _version;

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class TrConverter : IValueConverter
{
    public static TrConverter Instance { get; } = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        L.T(parameter as string ?? "");

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
