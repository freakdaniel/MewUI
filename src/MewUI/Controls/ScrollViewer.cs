using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A scrollable content container with the standard MewUI scrollbar chrome.
/// </summary>
public sealed class ScrollViewer : ScrollHost
{
    // Keep the original public descriptors as aliases so existing bindings and source references keep
    // resolving while the actual property owner moves to the extensible ScrollHost base class.
    public new static readonly MewProperty<ScrollMode> VerticalScrollProperty = ScrollHost.VerticalScrollProperty;
    public new static readonly MewProperty<ScrollMode> HorizontalScrollProperty = ScrollHost.HorizontalScrollProperty;
    public new static readonly MewProperty<double> VerticalOffsetProperty = ScrollHost.VerticalOffsetProperty;
    public new static readonly MewProperty<double> HorizontalOffsetProperty = ScrollHost.HorizontalOffsetProperty;
    public new static readonly MewProperty<bool> AutoHideScrollBarsProperty = ScrollHost.AutoHideScrollBarsProperty;

    private readonly ScrollBar _vBar;
    private readonly ScrollBar _hBar;
    private readonly ScrollBarFade _barFade;

    /// <summary>
    /// Initializes a new instance of the <see cref="ScrollViewer"/> class.
    /// </summary>
    public ScrollViewer()
    {
        _vBar = new ScrollBar { Orientation = Orientation.Vertical, IsVisible = false };
        _hBar = new ScrollBar { Orientation = Orientation.Horizontal, IsVisible = false };

        _vBar.Parent = this;
        _hBar.Parent = this;

        _barFade = new ScrollBarFade(this, _vBar, _hBar, InvalidateVisual);

        // A bar keeps the fade revealed while it holds the capture; re-evaluate once the drag lets go.
        _vBar.CaptureEnded = OnScrollChromeCaptureEnded;
        _hBar.CaptureEnded = OnScrollChromeCaptureEnded;

        _vBar.ValueChanged += value =>
        {
            SetVerticalOffsetCore(value);
            InvalidateVisual();
            ReevaluateMouseOverAfterScroll();
            NotifyScrollChanged();
        };

        _hBar.ValueChanged += value =>
        {
            SetHorizontalOffsetCore(value);
            InvalidateVisual();
            ReevaluateMouseOverAfterScroll();
            NotifyScrollChanged();
        };
    }

    // Preserve the old instance member surface explicitly. Besides documenting compatibility, these
    // forwarding members keep reflection and compiled callers that bind to ScrollViewer's members stable.
    public new bool AutoHideScrollBars
    {
        get => base.AutoHideScrollBars;
        set => base.AutoHideScrollBars = value;
    }

    public new event Action? ScrollChanged
    {
        add => base.ScrollChanged += value;
        remove => base.ScrollChanged -= value;
    }

    public new ScrollMode VerticalScroll
    {
        get => base.VerticalScroll;
        set => base.VerticalScroll = value;
    }

    public new ScrollMode HorizontalScroll
    {
        get => base.HorizontalScroll;
        set => base.HorizontalScroll = value;
    }

    public new double VerticalOffset => base.VerticalOffset;
    public new double HorizontalOffset => base.HorizontalOffset;
    public new double ViewportWidth => base.ViewportWidth;
    public new double ViewportHeight => base.ViewportHeight;

    public new void SetScrollOffsets(double horizontalOffset, double verticalOffset)
        => base.SetScrollOffsets(horizontalOffset, verticalOffset);

    public new void ScrollBy(double notches)
        => base.ScrollBy(notches);

    public new void ScrollByHorizontal(double notches)
        => base.ScrollByHorizontal(notches);

    protected override void OnAutoHideScrollBarsChanged(bool enabled)
    {
        base.OnAutoHideScrollBarsChanged(enabled);
        _barFade.Reset(enabled);
    }

