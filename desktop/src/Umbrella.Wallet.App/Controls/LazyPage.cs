using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;

namespace Umbrella.Wallet.App.Controls;

/// <summary>
/// A page built the first time it is shown. The shell holds fifteen pages and a session opens a few of
/// them; building every one before the window appeared cost about a third of a second on a cold start
/// (Settings alone 116 ms, Market 57, Send 49), and each hidden page's bindings went on answering every
/// change the wallet made. Once built, the page stays — with whatever was typed into it — exactly as
/// before.
/// </summary>
public sealed class LazyPage : Decorator
{
    /// <summary>The page to build, e.g. <c>{x:Type pages:SettingsPage}</c>.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public Type? Page { get; set; }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty) Build();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Build();
    }

    // Only once it is in a window: until its binding has a view model, IsVisible reads its default (true).
    private void Build()
    {
        if (Child is null && IsVisible && Page is not null && VisualRoot is not null)
            Child = (Control)Activator.CreateInstance(Page)!;
    }
}
