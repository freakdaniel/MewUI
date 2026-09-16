using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Rendering;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

[TestClass]
[DoNotParallelize]
public sealed class ScrollHostCustomizationTests
{
    [TestMethod]
    public void CustomHostReusesDocumentTranslationAndScrollState()
    {
        using var host = CreateHost();
        var before = host.Rows[4].Bounds;
        host.ScrollHost.SetScrollOffsets(0, 40);

        Assert.AreEqual(before, host.Rows[4].Bounds);
        Assert.IsFalse(host.ScrollHost.IsArrangeDirty);
        Assert.IsFalse(host.Content.IsArrangeDirty);
        Assert.AreEqual(420, host.ScrollHost.ExtentHeight, 0.01);
        Assert.IsGreaterThan(0, host.ScrollHost.ViewportHeight);
        Assert.IsFalse(host.CustomChrome.HorizontalVisible);
        Assert.IsTrue(host.CustomChrome.VerticalVisible);

        var context = new RecordingContext();
        var previousCull = UIElement.RenderCullViewport;
        UIElement.RenderCullViewport = null;
        try
        {
            host.Window.Render(context);
        }
        finally
        {
            UIElement.RenderCullViewport = previousCull;
        }

        Assert.HasCount(1, context.Translations);
        Assert.AreEqual(-40, context.Translations[0].Y, 0.01);

        var pointInRow = new Point(host.ScrollHost.Bounds.X + 10, host.ScrollHost.Bounds.Y + 45);
        Assert.AreSame(host.Rows[4], host.ScrollHost.HitTest(pointInRow));
        Assert.IsGreaterThan(0, host.CustomChrome.ArrangeCalls);
        Assert.AreEqual(1, host.CustomChrome.RenderCalls);
    }

    [TestMethod]
    public void CustomHostCanReplaceWheelHandlingAndPublishOffsets()
    {
        using var host = CreateHost();
        int changed = 0;
        host.ScrollHost.ScrollChanged += () => changed++;

        var args = new MouseWheelEventArgs(
            new Point(20, 20),
            new Point(20, 20),
            new Vector(0, -1));
        host.CustomChrome.RaiseWheel(args);

        Assert.AreEqual(1, host.CustomChrome.WheelCalls);
        Assert.IsTrue(args.Handled);
        Assert.AreEqual(7, host.ScrollHost.VerticalOffset, 0.01);
        Assert.IsGreaterThan(0, changed);
    }

    private static Host CreateHost()
    {
        var content = new StackPanel { Orientation = Orientation.Vertical };
        var rows = Enumerable.Range(0, 21)
            .Select(static _ => new ProbeControl { Width = 120, Height = 20 })
            .ToArray();
        foreach (var row in rows)
        {
            content.Add(row);
        }

        var scrollHost = new InspectableScrollHost
        {
            Width = 120,
            Height = 60,
            VerticalScroll = ScrollMode.Auto,
            HorizontalScroll = ScrollMode.Disabled,
            Content = content,
        };
        var window = HeadlessWindow.Create(120, 60);
        window.Content = scrollHost;
        window.PerformLayout();
        return new Host(window, scrollHost, content, rows);
    }

    private sealed class InspectableScrollHost : ScrollHost
    {
        public bool HorizontalVisible { get; private set; }
        public bool VerticalVisible { get; private set; }
        public int ArrangeCalls { get; private set; }
        public int RenderCalls { get; private set; }
        public int WheelCalls { get; private set; }

        protected override void UpdateScrollChrome(bool horizontalVisible, bool verticalVisible)
        {
            HorizontalVisible = horizontalVisible;
            VerticalVisible = verticalVisible;
        }

        protected override void ArrangeScrollChrome(Rect chromeBounds)
        {
            ArrangeCalls++;
        }

        protected override void RenderScrollChrome(IGraphicsContext context)
        {
            RenderCalls++;
        }

        protected override bool HandleMouseWheel(MouseWheelEventArgs e)
        {
            WheelCalls++;
            SetScrollOffsets(HorizontalOffset, VerticalOffset + 7);
            return true;
        }

        public void RaiseWheel(MouseWheelEventArgs args) => OnMouseWheel(args);
    }

    private sealed class ProbeControl : Control
    { }

    private sealed class RecordingContext : NoOpGraphicsContext
    {
        public List<(double X, double Y)> Translations { get; } = [];

        public override void Translate(double dx, double dy) => Translations.Add((dx, dy));
    }

    private sealed class Host(Window window, InspectableScrollHost scrollHost, StackPanel content, ProbeControl[] rows) : IDisposable
    {
        public Window Window { get; } = window;
        public InspectableScrollHost ScrollHost { get; } = scrollHost;
        public InspectableScrollHost CustomChrome => ScrollHost;
        public StackPanel Content { get; } = content;
        public ProbeControl[] Rows { get; } = rows;

        public void Dispose() => Window.Dispose();
    }
}
