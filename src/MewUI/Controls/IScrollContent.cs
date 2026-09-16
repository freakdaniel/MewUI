namespace Aprillz.MewUI.Controls;

/// <summary>
/// Contract for scroll-driven content.
/// The scroll owner (e.g., <see cref="ScrollHost"/>) provides viewport and offset,
/// while the content reports its logical extent and renders only what is necessary.
/// </summary>
public interface IScrollContent
{
    /// <summary>
    /// Gets the logical content extent in DIPs.
    /// </summary>
    Size Extent { get; }

    /// <summary>
    /// Updates the current viewport size in DIPs.
    /// </summary>
    void SetViewport(Size viewport);

    /// <summary>
    /// Updates the current scroll offset in DIPs.
    /// </summary>
    void SetOffset(Point offset);
}
