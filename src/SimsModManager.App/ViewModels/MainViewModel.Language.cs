using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SimsModManager.Core;

namespace SimsModManager.App.ViewModels;

/// <summary>Язык интерфейса: выбор в настройках и обновление текстов на лету.</summary>
public sealed partial class MainViewModel
{
    /// <summary>«auto» — как в Windows, «ru» или «en».</summary>
    [ObservableProperty] private string _language = "auto";

    [RelayCommand]
    private void SetLanguage(string value) => Language = value;

    partial void OnLanguageChanged(string value)
    {
        if (_loadingSettings) return;
        _settings.Language = value is L.Russian or L.English ? value : null;
        SaveSettings();
        L.SetLanguage(L.Resolve(_settings.Language));
    }

    private void HookLanguage() => L.Changed += OnUiLanguageChanged;

    /// <summary>
    /// Тексты в разметке обновляются сами. Здесь — то, что программа собирает в коде:
    /// подписи модов, сводки, найденные проблемы (их заново проверяем, они пишутся на нужном языке).
    /// </summary>
    private async void OnUiLanguageChanged()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return;
        if (!dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(OnUiLanguageChanged);
            return;
        }
        try
        {
            foreach (var m in Mods) m.RefreshTexts();
            Card?.RefreshTexts();
            Editor?.RefreshTexts();
            LastActionText = L.T("Готово к работе");
            UpdateSummary();
            ScanPacks();
            RefreshHelperStatus();
            OnPropertyChanged(string.Empty);
            await RefreshAsync();
        }
        catch (Exception)
        {
            // смена языка не должна ронять программу
        }
    }
}
