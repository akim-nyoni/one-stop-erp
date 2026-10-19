using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using One4All.Application.Pos;

namespace One4All.Desktop.Pos;

/// <summary>Turns a picture (PNG, JPG, BMP) into a receipt logo: scaled to fit the paper, then each dot dark or light.</summary>
public static class ReceiptLogoImage
{
    /// <summary>Loads and converts a picture.</summary>
    /// <param name="path">The file.</param>
    /// <param name="paperMm">The paper width (the logo fits its printable width).</param>
    /// <param name="maxHeight">The tallest the logo may be, in dots.</param>
    /// <returns>The logo.</returns>
    public static ReceiptLogo Load(string path, int paperMm, int maxHeight = 200)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"No picture at {path}.", path);
        }

        BitmapImage source = new();
        source.BeginInit();
        source.CacheOption = BitmapCacheOption.OnLoad;
        source.UriSource = new Uri(Path.GetFullPath(path));
        source.EndInit();

        // Fit within the printable width and the height limit, never enlarging; a white background under any transparency.
        double scale = Math.Min(1.0, Math.Min(ReceiptLogo.MaxDots(paperMm) / (double)source.PixelWidth, maxHeight / (double)source.PixelHeight));
        int width = Math.Max(8, (int)(source.PixelWidth * scale)), height = Math.Max(1, (int)(source.PixelHeight * scale));
        DrawingVisual visual = new();
        using (DrawingContext context = visual.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new System.Windows.Rect(0, 0, width, height));
            context.DrawImage(source, new System.Windows.Rect(0, 0, width, height));
        }

        RenderTargetBitmap rendered = new(width, height, 96, 96, PixelFormats.Pbgra32);
        rendered.Render(visual);
        FormatConvertedBitmap grey = new(rendered, PixelFormats.Gray8, null, 0);
        byte[] pixels = new byte[width * height];
        grey.CopyPixels(pixels, width, 0);
        bool[,] dark = new bool[height, width];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                dark[y, x] = pixels[(y * width) + x] < 128;
            }
        }

        return ReceiptLogo.FromDots(dark);
    }
}
