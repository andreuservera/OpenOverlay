using System.Windows;
using IRacingOverlay.App.Widgets;

namespace IRacingOverlay.App.Tests;

public class CountryFlagTests
{
    [Theory]
    // Flair names as iRacing sends them (FlairName), seen in recorded sessions.
    [InlineData("Spain", "Spain")]
    [InlineData("United States", "United_States")]
    [InlineData("United Kingdom", "United_Kingdom")]
    [InlineData("Türkiye", "Turkey")]
    [InlineData("Saudi Arabia", "Saudi_Arabia")]
    [InlineData("Côte d'Ivoire", "Côte_d'Ivoire")]
    [InlineData("Czechia", "Czech_Republic")]
    public void KeyOf_FindsTheFlag(string flair, string file) =>
        Assert.Equal($"OpenOverlay.Flags.Flag_{file}.svg", CountryFlags.KeyOf(flair));

    [Theory]
    [InlineData("-none-")]
    [InlineData("Global")]
    [InlineData("England")] // no flag of its own in the set
    [InlineData("")]
    [InlineData(null)]
    public void KeyOf_IsNullWithNoFlagToShow(string? flair) =>
        Assert.Null(CountryFlags.KeyOf(flair));

    [Fact]
    public void EveryFlag_ReadsFromItsSvg()
    {
        var failed = new List<string>();
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                foreach (var name in typeof(CountryFlags).Assembly.GetManifestResourceNames().Where(n => n.StartsWith("OpenOverlay.Flags.", StringComparison.Ordinal)))
                {
                    if (CountryFlags.DrawingOf(name) is null)
                    {
                        failed.Add(name);
                    }
                }
            }
            catch (Exception e) { error = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(error);
        Assert.Empty(failed);
    }

    [Fact]
    public void Fit_KeepsTheFlagsProportionsAndCentresIt()
    {
        var rect = CountryFlags.Fit(new Size(750, 500), new Size(22, 15));

        Assert.Equal(22, rect.Width, 3);
        Assert.Equal(14.667, rect.Height, 3);
        Assert.Equal(0.167, rect.Y, 3);
    }

    [Fact]
    public void RenderBitmap_FitsTheBoxAtTheFlagsOwnProportions()
    {
        var sizes = new List<(int, int)>();
        var thread = new Thread(() =>
        {
            foreach (var flag in new[] { "Spain", "Switzerland" })
            {
                var bitmap = (System.Windows.Media.Imaging.BitmapSource)CountryFlags.RenderBitmap(CountryFlags.KeyOf(flag)!, 22, 15)!;
                sizes.Add((bitmap.PixelWidth, bitmap.PixelHeight));
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Equal([(22, 15), (15, 15)], sizes);
    }
}
