using System.ComponentModel;
using System.Runtime.CompilerServices;
using WinRaycastEditor.Core;

namespace WinRaycastEditor;

public sealed class PlayerStartViewModel : INotifyPropertyChanged
{
    public PlayerStartViewModel(WorldPlayerStart playerStart)
    {
        PlayerStart = playerStart;
    }

    public WorldPlayerStart PlayerStart { get; }

    public double XCell
    {
        get => PlayerStart.XCell;
        set
        {
            if (Math.Abs(PlayerStart.XCell - value) < 0.001) {
                return;
            }

            var before = Capture();
            PlayerStart.XCell = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Position));
            NotifyStartChanged(before);
        }
    }

    public double YCell
    {
        get => PlayerStart.YCell;
        set
        {
            if (Math.Abs(PlayerStart.YCell - value) < 0.001) {
                return;
            }

            var before = Capture();
            PlayerStart.YCell = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Position));
            NotifyStartChanged(before);
        }
    }

    public double FacingDegrees
    {
        get => PlayerStart.FacingDegrees;
        set
        {
            if (Math.Abs(PlayerStart.FacingDegrees - value) < 0.001) {
                return;
            }

            var before = Capture();
            PlayerStart.FacingDegrees = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Facing));
            NotifyStartChanged(before);
        }
    }

    public string Position => $"{PlayerStart.XCell:0.##}, {PlayerStart.YCell:0.##}";
    public string Facing => $"{PlayerStart.FacingDegrees:0.#} deg";

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<PlayerStartChangedEventArgs>? StartChanged;

    private WorldPlayerStart Capture()
    {
        return new WorldPlayerStart {
            XCell = PlayerStart.XCell,
            YCell = PlayerStart.YCell,
            FacingDegrees = PlayerStart.FacingDegrees
        };
    }

    private void NotifyStartChanged(WorldPlayerStart before)
    {
        StartChanged?.Invoke(this, new PlayerStartChangedEventArgs(before, Capture()));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class PlayerStartChangedEventArgs : EventArgs
{
    public PlayerStartChangedEventArgs(WorldPlayerStart before, WorldPlayerStart after)
    {
        Before = before;
        After = after;
    }

    public WorldPlayerStart Before { get; }
    public WorldPlayerStart After { get; }
}
