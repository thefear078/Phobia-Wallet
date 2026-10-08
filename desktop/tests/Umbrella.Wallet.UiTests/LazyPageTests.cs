using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Umbrella.Wallet.App.Controls;
using Xunit;

namespace Umbrella.Wallet.UiTests;

/// <summary>
/// The shell's pages are built the first time they are shown (<see cref="LazyPage"/>), so a page nobody
/// opens costs nothing at start — and a page that was opened keeps what was typed into it.
/// </summary>
public sealed class LazyPageTests
{
    [AvaloniaFact]
    public void A_page_is_built_when_first_shown_and_then_kept()
    {
        var lazy = new LazyPage { Page = typeof(TextBox), IsVisible = false };
        var window = new Window { Content = lazy };
        window.Show();

        Assert.Null(lazy.Child);                       // hidden: nothing built

        lazy.IsVisible = true;
        var page = Assert.IsType<TextBox>(lazy.Child);
        page.Text = "typed";

        lazy.IsVisible = false;
        lazy.IsVisible = true;
        Assert.Same(page, lazy.Child);                 // the same page, with what was typed
        Assert.Equal("typed", page.Text);

        window.Close();
    }

    [AvaloniaFact]
    public void A_page_that_starts_visible_is_built_once_it_is_in_a_window()
    {
        var lazy = new LazyPage { Page = typeof(TextBox) };
        Assert.Null(lazy.Child);                       // not before: its binding has no view model yet

        var window = new Window { Content = lazy };
        window.Show();
        Assert.IsType<TextBox>(lazy.Child);

        window.Close();
    }
}
