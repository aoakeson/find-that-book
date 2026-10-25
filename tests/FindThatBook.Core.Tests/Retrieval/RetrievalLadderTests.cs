using FindThatBook.Core.Matching;
using FindThatBook.Core.Model;
using FindThatBook.Core.Normalization;
using FindThatBook.Core.Ports;
using FindThatBook.Core.Retrieval;

namespace FindThatBook.Core.Tests.Retrieval;

/// <summary>
/// The ladder's decisions: which call comes next, from the ranking's signals and the steps
/// already run. End-to-end call sequences on recorded pools are checked in Matching/ReferenceParityTests.
/// </summary>
public sealed class RetrievalLadderTests
{
    private static readonly IReadOnlyDictionary<string, int?> BroadOnly = new Dictionary<string, int?> { [CatalogSteps.Broad] = 20 };

    // ---------------- AfterInterpretation / AfterBroad ----------------

    [Fact]
    public void No_hypothesis_no_early_calls()
        => RetrievalLadder.AfterInterpretation(Q("hobbit dixon"), null).ShouldBeEmpty();

    [Fact]
    public void A_hypothesis_title_with_new_words_gets_a_fielded_call_in_the_llms_own_words() // row 52: the interpreter's own words, never "tale 2 cities"
    {
        var calls = RetrievalLadder.AfterInterpretation(Q("dickens two cities"), H("A Tale of Two Cities", "Charles Dickens"));
        calls.Count.ShouldBe(1);
        calls[0].Step.ShouldBe(CatalogSteps.Hypothesis);
        calls[0].Query.Title.ShouldBe("tale two cities");
        calls[0].Query.Author.ShouldBe("dickens");
    }

    [Fact]
    public void A_hypothesis_that_only_repeats_typed_words_waits_for_the_pool() // IN-056: optional words count as typed
    {
        RetrievalLadder.AfterInterpretation(Q("the hobbit"), H("The Hobbit", "J.R.R. Tolkien")).ShouldBeEmpty();
        RetrievalLadder.AfterInterpretation(Q("the collector fowles"), H("The Collector", "John Fowles")).ShouldBeEmpty();
    }

    [Fact]
    public void An_author_only_hypothesis_searches_the_author_when_it_was_not_typed()
    {
        var calls = RetrievalLadder.AfterInterpretation(Q("the scottish play"), H(null, "William Shakespeare"));
        calls.Single().Query.Author.ShouldBe("shakespeare");
        RetrievalLadder.AfterInterpretation(Q("shakespeare"), H(null, "William Shakespeare")).ShouldBeEmpty();
    }

    [Fact]
    public void An_original_language_title_gets_its_own_title_call()
    {
        var calls = RetrievalLadder.AfterInterpretation(Q("the three body problem"),
            H("The Three-Body Problem", "Liu Cixin", original: "三体"));
        calls.Select(c => c.Step).ShouldBe([CatalogSteps.OriginalTitle]); // the English title adds no words
        calls[0].Query.Title.ShouldBe("三体");
        calls[0].Query.Author.ShouldBeNull(); // a non-Latin title is specific enough (measured rule)
    }

    [Fact]
    public void An_original_title_equal_to_the_title_is_not_searched_twice()
        => RetrievalLadder.AfterInterpretation(Q("dune"), H("Dune", "Frank Herbert", original: "Dune")).ShouldBeEmpty();

    [Fact]
    public void Fix_7b_retries_the_hypothesis_title_when_call_1_did_not_return_it()
    {
        var q = Q("it said the wednesday wars and im already late");
        var h = H("The Wednesday Wars", "Gary D. Schmidt");
        RetrievalLadder.AfterInterpretation(q, h).ShouldBeEmpty();
        var noise = new[] { Work("OL1W", "Late Wars", "Someone Else") };
        RetrievalLadder.AfterBroad(q, h, noise, [CatalogSteps.Broad]).Single().Step.ShouldBe(CatalogSteps.Hypothesis);
        var found = new[] { Work("OL2W", "The Wednesday Wars", "Gary D. Schmidt") };
        RetrievalLadder.AfterBroad(q, h, found, [CatalogSteps.Broad]).ShouldBeEmpty();
        RetrievalLadder.AfterBroad(q, h, noise, [CatalogSteps.Broad, CatalogSteps.Hypothesis]).ShouldBeEmpty();
    }

    // ---------------- Next: weak pool ----------------

    [Fact]
    public void A_weak_pool_with_llm_characters_tries_person_with_the_typed_names()
    {
        var next = Next(Q("scout atticus"), H("To Kill a Mockingbird", "Harper Lee", ["Scout Finch", "Atticus Finch"]), Weak);
        next!.Step.ShouldBe(CatalogSteps.Person);
        next.Query.Q.ShouldBe("person:(scout AND atticus)");
        next.Query.IncludePersons.ShouldBeTrue();
    }

