using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Umbrella.Wallet.App;

/// <summary>
/// True when the bound value equals the ConverterParameter (case-insensitive). Used to light up
/// the active segmented-filter chip without a boolean-per-option on the view model.
/// </summary>
public sealed class StringEqualsConverter : IValueConverter
{
    public static readonly StringEqualsConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Toast accent — red for errors, green for notices. Value is the bool ToastIsError.</summary>
public sealed class ToastBorderConverter : IValueConverter
{
    public static readonly ToastBorderConverter Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Avalonia.Media.SolidColorBrush.Parse(value is true ? "#E24B4A" : "#3DDC97");
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Turns a change-colour hex string (e.g. "#8FCB9B") into a faint translucent fill, so a % change can
/// sit in a tinted pill badge — green wash when up, red wash when down — the way the reference apps
/// show it. One converter serves every change indicator, since they all expose the colour as a string.
/// </summary>
public sealed class ChangeWashConverter : IValueConverter
{
    public static readonly ChangeWashConverter Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hex = value?.ToString();
        var colour = string.IsNullOrWhiteSpace(hex)
            ? Avalonia.Media.Colors.Gray
            : Avalonia.Media.Color.Parse(hex);
        // ~14% alpha reads as a soft tint on any of the dark theme bases without muddying the text.
        return new Avalonia.Media.SolidColorBrush(colour) { Opacity = 0.14 };
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Toast glyph — a warning triangle for errors, a check for notices.</summary>
public sealed class ToastGlyphConverter : IValueConverter
{
    public static readonly ToastGlyphConverter Instance = new();
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "⚠" : "✓";
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Row visibility for the Market search box. Values are [Symbol, Name, Query]; the row is visible
/// when the query is empty or is a case-insensitive substring of either the ticker or the name.
/// Kept as an IsVisible filter (not a collection rebuild) so the live price/sparkline updates,
/// which index rows by symbol, keep working untouched.
/// </summary>
public sealed class MarketFilterConverter : IMultiValueConverter
{
    public static readonly MarketFilterConverter Instance = new();

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 3) return true;
        var symbol = values[0]?.ToString() ?? string.Empty;
        var name = values[1]?.ToString() ?? string.Empty;
        var query = values[2]?.ToString()?.Trim() ?? string.Empty;

        // The section a row belongs to: "chains" (a network's own coin) or "tokens" (a contract that
        // lives on someone else's network). A row shows in exactly one of the two lists.
        var section = parameter as string ?? (values.Count > 4 ? values[4] as string : null);
        if (values.Count > 3 && values[3] is bool isToken && section is not null)
        {
            if (section == "chains" && isToken) return false;
            if (section == "tokens" && !isToken) return false;
        }

        if (query.Length == 0) return true;
        return symbol.Contains(query, StringComparison.OrdinalIgnoreCase)
            || name.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Shows a localized label for an Activity filter value while the bound item stays the English key.
///
/// The filter collections are plain strings that double as the comparison key ("All", "Confirmed",
/// "Last 7 days"), so translating the collections themselves would break every filter comparison and
/// any persisted selection. Converting only at DISPLAY time keeps the logic untouched.
///
/// A value with no matching key — an asset ticker like "BTC", which the asset filter builds from the
/// feed — falls through unchanged, which is exactly right for a ticker.
/// </summary>
public sealed class ActivityLabelConverter : IValueConverter
{
    public static readonly ActivityLabelConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Translate(value?.ToString());

    /// <summary>The activity word in the wallet's language ("unlocked" → "розблоковано"); anything
    /// without an entry (a ticker, an address) as it is.</summary>
    public static string Translate(string? key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;

        var slug = "activity.opt." + key.ToLowerInvariant()
            .Replace(' ', '-')
            .Replace(".", string.Empty);

        var translated = Loc.Instance[slug];
        // Loc returns the key itself when it has no entry — that is the "not a translatable option"
        // case (a ticker), so show the original value rather than a slug.
        return translated == slug ? key : translated;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Bold when true, normal otherwise. Used by the send simulation so the two numbers that decide
/// whether somebody presses Confirm — what leaves the wallet, and what is left afterwards — carry more
/// weight than the lines that make them up.
/// </summary>
public sealed class BoolToWeightConverter : IValueConverter
{
    public static readonly BoolToWeightConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Avalonia.Media.FontWeight.SemiBold : Avalonia.Media.FontWeight.Normal;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// A brush from a theme resource key ("UmAccentBright") or a literal colour ("#E58A8A"); with the
/// parameter "wash", the same colour faint, for an icon's disc. Lets data pick a THEMED colour.
/// </summary>
public sealed class ThemeBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value as string ?? string.Empty;
        Avalonia.Media.Color color;
        if (key.StartsWith('#'))
        {
            color = Avalonia.Media.Color.Parse(key);
        }
        else if (Avalonia.Application.Current?.Resources.TryGetResource(key, null, out var res) == true &&
                 res is Avalonia.Media.ISolidColorBrush solid)
        {
            color = solid.Color;
        }
        else
        {
            color = Avalonia.Media.Colors.Gray;
        }

        var alpha = (parameter as string) switch
        {
            "wash" => (byte)0x26,
            "ring" => (byte)0x4D,
            _ => color.A,
        };
        return new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.FromArgb(alpha, color.R, color.G, color.B));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// One end of a chart guide line, from a single coordinate: a horizontal line at a given y spans the
/// plot (x 10 → 790), a vertical line at a given x spans price area and volume band (y 14 → 272).
/// Lets the crosshair and last-price lines bind to the one number the view model computes.
/// </summary>
public sealed class ChartLinePointConverter : IValueConverter
{
    public string Kind { get; set; } = "HStart";

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var v = value is double d ? d : 0;
        return Kind switch
        {
            "HStart" => new Avalonia.Point(10, v),
            "HEnd" => new Avalonia.Point(790, v),
            "VStart" => new Avalonia.Point(v, 14),
            _ => new Avalonia.Point(v, 272),
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
