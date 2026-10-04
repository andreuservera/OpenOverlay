using System.Globalization;

namespace IRacingOverlay.App.ViewModels;

/// <summary>How a strength of field is written wherever the tables show one.</summary>
public static class SofFormat
{
    /// <summary>"SOF 2.9k": thousands to one decimal, truncated rather than rounded, so 2,987 never
    /// claims to be a 3k lobby. Under 1,000 the actual SOF, as a whole number; nothing without a
    /// strength.</summary>
    public static string Format(double sof)
    {
        if (double.IsNaN(sof) || sof <= 0)
        {
            return "";
        }

        var whole = Math.Round(sof);
        return whole < 1000
            ? $"SOF {whole.ToString("0", CultureInfo.InvariantCulture)}"
            : $"SOF {(Math.Floor(whole / 100) / 10).ToString("0.0", CultureInfo.InvariantCulture)}k";
    }
}
