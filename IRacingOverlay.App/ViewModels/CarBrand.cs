using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace IRacingOverlay.App.ViewModels;

/// <summary>
/// The make of a car, as a driver table shows it: the brand's logo when the app carries one
/// (<see cref="Logo"/> names it in Assets/CarBrands), otherwise a short monogram.
/// </summary>
public sealed record CarBrand(string Name, string? Logo, string Monogram)
{
    // iRacing has no make field; the model name carries it instead ("BMW M4 GT3 EVO", "NASCAR Cup
    // Series Next Gen Chevrolet Camaro ZL1", "Super Formula SF23 - Toyota"). Each make lists the
    // spellings that name it, longest first where one contains another.
    private static readonly (CarBrand Brand, string[] Names)[] Known =
    [
        (new("Acura", "acura", "ACU"), ["Acura"]),
        (new("Aston Martin", "astonmartin", "AM"), ["Aston Martin"]),
        (new("Audi", "audi", "AUD"), ["Audi"]),
        (new("BMW", "bmw", "BMW"), ["BMW"]),
        (new("Cadillac", "cadillac", "CAD"), ["Cadillac"]),
        (new("Chevrolet", "chevrolet", "CHV"), ["Chevrolet", "Chevy"]),
        (new("Ferrari", "ferrari", "FER"), ["Ferrari"]),
        (new("Ford", "ford", "FRD"), ["Ford"]),
        // HPD: Honda Performance Development, Honda's racing arm (HPD ARX-01c).
        (new("Honda", "honda", "HON"), ["Honda", "HPD"]),
        (new("Hyundai", "hyundai", "HYU"), ["Hyundai"]),
        (new("Kia", "kia", "KIA"), ["Kia"]),
        (new("Lamborghini", "lamborghini", "LAM"), ["Lamborghini"]),
        (new("Mazda", "mazda", "MAZ"), ["Mazda"]),
        (new("McLaren", "mclaren", "MCL"), ["McLaren"]),
        (new("Nissan", "nissan", "NIS"), ["Nissan"]),
        (new("Porsche", "porsche", "POR"), ["Porsche"]),
        (new("RAM", "ram", "RAM"), ["RAM"]),
        (new("Renault", "renault", "REN"), ["Renault"]),
        (new("Subaru", "subaru", "SUB"), ["Subaru"]),
        (new("Toyota", "toyota", "TOY"), ["Toyota"]),
        (new("Volkswagen", "volkswagen", "VW"), ["Volkswagen", "VW"]),
        (new("Mercedes", "mercedes", "MB"), ["Mercedes-AMG", "Mercedes-Benz", "Mercedes"]),
        (new("Dallara", "dallara", "DAL"), ["Dallara"]),
        (new("Ligier", "ligier", "LIG"), ["Ligier"]),
        (new("Lotus", "lotus", "LOT"), ["Lotus"]),
        (new("Williams", "williams", "WIL"), ["Williams"]),
        (new("Radical", "radical", "RAD"), ["Radical"]),
        (new("Pontiac", "pontiac", "PON"), ["Pontiac"]),
        (new("RUF", "ruf", "RUF"), ["RUF"]),
        (new("Skip Barber", "skipbarber", "SB"), ["Skip Barber"]),
        // Makes with no usable free logo (Riley, Ray, Holden, Buick, Caterham) are left out: they
        // get the placeholder, like spec cars.
    ];

    private static readonly (CarBrand Brand, Regex Pattern)[] Patterns = Known
        .SelectMany(entry => entry.Names.Select(name => (entry.Brand,
            new Regex($@"(?<![\p{{L}}\d]){Regex.Escape(name)}(?![\p{{L}}\d])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))))
        .ToArray();

    /// <summary>What a car whose make isn't recognised shows: iRacing's emblem.</summary>
    public static readonly CarBrand Placeholder = new("iRacing", "iracing", "iR");

    /// <summary>Every make the app recognises.</summary>
    internal static IEnumerable<CarBrand> All => Known.Select(entry => entry.Brand);

    // Every car in a session shares a handful of models; each is looked up once.
    private static readonly ConcurrentDictionary<string, CarBrand?> Cache = new(StringComparer.Ordinal);

    /// <summary>
    /// The make named in an iRacing model name: the one it starts with ("Williams-Toyota FW31" is a
    /// Williams), else the first one mentioned. Null when the name names no make this app knows,
    /// as with spec and dirt cars ("Stock Winged Micro Sprint").
    /// </summary>
    public static CarBrand? FromScreenName(string? screenName) => string.IsNullOrWhiteSpace(screenName) ? null : Lookup(screenName);

    /// <summary>What a car's make cell shows: its make, or <see cref="Placeholder"/> when the model
    /// name names none the app knows.</summary>
    public static CarBrand ForCar(string? screenName) => FromScreenName(screenName) ?? Placeholder;

    private static CarBrand? Lookup(string screenName) =>
        Cache.GetOrAdd(screenName.Trim(), static name =>
        {
            CarBrand? found = null;
            var at = int.MaxValue;
            foreach (var (brand, pattern) in Patterns)
            {
                var match = pattern.Match(name);
                if (match.Success && match.Index < at)
                {
                    (found, at) = (brand, match.Index);
                }
            }

            return found;
        });
}
