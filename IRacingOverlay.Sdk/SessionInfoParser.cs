using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace IRacingOverlay.Sdk;

/// <summary>Outcome of parsing one session-info update.</summary>
/// <param name="Session">Always usable: sections that could not be read carry the previous value.</param>
/// <param name="FailedSections">Sections kept from the previous update because they did not parse.</param>
/// <param name="Error">Why the document as a whole did not parse, when it had to be read section by section.</param>
public sealed record SessionInfoParseResult(
    IracingSessionInfo Session,
    IReadOnlyList<string> FailedSections,
    Exception? Error)
{
    public bool IsDegraded => FailedSections.Count > 0;
}

/// <summary>
/// Turns iRacing's session-info YAML into <see cref="IracingSessionInfo"/> without letting one bad
/// value take the whole blob down. iRacing writes user-entered text (driver, team and setup names)
/// unquoted, so "[TAG] Racing" or "Team: X" is invalid YAML and "Team #44" silently reads as "Team".
/// Those fields are therefore always quoted first; if the document still doesn't parse, it is read
/// section by section, keeping the last good copy of any section that fails.
/// </summary>
public static class SessionInfoParser
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly string[] Sections = [nameof(IracingSessionInfo.WeekendInfo), nameof(IracingSessionInfo.SessionInfo), nameof(IracingSessionInfo.DriverInfo)];

    // Free-text fields iRacing does not quote (the same set other SDK ports repair).
    private static readonly Regex FreeTextField = new(
        @"^(?<key>[ \t]*(?:-[ \t]+)?(?:UserName|TeamName|AbbrevName|Initials|DriverSetupName)):[ \t]*(?<value>[^\r\n]*?)[ \t]*\r?$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static SessionInfoParseResult Parse(string yaml, IracingSessionInfo? previous = null)
    {
        var sanitized = Sanitize(yaml);
        Exception firstError;
        try
        {
            return new SessionInfoParseResult(Normalize(Deserialize(sanitized)), [], null);
        }
        catch (Exception e)
        {
            // Broken outside the free-text fields; read what can still be read, section by section.
            firstError = e;
        }

        var result = new IracingSessionInfo();
        var failed = new List<string>();
        var parsedAny = false;
        foreach (var section in Sections)
        {
            IracingSessionInfo? parsed = null;
            if (ExtractSection(sanitized, section) is { } block)
            {
                try
                {
                    parsed = Deserialize(block);
                    parsedAny = true;
                }
                catch (Exception)
                {
                    failed.Add(section);
                }
            }

            // A section that is missing or broken in this update keeps its last good copy.
            switch (section)
            {
                case nameof(IracingSessionInfo.WeekendInfo):
                    result.WeekendInfo = parsed?.WeekendInfo ?? previous?.WeekendInfo;
                    break;
                case nameof(IracingSessionInfo.SessionInfo):
                    result.SessionInfo = parsed?.SessionInfo ?? previous?.SessionInfo;
                    break;
                default:
                    result.DriverInfo = parsed?.DriverInfo ?? previous?.DriverInfo;
                    break;
            }
        }

        if (!parsedAny)
        {
            throw new InvalidDataException("Session info could not be parsed.", firstError);
        }

        return new SessionInfoParseResult(Normalize(result), failed, firstError);
    }

    /// <summary>Quotes the free-text fields iRacing leaves bare, escaping what YAML requires.</summary>
    public static string Sanitize(string yaml) => FreeTextField.Replace(yaml, match =>
    {
        var value = match.Groups["value"].Value;
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"' && !value[1..^1].Contains('"'))
        {
            return match.Value;
        }

        var escaped = new StringBuilder(value.Length + 2).Append('"');
        foreach (var c in value)
        {
            _ = c switch
            {
                '\\' => escaped.Append(@"\\"),
                '"' => escaped.Append("\\\""),
                _ when char.IsControl(c) => escaped.Append(' '),
                _ => escaped.Append(c),
            };
        }

        return $"{match.Groups["key"].Value}: {escaped.Append('"')}";
    });

    private static IracingSessionInfo Deserialize(string yaml) =>
        Deserializer.Deserialize<IracingSessionInfo>(yaml) ?? new IracingSessionInfo();

    /// <summary>One top-level block ("DriverInfo:" and its indented lines), or null when absent.</summary>
    private static string? ExtractSection(string yaml, string section)
    {
        var start = Regex.Match(yaml, $@"^{section}:[^\r\n]*\r?$", RegexOptions.Multiline | RegexOptions.CultureInvariant);
        if (!start.Success)
        {
            return null;
        }

        var end = Regex.Match(yaml[(start.Index + start.Length)..], @"^(?:[A-Za-z]|\.\.\.)", RegexOptions.Multiline | RegexOptions.CultureInvariant);
        var length = end.Success ? start.Length + end.Index : yaml.Length - start.Index;
        return yaml.Substring(start.Index, length);
    }

    /// <summary>YAML empty values (<c>ResultsPositions:</c>, <c>TeamName:</c>) deserialize to null
    /// even where the model promises a list or a string; downstream code relies on the promise.</summary>
    private static IracingSessionInfo Normalize(IracingSessionInfo info)
    {
        if (info.DriverInfo is { } driverInfo)
        {
            driverInfo.Drivers ??= [];
            driverInfo.Drivers.RemoveAll(d => d is null);
            driverInfo.DriverTires ??= [];
            driverInfo.DriverTires.RemoveAll(t => t is null);
            foreach (var tire in driverInfo.DriverTires)
            {
                tire.TireCompoundType ??= "";
            }
            foreach (var driver in driverInfo.Drivers)
            {
                driver.UserName ??= "";
                driver.TeamName ??= "";
                driver.CarNumber ??= "";
                driver.CarClassShortName ??= "";
                driver.CarScreenNameShort ??= "";
                driver.CarScreenName ??= "";
                driver.LicString ??= "";
                driver.CarClassColor ??= "";
            }
        }

        if (info.SessionInfo is { } sessionInfo)
        {
            sessionInfo.Sessions ??= [];
            sessionInfo.Sessions.RemoveAll(s => s is null);
            foreach (var session in sessionInfo.Sessions)
            {
                session.SessionType ??= "";
                session.SessionName ??= "";
                session.SessionLaps ??= "";
                session.SessionTime ??= "";
                session.SessionTrackRubberState ??= "";
                session.ResultsPositions ??= [];
                session.ResultsPositions.RemoveAll(p => p is null);
            }
        }

        return info;
    }
}
