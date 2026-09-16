using Aprillz.MewUI.Input;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Scroll mode for a scroll host's scroll axes.
/// </summary>
public enum ScrollMode
{
    /// <summary>Scrolling is disabled.</summary>
    Disabled,
    /// <summary>Scroll chrome appears automatically when needed.</summary>
    Auto,
    /// <summary>Scroll chrome is always visible.</summary>
    Visible
}

/// <summary>
/// Base class for controls that display content through a scrollable viewport.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ScrollViewer"/> is the standard implementation with overlay scrollbars. Derive from
/// this class when the scroll physics, chrome, or content rendering needs to be customized.
/// </para>
/// <para>
/// Normal content is arranged in stable document coordinates. Scrolling changes the render transform
/// and the render cull mapping, so a scroll offset update does not force the content tree through layout.
/// Content that implements <see cref="IScrollContent"/> owns its own viewport-aware arrangement instead.
/// </para>
/// </remarks>
public abstract class ScrollHost : ContentControl, IVisualTreeHost, IFocusIntoViewHost
{
    public static readonly MewProperty<ScrollMode> VerticalScrollProperty =
        MewProperty<ScrollMode>.Register<ScrollHost>(nameof(VerticalScroll), ScrollMode.Auto, MewPropertyOptions.AffectsLayout);

    public static readonly MewProperty<ScrollMode> HorizontalScrollProperty =
        MewProperty<ScrollMode>.Register<ScrollHost>(nameof(HorizontalScroll), ScrollMode.Disabled, MewPropertyOptions.AffectsLayout);

    private static readonly MewPropertyKey<double> VerticalOffsetPropertyKey =
        MewProperty<double>.RegisterReadOnly<ScrollHost>(nameof(VerticalOffset), 0);
    public static readonly MewProperty<double> VerticalOffsetProperty = VerticalOffsetPropertyKey.Property;

    private static readonly MewPropertyKey<double> HorizontalOffsetPropertyKey =
        MewProperty<double>.RegisterReadOnly<ScrollHost>(nameof(HorizontalOffset), 0);
    public static readonly MewProperty<double> HorizontalOffsetProperty = HorizontalOffsetPropertyKey.Property;

    private readonly ScrollController _scroll = new();

    private Size _extent = Size.Empty;
    private Size _viewport = Size.Empty;
    private Size _lastNotifiedExtent = Size.Empty;
    private Size _lastNotifiedViewport = Size.Empty;
    private Point _lastNotifiedOffset = new(double.NaN, double.NaN);

    // Whether content overflows on each axis, independent of the host's chrome implementation.
    private bool _canScrollV;
    private bool _canScrollH;

    public static readonly MewProperty<bool> AutoHideScrollBarsProperty =
        MewProperty<bool>.Register<ScrollHost>(nameof(AutoHideScrollBars), false,
            MewPropertyOptions.AffectsLayout,
            static (self, _, newVal) => self.OnAutoHideScrollBarsChanged(newVal));

    /// <summary>
    /// When true, a scroll host's standard chrome may overlay content and reveal itself only while
    /// scrolling or hovering. The base class stores the setting; chrome implementations decide how
    /// to present it.
    /// </summary>
    public bool AutoHideScrollBars
    {
        get => GetValue(AutoHideScrollBarsProperty);
        set => SetValue(AutoHideScrollBarsProperty, value);
    }

    /// <summary>
    /// Raised when scroll metrics or offsets change.
    /// </summary>
    public event Action? ScrollChanged;

    /// <summary>
    /// Gets or sets the vertical scroll mode.
    /// </summary>
    public ScrollMode VerticalScroll
    {
        get => GetValue(VerticalScrollProperty);
        set => SetValue(VerticalScrollProperty, value);
    }

    /// <summary>
    /// Gets or sets the horizontal scroll mode.
    /// </summary>
    public ScrollMode HorizontalScroll
    {
        get => GetValue(HorizontalScrollProperty);
        set => SetValue(HorizontalScrollProperty, value);
    }

