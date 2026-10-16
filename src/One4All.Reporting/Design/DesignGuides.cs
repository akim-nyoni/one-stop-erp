namespace One4All.Reporting.Design;

/// <summary>A rectangle on the design surface, in points.</summary>
/// <param name="Left">The left edge.</param>
/// <param name="Top">The top edge.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
public readonly record struct DesignRect(double Left, double Top, double Width, double Height)
{
    /// <summary>Gets the right edge.</summary>
    public double Right => Left + Width;

    /// <summary>Gets the bottom edge.</summary>
    public double Bottom => Top + Height;

    /// <summary>Gets the horizontal centre.</summary>
    public double CentreX => Left + (Width / 2);

    /// <summary>Gets the vertical centre.</summary>
    public double CentreY => Top + (Height / 2);
}

/// <summary>What a drag snapped to: the corrected offset and the guide lines to show.</summary>
/// <param name="Dx">The horizontal offset after snapping.</param>
/// <param name="Dy">The vertical offset after snapping.</param>
/// <param name="VerticalGuides">The x positions of edges the element now lines up with.</param>
/// <param name="HorizontalGuides">The y positions of edges the element now lines up with.</param>
public readonly record struct GuideSnap(double Dx, double Dy, IReadOnlyList<double> VerticalGuides, IReadOnlyList<double> HorizontalGuides);

/// <summary>
/// Alignment guides while dragging (brief §13.2): when an edge or centre of the moving element comes within a tolerance of another element's
/// edge or centre, the drag snaps to it and the line is shown. Pure.
/// </summary>
public static class DesignGuides
{
    /// <summary>Snaps a drag of a rectangle to the other rectangles' edges and centres.</summary>
    /// <param name="moving">The element before the drag.</param>
    /// <param name="dx">The drag's horizontal offset.</param>
    /// <param name="dy">The drag's vertical offset.</param>
    /// <param name="others">The other elements of the band.</param>
    /// <param name="tolerance">How close counts, in points.</param>
    /// <returns>The snapped offsets and the guides.</returns>
    public static GuideSnap Snap(DesignRect moving, double dx, double dy, IEnumerable<DesignRect> others, double tolerance = 3)
    {
        ArgumentNullException.ThrowIfNull(others);
        List<DesignRect> rects = [.. others];
        DesignRect moved = moving with { Left = moving.Left + dx, Top = moving.Top + dy };
        (double? shiftX, List<double> vertical) = Nearest([moved.Left, moved.Right, moved.CentreX], rects.SelectMany(r => new[] { r.Left, r.Right, r.CentreX }), tolerance);
        (double? shiftY, List<double> horizontal) = Nearest([moved.Top, moved.Bottom, moved.CentreY], rects.SelectMany(r => new[] { r.Top, r.Bottom, r.CentreY }), tolerance);
        return new GuideSnap(dx + (shiftX ?? 0), dy + (shiftY ?? 0), vertical, horizontal);
    }

    // The smallest shift that puts one of the moving edges on a target edge, and every target edge that shift reaches.
    private static (double? Shift, List<double> Guides) Nearest(double[] edges, IEnumerable<double> targets, double tolerance)
    {
        double? best = null;
        foreach (double target in targets)
        {
            foreach (double edge in edges)
            {
                double shift = target - edge;
                if (Math.Abs(shift) <= tolerance && (best is null || Math.Abs(shift) < Math.Abs(best.Value)))
                {
                    best = shift;
                }
            }
        }

        if (best is not double chosen)
        {
            return (null, []);
        }

        List<double> guides = [.. targets.Where(t => edges.Any(e => Math.Abs(t - (e + chosen)) < 0.01)).Distinct()];
        return (chosen, guides);
    }
}
