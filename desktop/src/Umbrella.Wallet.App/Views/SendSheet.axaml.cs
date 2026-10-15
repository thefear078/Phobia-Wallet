using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Umbrella.Wallet.App.ViewModels;

namespace Umbrella.Wallet.App.Views;

/// <summary>
/// The send sheet — review, in flight, receipt — shared by the desktop window and the phone shell.
/// The view model decides what it shows; this file owns only what needs pixels or focus: saving the
/// receipt as a picture, and putting the caret in the password box when the review opens.
/// </summary>
public partial class SendSheet : UserControl
{
    private MainViewModel? _observed;

    public SendSheet()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Observe(DataContext as MainViewModel);
    }

    private void Observe(MainViewModel? vm)
    {
        if (ReferenceEquals(_observed, vm)) return;
        if (_observed is not null) _observed.PropertyChanged -= OnViewModelChanged;
        _observed = vm;
        if (_observed is not null) _observed.PropertyChanged += OnViewModelChanged;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The review has one thing to type. The caret goes there, so the send is: read, password, Enter.
        if (e.PropertyName is nameof(MainViewModel.IsSendReviewShown) && _observed is { IsSendReviewShown: true, RequirePasswordForSend: true })
            Dispatcher.UIThread.Post(() => this.FindControl<TextBox>("ReviewPassword")?.Focus(), DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Writes the receipt card — the part above the buttons — to a PNG the user picks a place for.
    /// Rendered at twice its size so it stays sharp when shared.
    /// </summary>
    private async void OnSaveReceiptImage(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || this.FindControl<Border>("ReceiptCard") is not { } card) return;
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null) { vm.ReceiptImageSaved(false); return; }

            var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                SuggestedFileName = vm.ReceiptFileName,
                DefaultExtension = "png",
                FileTypeChoices = [new FilePickerFileType("PNG") { Patterns = ["*.png"] }],
            });
            if (file is null) return;   // cancelled: nothing to say

            const double scale = 2;
            var size = card.Bounds.Size;
            var pixels = new PixelSize((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale));
            using var bitmap = new RenderTargetBitmap(pixels, new Vector(96 * scale, 96 * scale));
            bitmap.Render(card);

            await using var stream = await file.OpenWriteAsync();
            bitmap.Save(stream);
            vm.ReceiptImageSaved(true);
        }
        catch
        {
            vm.ReceiptImageSaved(false);
        }
    }
}