    /// <summary>
    /// Gets the vertical scroll offset. The value is published when the current scroll state is
    /// committed, together with <see cref="ScrollChanged"/>.
    /// </summary>
    public double VerticalOffset => GetValue(VerticalOffsetProperty);

    /// <summary>
    /// Gets the horizontal scroll offset. The value is published when the current scroll state is
    /// committed, together with <see cref="ScrollChanged"/>.
    /// </summary>
    public double HorizontalOffset => GetValue(HorizontalOffsetProperty);

    /// <summary>
    /// Gets the logical content extent width in DIPs.
    /// </summary>
    public double ExtentWidth => _extent.Width;

    /// <summary>
    /// Gets the logical content extent height in DIPs.
    /// </summary>
    public double ExtentHeight => _extent.Height;

    /// <summary>
    /// Gets the content viewport width in DIPs, excluding border inset and padding.
    /// </summary>
    public double ViewportWidth => _viewport.Width;

    /// <summary>
    /// Gets the content viewport height in DIPs, excluding border inset and padding.
    /// </summary>
    public double ViewportHeight => _viewport.Height;

    /// <summary>
    /// Gets whether the content currently overflows vertically.
    /// </summary>
    protected bool CanScrollVertically => _canScrollV;

    /// <summary>
    /// Gets whether the content currently overflows horizontally.
    /// </summary>
    protected bool CanScrollHorizontally => _canScrollH;

    /// <summary>
    /// Gets the current DPI scale used by the scroll metrics.
    /// </summary>
    protected double DpiScale => GetDpi() / 96.0;

    /// <summary>
    /// Sets both scroll offsets simultaneously.
    /// </summary>
    /// <param name="horizontalOffset">The horizontal offset.</param>
    /// <param name="verticalOffset">The vertical offset.</param>
    public void SetScrollOffsets(double horizontalOffset, double verticalOffset)
    {
        // Sync extent metrics before applying the offset; ScrollController.SetOffsetDip clamps against
        // (extent - viewport), and scroll-driven content may have grown before the next arrange.
        if (Content is IScrollContent scrollContent)
        {
            _scroll.DpiScale = DpiScale;
            var contentExtent = scrollContent.Extent;
            _scroll.SetMetricsDip(0, contentExtent.Width, _viewport.Width);
            _scroll.SetMetricsDip(1, contentExtent.Height, _viewport.Height);
        }

        SetHorizontalOffsetCore(horizontalOffset);
        SetVerticalOffsetCore(verticalOffset);
        SyncScrollChrome();
        InvalidateVisual();
        ReevaluateMouseOverAfterScroll();
        NotifyScrollChanged();
    }

    /// <summary>
    /// Applies a clamped vertical offset to the shared scroll state.
    /// </summary>
    /// <remarks>
    /// The method invalidates the visual when the offset changes. Call <see cref="NotifyScrollChanged"/>
    /// after changing one or both axes from a custom input or animation implementation.
    /// </remarks>
    protected bool SetVerticalOffsetCore(double value)
    {
        _scroll.DpiScale = DpiScale;
        if (!_scroll.SetOffsetDip(1, value))
        {
            return false;
        }

        InvalidateVisual();
        return true;
    }

    /// <summary>
    /// Applies a clamped horizontal offset to the shared scroll state.
    /// </summary>
    /// <remarks>
    /// The method invalidates the visual when the offset changes. Call <see cref="NotifyScrollChanged"/>
    /// after changing one or both axes from a custom input or animation implementation.
    /// </remarks>
    protected bool SetHorizontalOffsetCore(double value)
    {
        _scroll.DpiScale = DpiScale;
        if (!_scroll.SetOffsetDip(0, value))
        {
            return false;
        }

        InvalidateVisual();
        return true;
    }

