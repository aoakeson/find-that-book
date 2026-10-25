using FindThatBook.Core.Matching;
using FindThatBook.Core.Model;
using FindThatBook.Core.Normalization;

namespace FindThatBook.Core.Tests.Matching;

/// <summary>Ranker invariants and evidence semantics on small hand-built pools (and determinism on every recorded pool).</summary>
public sealed class MatchRankerTests
{
    public static TheoryData<string> AllCases => PoolFixtures.Ids(PoolFixtures.GoldenHard, PoolFixtures.Targeted);

    [Theory]
    [MemberData(nameof(AllCases))]
    public void Ranking_is_deterministic_and_independent_of_pool_order(string id)
    {
        var c = PoolFixtures.Case(id);
        var q = QueryCleaner.Clean(c.Query);
        var pool = c.Pool();
        var first = Signature(MatchRanker.Rank(q, c.Hypothesis, pool));
        Signature(MatchRanker.Rank(q, c.Hypothesis, pool)).ShouldBe(first);
        var shuffled = pool.OrderBy(_ => Random.Shared.Next()).ToList();
        Signature(MatchRanker.Rank(q, c.Hypothesis, shuffled)).ShouldBe(first);
    }

    [Fact]
    public void An_empty_pool_ranks_nothing_and_says_none() // 01 row 49
    {
        var r = MatchRanker.Rank(QueryCleaner.Clean("the hobbit"), null, []);
        r.Ranked.ShouldBeEmpty();
        r.Status.ShouldBe(MatchStatus.None);
        r.Reason.ShouldBe(StatusReason.NoCandidates);
    }

    [Fact]
    public void Score_ties_fall_to_edition_count_then_work_id()
    {
        // Same title, different single authors, same editions and reading log: identical scores.
        var pool = new[] { Work("OL30W", "Kindred", ["A One"], 5), Work("OL20W", "Kindred", ["B Two"], 5), Work("OL10W", "Kindred", ["C Three"], 4) };
        var r = MatchRanker.Rank(QueryCleaner.Clean("kindred"), null, pool);
        r.Ranked.Select(w => w.Work.WorkId).ShouldBe(["OL20W", "OL30W", "OL10W"]);
    }

    [Fact]
    public void Duplicate_author_records_merge_by_name_key() // 01 row 48: Mark Twain OL18319A + OL9937375A
    {
        var pool = new[]
        {
            Work("OL53908W", "Adventures of Huckleberry Finn", ["Mark Twain"], 2623, keys: ["OL18319A"]),
            Work("OL37513138W", "The Adventures of Huckleberry Finn", ["Mark Twain", "Mark Twain"], 161, keys: ["OL9937375A", "OL18319A"]),
        };
        var r = MatchRanker.Rank(QueryCleaner.Clean("huckleberry finn twain"), null, pool);
        r.Ranked.Count.ShouldBe(1);
        r.Ranked[0].Work.WorkId.ShouldBe("OL53908W");
        r.Ranked[0].FoldedWorkIds.ShouldBe(["OL37513138W"]);
        r.Ranked[0].PrimaryAuthors.Select(a => a.Name).ShouldBe(["Mark Twain"]);
    }

    [Fact]
    public void A_tiny_top_gives_way_to_a_same_title_peer_with_ten_times_its_editions() // DA-009 tiny-record rule
    {
        var pool = new[]
        {
            Work("OL1W", "The Cuckoo's Calling", ["Robert Galbraith"], 2, readingLog: 50_000),
            Work("OL2W", "The Cuckoo's Calling", ["J. K. Rowling"], 28),
        };
        var r = MatchRanker.Rank(QueryCleaner.Clean("cuckoos calling"), null, pool);
        r.Ranked[0].Work.WorkId.ShouldBe("OL2W");
    }

