using One4All.Core.Common;
using One4All.Core.Pos;

namespace One4All.Application.Pos;

/// <summary>
/// The till's function keys (brief §4.1 "configurable layout per terminal profile ... function keys"): F1–F12 mapped to till actions, from the
/// terminal's own map over its profile's defaults. A map is written "F2=TenderCash; F3=TenderCard"; an action name is a till command.
/// </summary>
public static class TillFunctionKeys
{
    /// <summary>The actions a function key can run.</summary>
    public static readonly IReadOnlyList<string> Actions =
    [
        "TenderCash", "TenderCard", "TenderMobileMoney", "TenderAccount", "TenderVoucher", "Pay", "NewSale", "VoidSale", "RefundSale", "AddTip", "Park", "Resume",
        "DiscountLine", "DiscountSale", "OverridePrice", "Weigh", "XRead", "ApplyCoupon", "ClearTenders", "NextCurrency", "FlushOffline",
    ];

    /// <summary>Reads a map (blank: none).</summary>
    /// <param name="map">"F2=TenderCash; F3=TenderCard".</param>
    /// <returns>Key to action, or why the map is wrong.</returns>
    public static Result<IReadOnlyDictionary<string, string>> Parse(string? map)
    {
        Dictionary<string, string> keys = new(StringComparer.OrdinalIgnoreCase);
        foreach (string part in (map ?? string.Empty).Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] bits = part.Split('=', 2, StringSplitOptions.TrimEntries);
            string key = bits[0].ToUpperInvariant();
            if (bits.Length != 2 || !IsFunctionKey(key))
            {
                return Error.Validation("Pos.FunctionKey", $"Write each key as F1..F12=Action (\"{part}\").");
            }

            string? action = Actions.FirstOrDefault(a => a.Equals(bits[1], StringComparison.OrdinalIgnoreCase));
            if (action is null)
            {
                return Error.Validation("Pos.FunctionKeyAction", $"Unknown till action {bits[1]}; one of {string.Join(", ", Actions)}.");
            }

            keys[key] = action;
        }

        return keys;
    }

    /// <summary>The keys a profile starts with.</summary>
    /// <param name="profile">The terminal profile.</param>
    /// <returns>Key to action.</returns>
    public static IReadOnlyDictionary<string, string> Defaults(TerminalProfile profile)
    {
        Dictionary<string, string> keys = new(StringComparer.OrdinalIgnoreCase)
        {
            ["F2"] = "TenderCash", ["F3"] = "TenderCard", ["F4"] = "TenderMobileMoney", ["F5"] = "Pay", ["F6"] = "Park", ["F7"] = "Resume", ["F8"] = "NewSale", ["F12"] = "XRead",
        };
        switch (profile)
        {
            case TerminalProfile.Supermarket or TerminalProfile.Butchery or TerminalProfile.WeighbridgeKiosk:
                keys["F10"] = "Weigh";
                keys["F11"] = "ApplyCoupon";
                break;
            case TerminalProfile.Bar or TerminalProfile.Restaurant:
                keys["F10"] = "AddTip";
                keys["F11"] = "TenderAccount";
                break;
            default:
                keys["F10"] = "DiscountLine";
                keys["F11"] = "TenderVoucher";
                break;
        }

        return keys;
    }

    /// <summary>The terminal's keys: its own map over its profile's defaults (an unreadable map keeps the defaults).</summary>
    /// <param name="profile">The terminal profile.</param>
    /// <param name="map">The terminal's own map.</param>
    /// <returns>Key to action.</returns>
    public static IReadOnlyDictionary<string, string> For(TerminalProfile profile, string? map)
    {
        Dictionary<string, string> keys = new(Defaults(profile), StringComparer.OrdinalIgnoreCase);
        if (Parse(map) is { IsSuccess: true } own)
        {
            foreach ((string key, string action) in own.Value)
            {
                keys[key] = action;
            }
        }

        return keys;
    }

    /// <summary>Describes the keys for the till's help line ("F2 Cash · F3 Card ...").</summary>
    /// <param name="keys">Key to action.</param>
    /// <returns>The text.</returns>
    public static string Describe(IReadOnlyDictionary<string, string> keys)
        => string.Join("  ", keys.OrderBy(k => int.Parse(k.Key[1..], System.Globalization.CultureInfo.InvariantCulture)).Select(k => $"{k.Key} {k.Value}"));

    private static bool IsFunctionKey(string key) => key.Length is 2 or 3 && key[0] == 'F' && int.TryParse(key[1..], out int n) && n is >= 1 and <= 12;
}