    /// <summary>
    /// Gets the current maximum offset in DIPs for one axis.
    /// </summary>
    /// <param name="horizontal">True for the horizontal axis, false for the vertical axis.</param>
    protected double GetScrollMaximum(bool horizontal)
    {
        _scroll.DpiScale = DpiScale;
        return _scroll.GetMaxDip(horizontal ? 0 : 1);
    }

    /// <summary>
    /// Gets the current viewport size in DIPs for one axis.
    /// </summary>
    /// <param name="horizontal">True for the horizontal axis, false for the vertical axis.</param>
    protected double GetScrollViewport(bool horizontal)
    {
        _scroll.DpiScale = DpiScale;
        return _scroll.GetViewportDip(horizontal ? 0 : 1);
    }

    /// <summary>
    /// Gets the current live offset in DIPs. Unlike <see cref="VerticalOffset"/> and
    /// <see cref="HorizontalOffset"/>, this value is available before the pending scroll state is
    /// published through <see cref="ScrollChanged"/>.
    /// </summary>
    /// <param name="horizontal">True for the horizontal axis, false for the vertical axis.</param>
    protected double GetScrollOffset(bool horizontal)
    {
        _scroll.DpiScale = DpiScale;
        return _scroll.GetOffsetDip(horizontal ? 0 : 1);
    }

    bool IFocusIntoViewHost.OnDescendantFocused(UIElement focusedElement)
    {
        if (focusedElement == this || Content == null)
        {
            return false;
        }

        // Don't scroll when the focused element is the direct content itself - it spans the entire
        // scrollable area and scrolling it into view is nonsensical.
        if (focusedElement == Content)
        {
            return true;
        }

        var size = focusedElement.RenderSize;
        if (size.Width <= 0 || size.Height <= 0)
        {
            // An element that has not been arranged sits at the tree origin with an empty box, so
            // scrolling it into view would drag the viewport to the top instead of leaving it alone.
            return false;
        }

        var localRect = new Rect(0, 0, size.Width, size.Height);

        Rect rectInHost;
        try
        {
            // TranslateRect returns coords in ScrollHost-local space (relative to this.Bounds.TopLeft).
            rectInHost = focusedElement.TranslateRect(localRect, this);
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        // GetContentViewportBounds returns in parent coordinate space; convert to local space.
        var borderInset = GetBorderVisualInset();
        var vpParent = GetContentViewportBounds(Bounds, borderInset);
        var vp = new Rect(vpParent.X - Bounds.X, vpParent.Y - Bounds.Y, vpParent.Width, vpParent.Height);

        double newOffsetX = HorizontalOffset;
        double newOffsetY = VerticalOffset;

        if (_canScrollV)
        {
            if (rectInHost.Y < vp.Y)
                newOffsetY = VerticalOffset - (vp.Y - rectInHost.Y);
            else if (rectInHost.Bottom > vp.Bottom)
                newOffsetY = VerticalOffset + (rectInHost.Bottom - vp.Bottom);
        }

        if (_canScrollH)
        {
            if (rectInHost.X < vp.X)
                newOffsetX = HorizontalOffset - (vp.X - rectInHost.X);
            else if (rectInHost.Right > vp.Right)
                newOffsetX = HorizontalOffset + (rectInHost.Right - vp.Right);
        }

        // Clamp via ScrollController (DPI-aware pixel-accurate max) rather than raw extent arithmetic.
        _scroll.DpiScale = DpiScale;
        newOffsetX = Math.Clamp(newOffsetX, 0, _scroll.GetMaxDip(0));
        newOffsetY = Math.Clamp(newOffsetY, 0, _scroll.GetMaxDip(1));

        bool changed = !newOffsetX.Equals(HorizontalOffset) || !newOffsetY.Equals(VerticalOffset);
        if (changed)
        {
            SetScrollOffsets(newOffsetX, newOffsetY);
        }

        return true;
    }

    protected override void OnDpiChanged(uint oldDpi, uint newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);

        if (oldDpi == 0 || newDpi == 0 || oldDpi == newDpi)
        {
            return;
        }

        double oldScale = oldDpi / 96.0;
        double newScale = newDpi / 96.0;
        if (oldScale <= 0 || newScale <= 0 || double.IsNaN(oldScale) || double.IsNaN(newScale) || double.IsInfinity(oldScale) || double.IsInfinity(newScale))
        {
            return;
        }

        // Preserve logical (DIP) scroll offsets across DPI changes. ScrollController stores metrics
        // in pixels for stable rounding, so rescale the stored pixel metrics around the same DIP offset.
        _scroll.DpiScale = oldScale;
        double offsetX = _scroll.GetOffsetDip(0);
        double offsetY = _scroll.GetOffsetDip(1);

        _scroll.DpiScale = newScale;
        _scroll.SetMetricsDip(0, _extent.Width, _viewport.Width);
        _scroll.SetMetricsDip(1, _extent.Height, _viewport.Height);

        bool changed = false;
        changed |= _scroll.SetOffsetDip(0, offsetX);
        changed |= _scroll.SetOffsetDip(1, offsetY);

        if (changed)
        {
            InvalidateArrange();
        }

        SyncScrollChrome();
    }