    protected override void UpdateScrollChrome(bool horizontalVisible, bool verticalVisible)
    {
        _vBar.IsVisible = verticalVisible;
        _hBar.IsVisible = horizontalVisible;
        _vBar.RenderOpacity = AutoHideScrollBars ? _barFade.Opacity : 1.0;
        _hBar.RenderOpacity = AutoHideScrollBars ? _barFade.Opacity : 1.0;
    }

    protected override void SyncScrollChrome()
    {
        double viewportW = GetScrollViewport(horizontal: true);
        double viewportH = GetScrollViewport(horizontal: false);
        double maxH = GetScrollMaximum(horizontal: true);
        double maxV = GetScrollMaximum(horizontal: false);

        if (_vBar.IsVisible)
        {
            _vBar.Minimum = 0;
            _vBar.Maximum = maxV;
            _vBar.ViewportSize = viewportH;
            _vBar.SmallChange = Theme.Metrics.ScrollBarSmallChange;
            _vBar.LargeChange = Theme.Metrics.ScrollBarLargeChange;
            _vBar.Value = GetScrollOffset(horizontal: false);
        }

        if (_hBar.IsVisible)
        {
            _hBar.Minimum = 0;
            _hBar.Maximum = maxH;
            _hBar.ViewportSize = viewportW;
            _hBar.SmallChange = Theme.Metrics.ScrollBarSmallChange;
            _hBar.LargeChange = Theme.Metrics.ScrollBarLargeChange;
            _hBar.Value = GetScrollOffset(horizontal: true);
        }
    }

    protected override void ArrangeScrollChrome(Rect viewport)
    {
        double t = Theme.Metrics.ScrollBarHitThickness;
        const double inset = 0;

        if (_vBar.IsVisible)
        {
            _vBar.Arrange(new Rect(
                viewport.Right - t - inset,
                viewport.Y + inset,
                t,
                Math.Max(0, viewport.Height - (_hBar.IsVisible ? t : 0) - inset * 2)));
        }

        if (_hBar.IsVisible)
        {
            _hBar.Arrange(new Rect(
                viewport.X + inset,
                viewport.Bottom - t - inset,
                Math.Max(0, viewport.Width - (_vBar.IsVisible ? t : 0) - inset * 2),
                t));
        }
    }

    protected override void RenderScrollChrome(IGraphicsContext context)
    {
        // Scrollbars render on top of the clipped content.
        if (_vBar.IsVisible)
        {
            _vBar.Render(context);
        }

        if (_hBar.IsVisible)
        {
            _hBar.Render(context);
        }
    }

    protected override bool TryHitTestScrollChrome(Point point, out UIElement? hit)
    {
        if (_vBar.IsVisible && _vBar.Bounds.Contains(point))
        {
            hit = _vBar;
            return true;
        }

        if (_hBar.IsVisible && _hBar.Bounds.Contains(point))
        {
            hit = _hBar;
            return true;
        }

        hit = null;
        return false;
    }

    protected override bool VisitScrollChrome(Func<Element, bool> visitor)
    {
        if (!visitor(_vBar))
        {
            return false;
        }

        return visitor(_hBar);
    }

    protected override void OnScrollPointerWheel()
    {
        if (AutoHideScrollBars)
        {
            _barFade.UpdateHot();
        }
    }

    protected override void OnScrollPointerMove()
    {
        if (AutoHideScrollBars)
        {
            _barFade.UpdateHot();
        }
    }

    protected override void OnScrollPointerLeave()
    {
        if (AutoHideScrollBars)
        {
            _barFade.ClearHot();
        }
    }

    protected override void OnScrollChromeCaptureEnded()
    {
        if (AutoHideScrollBars)
        {
            _barFade.UpdateHot();
        }
    }

    // Called when the offset actually moves; reveals the auto-hidden bars for the idle window.
    protected override void OnScrolled()
    {
        if (AutoHideScrollBars)
        {
            _barFade.NotifyScrolled();
        }
    }

    protected override void OnDispose()
    {
        _barFade.Dispose();

        if (_vBar is IDisposable dv)
        {
            dv.Dispose();
        }

        if (_hBar is IDisposable dh)
        {
            dh.Dispose();
        }

        base.OnDispose();
    }

