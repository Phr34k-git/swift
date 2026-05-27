using System;
using System.Windows.Input;

namespace Client.ViewModels;

/// <summary>
/// Minimal command implementation for view models.
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Func<object?, bool>? _canExecute;
    private readonly Func<object?, System.Threading.Tasks.Task> _executeAsync;

    /// <summary>
    /// Creates a command from asynchronous execute and optional can-execute delegates.
    /// </summary>
    public RelayCommand(
        Func<object?, System.Threading.Tasks.Task> executeAsync,
        Func<object?, bool>? canExecute = null)
    {
        _executeAsync = executeAsync;
        _canExecute = canExecute;
    }

    /// <summary>
    /// Fires when the command can-execute state changes.
    /// </summary>
    public event EventHandler? CanExecuteChanged;

    /// <summary>
    /// Returns whether the command can execute.
    /// </summary>
    public bool CanExecute(object? parameter)
    {
        return _canExecute?.Invoke(parameter) ?? true;
    }

    /// <summary>
    /// Executes the command.
    /// </summary>
    public async void Execute(object? parameter)
    {
        await _executeAsync(parameter);
    }

    /// <summary>
    /// Raises the command can-execute changed event.
    /// </summary>
    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
