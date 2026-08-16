using System.Windows.Input;

namespace NuRcade.Editor;

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> m_execute;
    private readonly Predicate<object?>? m_canExecute;

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        m_execute = execute;
        m_canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return m_canExecute?.Invoke(parameter) ?? true;
    }

    public void Execute(object? parameter)
    {
        m_execute(parameter);
    }

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
