using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using FindThatBook.Api.Contracts;
using FindThatBook.Api.Tests.Support;
using FindThatBook.Core.Search;

namespace FindThatBook.Api.Tests.Contract;

/// <summary>
/// The wire contract as approved JSON: the whole response for the brief's two hardest shapes (a contributor-only
/// author, an author-only query), volatile fields scrubbed. A contract change shows up as a diff of a .received.json
/// file next to the approved one; accept it by renaming once the change is intended.
/// </summary>
public sealed class SearchContractTests
{
    public static TheoryData<string> Scenarios =>
        ["contributor_match", "ambiguous_author_only", "clear_match", "none", "degraded_llm_down", "topic_search", "topic_none"];

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task Response_MatchesApprovedJson(string scenario)
    {
        var outcome = Fixture(scenario);
        await using var api = new ApiFactory { Search = FakeBookSearch.Returning(outcome) };
        var response = await api.Client().GetAsync($"/api/books/search?q={Uri.EscapeDataString(outcome.Query.Raw)}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/json");
        var json = await response.Json();
        json["meta"]!["request_id"]!.GetValue<string>().ShouldBe(response.Headers.GetValues("X-Request-Id").Single());
        json["meta"]!.AsObject()["request_id"] = "<scrubbed>";

        Approve(scenario, json);
    }

    [Fact]
    public async Task ContributorCase_SeparatesPrimaryAuthorFromAdapter()
    {
        await using var api = new ApiFactory { Search = FakeBookSearch.Returning(Outcomes.ContributorMatch()) };
        var r = await (await api.Client().GetAsync("/api/books/search?q=hobbit%20dixon%20graphic%20novel", TestContext.Current.CancellationToken))
            .Read<SearchResponse>();

        var top = r.Candidates[0];
        top.MatchTier.ShouldBe(Core.Model.MatchTier.B);
        top.MatchTierLabel.ShouldBe("Exact title + contributor-only author");
        top.Author.ShouldBe("J.R.R. Tolkien");                       // the brief's field: primary authors only
        top.PrimaryAuthors.ShouldHaveSingleItem().Url.ShouldBe("https://openlibrary.org/authors/OL26320A");
        top.Contributors.Select(c => (c.Name, c.Role)).ShouldContain(("Charles Dixon", "adapter"));
        top.Explanation.ShouldContain("adapter");
        r.Meta.Judge.Status.ShouldBe(Core.Model.JudgeStatus.Off); // the default
        r.Meta.Judge.Enabled.ShouldBeFalse();
        r.Meta.OpenLibrary.Retries.ShouldBe(1);
        r.Meta.OpenLibrary.UpstreamRequests.ShouldBe(3);
    }

    [Fact]
    public async Task AuthorOnlyCase_IsAmbiguous_WithHint_AndTierDCandidates()
    {
        await using var api = new ApiFactory { Search = FakeBookSearch.Returning(Outcomes.AmbiguousAuthorOnly()) };
        var r = await (await api.Client().GetAsync("/api/books/search?q=dickens", TestContext.Current.CancellationToken)).Read<SearchResponse>();

        r.Status.ShouldBe(Core.Model.MatchStatus.Ambiguous);
        r.StatusReason.ShouldBe(Core.Model.StatusReason.AuthorOnly);
        r.Hint.ShouldNotBeNullOrWhiteSpace();
        r.Candidates.Count.ShouldBe(5);
        r.Candidates.ShouldAllBe(c => c.MatchTier == Core.Model.MatchTier.D && c.Author == "Charles Dickens");
        r.Candidates.Select(c => c.Rank).ShouldBe([1, 2, 3, 4, 5]);
        r.Meta.Judge.Status.ShouldBe(Core.Model.JudgeStatus.Skipped);
        r.Meta.Judge.Agreed.ShouldBeNull();
        r.Meta.Judge.LatencyMs.ShouldBeNull();
    }

    [Fact]
    public async Task TopicRequest_IsBooksAboutTheTopic_NeverAMatch()
    {
        await using var api = new ApiFactory { Search = FakeBookSearch.Returning(Outcomes.TopicSearch()) };
        var r = await (await api.Client().GetAsync("/api/books/search?q=find%20me%20a%20book%20about%20bears", TestContext.Current.CancellationToken))
            .Read<SearchResponse>();

        r.Status.ShouldBe(Core.Model.MatchStatus.Ambiguous);
        r.StatusReason.ShouldBe(Core.Model.StatusReason.TopicSearch);
        r.Interpretation.Intent.ShouldBe(Core.Model.QueryIntent.Topic);
        r.Interpretation.Topics.ShouldBe(["bears"]);
        r.Candidates[0].MatchedSubjects.ShouldBe(["Bears", "Teddy bears", "Bears -- Fiction"]); // capped at 3
        r.Candidates.ShouldAllBe(c => c.MatchedSubjects.Count > 0);
        r.Meta.Judge.Status.ShouldBe(Core.Model.JudgeStatus.Off);
    }

    [Fact]
    public async Task TopicNone_KeepsAnEmptyCandidateList()
    {
        await using var api = new ApiFactory { Search = FakeBookSearch.Returning(Outcomes.TopicNone()) };
        var json = await (await api.Client().GetAsync("/api/books/search?q=a%20book%20about%20xqzv", TestContext.Current.CancellationToken)).Json();

        json["status"]!.GetValue<string>().ShouldBe("none");
        json["candidates"]!.AsArray().ShouldBeEmpty();
        json["interpretation"]!["intent"]!.GetValue<string>().ShouldBe("topic");
        json["interpretation"]!["topics"]!.AsArray().Select(t => t!.GetValue<string>()).ShouldBe(["xqzv"]);
    }

    [Fact]
    public async Task BookRequest_HasBookIntent_AndNoTopics()
    {
        await using var api = new ApiFactory { Search = FakeBookSearch.Returning(Outcomes.ClearMatch()) };
        var r = await (await api.Client().GetAsync("/api/books/search?q=hobbit", TestContext.Current.CancellationToken)).Read<SearchResponse>();
        r.Interpretation.Intent.ShouldBe(Core.Model.QueryIntent.Book);
        r.Interpretation.Topics.ShouldBeEmpty();
        r.Candidates.ShouldAllBe(c => c.MatchedSubjects.Count == 0);
    }

    internal static SearchOutcome Fixture(string scenario) => scenario switch
    {
        "contributor_match" => Outcomes.ContributorMatch(),
        "ambiguous_author_only" => Outcomes.AmbiguousAuthorOnly(),
        "clear_match" => Outcomes.ClearMatch(),
        "none" => Outcomes.None(),
        "degraded_llm_down" => Outcomes.DegradedLlmDown(),
        "topic_search" => Outcomes.TopicSearch(),
        "topic_none" => Outcomes.TopicNone(),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
    };

    private static void Approve(string name, JsonNode actual, [CallerFilePath] string source = "")
    {
        var dir = Path.Combine(Path.GetDirectoryName(source)!, "Approved");
        var approvedPath = Path.Combine(dir, $"{name}.approved.json");
        var receivedPath = Path.Combine(dir, $"{name}.received.json");
        var text = actual.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n";
        if (File.Exists(approvedPath) && JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(approvedPath)), actual))
        {
            if (File.Exists(receivedPath)) File.Delete(receivedPath);
            return;
        }
        Directory.CreateDirectory(dir);
        File.WriteAllText(receivedPath, text);
        Assert.Fail($"The response differs from {approvedPath}. Compare it with {receivedPath}; rename it to .approved.json if the change is intended.");
    }
}