    [Fact]
    public void Characters_the_user_did_not_type_fall_back_to_the_first_two_tokens()
        => Next(Q("the boy who lived"), H(null, null, ["Harry Potter", "Ron Weasley"]), Weak)!.Query.Q.ShouldBe("person:(harry AND potter)");

    [Fact]
    public void Person_and_with_no_hits_retries_with_or_then_the_or_join()
    {
        var q = Q("scout atticus");
        var h = H("To Kill a Mockingbird", "Harper Lee", ["Scout Finch", "Atticus Finch"]);
        var steps = Steps((CatalogSteps.Broad, 3), (CatalogSteps.Person, 0));
        Next(q, h, Weak, steps)!.Query.Q.ShouldBe("person:(scout OR atticus)");
        Next(q, h, Weak, Steps((CatalogSteps.Broad, 3), (CatalogSteps.Person, 0), (CatalogSteps.PersonAny, 4)))!.Step.ShouldBe(CatalogSteps.AnyOf);
        Next(q, h, Weak, Steps((CatalogSteps.Broad, 3), (CatalogSteps.Person, 5)))!.Query.Q.ShouldBe("scout OR atticus");
    }

    [Fact]
    public void A_weak_pool_without_characters_goes_to_the_or_join_only_with_two_words()
    {
        Next(Q("tolkein hobbit"), null, Weak)!.Query.Q.ShouldBe("tolkein OR hobbit");
        Next(Q("hobbitt"), null, Weak).ShouldBeNull();
    }

    [Fact]
    public void A_strong_ranking_skips_the_weak_pool_calls()
        => Next(Q("tolkein hobbit"), H("The Hobbit", "J.R.R. Tolkien", ["Bilbo Baggins"]), new LadderSignals { Strong = true, HasExactTier = true }).ShouldBeNull();

    // ---------------- Next: character fallback (DE-02) ----------------

    [Fact]
    public void Leftover_words_are_retried_as_character_names_and_with_or_after_zero_hits()
    {
        var signals = new LadderSignals { CharacterFallbackTokens = ["elizabeth", "darcy"] };
        var q = Q("elizabeth darcy");
        Next(q, null, signals, Steps((CatalogSteps.Broad, 20), (CatalogSteps.AnyOf, 20)))!.Query.Q.ShouldBe("person:(elizabeth AND darcy)");
        Next(q, null, signals, Steps((CatalogSteps.Broad, 20), (CatalogSteps.AnyOf, 20), (CatalogSteps.PersonFallback, 0)))!
            .Query.Q.ShouldBe("person:(elizabeth OR darcy)");
        Next(q, null, signals, Steps((CatalogSteps.Broad, 20), (CatalogSteps.AnyOf, 20), (CatalogSteps.PersonFallback, 6))).ShouldBeNull();
    }

    [Fact]
    public void One_leftover_name_is_never_retried_with_or()
    {
        var signals = new LadderSignals { CharacterFallbackTokens = ["hermione"], Strong = true };
        Next(Q("hermione"), null, signals, Steps((CatalogSteps.Broad, 20), (CatalogSteps.PersonFallback, 0))).ShouldBeNull();
    }

    [Fact]
    public void The_character_fallback_is_skipped_after_a_person_call()
    {
        var signals = new LadderSignals { CharacterFallbackTokens = ["scout", "atticus"], Strong = true };
        Next(Q("scout atticus"), H(null, null, ["Scout Finch"]), signals, Steps((CatalogSteps.Broad, 3), (CatalogSteps.Person, 5))).ShouldBeNull();
    }

    [Fact]
    public void A_later_stage_closes_the_weak_pool_stage()
    {
        // The fallback ran; even if the ranking is weak again, the OR-join (an earlier stage) is not issued afterwards.
        var signals = new LadderSignals { CharacterFallbackTokens = ["ahab", "whale"] };
        Next(Q("ahab whale"), null, signals, Steps((CatalogSteps.Broad, 3), (CatalogSteps.PersonFallback, 4))).ShouldBeNull();
    }

    // ---------------- Next: author works ----------------

    [Fact]
    public void An_author_only_query_fetches_the_picked_authors_top_works()
    {
        var next = Next(Q("dickens"), null, new LadderSignals { AuthorOnly = true, Strong = true, TopWorksAuthorKey = "OL24638A" });
        next!.Step.ShouldBe(CatalogSteps.AuthorWorks);
        next.Query.AuthorKey.ShouldBe("OL24638A");
        next.Query.Sort.ShouldBe(CatalogSort.Editions);
        next.Query.Limit.ShouldBe(10);
    }