    [Fact]
    public void A_contributor_role_from_the_contributor_field_labels_the_demoted_author_and_extra_credits()
    {
        var pool = new[]
        {
            Work("OL27482W", "The Hobbit", ["J.R.R. Tolkien"], 482, contributors: ["Alan Lee (Illustrator)", "Lee, Alan, ill."]),
            Work("OL219602W", "The Hobbit", ["Charles Dixon", "J.R.R. Tolkien"], 10, contributors: ["Charles Dixon (Adapter)"]),
        };
        var r = MatchRanker.Rank(QueryCleaner.Clean("hobbit dixon"), null, pool);
        var gn = r.Ranked.Single(w => w.Work.WorkId == "OL219602W");
        gn.Contributors.ShouldContain(c => c.Name == "Charles Dixon" && c.Role == "adapter");
        gn.PrimaryAuthors.Select(a => a.Name).ShouldBe(["J.R.R. Tolkien"]); // author order carries no meaning (01 Q2)
        r.Ranked.Single(w => w.Work.WorkId == "OL27482W").Contributors.ShouldContain(c => c.Name == "Alan Lee" && c.Role == "illustrator");
    }

    [Fact]
    public void A_lone_explicit_adapter_is_never_promoted_to_primary_author()
    {
        var pool = new[] { Work("OL1W", "The Hobbit", ["Charles Dixon"], 10, contributors: ["Charles Dixon (Adapter)"]) };
        var top = MatchRanker.Rank(QueryCleaner.Clean("hobbit dixon"), null, pool).Ranked[0];
        top.PrimaryAuthors.ShouldBeEmpty();
        top.Contributors.ShouldContain(c => c.Name == "Charles Dixon" && c.Role == "adapter");
        top.Tier.ShouldNotBe(MatchTier.A);
    }

    [Fact]
    public void An_exact_primary_title_precedes_a_more_popular_near_title()
    {
        var pool = new[]
        {
            Work("OL1W", "Quantum Garden", ["Alice Writer"], 10),
            Work("OL2W", "Quantum Garden Revisited", ["Alice Writer"], 1000),
        };
        var top = MatchRanker.Rank(QueryCleaner.Clean("quantum garden alice writer"), null, pool).Ranked[0];
        top.Work.WorkId.ShouldBe("OL1W");
        top.Tier.ShouldBe(MatchTier.A);
    }

    [Fact]
    public void Placeholder_credit_names_never_reach_the_contributor_list() // seen live on OL27482W
    {
        var pool = new[]
        {
            Work("OL27482W", "The Hobbit", ["J.R.R. Tolkien"], 482,
                contributors: ["Author (Illustrator)", "Dramatization (Narrator)", "Various (Editor)", "Michael Hague (Illustrator)"]),
        };
        var top = MatchRanker.Rank(QueryCleaner.Clean("the hobbit"), null, pool).Ranked[0];
        top.Contributors.Select(c => c.Name).ShouldBe(["Michael Hague"]);
        ContributorRoles.IsPlaceholderName("Author").ShouldBeTrue();
        ContributorRoles.IsPlaceholderName("Full Cast").ShouldBeTrue();
        ContributorRoles.IsPlaceholderName("Alan Lee").ShouldBeFalse();
    }

    [Fact]
    public void A_work_listing_only_the_adapter_inherits_the_canonical_primary() // contributor propagation (OL19622663W)
    {
        var pool = new[]
        {
            Work("OL27482W", "The Hobbit", ["J.R.R. Tolkien"], 482),
            Work("OL219602W", "The Hobbit", ["Charles Dixon", "J.R.R. Tolkien"], 10, contributors: ["Charles Dixon (Adapter)"]),
            Work("OL19622663W", "The Hobbit", ["Charles Dixon"], 1),
        };
        var r = MatchRanker.Rank(QueryCleaner.Clean("hobbit dixon"), null, pool);
        r.Ranked[0].Work.WorkId.ShouldBe("OL219602W");
        r.Ranked[0].FoldedWorkIds.ShouldBe(["OL19622663W"]); // same group: inherited Tolkien + matched contributor Dixon
    }

