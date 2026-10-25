using FindThatBook.Core.Matching;
using FindThatBook.Core.Model;
using FindThatBook.Core.Normalization;
using FindThatBook.Core.Retrieval;

namespace FindThatBook.Core.Tests.Matching;

/// <summary>
/// Matching-spec rows 29–68 and the added rules (R3-…, IN-…, DA-…, DE-…), each on a real
/// recorded Open Library pool, replayed through the production ladder + ranker with the C# cleaner.
/// </summary>
public sealed class RankingCaseTests
{
    /// <summary>(fixture id, expected #1, expected status).</summary>
    public static TheoryData<string, string, MatchStatus> TopAndStatus => new()
    {
        // matching spec
        { "golden:contrib-01:off", "OL219602W", MatchStatus.Match },          // row 29/64 hobbit dixon: tier B
        { "golden:title-01:off", "OL27482W", MatchStatus.Match },             // row 30 the hobbit
        { "golden:brief-03:off", "OL27482W", MatchStatus.Match },             // row 31 tolkien hobbit illustrated deluxe 1937 (R4)
        { "golden:brief-01:on", "OL53908W", MatchStatus.Match },              // row 32 mark huckleberry (LLM)
        { "golden:brief-01:off", "OL53908W", MatchStatus.Ambiguous },         // row 32 LLM off: tier C
        { "golden:brief-02:off", "OL5720023W", MatchStatus.Match },           // row 33 twilight meyer
        { "hard:twilight:off", "OL5720023W", MatchStatus.Match },             // row 34 twilight: gap ≥ 0.5 over Koontz/Hunter
        { "golden:ta-06:off", "OL8193465W", MatchStatus.Match },              // row 36 dickens tale two cities
        { "golden:diacritics-02:off", "OL66554W", MatchStatus.Match },        // row 37 pride & prejudice
        { "golden:diacritics-01:off", "OL1095427W", MatchStatus.Match },      // row 38 bronte jane eyre
        { "golden:typo-01:off", "OL27482W", MatchStatus.Match },              // row 39 tolkein hobbit (OR rescue)
        { "golden:subtitle-01:off", "OL27482W", MatchStatus.Match },          // row 40 the hobbit or there and back again
        { "hard:sparknotes catcher in the rye:off", "OL14942753W", MatchStatus.Ambiguous }, // row 44: no study penalty
        { "golden:character-01:on", "OL3140822W", MatchStatus.Match },        // row 45 scout atticus + LLM
        { "golden:character-01:off", "OL3140822W", MatchStatus.Ambiguous },   // row 45/62 without the LLM: E
        { "golden:ta-02:off", "OL1168083W", MatchStatus.Match },              // row 46/53 1984 orwell, LLM off (DE-04)
        { "golden:ta-02:on", "OL1168083W", MatchStatus.Match },
        { "golden:typo-02:off", "OL8721462W", MatchStatus.Match },            // row 50 great expecttions dikens
        // accuracy fixes
        { "04-54:the-39-steps-buchan:off", "OL76524W", MatchStatus.Match },   // row 54: 881-ed canonical over the play
        { "04-57:hobbit-comic:off", "OL219602W", MatchStatus.Ambiguous },     // row 57: adapter-role intent hit first
        { "golden:contrib-02:on", "OL44552594W", MatchStatus.Match },         // row 56 / DE-03 hobbit graphic novel
        { "hard:hobbit graphic novel:off", "OL44552594W", MatchStatus.Match },
        { "hard:elizabeth darcy:off", "OL66554W", MatchStatus.Ambiguous },    // row 60 / DE-02
        { "hard:ahab whale:off", "OL102749W", MatchStatus.Ambiguous },        // row 61
        { "golden:contrib-03:on", "OL219602W", MatchStatus.Match },           // row 63 the hobbit tolkien dixon
        { "golden:contrib-03:off", "OL219602W", MatchStatus.Match },
        { "hard:tom sawyer:off", "OL53919W", MatchStatus.Ambiguous },         // row 65
        // round 3 and QA fixes
        { "R3-0366:v3", "OL7967475W", MatchStatus.Match },                    // Gorky's Childhood, not Tolstoy's Детство
        { "R3-0390:v1", "OL29196692W", MatchStatus.Match },                   // "orange: To You, Dear One": subtitle required
        { "R3-0390:v3", "OL29196692W", MatchStatus.Ambiguous },               // authored exact title over OR-call noise
        { "R3-0283:v3", "OL3963416W", MatchStatus.Ambiguous },                // volume 1 required; no match on the bare record
        { "R3-0195:v3", "OL471724W", MatchStatus.Ambiguous },                 // mashed title: capped
        { "R3-0025:off", "OL37887430W", MatchStatus.Ambiguous },              // rose windy, LLM off
        { "R3-0025:v3", "OL17094879W", MatchStatus.Match },                   // with the LLM: This One Summer
        { "IN-058:harry-potter:off", "OL82563W", MatchStatus.Ambiguous },     // series name, HP1 first
        { "IN-058:harry-potter:hand", "OL82563W", MatchStatus.Ambiguous },
        { "IN-037:the-little-prince:hand", "OL10263W", MatchStatus.Match },   // DE-07/IN-037 via the original title
        { "DA-052:women-in-love:off", "OL81294W", MatchStatus.Match },        // exact beats fuzzy (Little Women)
        { "IN-044:the-hobbit-1937:off", "OL27482W", MatchStatus.Match },      // soft year
        { "DA-036:crime-punishment-dostoevsky:off", "OL166894W", MatchStatus.Match }, // Cyrillic canonical via editions.title
        { "DA-037:homer-odyssey:off", "OL61982W", MatchStatus.Match },        // Greek canonical via editions.title
        { "DA-016:nancy-drew-keene:off", "OL15046261W", MatchStatus.Ambiguous }, // 1-ed record never matches over a 41-ed one
        { "DA-006:freakonomics:off", "OL278022W", MatchStatus.Match },
        { "DE-06:three-body:off", "OL17267881W", MatchStatus.Match },         // 三体 via its English edition title
        { "DE-07:le-petit-prince:off", "OL10263W", MatchStatus.Match },
        { "DE-07:anna-karenina-cyrillic:off", "OL267096W", MatchStatus.Match }, // canonical over the 1-ed duplicate
        { "DE-07:norwegian-wood-jp:off", "OL2625457W", MatchStatus.Match },
        { "IN-050:it:off", "OL81613W", MatchStatus.Match },
        { "DA-003:the-talisman:off", "OL81599W", MatchStatus.Ambiguous },     // two real books titled The Talisman
        { "DA-003:the-talisman:hand", "OL81599W", MatchStatus.Ambiguous },    // an interpreted author alone doesn't pick one                   // King's It over Five Children and It
        { "golden:title-02:off", "OL450063W", MatchStatus.Match },            // frankenstein, unchanged by ExactOverPartial
        { "golden:ambig-04:off", "OL66513W", MatchStatus.Match },             // emma, unchanged
    };