    [Fact]
    public void An_llm_author_we_did_not_retrieve_is_searched_by_name()
    {
        var next = Next(Q("pratchett"), H(null, "Terry Pratchett"), new LadderSignals { AuthorOnly = true, Strong = true });
        next!.Query.Author.ShouldBe("terry pratchett");
        next.Query.Sort.ShouldBe(CatalogSort.Editions);
        Next(Q("pratchett"), null, new LadderSignals { AuthorOnly = true, Strong = true }).ShouldBeNull();
        Next(Q("pratchett"), H(null, "Terry Pratchett"), new LadderSignals { AuthorOnly = true, Strong = true },
            Steps((CatalogSteps.Broad, 20), (CatalogSteps.AuthorWorks, 10))).ShouldBeNull();
    }

    // ---------------- Next: R3-0195 mashup probe, budget, unusable input ----------------

    [Fact]
    public void A_mashed_title_probe_runs_once()
    {
        var signals = new LadderSignals { Strong = true, HasExactTier = true, MashupProbeTokens = ["christie", "murder", "express"] };
        var q = Q("agatha christie murder on nile express");
        Next(q, null, signals)!.Query.Q.ShouldBe("christie OR murder OR express");
        Next(q, null, signals, Steps((CatalogSteps.Broad, 0), (CatalogSteps.AnyOf, 20))).ShouldBeNull();
    }

    [Fact]
    public void No_call_is_planned_without_budget()
        => Next(Q("tolkein hobbit"), null, Weak, BroadOnly, callsRemaining: 0).ShouldBeNull();

    [Fact]
    public void Characters_without_a_usable_word_send_nothing_and_fall_through()
        => Next(Q("tolkein hobbit"), H(null, null, ["?!"]), Weak)!.Step.ShouldBe(CatalogSteps.AnyOf);

    [Fact]
    public void A_topic_request_whose_text_is_a_title_keeps_the_book_search_fallbacks() // LLM off; the cleaner reads a topic frame
    {
        var q = Q("Novel on Yellow Paper");
        var steps = Steps((CatalogSteps.Broad, 20), (CatalogSteps.Topic, 8));
        CatalogRequest? NextFor(params CandidateWork[] pool)
            => RetrievalLadder.Next(new LadderState(q, null, pool, MatchRanker.Rank(q, null, pool) with { Ladder = Weak }, steps, 3));
        NextFor(Work("OL1W", "Novel on Yellow Paper", "Stevie Smith"))!.Step.ShouldBe(CatalogSteps.AnyOf); // a book search
        NextFor(Work("OL2W", "Paper Making", "Dard Hunter") with { Subjects = ["Yellow paper"] }).ShouldBeNull(); // a topic search
    }

    [Fact]
    public void The_ladder_never_reissues_a_step_on_a_recorded_replay()
    {
        // Replay asserts it (Matching/Replay.cs); run it over every recorded pool with the production options.
        foreach (var id in PoolFixtures().Keys)
        {
            var c = Tests.Matching.PoolFixtures.Case(id);
            var r = Tests.Matching.Replay.Run(c, QueryCleaner.Clean(c.Query), RankerOptions.Default);
            r.Steps.Distinct().Count().ShouldBe(r.Steps.Count, id);
            r.Steps.Count.ShouldBeLessThanOrEqualTo(5, id);
        }
    }

    private static IReadOnlyDictionary<string, Tests.Matching.PoolCase> PoolFixtures()
        => Tests.Matching.PoolFixtures.Load(Tests.Matching.PoolFixtures.GoldenHard)
            .Concat(Tests.Matching.PoolFixtures.Load(Tests.Matching.PoolFixtures.Targeted)).ToDictionary();

    private static readonly LadderSignals Weak = new() { Strong = false };

    private static CatalogRequest? Next(CleanedQuery q, QueryHypothesis? h, LadderSignals signals,
        IReadOnlyDictionary<string, int?>? steps = null, int callsRemaining = 3)
        => RetrievalLadder.Next(new LadderState(q, h, [], new RankingResult([], MatchStatus.Ambiguous, StatusReason.WeakEvidence) { Ladder = signals },
            steps ?? BroadOnly, callsRemaining));

    private static IReadOnlyDictionary<string, int?> Steps(params (string Step, int? N)[] steps) => steps.ToDictionary(s => s.Step, s => s.N);

    private static CleanedQuery Q(string text) => QueryCleaner.Clean(text);

    private static QueryHypothesis H(string? title, string? author, string[]? characters = null, string? original = null)
        => new(title, author, characters ?? [], [], null, original);

    private static CandidateWork Work(string id, string title, string author)
        => new() { WorkId = id, Title = title, AuthorNames = [author], EditionCount = 5 };
}