    protected override Size MeasureContent(Size availableSize)
    {
        // We don't draw our own border by default; rely on content.
        var borderInset = GetBorderVisualInset();
        var chromeSlot = new Rect(0, 0, availableSize.Width, availableSize.Height)
            .Deflate(new Thickness(borderInset));

        // Get DPI scale for consistent layout rounding between Measure and Arrange.
        // Without this, viewport calculated here may differ from the one in ArrangeContent/Render
        // due to rounding differences, causing content clipping at non-100% DPI.
        var dpiScale = DpiScale;

        if (Content is not UIElement content)
        {
            _extent = Size.Empty;
            _viewport = Size.Empty;
            return new Size(0, 0).Inflate(Padding);
        }

        double slotW = Math.Max(0, chromeSlot.Width);
        double slotH = Math.Max(0, chromeSlot.Height);

        double viewportW0 = Math.Max(0, slotW - Padding.HorizontalThickness);
        double viewportH0 = Math.Max(0, slotH - Padding.VerticalThickness);

        var viewportRect = LayoutRounding.SnapConstraintRectToPixels(new Rect(0, 0, viewportW0, viewportH0), dpiScale);
        _viewport = LayoutRounding.RoundSizeToPixels(viewportRect.Size, dpiScale);

        var measureSize = new Size(
            HorizontalScroll == ScrollMode.Disabled ? _viewport.Width : double.PositiveInfinity,
            VerticalScroll == ScrollMode.Disabled ? _viewport.Height : double.PositiveInfinity);

        if (content is IScrollContent scrollContent)
        {
            // An unconstrained measure is hypothetical: an infinite viewport would make the content
            // see every offset as the end and re-anchor there. Arrange supplies the displayed viewport.
            if (!double.IsPositiveInfinity(_viewport.Width) && !double.IsPositiveInfinity(_viewport.Height))
            {
                scrollContent.SetViewport(_viewport);
            }

            // Scroll-driven content should not require infinite measurement; it virtualizes internally.
            content.Measure(_viewport);

            // Read extent after measuring because measurement may compute the real extent.
            _extent = LayoutRounding.RoundSizeToPixels(scrollContent.Extent, dpiScale);
        }
        else
        {
            content.Measure(measureSize);
            _extent = LayoutRounding.RoundSizeToPixels(content.DesiredSize, dpiScale);
        }

        // Measure may be hypothetical, so metrics, chrome, and offsets are mutated only in Arrange.
        double capW = Math.Max(0, slotW - Padding.HorizontalThickness);
        double capH = Math.Max(0, slotH - Padding.VerticalThickness);
        double desiredW = double.IsPositiveInfinity(availableSize.Width) ? _extent.Width : Math.Min(_extent.Width, capW);
        double desiredH = double.IsPositiveInfinity(availableSize.Height) ? _extent.Height : Math.Min(_extent.Height, capH);

        var finalDesired = new Size(desiredW, desiredH).Inflate(Padding).Inflate(new Thickness(borderInset));
        return finalDesired;
    }

