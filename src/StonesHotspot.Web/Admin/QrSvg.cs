using System.Globalization;
using System.Text;
using QRCoder;

namespace StonesHotspot.Web.Admin;

/// <summary>QR codes as inline SVG (sharp when printed, no image files).</summary>
public static class QrSvg
{
    public static string Render(string text, int sizeMm)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var matrix = data.ModuleMatrix;
        var n = matrix.Count;

        var path = new StringBuilder();
        for (var y = 0; y < n; y++)
        {
            for (var x = 0; x < n; x++)
            {
                if (matrix[y][x])
                    path.Append(CultureInfo.InvariantCulture, $"M{x},{y}h1v1h-1z");
            }
        }
        return $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {n} {n}" width="{sizeMm}mm" height="{sizeMm}mm" shape-rendering="crispEdges" role="img" aria-label="QR"><rect width="{n}" height="{n}" fill="#fff"/><path d="{path}" fill="#000"/></svg>""";
    }
}
