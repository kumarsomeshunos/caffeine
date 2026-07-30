using System;
using System.Windows.Controls;
using System.Windows.Threading;

namespace CaffeineWin.Controls;

/// <summary>
/// Wheel easing for one scroll viewer. Deliberately per-viewer: a single shared target makes
/// scrolling one pane disturb another mid-animation.
/// </summary>
internal sealed class SmoothScroller
{
    private const double PixelsPerNotch = 0.4;
    private const double ApproachPerFrame = 0.2;

    private readonly ScrollViewer _view;
    private readonly DispatcherTimer _timer;
    private double _target;
    private bool _running;

    public SmoothScroller(ScrollViewer view)
    {
        _view = view;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += OnTick;
    }

    public void Nudge(int delta)
    {
        if (!_running) _target = _view.VerticalOffset;

        _target = Math.Clamp(_target - delta * PixelsPerNotch, 0, _view.ScrollableHeight);

        if (_running) return;

        _running = true;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var gap = _target - _view.VerticalOffset;

        if (Math.Abs(gap) < 0.5)
        {
            _view.ScrollToVerticalOffset(_target);
            _timer.Stop();
            _running = false;
            return;
        }

        _view.ScrollToVerticalOffset(_view.VerticalOffset + gap * ApproachPerFrame);
    }
}
