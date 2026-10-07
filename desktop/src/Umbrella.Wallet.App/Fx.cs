using System.Globalization;

namespace Umbrella.Wallet.App;

/// <summary>
/// The display currency. Prices and balances are computed in USD everywhere; this converts them to the
/// user's chosen fiat at display time (one USD→currency rate, refreshed with the market) and supplies
/// the symbol. A process-wide holder so the lightweight row records can format without a back-reference
/// to the view model — there is a single wallet window, so there is no cross-window contention.
/// </summary>
public static class Fx
{
    /// <summary>USD → the currency figures are shown in. 1.0 while that is US dollars.</summary>
    public static decimal Rate { get; set; } = 1m;

    /// <summary>The symbol of the currency figures are shown in (e.g. "$", "€", "₴").</summary>
    public static string Symbol { get; set; } = "$";

    /// <summary>The currency figures are actually shown in: the chosen one once its rate is known, US
    /// dollars until then. Captions name this, not the chosen one, so they never claim a conversion that
    /// has not happened.</summary>
    public static string Code { get; private set; } = "USD";

    /// <summary>
    /// Switches every figure to <paramref name="code"/> at <paramref name="rate"/> units per US dollar —
    /// symbol, rate and code together, so no figure can be drawn with one currency's symbol and another's
    /// amount. Without a usable rate the figures stay in US dollars and say so: "¥104,32" for 104 dollars
    /// (a rate that failed to load used to be taken as 1.0) is worse than "$104,32".
    /// </summary>
    public static void Use(string code, decimal? rate)
    {
        var known = Currencies.Any(c => c.Code == code);
        if (!known || code == "USD" || rate is not > 0m)
        {
            (Code, Symbol, Rate) = ("USD", "$", 1m);
            return;
        }
        (Code, Symbol, Rate) = (code, SymbolFor(code), rate.Value);
    }

    /// <summary>Locale used to format FIAT amounts (digit grouping + decimal separator), so a
    /// German/Ukrainian user sees "1.234,56" / "1 234,56" rather than the US "1,234.56". Crypto amounts
    /// are deliberately left in the universal "." form elsewhere. Display only — never re-parsed.</summary>
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en-US");

    /// <summary>Point the fiat formatter at the locale for the given UI-language code. Falls back to
    /// en-US if the OS lacks that culture, so formatting can never throw.</summary>
    public static void SetLanguage(string code)
    {
        try { Culture = CultureInfo.GetCultureInfo(CultureName(code)); }
        catch { Culture = CultureInfo.GetCultureInfo("en-US"); }
    }

    private static string CultureName(string code) => (code ?? "").Trim().ToLowerInvariant() switch
    {
        "uk" => "uk-UA",
        "ru" => "ru-RU",
        "de" => "de-DE",
        "es" => "es-ES",
        "zh" => "zh-CN",
        _ => "en-US",
    };

    public sealed record Currency(string Code, string Symbol, string Name);

    /// <summary>The fiat currencies the wallet can display balances in.</summary>
    public static readonly IReadOnlyList<Currency> Currencies =
    [
        new("USD", "$", "US Dollar"),
        new("EUR", "€", "Euro"),
        new("UAH", "₴", "Ukrainian Hryvnia"),
        new("RUB", "₽", "Russian Ruble"),
        new("GBP", "£", "British Pound"),
        new("CNY", "¥", "Chinese Yuan"),
        new("JPY", "¥", "Japanese Yen"),
        new("PLN", "zł", "Polish Zloty"),
        new("TRY", "₺", "Turkish Lira"),
        new("INR", "₹", "Indian Rupee"),
        // + popular currencies (rates from open.er-api.com, which covers all of these).
        new("CAD", "C$", "Canadian Dollar"),
        new("AUD", "A$", "Australian Dollar"),
        new("CHF", "Fr", "Swiss Franc"),
        new("BRL", "R$", "Brazilian Real"),
        new("KRW", "₩", "South Korean Won"),
        new("MXN", "MX$", "Mexican Peso"),
        new("ZAR", "R", "South African Rand"),
        new("SEK", "kr", "Swedish Krona"),
        new("NOK", "kr", "Norwegian Krone"),
        new("AED", "د.إ", "UAE Dirham"),
        new("SGD", "S$", "Singapore Dollar"),
        new("HKD", "HK$", "Hong Kong Dollar"),
        new("KZT", "₸", "Kazakhstani Tenge"),
    ];

    public static string SymbolFor(string code) =>
        Currencies.FirstOrDefault(c => c.Code == code)?.Symbol ?? "$";

    /// <summary>A converted money amount with the current symbol, formatted for the user's locale,
    /// e.g. "₴1 234,56" (uk) or "$1,234.56" (en).</summary>
    public static string Money(double usd) =>
        Symbol + ((decimal)usd * Rate).ToString("N2", Culture);

    /// <summary>A converted price: 2 decimals at/above 1 unit; below it, four significant digits
    /// ("0,2486", "0,08396", "0,00001234") — a fixed six decimals read "0,248600" and hid the cheap
    /// coins' real precision behind zeros.</summary>
    public static string Price(double usd)
    {
        var v = (decimal)usd * Rate;
        return Symbol + v.ToString("N" + PriceDecimals(v), Culture);
    }

    /// <summary>Decimals for a price: 2 from one unit up, else enough for four significant digits (max 10).</summary>
    public static int PriceDecimals(decimal v)
    {
        if (v >= 1 || v <= 0) return 2;
        var leadingZeros = (int)Math.Floor(-Math.Log10((double)v));
        return Math.Min(10, Math.Max(4, leadingZeros + 4));
    }
}
