using System;
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
