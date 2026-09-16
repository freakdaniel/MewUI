using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Rendering;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

[TestClass]
[DoNotParallelize]
public sealed class ScrollViewerRenderOffsetTests
{
    [TestMethod]
    public void OffsetChangeKeepsDocumentBoundsAndDoesNotDirtyArrange()
    {
        using var host = CreateHost();
        var before = host.Rows[4].Bounds;

        host.Viewer.SetScrollOffsets(0, 40);

        Assert.AreEqual(before, host.Rows[4].Bounds);
        Assert.IsFalse(host.Viewer.IsArrangeDirty);
        Assert.IsFalse(host.Content.IsArrangeDirty);

        host.Window.PerformLayout();

        Assert.AreEqual(before, host.Rows[4].Bounds);
        Assert.IsFalse(host.Viewer.IsArrangeDirty);
        Assert.IsFalse(host.Content.IsArrangeDirty);
    }

    [TestMethod]
    public void ScrollBarSyncUsesLiveOffsetBeforePublicOffsetIsPublished()
    {
        using var host = CreateHost();
        var verticalBar = (ScrollBar?)VisualTree.Find(
            host.Viewer,
            static element => element is ScrollBar { Orientation: Orientation.Vertical });

        Assert.IsNotNull(verticalBar);

        host.Viewer.SetScrollOffsets(0, 10);
        host.Viewer.SetScrollOffsets(0, 20);

        Assert.AreEqual(20, host.Viewer.VerticalOffset, 0.01);
        Assert.AreEqual(20, verticalBar.Value, 0.01);
    }

    [TestMethod]
    public void RenderTranslatesContentAndHitTestUsesDocumentCoordinates()
    {
        using var host = CreateHost();
        host.Viewer.SetScrollOffsets(0, 40);

        var context = new RecordingContext();
        var previousCull = UIElement.RenderCullViewport;
        UIElement.RenderCullViewport = null;
        try
        {
            host.Viewer.Render(context);
        }
        finally
        {
            UIElement.RenderCullViewport = previousCull;
        }

        Assert.HasCount(1, context.Translations);
        Assert.AreEqual(0, context.Translations[0].X, 0.01);
        Assert.AreEqual(-40, context.Translations[0].Y, 0.01);

        var rowOriginInViewer = host.Rows[4].TranslatePoint(new Point(0, 0), host.Viewer);
        Assert.AreEqual(host.Rows[4].Bounds.Y - host.Viewer.Bounds.Y - host.Viewer.VerticalOffset,
            rowOriginInViewer.Y, 0.01);

        var pointInRow = new Point(host.Viewer.Bounds.X + 10, host.Viewer.Bounds.Y + 45);
        Assert.AreSame(host.Rows[4], host.Viewer.HitTest(pointInRow));

        ((IFocusIntoViewHost)host.Viewer).OnDescendantFocused(host.Rows[3]);
        Assert.AreEqual(40, host.Viewer.VerticalOffset, 0.01);
    }

    [TestMethod]
    public void RenderCullMovesWithTheDocumentOffset()
    {
        using var host = CreateHost();
        host.Viewer.SetScrollOffsets(0, 40);

        var previousCull = UIElement.RenderCullViewport;
        UIElement.RenderCullViewport = new Rect(0, 0, 120, 60);
        try
        {
            host.Window.Render(new RecordingContext());
        }
        finally
        {
            UIElement.RenderCullViewport = previousCull;
        }

        Assert.AreEqual(0, host.Rows[0].RenderCount);
        Assert.AreEqual(0, host.Rows[1].RenderCount);
        Assert.AreEqual(1, host.Rows[2].RenderCount);
        Assert.AreEqual(1, host.Rows[3].RenderCount);
        Assert.AreEqual(1, host.Rows[4].RenderCount);
    }

    [TestMethod]
    public void RepeatedVisualInvalidationsShareOneWindowRequestUntilRender()
    {
        using var window = new CountingWindow();
        window.AttachBackend(new HeadlessWindowBackend());
        window.SetClientSizeDip(120, 60);

        var content = new StackPanel { Orientation = Orientation.Vertical };
        var first = new Border { Width = 120, Height = 20 };
        var second = new Border { Width = 120, Height = 20 };
        content.Add(first);
        content.Add(second);
        window.Content = content;
        window.PerformLayout();

        Render(window);
        window.ResetRenderRequests();

        first.InvalidateVisual();
        second.InvalidateVisual();

        Assert.AreEqual(1, window.RenderRequests);

        Render(window);
        first.InvalidateVisual();

        Assert.AreEqual(2, window.RenderRequests);
    }

    private static Host CreateHost()
    {
        var content = new StackPanel { Orientation = Orientation.Vertical };
        var rows = Enumerable.Range(0, 10)
            .Select(static _ => new ProbeControl { Width = 120, Height = 20 })
            .ToArray();
        foreach (var row in rows)
        {
            content.Add(row);
        }

        var viewer = new ScrollViewer
        {
            Width = 120,
            Height = 60,
            VerticalScroll = ScrollMode.Auto,
            Content = content,
        };
        var window = HeadlessWindow.Create(120, 60);
        window.Content = viewer;
        window.PerformLayout();
        return new Host(window, viewer, content, rows);
    }

    private static void Render(Window window)
    {
        var previousCull = UIElement.RenderCullViewport;
        UIElement.RenderCullViewport = null;
        try
        {
            window.Render(new RecordingContext());
        }
        finally
        {
            UIElement.RenderCullViewport = previousCull;
        }
    }

    private sealed class RecordingContext : NoOpGraphicsContext
    {
        public List<(double X, double Y)> Translations { get; } = [];

        public override void Translate(double dx, double dy) => Translations.Add((dx, dy));
    }

    private sealed class ProbeControl : Control
    {
        public int RenderCount { get; private set; }

        protected override void OnRender(IGraphicsContext context)
        {
            RenderCount++;
            base.OnRender(context);
        }
    }

    private sealed class CountingWindow : Window
    {
        public int RenderRequests { get; private set; }

        public override void InvalidateVisual()
        {
            base.InvalidateVisual();
            RenderRequests++;
        }

        public void ResetRenderRequests() => RenderRequests = 0;
    }

    private sealed class Host(Window window, ScrollViewer viewer, StackPanel content, ProbeControl[] rows) : IDisposable
    {
        public Window Window { get; } = window;
        public ScrollViewer Viewer { get; } = viewer;
        public StackPanel Content { get; } = content;
        public ProbeControl[] Rows { get; } = rows;

        public void Dispose() => Window.Dispose();
    }
}
