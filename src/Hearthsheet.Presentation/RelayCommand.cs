using System.Windows.Input;

namespace Hearthsheet.Presentation;

/// <summary>
/// Base of the relay commands: <see cref="CanExecuteChanged"/> is an ordinary event, raised for every live command
/// by <see cref="CommandRequery.Raise"/>.
/// </summary>
public abstract class RequeryCommand : ICommand
{
    protected RequeryCommand() => CommandRequery.Register(this);

    public event EventHandler? CanExecuteChanged;

    public abstract bool CanExecute(object? parameter);
    public abstract void Execute(object? parameter);

    internal void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : RequeryCommand
{
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute()) { }

    /// <summary>Runs the task fire-and-forget; a failure surfaces on the UI's unhandled-exception path, as a throwing synchronous command does.</summary>
    public RelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
        : this(_ => Run(execute()), canExecute is null ? null : _ => canExecute()) { }

    public RelayCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null)
        : this(p => Run(execute(p)), canExecute) { }

    private static async void Run(Task task) => await task;

    public override bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;
    public override void Execute(object? parameter) => execute(parameter);
}

/// <summary>Command taking a typed parameter; ignores parameters of other types.</summary>
public sealed class RelayCommand<T>(Action<T> execute, Func<T, bool>? canExecute = null) : RequeryCommand
{
    public override bool CanExecute(object? parameter) => parameter is T t && (canExecute?.Invoke(t) ?? true);

    public override void Execute(object? parameter)
    {
        if (parameter is T t) execute(t);
    }
}

/// <summary>
/// Asks every live command to re-evaluate <see cref="ICommand.CanExecute"/>. The WPF app calls <see cref="Raise"/> from
/// <c>CommandManager.RequerySuggested</c>; a web UI calls it after handling an event. Commands are held weakly, so the
/// registry never keeps a view model alive.
/// </summary>
public static class CommandRequery
{
    private static readonly List<WeakReference<RequeryCommand>> Commands = [];

    public static void Raise()
    {
        var live = new List<RequeryCommand>();
        lock (Commands)
        {
            Commands.RemoveAll(r => !r.TryGetTarget(out _));
            foreach (var r in Commands)
                if (r.TryGetTarget(out var command)) live.Add(command);
        }
        // Raised outside the lock: a handler may create commands.
        foreach (var command in live) command.RaiseCanExecuteChanged();
    }

    internal static int LiveCountForTests
    {
        get { lock (Commands) return Commands.Count; }
    }

    internal static void Register(RequeryCommand command)
    {
        lock (Commands) Commands.Add(new WeakReference<RequeryCommand>(command));
    }
}
