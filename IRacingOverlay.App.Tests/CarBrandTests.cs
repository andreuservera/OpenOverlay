using System.Windows;
using IRacingOverlay.App.ViewModels;
using IRacingOverlay.App.Widgets;

namespace IRacingOverlay.App.Tests;

public class CarBrandTests
{
    [Theory]
    // Model names as iRacing sends them in DriverInfo (CarScreenName).
    [InlineData("Porsche 911 GT3 R (992)", "Porsche")]
    [InlineData("Mercedes-AMG GT3 2020", "Mercedes")]
    [InlineData("Aston Martin Valkyrie LMH", "Aston Martin")]
    [InlineData("Chevrolet Corvette Z06 GT3.R", "Chevrolet")]
    [InlineData("Mazda MX-5 Cup", "Mazda")]
    [InlineData("Global Mazda MX-5 Cup", "Mazda")]
    [InlineData("Volkswagen Beetle GRC Lite", "Volkswagen")]
    [InlineData("NASCAR Cup Series Next Gen Chevrolet Camaro ZL1", "Chevrolet")]
    [InlineData("Super Formula SF23 - Toyota", "Toyota")]
    [InlineData("Williams-Toyota FW31", "Williams")]
    [InlineData("HPD ARX-01c", "Honda")]
    [InlineData("SCCA Spec Racer Ford", "Ford")]
    [InlineData("NASCAR Truck RAM", "RAM")]
    public void FromScreenName_FindsTheMake(string screenName, string make) =>
        Assert.Equal(make, CarBrand.FromScreenName(screenName)?.Name);

    [Theory]
    [InlineData("Stock Winged Micro Sprint")]
    [InlineData("safety pcsedan")]
    [InlineData("FIA Cross Car")]
    [InlineData("Riley Mk XX Daytona Prototype")]
    [InlineData("")]
    [InlineData(null)]
    public void FromScreenName_NamesNoMakeWhenThereIsNone(string? screenName) =>
        Assert.Null(CarBrand.FromScreenName(screenName));

    [Theory]
    [InlineData("Stock Winged Micro Sprint")]
    [InlineData("Supercars Holden ZB Commodore")]
    [InlineData(null)]
    public void ForCar_ShowsTheIRacingEmblemForAnUnknownMake(string? screenName) =>
        Assert.Equal("iracing", CarBrand.ForCar(screenName).Logo);

    [Fact]
    public void FromScreenName_MatchesWholeWordsOnly() =>
        // "Ray" must not be read out of "Grayson", nor "Kia" out of "Kiawah".
        Assert.Null(CarBrand.FromScreenName("Grayson Kiawah Special"));

    [Fact]
    public void EveryLogoNamedByABrand_LoadsAsAShape()
    {
        var keys = CarBrand.All.Append(CarBrand.Placeholder).Select(brand => brand.Logo).OfType<string>().ToList();

        Assert.NotEmpty(keys);
        Assert.All(keys, key =>
        {
            var logo = CarBrandIcon.LogoOf(key);
            Assert.True(logo is not null, $"logo '{key}' did not load");
            // Simple Icons draw on a 24×24 canvas; anything outside means the path was misread.
            Assert.True(new Rect(-0.5, -0.5, 25, 25).Contains(logo!.Geometry.Bounds), $"logo '{key}' bounds {logo.Geometry.Bounds}");
        });
    }

    [Fact]
    public void Fit_CentresAWideLogoAndKeepsItsShape()
    {
        var matrix = CarBrandIcon.Fit(new Rect(0, 6, 24, 12), new Size(24, 16), new Point(12, 8));

        var topLeft = matrix.Transform(new Point(0, 6));
        var bottomRight = matrix.Transform(new Point(24, 18));
        Assert.Equal(new Point(0, 2), topLeft);
        Assert.Equal(new Point(24, 14), bottomRight);
    }

    [Fact]
    public void McLaren_FillsLessOfTheBox() =>
        Assert.True(CarBrandIcon.LogoOf("mclaren")!.Scale < 1);
}