    [Theory]
    [MemberData(nameof(TopAndStatus))]
    public void Top_work_and_status(string id, string top, MatchStatus status)
    {
        var r = Run(id);
        r.Top.Work.WorkId.ShouldBe(top, $"top 3: {string.Join(",", r.TopIds(3))}");
        r.Ranking.Status.ShouldBe(status);
    }

    [Fact]
    public void Hobbit_dixon_is_tier_B_with_tolkien_primary_and_the_adapters_as_contributors() // 01 row 29
    {
        var r = Run("golden:contrib-01:off");
        r.Top.Tier.ShouldBe(MatchTier.B);
        r.Top.PrimaryAuthors.Select(a => a.Name).ShouldBe(["J.R.R. Tolkien"]);
        r.Top.Contributors.Where(c => c.Key is not null).Select(c => (c.Name, c.Role)).ShouldBe([("Charles Dixon", "adapter"), ("Sean Deming", "adapter")]);
        r.Top.Evidence.Role.ShouldBe(AuthorRole.Contributor);
        r.Top.Evidence.MatchedContributorRole.ShouldBe("adapter");
        var tolkien = r.Ranking.Ranked.Single(w => w.Work.WorkId == "OL27482W");
        tolkien.Tier.ShouldBe(MatchTier.E);
        tolkien.Evidence.ForeignAuthor.ShouldBeTrue(); // "dixon" names another author
    }

