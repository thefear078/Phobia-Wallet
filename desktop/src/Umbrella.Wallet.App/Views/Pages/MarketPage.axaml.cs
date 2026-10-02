using Avalonia.Controls;
using Avalonia.Input;
using Umbrella.Wallet.App.ViewModels;

namespace Umbrella.Wallet.App.Views.Pages;

/// <summary>The Market screen, shared by the desktop window and the Android shell. The chart's
/// crosshair follows the pointer from here.</summary>
public partial class MarketPage : UserControl
{
    public MarketPage() => InitializeComponent();

    private void OnChartPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is not MainViewModel vm || sender is not Control canvas) return;
        var at = e.GetPosition(canvas);
        vm.UpdateCrosshair(at.X, at.Y);
    }

    private void OnChartPointerExited(object? sender, PointerEventArgs e)
    {
        (DataContext as MainViewModel)?.HideCrosshair();
    }
}
