using System.Diagnostics;

namespace Inscribed.Cli.Ui;

internal sealed class Activity : IDisposable
{
    private const int DelayMilliseconds = 150;

    private const int FrameMilliseconds = 90;

    private static readonly string[] Frames = ["·", "✢", "✳", "✶", "✻", "✽", "✻", "✶", "✳", "✢"];

    private readonly Screen _screen;
    private readonly string _label;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Lock _gate = new();
    private readonly Timer _timer;
    private int _suspended;
    private bool _stopped;

    public Activity(Screen screen, string label)
    {
        _screen = screen;
        _label = label;
        _timer = new Timer(_ => Tick(), null, DelayMilliseconds, FrameMilliseconds);
    }

    public IDisposable Suspend()
    {
        lock (_gate)
        {
            _suspended++;
            _screen.Hide();
        }

        return new Release(() =>
        {
            lock (_gate)
            {
                _suspended--;
            }
        });
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _stopped = true;
            _timer.Dispose();
            _screen.Hide();
        }
    }

    private void Tick()
    {
        lock (_gate)
        {
            if (_stopped || _suspended > 0)
            {
                return;
            }

            var frame = Frames[(int)(_clock.ElapsedMilliseconds / FrameMilliseconds % Frames.Length)];
            var seconds = (int)_clock.Elapsed.TotalSeconds;
            var elapsed = seconds > 0 ? Output.Dim($" ({seconds}s)") : string.Empty;

            _screen.Show([$"  {Output.Accent(frame)} {_label}{Output.Dim("…")}{elapsed}"]);
        }
    }
}