    [Fact]
    public void Evidence_says_typed_when_the_typed_title_matches_even_if_the_interpreter_agrees()
    {
        var pool = new[] { Work("OL1W", "Dune", ["Frank Herbert"], 300) };
        var r = MatchRanker.Rank(QueryCleaner.Clean("dune"), Hyp("Dune", "Frank Herbert"), pool);
        r.Ranked[0].Evidence.TitleSource.ShouldBe(TitleSource.Typed);
        r.Ranked[0].Evidence.MatchedTitleTerms.ShouldBe(["dune"]);
        r.Ranked[0].Evidence.AuthorFromHypothesis.ShouldBeTrue();
        r.Ranked[0].Evidence.Role.ShouldBe(AuthorRole.Primary);
        r.Ranked[0].Evidence.MatchedAuthor.ShouldBe("Frank Herbert");
    }

    [Fact]
    public void Evidence_characters_need_a_typed_word_and_allow_a_typo_only_on_five_letters()
    {
        // Measured on live data: "ring" named King Thranduil (one edit on four letters, DA-052).
        var pool = new[] { Work("OL27448W", "The Lord of the Rings", ["J.R.R. Tolkien"], 400, persons: ["Frodo Baggins", "King Thranduil", "Gandalf"]) };
        var lotr = MatchRanker.Rank(QueryCleaner.Clean("frodo ring"), Hyp("The Lord of the Rings", "J.R.R. Tolkien"), pool);
        lotr.Ranked.Single().Evidence.MatchedCharacters.ShouldBe(["Frodo Baggins"]);

        var typo = MatchRanker.Rank(QueryCleaner.Clean("frodo gandlaf"), Hyp("The Lord of the Rings", "J.R.R. Tolkien"), pool);
        typo.Ranked.Single().Evidence.MatchedCharacters.ShouldBe(["Frodo Baggins", "Gandalf"]);
    }

    [Fact]
    public void Evidence_says_hypothesis_or_original_title_only_when_the_title_came_from_the_interpreter()
    {
        var pool = new[]
        {
            Work("OL1W", "To Kill a Mockingbird", ["Harper Lee"], 213, persons: ["Scout Finch", "Atticus Finch"]),
            Work("OL2W", "Le petit prince", ["Antoine de Saint-Exupéry"], 699),
        };
        var mockingbird = MatchRanker.Rank(QueryCleaner.Clean("scout atticus"), Hyp("To Kill a Mockingbird", "Harper Lee"), pool);
        var w = mockingbird.Ranked.Single(x => x.Work.WorkId == "OL1W");
        w.Evidence.TitleSource.ShouldBe(TitleSource.Hypothesis);
        w.Evidence.MatchedTitleTerms.ShouldBeEmpty();
        w.Evidence.MatchedCharacters.ShouldBe(["Scout Finch", "Atticus Finch"]);
        w.Evidence.CharacterCoverage.ShouldBe(1.0);

        var prince = MatchRanker.Rank(QueryCleaner.Clean("the little prince"),
            Hyp("The Little Prince", "Antoine de Saint-Exupéry", original: "Le Petit Prince"), pool);
        prince.Ranked[0].Work.WorkId.ShouldBe("OL2W");
        prince.Ranked[0].Evidence.TitleSource.ShouldBe(TitleSource.OriginalTitle);
    }

    [Fact]
    public void A_volume_marked_part_is_penalised_unless_the_query_asks_for_a_volume() // IN-035
    {
        var pool = new[] { Work("OL1W", "ノルウェイの森 (下)", ["村上春樹"], 9), Work("OL2W", "ノルウェイの森", ["村上春樹"], 5) };
        var plain = MatchRanker.Rank(QueryCleaner.Clean("ノルウェイの森"), null, pool);
        plain.Ranked[0].Work.WorkId.ShouldBe("OL2W");
        plain.Ranked.Single(x => x.Work.WorkId == "OL1W").Evidence.Penalties.ShouldBe(["volume"]);

        var asked = MatchRanker.Rank(QueryCleaner.Clean("ノルウェイの森 volume 2"), null, pool);
        asked.Ranked.Single(x => x.Work.WorkId == "OL1W").Evidence.Penalties.ShouldBeEmpty();
    }