    [Fact]
    public void The_hobbit_folds_the_graphic_novel_record_into_the_canonical_work() // 01 row 30
    {
        var r = Run("golden:title-01:off");
        r.Top.Tier.ShouldBe(MatchTier.A);
        r.Top.FoldedWorkIds.ShouldContain("OL219602W");
        r.Top.Evidence.TitleSource.ShouldBe(TitleSource.Typed);
        r.Top.Evidence.MatchedTitleTerms.ShouldBe(["hobbit"]);
    }

    [Fact]
    public void An_author_only_query_shows_the_authors_most_published_works() // 01 row 35, hint
    {
        var r = Run("golden:brief-04:off");
        r.Ranking.Status.ShouldBe(MatchStatus.Ambiguous);
        r.Ranking.Reason.ShouldBe(StatusReason.AuthorOnly);
        r.Steps.ShouldBe([CatalogSteps.Broad, CatalogSteps.AuthorWorks]);
        r.Issued[1].Query.AuthorKey.ShouldBe("OL24638A");
        r.Ranking.Ranked.Take(5).ShouldAllBe(w => w.Tier == MatchTier.D);
        StatusPolicy.Decide(r.Ranking, Ports.JudgeVerdict.Off, 0.9).Hint
            .ShouldBe("These are Charles Dickens's most-published works. Add a title to narrow it.");
    }

    [Fact]
    public void Meyer_picks_stephenie_meyer_by_pool_editions() // 01 row 41
    {
        var r = Run("hard:meyer:off");
        r.Issued.Last().Query.AuthorKey.ShouldBe("OL1391085A");
        r.Top.Work.WorkId.ShouldBe("OL5720023W");
    }

    [Fact]
    public void Works_by_other_authors_in_an_author_only_pool_are_tier_E() // 01 row 43: dominance
    {
        // The dickens pool holds works by other authors titled or about "Dickens" (biographies); none may be tier A–D.
        var r = Run("golden:brief-04:off");
        r.Ranking.Ranked.Where(w => !w.Work.AuthorNames.Any(n => n.Contains("Dickens", StringComparison.Ordinal)))
            .ShouldAllBe(w => w.Tier == MatchTier.E);
    }

    [Fact]
    public void Omnibus_and_adaptation_records_are_penalised_below_the_canonical_work() // 01 rows 36, 38, 50
    {
        Run("golden:diacritics-01:off").Ranking.Ranked.Single(w => w.Work.WorkId == "OL1095380W").Evidence.Penalties.ShouldBe(["omnibus"]);
        var typo = Run("golden:typo-02:off").Ranking.Ranked;
        typo[0].Work.WorkId.ShouldBe("OL8721462W");
        typo[0].Evidence.Penalties.ShouldBeEmpty();
    }

    [Fact]
    public void Hermione_is_never_a_match_and_shows_potter_books() // DE-01, rows 47/58/59, AI-43
    {
        foreach (var id in new[] { "hard:hermione:off", "hard:hermione:on", "04-58:hermione:v2" })
        {
            var r = Run(id);
            r.Ranking.Status.ShouldNotBe(MatchStatus.Match, id);
            r.Top.Work.WorkId.ShouldBe("OL82563W", id); // Philosopher's Stone first (person: data or the LLM title)
            r.Steps.ShouldNotContain(CatalogSteps.AuthorWorks, id); // "hermione" is a given name, not an author query
            r.Ranking.Facts.SingleTokenNoAuthor.ShouldBeTrue(id);
        }
        Run("hard:hermione:off").Steps.ShouldContain(CatalogSteps.PersonFallback);
        Run("hard:hermione:off").TopIds(5).Count(w => w.StartsWith("OL825", StringComparison.Ordinal)).ShouldBeGreaterThanOrEqualTo(3);
        Run("04-58:hermione:v2").TopIds(5).ShouldContain("OL52818W"); // HERmione stays visible
    }

    [Fact]
    public void Katniss_is_capped_by_the_one_word_character_rule() // DE-01 takes precedence over this measured cost
    {
        var r = Run("hard:katniss:on");
        r.Top.Work.WorkId.ShouldBe("OL5735363W");
        r.Ranking.Status.ShouldBe(MatchStatus.Ambiguous);
        r.Ranking.Reason.ShouldBe(StatusReason.WeakEvidence);
    }