    protected override void ArrangeContent(Rect bounds)
    {
        var borderInset = GetBorderVisualInset();
        var viewport = GetContentViewportBounds(bounds, borderInset);

        var dpiScale = DpiScale;
        // Keep viewport consistent with the one used for clamping offsets and chrome ranges.
        _viewport = LayoutRounding.RoundSizeToPixels(viewport.Size, dpiScale);
        _scroll.DpiScale = dpiScale;
        _scroll.SetMetricsDip(0, _extent.Width, _viewport.Width);
        _scroll.SetMetricsDip(1, _extent.Height, _viewport.Height);

        // Chrome visibility reflects actual arranged viewport versus content extent. Allow one device
        // pixel of tolerance to suppress chrome caused only by fractional-DPI rounding.
        bool needV = (long)_scroll.GetExtentPx(1) > (long)_scroll.GetViewportPx(1) + 1;
        bool needH = (long)_scroll.GetExtentPx(0) > (long)_scroll.GetViewportPx(0) + 1;
        _canScrollV = IsScrollChromeVisible(VerticalScroll, needV);
        _canScrollH = IsScrollChromeVisible(HorizontalScroll, needH);
        UpdateScrollChrome(_canScrollH, _canScrollV);

        // Clamp offsets against the latest extent/viewport before arranging children.
        _scroll.SetOffsetDip(0, _scroll.GetOffsetDip(0));
        _scroll.SetOffsetDip(1, _scroll.GetOffsetDip(1));
        SyncScrollChrome();

        if (Content is UIElement content)
        {
            ArrangeScrollableContent(content, viewport, _extent);
        }

        ArrangeScrollChrome(GetChromeBounds(bounds, borderInset));
        NotifyScrollChanged();
    }

