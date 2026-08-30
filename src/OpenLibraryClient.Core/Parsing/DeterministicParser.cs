using System.Text.RegularExpressions;
using OpenLibraryClient.Core.Abstractions;
using OpenLibraryClient.Core.Models;

namespace OpenLibraryClient.Core.Parsing;

/// <summary>
/// Parses messy "bookInfo" strings using ordered regex patterns for common, unambiguous
/// separators (quoted titles, "by", parentheses, hyphen). Falls back to treating the whole
/// string as an unstructured keyword bag when none of them match - including for inputs whose
/// only separator is a bare comma, which is deliberately not recognized here since comma order
/// between title and author is ambiguous (e.g. "Dune, Frank Herbert" vs. "Herbert, Frank" as a
/// surname-first author listing) and not worth guessing at deterministically.
///
/// This is intentionally pure/no-I/O: it does not call Open Library or an LLM. Verification
/// against real Open Library data happens later, in the ranking stage.
/// </summary>
public sealed class DeterministicParser : IDeterministicParser
{
    private static readonly string[] StopWords =
    [
        "the", "a", "an", "book", "books", "by", "called", "titled", "novel", "author",
        "and", "or", "of", "in", "on", "with", "about"
    ];

    // Named groups: title, author. Only unambiguous separators appear here - a match against any
    // of these is trusted outright (see IBookInfoExtractor), so anything weaker (e.g. a bare
    // comma) is deliberately left out and handled by the unstructured-keyword-bag fallback below.
    private static readonly Regex[] Patterns =
    [
        // "Title" by Author  /  'Title' by Author
        // Author capture stops at a comma so trailing free text (e.g. ", sci-fi desert planet")
        // is left in the remainder for ExtractKeywords rather than being swallowed as part of the name.
        new(@"^[""'](?<title>[^""']+)[""']\s*(?:by)\s*(?<author>[^,]+)", RegexOptions.IgnoreCase),

        // Title by Author
        new(@"^(?<title>.+?)\s+by\s+(?<author>[^,]+)", RegexOptions.IgnoreCase),

        // Title (Author)
        new(@"^(?<title>.+?)\s*\((?<author>[^)]+)\)\s*$", RegexOptions.IgnoreCase),

        // Title - Author  /  Title – Author  /  Title — Author
        new(@"^(?<title>.+?)\s*[-–—]\s*(?<author>.+)$", RegexOptions.IgnoreCase),
    ];

    public ExtractionResult Parse(string bookInfo)
    {
        var normalized = Normalize(bookInfo);

        foreach (var pattern in Patterns)
        {
            var match = pattern.Match(normalized);
            if (!match.Success)
            {
                continue;
            }

            var title = CleanField(match.Groups["title"].Value);
            var author = CleanField(match.Groups["author"].Value);

            if (title.Length == 0 || author.Length == 0)
            {
                continue;
            }

            var keywords = ExtractKeywords(normalized, title, author);

            return new ExtractionResult
            {
                Title = title,
                Author = author,
                Keywords = keywords,
                Source = ExtractionSource.Deterministic,
                Explanation = "input clear enough for deterministic match",
                RawQuery = bookInfo
            };
        }

        // No recognizable separator: treat everything as an unstructured keyword bag.
        var fallbackKeywords = ExtractKeywords(normalized, title: null, author: null);
        return new ExtractionResult
        {
            Title = null,
            Author = null,
            Keywords = fallbackKeywords,
            Source = ExtractionSource.Deterministic,
            Explanation = "input did not match a recognizable pattern; treated as an unstructured keyword search",
            RawQuery = bookInfo
        };
    }

    private static string Normalize(string input)
    {
        var trimmed = input.Trim();
        // Collapse repeated whitespace.
        return Regex.Replace(trimmed, @"\s+", " ");
    }

    private static string CleanField(string value)
    {
        return value.Trim(' ', '"', '\'', '.', ',', '-', '–', '—').Trim();
    }

    private static IReadOnlyList<string> ExtractKeywords(string normalized, string? title, string? author)
    {
        var remainder = normalized;
        if (title is not null)
        {
            remainder = RemoveWholeWordOccurrence(remainder, title);
        }
        if (author is not null)
        {
            remainder = RemoveWholeWordOccurrence(remainder, author);
        }

        var tokens = Regex.Split(remainder.ToLowerInvariant(), @"[^\p{L}\p{Nd}]+")
            .Where(t => t.Length > 2 && !StopWords.Contains(t))
            .Distinct()
            .ToList();

        return tokens;
    }

    /// <summary>
    /// Removes a whole-word/whole-phrase occurrence of <paramref name="value"/> from
    /// <paramref name="input"/> using word boundaries, rather than a naive substring replace.
    /// A plain <c>string.Replace</c> would incorrectly strip partial-word matches - e.g. a short
    /// title/author like "Dun" or "It" could otherwise delete letters out of unrelated words
    /// (e.g. "Dune") elsewhere in the remainder, corrupting keyword extraction.
    /// </summary>
    private static string RemoveWholeWordOccurrence(string input, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return input;
        }

        var pattern = $@"\b{Regex.Escape(value)}\b";
        return Regex.Replace(input, pattern, "", RegexOptions.IgnoreCase);
    }
}