    [Fact]
    public void A_typed_title_that_is_the_interpreters_book_is_not_capped_by_the_character_rule() // DE-01 boundary
    {
        Run("golden:title-02:on").Ranking.Status.ShouldBe(MatchStatus.Match); // frankenstein
    }

    [Fact]
    public void Character_fallback_tokens_never_include_initials() // row 67: no person:(j AND k)
    {
        var r = Run("golden:author-04:off");
        r.Issued.ShouldNotContain(i => i.Query.Q != null && i.Query.Q.StartsWith("person:", StringComparison.Ordinal));
    }

    [Fact]
    public void Ahab_whale_retries_person_with_or_after_zero_hits() // row 61
    {
        var r = Run("hard:ahab whale:off");
        r.Steps.ShouldBe([CatalogSteps.Broad, CatalogSteps.AnyOf, CatalogSteps.PersonFallback, CatalogSteps.PersonFallbackAny]);
        r.Issued[3].Query.Q.ShouldBe("person:(ahab OR whale)");
    }

    [Fact]
    public void The_hobbit_tolkien_dixon_ranks_the_named_contributors_edition_first() // row 63, golden contrib-03
    {
        var on = Run("golden:contrib-03:on");
        on.Top.Tier.ShouldBe(MatchTier.A);
        on.Ranking.Ranked.Single(w => w.Work.WorkId == "OL27482W").Tier.ShouldBe(MatchTier.C); // a named contributor the work leaves unexplained
        var off = Run("golden:contrib-03:off");
        off.Steps.ShouldBe([CatalogSteps.Broad]); // the ranking is strong now, so the OR search is not needed
    }

    [Fact]
    public void R3_0366_original_title_by_another_author_is_not_tier_A()
    {
        var r = Run("R3-0366:v3");
        var tolstoy = r.Ranking.Ranked.Single(w => w.Work.WorkId == "OL15698346W");
        tolstoy.Tier.ShouldBe(MatchTier.E);
        tolstoy.Evidence.TitleSource.ShouldBe(TitleSource.OriginalTitle);
        tolstoy.Evidence.ForeignAuthor.ShouldBeTrue();
        r.Top.Evidence.AuthorFromHypothesis.ShouldBeTrue(); // Gorky via the record's alternative names
    }

    [Fact]
    public void R3_0390_a_typed_hypothesis_subtitle_is_required()
    {
        var r = Run("R3-0390:v1");
        r.Ranking.Ranked.Single(w => w.Work.WorkId == "OL20028572W").Tier.ShouldNotBe(MatchTier.A); // bare "Orange"
        r.Ranking.Ranked.Where(w => w.Work.Title.Contains("Vol", StringComparison.OrdinalIgnoreCase))
            .ShouldAllBe(w => w.Evidence.Penalties.Contains("volume")); // IN-035
    }

    [Fact]
    public void R3_0283_the_original_title_without_the_typed_volume_is_not_a_match()
    {
        var r = Run("R3-0283:v3");
        r.Ranking.Ranked.Single(w => w.Work.WorkId == "OL28758703W").Tier.ShouldNotBe(MatchTier.A);
        r.Ranking.Ranked.ShouldNotContain(w => w.Tier == MatchTier.A);
    }

    [Fact]
    public void R3_0195_a_title_mashed_from_two_christie_books_shows_both_and_is_ambiguous()
    {
        var r = Run("R3-0195:v3");
        r.TopIds(2).ShouldBe(["OL471724W", "OL471576W"]); // Death on the Nile, Murder on the Orient Express
        r.Ranking.Reason.ShouldBe(StatusReason.UnsupportedGuess);
        r.Steps.ShouldBe([CatalogSteps.Broad, CatalogSteps.Hypothesis, CatalogSteps.AnyOf]);
        r.Issued[2].Query.Q.ShouldBe("christie OR murder OR train");
        r.Unrecorded.ShouldBeEmpty();
    }

    [Fact]
    public void IN_037_a_fuzzy_prince_princess_pair_is_never_an_exact_title()
    {
        var r = Run("IN-037:the-little-prince:off");
        r.Ranking.Status.ShouldNotBe(MatchStatus.Match);
        r.Ranking.Ranked.Where(w => w.Work.Title.Contains("Princess", StringComparison.Ordinal)).ShouldAllBe(w => !w.Evidence.ExactTitle);
    }