    protected override void OnRender(IGraphicsContext context)
    {
        // Optional background/border (thin style defaults to none).
        if (Background.A > 0 || BorderThickness > 0)
        {
            DrawBackgroundAndBorder(context, Bounds, Background, BorderBrush, BorderThickness, CornerRadius);
        }
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        var borderInset = GetBorderVisualInset();
        var viewport = GetContentViewportBounds(Bounds, borderInset);
        var clip = GetContentClipBounds(viewport);

        // Render content clipped to the viewport. The content hook owns the translation and cull
        // mapping so custom hosts can replace it while retaining the default implementation via base.
        context.Save();
        double r = Math.Max(0, CornerRadius - Math.Min(Padding.Left, Math.Min(Padding.Right, Math.Min(Padding.Top, Padding.Bottom))));
        if (r > 0)
        {
            r = Math.Min(r, Math.Min(clip.Width, clip.Height) / 2);
            context.SetClipRoundedRect(clip, r, r);
        }
        else
        {
            context.SetClip(clip);
        }

        if (Content is UIElement content)
        {
            RenderScrollableContent(context, content, viewport);
        }
        else
        {
            Content?.Render(context);
        }

        context.Restore();
        RenderScrollChrome(context);
    }

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible || !IsEffectivelyEnabled)
        {
            return null;
        }

        if (TryHitTestScrollChrome(point, out var chromeHit) && chromeHit != null)
        {
            return chromeHit;
        }

        var borderInset = GetBorderVisualInset();
        var viewport = GetContentViewportBounds(Bounds, borderInset);
        if (!viewport.Contains(point))
        {
            return Bounds.Contains(point) ? this : null;
        }

        if (Content is UIElement uiContent)
        {
            var contentPoint = uiContent is IScrollContent
                ? point
                : point.Offset(_scroll.GetOffsetDip(0), _scroll.GetOffsetDip(1));
            var hit = uiContent.HitTest(contentPoint);
            if (hit != null)
            {
                return hit;
            }
        }

        return this;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        if (e.Handled)
        {
            return;
        }

        OnScrollPointerWheel();
        if (HandleMouseWheel(e))
        {
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        OnScrollPointerMove();
    }

    protected override void OnMouseLeave()
    {
        base.OnMouseLeave();
        OnScrollPointerLeave();
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
    {
        if (Content != null && !visitor(Content))
        {
            return false;
        }

        return VisitScrollChrome(visitor);
    }

    /// <summary>
    /// Scrolls vertically by a fractional notch count. 1.0 equals one wheel notch worth of DIPs,
    /// defined by <see cref="ThemeMetrics.ScrollWheelStep"/>.
    /// </summary>
    public void ScrollBy(double notches)
    {
        ScrollAxisByNotches(axis: 1, notches);
    }

    /// <summary>
    /// Scrolls horizontally by a fractional notch count. 1.0 equals one wheel notch worth of DIPs,
    /// defined by <see cref="ThemeMetrics.ScrollWheelStep"/>.
    /// </summary>
    public void ScrollByHorizontal(double notches)
    {
        ScrollAxisByNotches(axis: 0, notches);
    }

    /// <summary>
    /// Applies the default wheel behavior. Override this method to provide custom physics or input
    /// routing and return whether the event was consumed.
    /// </summary>
    protected virtual bool HandleMouseWheel(MouseWheelEventArgs e)
    {
        bool handled = false;
        if (_canScrollV && e.Delta.Y != 0)
        {
            ScrollBy(-e.Delta.Y);
            handled = true;
        }
        if (_canScrollH && e.Delta.X != 0)
        {
            ScrollByHorizontal(-e.Delta.X);
            handled = true;
        }

        return handled;
    }

    /// <summary>
    /// Applies a wheel delta to one axis using the default notch-to-DIP conversion. Override when a
    /// custom host needs a different scrolling curve.
    /// </summary>
    /// <param name="axis">0 for horizontal, 1 for vertical.</param>
    /// <param name="notches">The fractional notch count.</param>
    protected virtual void ScrollAxisByNotches(int axis, double notches)
    {
        double dip = notches * Theme.Metrics.ScrollWheelStep;

        // A movement smaller than one device pixel cannot change what is on screen. Measuring that
        // in DIPs would discard movement at non-100% DPI scales.
        double dpiScale = DpiScale > 0 ? DpiScale : 1;
        if (Math.Abs(dip) * dpiScale < 1)
        {
            return;
        }

        _scroll.DpiScale = DpiScale;
        if (_scroll.ScrollByDip(axis, dip))
        {
            // Content is already arranged in document coordinates. Scrolling only changes the render
            // transform and must not schedule a layout pass.
            InvalidateVisual();
        }
        SyncScrollChrome();
        InvalidateVisual();
        ReevaluateMouseOverAfterScroll();
        NotifyScrollChanged();
    }

    /// <summary>
    /// Re-evaluates pointer state after content moves beneath a stationary pointer.
    /// </summary>
    protected void ReevaluateMouseOverAfterScroll()
    {
        if (FindVisualRoot() is Window window)
        {
            window.ReevaluateMouseOver();
        }
    }

    /// <summary>
    /// Returns whether <paramref name="element"/> is in the content branch translated by this host.
    /// </summary>
    internal bool IsScrollableContentDescendant(Element element)
    {
        var content = Content;
        return content is not IScrollContent &&
            content != null &&
            (ReferenceEquals(content, element) || element.IsDescendantOf(content));
    }

    /// <summary>
    /// Arranges the content for the current viewport. Override to replace the default document-space
    /// arrangement; call the base implementation for ordinary and <see cref="IScrollContent"/> content.
    /// </summary>
    protected virtual void ArrangeScrollableContent(UIElement content, Rect viewport, Size extent)
    {
        if (content is IScrollContent scrollContent)
        {
            scrollContent.SetViewport(_viewport);
            scrollContent.SetOffset(new Point(_scroll.GetOffsetDip(0), _scroll.GetOffsetDip(1)));

            // Scroll-driven content renders/arranges internally using the supplied offset.
            content.Arrange(new Rect(viewport.X, viewport.Y, viewport.Width, viewport.Height));
        }
        else
        {
            // Keep ordinary content in stable document coordinates. RenderScrollableContent applies
            // the offset through the graphics transform, avoiding a full layout pass per wheel update.
            content.Arrange(new Rect(
                viewport.X,
                viewport.Y,
                Math.Max(extent.Width, viewport.Width),
                Math.Max(extent.Height, viewport.Height)));
        }
    }

    /// <summary>
    /// Renders the content inside the current viewport. The default implementation translates normal
    /// content and maps the active render cull rectangle into document coordinates.
    /// </summary>
    protected virtual void RenderScrollableContent(IGraphicsContext context, UIElement content, Rect viewport)
    {
        if (content is UIElement uiContent && content is not IScrollContent)
        {
            // Bounds remain in document coordinates, so both the backend cull rectangle and the retained
            // visual tree see the same render-only translation.
            double offsetX = _scroll.GetOffsetDip(0);
            double offsetY = _scroll.GetOffsetDip(1);
            var contentCull = new Rect(
                viewport.X + offsetX,
                viewport.Y + offsetY,
                viewport.Width,
                viewport.Height);
            var previousCull = UIElement.RenderCullViewport;
            // The parent cull is in the coordinate space active before this host's translation. Move it
            // into document coordinates before intersecting with the content viewport.
            UIElement.RenderCullViewport = previousCull is Rect parentCull
                ? parentCull.Offset(offsetX, offsetY).Intersect(contentCull)
                : contentCull;
            context.Translate(-offsetX, -offsetY);
            try
            {
                uiContent.Render(context);
            }
            finally
            {
                UIElement.RenderCullViewport = previousCull;
            }
        }
        else
        {
            content.Render(context);
        }
    }

    /// <summary>
    /// Updates custom chrome after the host determines whether each axis overflows.
    /// </summary>
    /// <param name="horizontalVisible">Whether horizontal content currently overflows.</param>
    /// <param name="verticalVisible">Whether vertical content currently overflows.</param>
    protected virtual void UpdateScrollChrome(bool horizontalVisible, bool verticalVisible)
    { }

    /// <summary>
    /// Synchronizes custom chrome with the current offset and viewport metrics.
    /// </summary>
    protected virtual void SyncScrollChrome()
    { }

    /// <summary>
    /// Arranges custom chrome in the host's snapped chrome bounds.
    /// </summary>
    protected virtual void ArrangeScrollChrome(Rect chromeBounds)
    { }

    /// <summary>
    /// Renders custom chrome after the clipped content has been rendered.
    /// </summary>
    protected virtual void RenderScrollChrome(IGraphicsContext context)
    { }

    /// <summary>
    /// Performs hit testing for custom chrome. Return true and provide the hit element when chrome
    /// should receive the pointer before content.
    /// </summary>
    protected virtual bool TryHitTestScrollChrome(Point point, out UIElement? hit)
    {
        hit = null;
        return false;
    }

    /// <summary>
    /// Visits custom chrome as part of the visual tree traversal.
    /// </summary>
    protected virtual bool VisitScrollChrome(Func<Element, bool> visitor) => true;

    /// <summary>
    /// Called when <see cref="AutoHideScrollBars"/> changes.
    /// </summary>
    protected virtual void OnAutoHideScrollBarsChanged(bool enabled)
    { }

    /// <summary>
    /// Called after a real offset movement has been published.
    /// </summary>
    protected virtual void OnScrolled()
    { }

    /// <summary>
    /// Called before default wheel handling, allowing custom chrome to reveal itself.
    /// </summary>
    protected virtual void OnScrollPointerWheel()
    { }

    /// <summary>
    /// Called after the pointer moves over the host.
    /// </summary>
    protected virtual void OnScrollPointerMove()
    { }

    /// <summary>
    /// Called after the pointer leaves the host.
    /// </summary>
    protected virtual void OnScrollPointerLeave()
    { }

    /// <summary>
    /// Called by custom chrome when a captured interaction ends.
    /// </summary>
    protected virtual void OnScrollChromeCaptureEnded()
    { }

    /// <summary>
    /// Publishes the current metrics and offset and raises <see cref="ScrollChanged"/>.
    /// </summary>
    protected void NotifyScrollChanged()
    {
        // The controller holds the live offset; publishing it here keeps the observable properties in
        // step with the ScrollChanged notification.
        var offset = new Point(_scroll.GetOffsetDip(0), _scroll.GetOffsetDip(1));
        SetValue(HorizontalOffsetPropertyKey, offset.X);
        SetValue(VerticalOffsetPropertyKey, offset.Y);

        if (_lastNotifiedExtent == _extent && _lastNotifiedViewport == _viewport && _lastNotifiedOffset == offset)
        {
            return;
        }

        // These hooks react to actual scrolling, not to layout/extent changes or the initial offset
        // being established from its NaN sentinel.
        if (!double.IsNaN(_lastNotifiedOffset.X) && _lastNotifiedOffset != offset)
        {
            OnScrolled();

            // Popups anchored to content inside this host would point at the wrong place after scroll.
            if (FindVisualRoot() is Window window)
            {
                window.RequestClosePopups(PopupCloseRequest.Scroll(source: this));
            }
        }

        _lastNotifiedExtent = _extent;
        _lastNotifiedViewport = _viewport;
        _lastNotifiedOffset = offset;
        ScrollChanged?.Invoke();
    }

    /// <summary>
    /// Returns the snapped chrome bounds for the supplied host bounds.
    /// </summary>
    protected Rect GetChromeBounds(Rect bounds, double borderInset)
    {
        // Avoid GetSnappedBorderBounds: it rounds edges and can shift the viewport by 1px at fractional DPI.
        return LayoutRounding.SnapViewportRectToPixels(bounds.Deflate(new Thickness(borderInset)), DpiScale);
    }

    /// <summary>
    /// Returns the snapped content viewport in parent coordinates.
    /// </summary>
    protected Rect GetContentViewportBounds(Rect bounds, double borderInset)
    {
        var viewport = bounds.Deflate(new Thickness(borderInset)).Deflate(Padding);
        return LayoutRounding.SnapViewportRectToPixels(viewport, DpiScale);
    }

    /// <summary>
    /// Returns the content clip, allowing a one-device-pixel edge tolerance for glyph and border overhang.
    /// </summary>
    protected Rect GetContentClipBounds(Rect viewport)
    {
        var dpiScale = DpiScale;
        var onePx = 1.0 / dpiScale;

        var borderInset = GetBorderVisualInset();
        var chrome = GetChromeBounds(Bounds, borderInset);
        double leftRoom = Math.Max(0, viewport.X - chrome.X);
        double rightRoom = Math.Max(0, chrome.Right - viewport.Right);

        double expandL = Math.Min(onePx, leftRoom);
        double expandR = Math.Min(onePx, rightRoom);

        var expanded = new Rect(
            viewport.X - expandL,
            viewport.Y,
            viewport.Width + expandL + expandR,
            viewport.Height);
        return LayoutRounding.MakeClipRect(expanded, dpiScale, rightPx: 0, bottomPx: 0);
    }

    private static bool IsScrollChromeVisible(ScrollMode visibility, bool needed)
        => visibility switch
        {
            ScrollMode.Disabled => false,
            ScrollMode.Visible => true,
            ScrollMode.Auto => needed,
            _ => false
        };

    protected override void OnDispose()
    {
        ScrollChanged = null;
        base.OnDispose();
    }
}
