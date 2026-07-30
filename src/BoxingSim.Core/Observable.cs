using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BoxingSim.Core;

/// <summary>The smallest possible INotifyPropertyChanged base.
///
/// It lives in Core rather than the desktop project because FightCall does — and FightCall is simulation
/// output, not chrome: eight hundred lines turning a FightResult into the words a commentator would say, with
/// no reference to WPF of any kind. It sat in a WinExe purely by accident of where it was first written, which
/// meant the test project could not reference it and none of it was ever covered.
///
/// Nothing here is WPF. INotifyPropertyChanged is System.ComponentModel, available to any .NET project, and a
/// console or web front end would bind to it just as happily.</summary>
public class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Tell every binding on this object to re-read. A null property name is the framework's "assume
    /// all of them changed", which is what a full refresh actually means — and what a hand-maintained list of
    /// property names only ever approximates.</summary>
    protected void RaiseAll() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
