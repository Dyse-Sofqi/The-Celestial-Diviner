using System.Windows.Input;

namespace TheCelestialDiviner.ViewModels;

/// <summary>通用命令实现（MVVM）。</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    /// <summary>创建命令。</summary>
    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    /// <summary>创建无参数命令。</summary>
    public static RelayCommand Create(Action execute, Func<bool>? canExecute = null)
        => new(_ => execute(), canExecute is null ? null : _ => canExecute());

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);
}
