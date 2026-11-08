using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Umbrella.Wallet.App;

/// <summary>What comparing a typed phrase with the wallet's own found.</summary>
/// <param name="Matches">Every word, in order, is the wallet's.</param>
/// <param name="TypedCount">How many words were typed.</param>
/// <param name="StoredCount">How many words the wallet's phrase has.</param>
/// <param name="Mismatches">The 1-based positions whose word differs (only when the counts agree).</param>
public sealed record PhraseCheckResult(bool Matches, int TypedCount, int StoredCount, IReadOnlyList<int> Mismatches);

/// <summary>
/// The paper check (roadmap P1.8): does the phrase the user wrote down restore THIS wallet?
///
/// A backup nobody has tried is a hope, not a backup — and the only way most people find out their
/// paper has a wrong word is the day they need it. This compares what they type from the paper with the
/// wallet's own phrase and says yes, or which positions differ, without ever showing the phrase.
///
/// It reads a phrase the way people copy one: numbered ("1. abandon 2) ability"), split over lines,
/// separated by commas, in capitals. Pure and offline, so it is pinned by tests.
/// </summary>
public static class PhraseCheck
{
    /// <summary>The words of a phrase as typed, with numbering, punctuation and case taken away.</summary>
    public static IReadOnlyList<string> Words(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
        var normalized = text.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        var tokens = normalized.Split([' ', '\t', '\r', '\n', ',', ';', '　', '、'], StringSplitOptions.RemoveEmptyEntries);
        var words = new List<string>(tokens.Length);
        foreach (var raw in tokens)
        {
            var token = raw.Trim('.', ')', '(', ':', '-', '"', '\'', '«', '»');
            if (token.Length == 0) continue;
            if (token.All(char.IsDigit)) continue;   // "1." "2)" — the number written beside a word
            // "1.abandon", "2)ability": a number glued to its word
            var start = 0;
            while (start < token.Length && char.IsDigit(token[start])) start++;
            if (start > 0 && start < token.Length && token[start] is '.' or ')' or ':')
                token = token[(start + 1)..];
            if (token.Length > 0) words.Add(token);
        }

        return words;
    }

    /// <summary>Compares a typed phrase with the wallet's own, word by word.</summary>
    public static PhraseCheckResult Compare(string stored, string typed)
    {
        var mine = Words(stored);
        var theirs = Words(typed);
        if (mine.Count != theirs.Count)
            return new PhraseCheckResult(false, theirs.Count, mine.Count, Array.Empty<int>());

        var mismatches = new List<int>();
        for (var i = 0; i < mine.Count; i++)
            if (!string.Equals(mine[i], theirs[i], StringComparison.Ordinal)) mismatches.Add(i + 1);

        return new PhraseCheckResult(mismatches.Count == 0 && mine.Count > 0, theirs.Count, mine.Count, mismatches);
    }
}
