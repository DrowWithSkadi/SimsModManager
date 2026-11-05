using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SimsModManager.App.ViewModels;

public enum DialogResult { Cancel, Primary, Extra }

/// <summary>Модальное окно внутри программы (по макету «Диалоги и уведомления»).</summary>
public sealed partial class DialogViewModel : ObservableObject
{
    private readonly TaskCompletionSource<DialogResult> _tcs = new();

    public required string Title { get; init; }
    public string? Message { get; init; }
    public string? Message2 { get; init; }
    public Geometry? Icon { get; init; }
    public bool IconIsDanger { get; init; }

    /// <summary>Плашка с данными (например, мод, который удаляем).</summary>
    public string? InfoTitle { get; init; }
    public string? InfoSubtitle { get; init; }

    /// <summary>Технические подробности ошибки — моноширинным шрифтом, их можно скопировать.</summary>
    public string? Details { get; init; }

    /// <summary>Путь к папке — в рамке, как поле.</summary>
    public string? PathText { get; init; }

    public required string PrimaryText { get; init; }
    public bool PrimaryIsDanger { get; init; }
    public string? SecondaryText { get; init; }
    public string? ExtraText { get; init; }

    public bool HasMessage2 => !string.IsNullOrEmpty(Message2);
    public bool HasInfo => !string.IsNullOrEmpty(InfoTitle);
    public bool HasDetails => !string.IsNullOrEmpty(Details);
    public bool HasPath => !string.IsNullOrEmpty(PathText);
    public bool HasSecondary => !string.IsNullOrEmpty(SecondaryText);
    public bool HasExtra => !string.IsNullOrEmpty(ExtraText);

    public Task<DialogResult> Result => _tcs.Task;

    [RelayCommand]
    private void Primary() => _tcs.TrySetResult(DialogResult.Primary);

    [RelayCommand]
    private void Cancel() => _tcs.TrySetResult(DialogResult.Cancel);

    [RelayCommand]
    private void Extra()
    {
        // «Скопировать» не закрывает окно ошибки.
        if (Details is not null)
        {
            try { Clipboard.SetText(Details); } catch (Exception) { /* буфер занят — не страшно */ }
            Copied = true;
            return;
        }
        _tcs.TrySetResult(DialogResult.Extra);
    }

    [ObservableProperty] private bool _copied;
}

/// <summary>Ненавязчивое уведомление внизу окна, исчезает само.</summary>
public sealed partial class ToastViewModel : ObservableObject
{
    private readonly Action<ToastViewModel> _close;

    public ToastViewModel(string message, Geometry icon, Action<ToastViewModel> close, Action? undo = null)
    {
        Message = message;
        Icon = icon;
        _close = close;
        UndoAction = undo;
    }

    public string Message { get; }
    public Geometry Icon { get; }
    public Action? UndoAction { get; }
    public bool CanUndo => UndoAction is not null;

    [ObservableProperty] private bool _isVisible = true;

    [RelayCommand]
    private void Close() => _close(this);

    [RelayCommand]
    private void Undo()
    {
        UndoAction?.Invoke();
        _close(this);
    }
}
