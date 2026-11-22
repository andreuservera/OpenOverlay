using System.Text.Json.Nodes;
using IRacingOverlay.App.ControlPanel;
using IRacingOverlay.App.Layouts;
using IRacingOverlay.App.ViewModels;

namespace IRacingOverlay.App.Tests;

public class DriverNameFormatterTests
{
    private static IReadOnlyList<string> Of(string name, DriverNameFormat format, bool upper = false) =>
        DriverNameFormatter.Candidates(name, format, upper);

    [Fact]
    public void Auto_OffersTheWholeName_ThenShorterFormsInTurn()
    {
        Assert.Equal(
            ["Carlos Carrasco Alvarez", "C. C. Alvarez", "C. Alvarez", "Alvarez"],
            Of("Carlos Carrasco Alvarez", DriverNameFormat.Auto));
    }

    [Fact]
    public void Auto_TwoWordName_DropsTheRepeatedStep()
    {
        Assert.Equal(["Max Verstappen", "M. Verstappen", "Verstappen"], Of("Max Verstappen", DriverNameFormat.Auto));
    }

    [Fact]
    public void Auto_ParticlesBecomeInitials_LikeAnyOtherGivenName()
    {
        Assert.Equal("J. d. l. Fuente", Of("José de la Fuente", DriverNameFormat.Auto)[1]);
    }

    [Theory]
    [InlineData(DriverNameFormat.Full, "Maria Garcia Lopez-Ortega")]
    [InlineData(DriverNameFormat.InitialSurname, "M. Lopez-Ortega")]
    [InlineData(DriverNameFormat.Surname, "Lopez-Ortega")]
    [InlineData(DriverNameFormat.Code, "LOP")]
    public void FixedFormats_GiveOneText_WhateverTheRoom(DriverNameFormat format, string expected)
    {
        Assert.Equal([expected], Of("  Maria  Garcia Lopez-Ortega ", format));
    }

    [Fact]
    public void Code_SkipsTheDigitsIRacingAppendsToRepeatedNames()
    {
        Assert.Equal(["SMI"], Of("John Smith2", DriverNameFormat.Code));
        Assert.Equal(["LI"], Of("Wei Li", DriverNameFormat.Code));
    }

    [Theory]
    [InlineData(DriverNameFormat.Auto, "Carlos ALVAREZ")]
    [InlineData(DriverNameFormat.InitialSurname, "C. ALVAREZ")]
    [InlineData(DriverNameFormat.Surname, "ALVAREZ")]
    public void UppercaseSurname_CapitalisesOnlyTheSurname(DriverNameFormat format, string expected)
    {
        Assert.Equal(expected, Of("Carlos Alvarez", format, upper: true)[0]);
    }

    [Fact]
    public void SingleWordOrEmpty_IsLeftAsItIs()
    {
        Assert.Equal(["Kimi"], Of("Kimi", DriverNameFormat.Auto));
        Assert.Equal(["Kimi"], Of("Kimi", DriverNameFormat.InitialSurname));
        Assert.Equal([""], Of("   ", DriverNameFormat.Auto));
    }

    [Fact]
    public void LayoutCodec_RoundTripsTheNameSettings()
    {
        var options = new DriverTableOptions(DriverTable.Relative);
        var codec = new DriverTableConfigCodec(WidgetCatalog.Relative, options, _ => { }, _ => { });

        codec.Apply(new JsonObject { ["nameFormat"] = "Code", ["uppercaseSurname"] = true });

        Assert.Equal(DriverNameFormat.Code, options.NameFormat);
        Assert.True(options.UppercaseSurname);
        Assert.Equal("Code", (string)codec.Read()["nameFormat"]!);
    }

    [Fact]
    public void LayoutCodec_UnknownFormat_KeepsTheCurrentOne()
    {
        var options = new DriverTableOptions(DriverTable.Standings) { NameFormat = DriverNameFormat.Surname };
        var codec = new DriverTableConfigCodec(WidgetCatalog.Standings, options, _ => { }, _ => { });

        codec.Apply(new JsonObject { ["nameFormat"] = "Nickname" });

        Assert.Equal(DriverNameFormat.Surname, options.NameFormat);
    }
}