    /// <summary>
    /// Overlay scrollbar auto-hide fade state machine. Bars stay hidden at rest and fade in on scroll
    /// or hover, then fade back out after an idle delay or on leave.
    /// </summary>
    private sealed class ScrollBarFade
    {
        private readonly ScrollViewer _owner;
        private readonly ScrollBar _vBar;
        private readonly ScrollBar _hBar;
        private readonly Action _invalidate;

        private bool _hot;
        private bool _scrollActive;
        private double _opacity;
        private double _fadeFrom;
        private double _fadeTarget;
        private DispatcherTimer? _idleTimer;
        private AnimationClock? _fadeClock;

        public ScrollBarFade(ScrollViewer owner, ScrollBar vBar, ScrollBar hBar, Action invalidate)
        {
            _owner = owner;
            _vBar = vBar;
            _hBar = hBar;
            _invalidate = invalidate;
        }

        /// <summary>Current thumb opacity from 0 hidden to 1 shown.</summary>
        public double Opacity => _opacity;

        /// <summary>Resets to the mode baseline.</summary>
        public void Reset(bool enabled)
        {
            _idleTimer?.Stop();
            _fadeClock?.Stop();
            _scrollActive = false;
            _hot = false;
            _opacity = enabled ? 0.0 : 1.0;
        }

        /// <summary>Reveals the bars on a real scroll, then fades them out after an idle delay.</summary>
        public void NotifyScrolled()
        {
            _scrollActive = true;
            (_idleTimer ??= CreateIdleTimer()).Stop();
            _idleTimer.Start();
            UpdateFade();
        }

        /// <summary>Recomputes hover from the bars and refreshes the fade.</summary>
        public void UpdateHot()
        {
            bool hot = IsBarHot(_vBar) || IsBarHot(_hBar);
            if (hot == _hot)
            {
                return;
            }

            _hot = hot;
            if (hot)
            {
                _idleTimer?.Stop();
                _scrollActive = false;
            }
            UpdateFade();
        }

        /// <summary>Clears hover and lets the bars fade out unless a thumb is being dragged.</summary>
        public void ClearHot()
        {
            if (!_hot || _vBar.IsMouseCaptured || _hBar.IsMouseCaptured)
            {
                return;
            }

            _hot = false;
            UpdateFade();
        }

        private static bool IsBarHot(ScrollBar bar) => bar.IsVisible && (bar.IsMouseOver || bar.IsMouseCaptured);

        public void Dispose()
        {
            _idleTimer?.Stop();
            _fadeClock?.Stop();
        }

        private DispatcherTimer CreateIdleTimer()
        {
            var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(900));
            timer.Tick += () =>
            {
                timer.Stop();
                _scrollActive = false;
                UpdateFade();
            };
            return timer;
        }

        private void UpdateFade() => StartFade(_hot || _scrollActive ? 1.0 : 0.0);

        private void StartFade(double target)
        {
            if (target.Equals(_fadeTarget) && _fadeClock is { IsRunning: true })
            {
                return;
            }

            _fadeFrom = _opacity;
            _fadeTarget = target;
            if (_fadeFrom.Equals(target))
            {
                return;
            }

            _fadeClock ??= CreateFadeClock();
            _fadeClock.Stop();
            _fadeClock.Duration = TimeSpan.FromMilliseconds(target > _fadeFrom ? 140 : 320);
            _fadeClock.Start();
        }

        private AnimationClock CreateFadeClock()
        {
            var clock = new AnimationClock(TimeSpan.FromMilliseconds(200), Easing.EaseOutCubic)
                .AttachTo(_owner);
            clock.TickCallback = progress =>
            {
                _opacity = Math.Clamp(_fadeFrom + (_fadeTarget - _fadeFrom) * progress, 0, 1);
                _vBar.RenderOpacity = _opacity;
                _hBar.RenderOpacity = _opacity;
                _invalidate();
            };
            return clock;
        }
    }
}
