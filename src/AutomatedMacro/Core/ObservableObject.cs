using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AutomatedMacro;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}

/// <summary>Javlja da se sadrzaj procedure promijenio (za oznaku "nespremljeno").</summary>
public static class ChangeTracker
{
    private static int _suspended;

    public static event Action? Changed;

    /// <summary>Promijenio se raspored cvorova ili prosirenost grupa (treba osvjeziti tablicu).</summary>
    public static event Action? StructureChanged;

    public static void Notify()
    {
        if (_suspended == 0) Changed?.Invoke();
    }

    public static void NotifyStructure()
    {
        if (_suspended == 0) StructureChanged?.Invoke();
    }

    public static IDisposable Suspend()
    {
        _suspended++;
        return new Resumer();
    }

    private sealed class Resumer : IDisposable
    {
        private bool _done;
        public void Dispose()
        {
            if (_done) return;
            _done = true;
            _suspended--;
        }
    }
}