    [Fact]
    public void An_edition_intent_top_is_compared_only_with_other_intent_hits() // edition intent
    {
        var pool = new[]
        {
            Work("OL1W", "The Hobbit", ["J.R.R. Tolkien"], 482, readingLog: 30_000),
            Work("OL2W", "The Hobbit: Graphic Novel", ["J.R.R. Tolkien", "Charles Dixon"], 3, contributors: ["Charles Dixon (Adapter)"]),
        };
        var r = MatchRanker.Rank(QueryCleaner.Clean("hobbit graphic novel"), Hyp("The Hobbit", "J.R.R. Tolkien"), pool);
        r.Ranked[0].Work.WorkId.ShouldBe("OL2W");
        r.Ranked[0].Evidence.IntentMatch.ShouldBeTrue();
        r.Status.ShouldBe(MatchStatus.Match);
    }

    [Fact]
    public void The_tiny_record_rule_does_not_hand_back_first_place_to_a_work_the_named_contributor_ruled_out() 
    {
        var pool = new[]
        {
            Work("OL27482W", "The Hobbit", ["J.R.R. Tolkien"], 482, readingLog: 30_000),
            Work("OL9W", "The Hobbit", ["J.R.R. Tolkien", "Charles Dixon"], 1, contributors: ["Charles Dixon (Adapter)"]),
        };
        var r = MatchRanker.Rank(QueryCleaner.Clean("the hobbit tolkien dixon"), null, pool);
        r.Ranked[0].Work.WorkId.ShouldBe("OL9W");
    }

    [Fact]
    public void A_blank_author_key_is_never_picked_for_the_top_works_call() 
    {
        var pool = new[]
        {
            Work("OL1W", "Wealth", ["John Smith"], 100, keys: [""]),
            Work("OL2W", "Pocahontas", ["John Smith"], 10, keys: ["OL1A"]),
            Work("OL3W", "Travels", ["John Smith"], 5, keys: ["OL1A"]),
        };
        var r = MatchRanker.Rank(QueryCleaner.Clean("smith"), null, pool);
        r.Ladder.AuthorOnly.ShouldBeTrue();
        r.Ladder.TopWorksAuthorKey.ShouldBe("OL1A");
    }

    [Fact]
    public void A_different_authors_title_does_not_cancel_a_same_author_mashup() // R3-0195
    {
        var pool = new[]
        {
            Work("OL1W", "Death on the Nile", ["Agatha Christie"], 188, keys: ["OL27695A"]),
            Work("OL2W", "Murder on the Orient Express", ["Agatha Christie"], 248, keys: ["OL27695A"]),
            Work("OL3W", "Death Nile Murder Orient Express", ["Someone Else"], 1),
        };
        var r = MatchRanker.Rank(QueryCleaner.Clean("agatha christie murder on nile express"), Hyp("Death on the Nile", "Agatha Christie"), pool);
        r.Ranked.Take(2).Select(w => w.Work.WorkId).ShouldBe(["OL1W", "OL2W"]);
        r.Status.ShouldBe(MatchStatus.Ambiguous);
        r.Reason.ShouldBe(StatusReason.UnsupportedGuess);
    }

    [Fact]
    public void A_junk_alternative_name_does_not_make_another_author_agree_with_the_interpreter() // R3-0366
    {
        var pool = new[] { Work("OL1W", "Emma", ["John Smith"], 3, alternativeNames: ["Jane Austen (adapted by John Smith)"]) };
        var r = MatchRanker.Rank(QueryCleaner.Clean("emma schoolgirl"), Hyp("Emma", "Jane Austen"), pool);
        r.Ranked[0].Tier.ShouldNotBe(MatchTier.A);
        r.Ranked[0].Evidence.ForeignAuthor.ShouldBeTrue();
    }

