namespace One4All.Reporting.Engine;

/// <summary>
/// Flows detail rows across the page's columns (brief §13.1 page setup: multi-column labels): each run of detail bands is laid side by side,
/// a page's column width apart, across then down; group and other bands still span the page.
/// </summary>
public static class Columns
{
    /// <summary>Arranges the model's detail bands into the page's columns (nothing changes for one column).</summary>
    /// <param name="model">The model.</param>
    /// <returns>The same model.</returns>
    public static RenderModel Arrange(RenderModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        int columns = Math.Clamp(model.Page.Columns, 1, 20);
        if (columns == 1)
        {
            return model;
        }

        (double paperWidth, double _) = ReportLayout.PaperSize(model.Page);
        double gap = Math.Max(0, model.Page.ColumnGap);
        double printable = paperWidth - model.Page.MarginLeft - model.Page.MarginRight;
        double pitch = ((printable - (gap * (columns - 1))) / columns) + gap;

        List<RenderBand> arranged = [];
        List<RenderBand> run = [];
        void Flush()
        {
            for (int start = 0; start < run.Count; start += columns)
            {
                List<RenderBand> across = run.GetRange(start, Math.Min(columns, run.Count - start));
                arranged.Add(new RenderBand
                {
                    Type = Model.BandType.Detail, Height = across.Max(b => b.Height),
                    Cells = [.. across.SelectMany((band, column) => band.Cells.Select(c => c with { X = (c.AnchorRight ? Math.Max(0, pitch - gap - c.Width) : c.X) + (column * pitch) }))],
                });
            }

            run.Clear();
        }

        foreach (RenderBand band in model.Body)
        {
            if (band.Type == Model.BandType.Detail && !band.PageBreakBefore && !band.PageBreakAfter)
            {
                run.Add(band);
                continue;
            }

            Flush();
            arranged.Add(band);
        }

        Flush();
        model.Body.Clear();
        model.Body.AddRange(arranged);
        return model;
    }
}
