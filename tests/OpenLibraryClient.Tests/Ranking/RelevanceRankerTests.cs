using OpenLibraryClient.Core.Models;
using OpenLibraryClient.Core.Parsing;
using OpenLibraryClient.Core.Ranking;

namespace OpenLibraryClient.Tests.Ranking;

public class RelevanceRankerTests
{
    private readonly RelevanceRanker _ranker = new(new SimilarityScorer(), new RankingExplainer());

    private static OpenLibraryDoc Doc(string key, string title, string[] authors, int editionCount, string[] subjects) => new()
    {
        Key = key,
        Title = title,
        AuthorNames = authors,
        EditionCount = editionCount,
        Subjects = subjects
    };

    [Fact]
    public void Rank_DeterministicExtraction_ExactTitleAuthorMatchOutranksPopularDecoy()
    {
        var extraction = new ExtractionResult
        {
            Title = "Dune",
            Author = "Frank Herbert",
            Keywords = ["desert", "planet"],
            Confidence = 0.85,
            Source = ExtractionSource.Deterministic,
            RawQuery = "Dune by Frank Herbert"
        };

        var exactMatch = Doc("/works/OL1", "Dune", ["Frank Herbert"], editionCount: 50, ["Science fiction", "Desert", "Adventure"]);
        var popularDecoy = Doc("/works/OL2", "The Hobbit", ["J.R.R. Tolkien"], editionCount: 300, ["Fantasy", "Adventure"]);
        var sameAuthorDifferentBook = Doc("/works/OL3", "Dune Messiah", ["Frank Herbert"], editionCount: 20, ["Science fiction"]);

        var results = _ranker.Rank(extraction, [popularDecoy, exactMatch, sameAuthorDifferentBook]);

        Assert.Equal(3, results.Count);
        Assert.Equal("/works/OL1", results[0].Doc.Key);
        // Popularity alone should not beat an exact title+author match under deterministic weights.
        Assert.True(results[0].Score > results.Single(r => r.Doc.Key == "/works/OL2").Score);
    }

    [Fact]
    public void Rank_DeterministicExtraction_HighestScoreHasHighTitleAndAuthorSimilarity()
    {
        var extraction = new ExtractionResult
        {
            Title = "Dune",
            Author = "Frank Herbert",
            Keywords = [],
            Confidence = 0.85,
            Source = ExtractionSource.Deterministic,
            RawQuery = "Dune by Frank Herbert"
        };

        var exactMatch = Doc("/works/OL1", "Dune", ["Frank Herbert"], editionCount: 50, []);
        var unrelated = Doc("/works/OL2", "Moby Dick", ["Herman Melville"], editionCount: 400, []);

        var results = _ranker.Rank(extraction, [unrelated, exactMatch]);

        var top = results[0];
        Assert.Equal("/works/OL1", top.Doc.Key);
        Assert.True(top.Breakdown.TitleSimilarity > 0.9);
        Assert.True(top.Breakdown.AuthorSimilarity > 0.9);
        Assert.False(string.IsNullOrWhiteSpace(top.Explanation));
    }

    [Fact]
    public void Rank_LlmExtraction_WeightsKeywordOverlapAndPopularityMoreThanDeterministic()
    {
        // No structured title/author (LLM fallback scenario) - only keywords to go on.
        var extraction = new ExtractionResult
        {
            Title = null,
            Author = null,
            Keywords = ["desert", "planet", "sci-fi"],
            Confidence = 0.3,
            Source = ExtractionSource.Llm,
            RawQuery = "something about a desert planet sci-fi book"
        };

        var keywordMatch = Doc("/works/OL1", "Sandworms of the Void", [], editionCount: 10, ["Desert", "Planet", "Sci-fi"]);
        var noKeywordMatch = Doc("/works/OL2", "Unrelated Title", [], editionCount: 10, ["Romance"]);

        var results = _ranker.Rank(extraction, [noKeywordMatch, keywordMatch]);

        Assert.Equal("/works/OL1", results[0].Doc.Key);
        Assert.True(results[0].Breakdown.KeywordOverlap > results[1].Breakdown.KeywordOverlap);
        Assert.Contains("desert", results[0].Explanation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rank_EmptyCandidates_ReturnsEmptyList()
    {
        var extraction = new ExtractionResult
        {
            Confidence = 0.5,
            Source = ExtractionSource.Deterministic,
            RawQuery = "anything"
        };

        var results = _ranker.Rank(extraction, []);

        Assert.Empty(results);
    }

    [Fact]
    public void Rank_ResultsAreSortedDescendingByScore()
    {
        var extraction = new ExtractionResult
        {
            Title = "Dune",
            Author = "Frank Herbert",
            Keywords = [],
            Confidence = 0.85,
            Source = ExtractionSource.Deterministic,
            RawQuery = "Dune by Frank Herbert"
        };

        var docs = new[]
        {
            Doc("/works/OL1", "Dune", ["Frank Herbert"], 50, []),
            Doc("/works/OL2", "Dune Messiah", ["Frank Herbert"], 20, []),
            Doc("/works/OL3", "Completely Different Book", ["Someone Else"], 5, []),
        };

        var results = _ranker.Rank(extraction, docs);

        Assert.True(results.Zip(results.Skip(1)).All(pair => pair.First.Score >= pair.Second.Score));
    }
}