    [Fact]
    public void Huge_counts_do_not_overflow_the_score() 
    {
        var pool = new[] { Work("OL1W", "Bible", ["Anon Ymous"], 1_100_000_000, readingLog: int.MaxValue), Work("OL2W", "Bible", ["Other Person"], 1_100_000_000) };
        var r = MatchRanker.Rank(QueryCleaner.Clean("bible"), null, pool);
        r.Ranked.ShouldAllBe(w => double.IsFinite(w.Score));
    }

    [Theory]
    [InlineData("hobbit vol\u0662", "2")] // Arabic-Indic digit: used to crash before normalization
    [InlineData("hobbit #\u0662", "2")]
    [InlineData("hobbit vol 2", "2")]
    [InlineData("hobbit vol2", "2")]
    [InlineData("nausicaa volume one", "1")]
    [InlineData("hobbit #3", "3")]
    [InlineData("catch 22", null)]
    public void Typed_volume_numbers_are_ascii_only_and_never_throw(string query, string? volume)
    {
        var q = QueryCleaner.Clean(query);
        MatchRanker.TypedVolume(q).ShouldBe(volume);
        Should.NotThrow(() => MatchRanker.Rank(q, Hyp("The Hobbit, Volume 2", "J.R.R. Tolkien"), [Work("OL1W", "The Hobbit", ["J.R.R. Tolkien"], 5)]));
    }

    [Fact]
    public void The_tiny_record_rule_never_overrules_a_typed_author_with_another_authors_record() // DA-009
    {
        var pool = new[] { Work("OL1W", "Beacon", ["Alice Example"], 2), Work("OL2W", "Beacon", ["Bob Other"], 100) };
        MatchRanker.Rank(QueryCleaner.Clean("alice beacon"), null, pool).Ranked[0].Work.WorkId.ShouldBe("OL1W");
        // The pen-name case the rule exists for still applies: the big record carries the typed name as an alias.
        var galbraith = new[]
        {
            Work("OL3W", "The Cuckoo's Calling", ["Robert Galbraith"], 2),
            Work("OL4W", "The Cuckoo's Calling", ["J. K. Rowling"], 28, alternativeNames: ["Robert Galbraith", "Joanne Rowling"]),
        };
        MatchRanker.Rank(QueryCleaner.Clean("robert galbraith cuckoos calling"), null, galbraith).Ranked[0].Work.WorkId.ShouldBe("OL4W");
    }

    [Fact]
    public void A_title_match_that_needed_the_subtitle_does_not_fold_into_the_main_titles_work()
    {
        var garden = new[]
        {
            Work("OL1W", "Gardening", ["Ann Green"], 50) with { Subtitle = "Herbs" },
            Work("OL2W", "Gardening", ["Ann Green"], 5) with { Subtitle = "Trees" },
        };
        var r = MatchRanker.Rank(QueryCleaner.Clean("gardening trees"), null, garden);
        r.Ranked.Select(w => w.Work.WorkId).ShouldBe(["OL2W", "OL1W"]);
        r.Ranked[0].Evidence.ExactTitle.ShouldBeTrue();

        var rings = new[]
        {
            Work("OL27448W", "The Lord of the Rings", ["J.R.R. Tolkien"], 253),
            Work("OL9W", "The Lord of the Rings: The Two Towers", ["J.R.R. Tolkien"], 10),
        };
        var two = MatchRanker.Rank(QueryCleaner.Clean("lord of the rings two towers"), null, rings);
        two.Ranked.Count.ShouldBe(2); // was folded into The Lord of the Rings
        var towers = two.Ranked.Single(w => w.Work.WorkId == "OL9W");
        towers.Tier.ShouldBe(MatchTier.A);
        towers.Evidence.ExactTitle.ShouldBeTrue();

        // An "or" subtitle is the same work's alternative title: the plain record still folds into it (golden subtitle-02).
        var frankenstein = new[]
        {
            Work("OL450063W", "Frankenstein; or, The Modern Prometheus", ["Mary Shelley"], 2187),
            Work("OL36141872W", "Frankenstein", ["Mary Shelley"], 43),
        };
        MatchRanker.Rank(QueryCleaner.Clean("frankenstein the modern prometheus"), null, frankenstein).Ranked.Single().FoldedWorkIds
            .ShouldBe(["OL36141872W"]);
    }

