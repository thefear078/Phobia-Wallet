using System.Text.RegularExpressions;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// A <c>{StaticResource X}</c> resolves only in the file that defines X or in App.axaml.
///
/// When the pages were split into their own files, the market-row template went with the Market page
/// and the home page's side rail kept pointing at it. Nothing failed: the reference resolved to nothing,
/// the list fell back to <c>ToString()</c>, and the home screen printed
/// "MarketRowViewModel { Symbol = BTC, Name = … }" in place of six coins. This reads every view and
/// names any key used where it cannot be found.
/// </summary>
public sealed class XamlResourceScopeTests
{
    [Fact]
    public void Every_static_resource_is_defined_where_it_is_used_or_in_the_app()
    {
        var app = Path.Combine(RepoRoot(), "desktop", "src", "Umbrella.Wallet.App");
        var shared = Keys(File.ReadAllText(Path.Combine(app, "App.axaml")));
        var missing = new List<string>();

        foreach (var file in Directory.EnumerateFiles(app, "*.axaml", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            var own = Keys(text);
            foreach (Match m in Regex.Matches(text, @"StaticResource\s+([A-Za-z0-9_.]+)"))
            {
                var key = m.Groups[1].Value;
                if (!own.Contains(key) && !shared.Contains(key))
                    missing.Add($"{Path.GetRelativePath(app, file)} uses {key}");
            }
        }

        Assert.True(missing.Count == 0, "Static resources not reachable where they are used:\n" + string.Join("\n", missing.Distinct()));
    }

    private static HashSet<string> Keys(string xaml) =>
        Regex.Matches(xaml, @"x:Key=""([^""]+)""").Select(m => m.Groups[1].Value).ToHashSet();

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VERSION"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repository root");
    }
}