    [Fact]
    public void IN_044_a_soft_year_is_optional_for_exactness_and_earns_the_year_bonus()
    {
        var r = Run("IN-044:the-hobbit-1937:off");
        r.Top.Evidence.ExactTitle.ShouldBeTrue();
        r.Top.Evidence.YearMatched.ShouldBeTrue();
        r.Steps.ShouldBe([CatalogSteps.Broad]);
    }

    [Fact]
    public void DE_06_and_DA_036_edition_titles_are_title_evidence()
    {
        var three = Run("DE-06:three-body:off").Top.Evidence;
        three.TitleSource.ShouldBe(TitleSource.EditionTitle);
        three.MatchedEditionTitle.ShouldBe("The three-body problem");
        Run("DA-036:crime-punishment-dostoevsky:off").Top.Evidence.MatchedEditionTitle.ShouldBe("Crime and punishment");
    }

    [Fact]
    public void DA_026_narrators_and_illustrators_listed_as_authors_are_shown_as_also_credited()
    {
        var r = Run("DA-026:george-eliot:off");
        foreach (var w in r.Ranking.Ranked.Where(w => w.Work.AuthorNames.Contains("George Eliot")).Take(5))
        {
            w.PrimaryAuthors.Select(a => a.Name).ShouldBe(["George Eliot"], w.Work.Title);
            if (w.Work.AuthorNames.Count > 1) w.Contributors.ShouldContain(c => c.Role == "also credited", w.Work.Title);
        }
    }

    [Fact]
    public void DA_006_a_co_author_who_narrated_the_audiobook_stays_an_author()
    {
        var top = Run("DA-006:freakonomics:off").Top;
        top.PrimaryAuthors.Select(a => a.Name).ShouldBe(["Steven D. Levitt", "Stephen J. Dubner"]);
        top.Contributors.ShouldBeEmpty();
    }

    [Fact]
    public void DE_07_a_one_edition_duplicate_never_outranks_the_canonical_work()
    {
        var r = Run("DE-07:anna-karenina-cyrillic:off");
        r.TopIds(2).ShouldBe(["OL267096W", "OL37418660W"]);
    }

    [Fact]
    public void An_exact_title_outranks_a_much_more_popular_partial_title_by_another_author() // IN-050 ("it")
    {
        var r = Run("IN-050:it:off");
        r.Steps.ShouldBe([CatalogSteps.ShortTitle]); // DE-05: title=it, never q=it
        r.TopIds(2).ShouldBe(["OL81613W", "OL99499W"]);
        // The baseline order (score only) put Five Children and It first.
        var c = PoolFixtures.Case("IN-050:it:off");
        MatchRanker.Rank(c.ReferenceCleanedQuery(), null, c.Pool(), RankerOptions.Baseline).Ranked[0].Work.WorkId.ShouldBe("OL99499W");
    }

    [Fact]
    public void DA_003_two_books_sharing_a_title_stay_ambiguous_until_an_author_is_typed()
    {
        var c = PoolFixtures.Case("DA-003:the-talisman:hand");
        MatchRanker.Rank(c.ReferenceCleanedQuery(), c.Hypothesis, c.Pool(), RankerOptions.Baseline).Status.ShouldBe(MatchStatus.Match);
        var guessed = Run("DA-003:the-talisman:hand").Ranking;
        guessed.Status.ShouldBe(MatchStatus.Ambiguous);
        guessed.Reason.ShouldBe(StatusReason.CloseScores);
        // A typed author settles it.
        var typed = MatchRanker.Rank(QueryCleaner.Clean("the talisman scott"), null, c.Pool());
        typed.Ranked[0].Work.WorkId.ShouldBe("OL863795W");
        typed.Status.ShouldBe(MatchStatus.Match);
    }

    [Fact]
    public void Empty_queries_make_no_catalog_call() // row 68
    {
        var r = Run("golden:none-04:off");
        r.Issued.ShouldBeEmpty();
        r.Ranking.Status.ShouldBe(MatchStatus.None);
    }

    private static ReplayResult Run(string id)
    {
        var c = PoolFixtures.Case(id);
        return Replay.Run(c, QueryCleaner.Clean(c.Query), RankerOptions.Default);
    }
}