    [Fact]
    public void A_representative_that_takes_the_groups_tier_shows_the_evidence_that_earned_it()
    {
        var pool = new[]
        {
            Work("OL1W", "Преступление и наказание", ["Fyodor Dostoevsky"], 30, editionTitles: ["Crime and punishment"]),
            Work("OL2W", "Преступление и наказание", ["Fyodor Dostoevsky"], 1178),
        };
        var top = MatchRanker.Rank(QueryCleaner.Clean("crime and punishment dostoevsky"), null, pool).Ranked.Single();
        top.Work.WorkId.ShouldBe("OL2W");
        top.FoldedWorkIds.ShouldBe(["OL1W"]);
        top.Tier.ShouldBe(MatchTier.A);
        top.Evidence.ExactTitle.ShouldBeTrue();
        top.Evidence.TitleSource.ShouldBe(TitleSource.EditionTitle);
        top.Evidence.MatchedEditionTitle.ShouldBe("Crime and punishment");
        top.Evidence.MatchedTitleTerms.ShouldBe(["crime", "punishment"]);
    }

    [Fact]
    public void The_baseline_switches_reproduce_the_reference_and_the_defaults_add_the_fixes()
    {
        // R3-0025: on the same pool the baseline matches the 1-edition "Windy" by R. S. Rose; the defaults do not.
        var c = PoolFixtures.Case("R3-0025:off");
        MatchRanker.Rank(c.ReferenceCleanedQuery(), null, c.Pool(), RankerOptions.Baseline).Status.ShouldBe(MatchStatus.Match);
        MatchRanker.Rank(c.ReferenceCleanedQuery(), null, c.Pool()).Status.ShouldBe(MatchStatus.Ambiguous);
    }

    private static string Signature(RankingResult r)
        => $"{r.Status}/{r.Reason}/{r.Facts}/{r.Ladder.Strong}/{r.Ladder.AuthorOnly}/{r.Ladder.TopWorksAuthorKey}/"
           + $"{string.Join(" ", r.Ladder.CharacterFallbackTokens)}/{string.Join(" ", r.Ladder.MashupProbeTokens)}|"
           + string.Join("|", r.Ranked.Select(w => $"{w.Work.WorkId}:{w.Tier}:{w.Score:R}:{string.Join(",", w.FoldedWorkIds)}:"
                                                   + $"{string.Join(",", w.PrimaryAuthors.Select(a => a.Name))}:{w.Evidence.TitleSource}"));

    internal static CandidateWork Work(string id, string title, string[] authors, int editions, string[]? keys = null,
        string[]? contributors = null, string[]? persons = null, int readingLog = 0, string[]? editionTitles = null,
        string[]? alternativeNames = null, int? year = null)
        => new()
        {
            WorkId = id,
            Title = title,
            AuthorNames = authors,
            AuthorKeys = keys ?? [.. authors.Select((_, i) => $"OL{900 + i}{id.Length}A")],
            EditionCount = editions,
            Contributors = contributors ?? [],
            Persons = persons ?? [],
            ReadingLogCount = readingLog,
            EditionTitles = editionTitles ?? [],
            AuthorAlternativeNames = alternativeNames ?? [],
            FirstPublishYear = year,
        };

    internal static QueryHypothesis Hyp(string? title, string? author, string[]? characters = null, string? original = null, string? english = null)
        => new(title, author, characters ?? [], [], english, original);
}
