using System.Text.Json.Nodes;
using FindThatBook.Core.Model;
using FindThatBook.Core.Ports;
using FindThatBook.Infrastructure.Judge;

namespace FindThatBook.Infrastructure.Tests.Judge;

public sealed class DecisionsRequestAndParserTests
{
    private static readonly JudgeCandidate Hobbit = new("OL27482W", "The Hobbit", ["J.R.R. Tolkien"], ["Alan Lee (Illustrator)"], 1937, 482);
    private static readonly JudgeCandidate HobbitGn = new("OL219602W", "The Hobbit", ["Charles Dixon", "Sean Deming", "J.R.R. Tolkien"],
        ["Charles Dixon (Adapter)", "Sean Deming (Adapter)"], 1990, 10);

    private static (JsonObject Body, IReadOnlyList<string> Ids) Build(string query, params JudgeCandidate[] candidates) =>
        DecisionsRequestBuilder.Build(new JudgeRequest(query, candidates), new JudgeOptions());

    [Fact]
    public void Request_has_the_choice_question_shape_with_the_query_as_data()
    {
        var (body, ids) = Build("hobbit dixon graphic novel", Hobbit, HobbitGn);

        ids.ShouldBe(["OL27482W", "OL219602W"]);
        body["model"]!.GetValue<string>().ShouldBe("gpt-6-luna");
        var content = body["input"]![0]!["content"]!.AsArray();
        content[0]!["type"]!.GetValue<string>().ShouldBe("input_text");
        content[0]!["text"]!.GetValue<string>().ShouldContain("\"hobbit dixon graphic novel\"");
        content[1]!["type"]!.GetValue<string>().ShouldBe("input_json");
        var data = content[1]!["data"]!.AsArray();
        data[1]!["contributors"]!.AsArray().Select(c => c!.GetValue<string>()).ShouldBe(["Charles Dixon (Adapter)", "Sean Deming (Adapter)"]);
        data[0]!["first_publish_year"]!.GetValue<int>().ShouldBe(1937);
        data[0]!["edition_count"]!.GetValue<int>().ShouldBe(482);

        var question = body["questions"]![0]!;
        question["type"]!.GetValue<string>().ShouldBe("choice");
        question["name"]!.GetValue<string>().ShouldBe("best_match");
        question["instructions"]!.GetValue<string>().ShouldNotContain("hobbit dixon"); // AI-45: never in instructions
        var choices = question["choices"]!.AsArray();
        choices.Select(c => c!["value"]!.GetValue<string>()).ShouldBe(["OL27482W", "OL219602W", "NONE"]);
        choices[0]!["description"]!.GetValue<string>().ShouldBe("The Hobbit by J.R.R. Tolkien (1937), 482 editions");
    }

    [Fact]
    public void Twin_records_get_different_descriptions_AI39()
    {
        var (body, _) = Build("pride and prejudice",
            new("OL66554W", "Pride and Prejudice", ["Jane Austen"], [], 1813, 4042),
            new("OL21177W", "Pride and Prejudice", ["Jane Austen"], [], 1813, 56));
        var descriptions = body["questions"]![0]!["choices"]!.AsArray().Select(c => c!["description"]!.GetValue<string>()).ToList();
        descriptions[0].ShouldBe("Pride and Prejudice by Jane Austen (1813), 4,042 editions");
        descriptions[1].ShouldBe("Pride and Prejudice by Jane Austen (1813), 56 editions");
    }

    [Fact]
    public void Fully_identical_candidates_are_told_apart_by_id()
    {
        var (body, _) = Build("emma", new("OL1W", "Emma", ["Jane Austen"], [], null, 1), new("OL2W", "Emma", ["Jane Austen"], [], null, 1));
        var descriptions = body["questions"]![0]!["choices"]!.AsArray().Take(2).Select(c => c!["description"]!.GetValue<string>()).ToList();
        descriptions.ShouldBe(["Emma by Jane Austen, 1 edition [OL1W]", "Emma by Jane Austen, 1 edition [OL2W]"]);
        body["input"]![0]!["content"]![1]!["data"]![0]!.AsObject().ContainsKey("first_publish_year").ShouldBeFalse();
    }

    [Fact]
    public void Ids_are_deduped_blank_ids_skipped_and_the_list_capped_AI38_AI47()
    {
        var many = Enumerable.Range(1, 12).Select(i => new JudgeCandidate($"OL{i}W", $"Book {i}", ["A"], [], 2000, i)).ToList();
        many.Insert(1, new JudgeCandidate("OL1W", "Book 1 again", ["A"], [], 2000, 1));
        many.Insert(2, new JudgeCandidate(" ", "No id", ["A"], [], 2000, 1));
        var (body, ids) = DecisionsRequestBuilder.Build(new JudgeRequest("book", many), new JudgeOptions());
        ids.Count.ShouldBe(8);
        ids.Distinct().Count().ShouldBe(8);
        ids[0].ShouldBe("OL1W");
        body["questions"]![0]!["choices"]!.AsArray().Count.ShouldBe(9); // 8 + NONE
    }

    [Fact]
    public void Big_author_lists_and_long_titles_are_capped_AI46()
    {
        var anthology = new JudgeCandidate("OL100W", new string('T', 5000), [.. Enumerable.Range(0, 150).Select(i => $"Author {i}")],
            [.. Enumerable.Range(0, 40).Select(i => $"Editor {i} (Editor)")], 2000, 5);
        var (body, _) = Build("best short stories", anthology, Hobbit);
        var item = body["input"]![0]!["content"]![1]!["data"]![0]!;
        item["authors"]!.AsArray().Count.ShouldBe(5);
        item["contributors"]!.AsArray().Count.ShouldBe(5);
        item["title"]!.GetValue<string>().Length.ShouldBeLessThanOrEqualTo(200);
        body["questions"]![0]!["choices"]![0]!["description"]!.GetValue<string>().Length.ShouldBeLessThanOrEqualTo(200);
        body.ToJsonString().Length.ShouldBeLessThan(3000);
    }

    [Fact]
    public void Chosen_first_candidate_with_its_probability_is_agreed()
    {
        var v = DecisionsResponseParser.Parse(DecisionsFixtures.HobbitChoice, DecisionsFixtures.HobbitIds, "gpt-6-luna", 240);
        v.Status.ShouldBe(JudgeStatus.Agreed);
        v.Pick.ShouldBe("OL27482W");
        v.Probability.ShouldBe(1.0);
        v.InputTokens.ShouldBe(833);
        v.Model.ShouldBe("gpt-6-luna");
        v.ElapsedMs.ShouldBe(240);
    }

    [Fact]
    public void Probability_comes_from_probabilities_not_confidence_AI40()
    {
        var v = DecisionsResponseParser.Parse(DecisionsFixtures.TwinsSwapped, DecisionsFixtures.TwinIds, "gpt-6-luna", 600);
        v.Status.ShouldBe(JudgeStatus.Disagreed); // code's #1 here was the 56-edition twin
        v.Pick.ShouldBe("OL66554W");
        v.Probability.ShouldBe(0.56); // confidence was 0.41
    }

    [Fact]
    public void None_is_chose_none_with_its_probability()
    {
        var v = DecisionsResponseParser.Parse(DecisionsFixtures.NoneChosen, DecisionsFixtures.NoneIds, "gpt-6-luna", 380);
        v.Status.ShouldBe(JudgeStatus.ChoseNone);
        v.Pick.ShouldBeNull();
        v.Probability.ShouldBe(0.72);
    }

    [Fact]
    public void Refusal_with_200_is_refused_AI44()
    {
        var v = DecisionsResponseParser.Parse(DecisionsFixtures.Refusal, ["OL1B", "OL2B"], "gpt-6-luna", 257);
        v.Status.ShouldBe(JudgeStatus.Refused);
        v.Pick.ShouldBeNull();
        v.InputTokens.ShouldBe(285);
    }

    public static TheoryData<string, string> Unusable => new()
    {
        { "not JSON", "<html>" },
        { "no answers", """{"model":"gpt-6-luna"}""" },
        { "other question only", """{"answers":[{"type":"predicate","name":"x","probability":0.5}]}""" },
        { "choice not sent (AI-47)", """{"answers":[{"type":"choice","name":"best_match","choice":"OL999W","probabilities":[{"value":"OL999W","probability":0.9}]}]}""" },
        { "blank choice (AI-47)", """{"answers":[{"type":"choice","name":"best_match","choice":"","probabilities":[{"value":"","probability":0.9}]}]}""" },
        { "confidence only (AI-40)", """{"answers":[{"type":"choice","name":"best_match","choice":"OL27482W","confidence":0.99}]}""" },
        { "probability out of range", """{"answers":[{"type":"choice","name":"best_match","choice":"OL27482W","probabilities":[{"value":"OL27482W","probability":1.7}]}]}""" },
    };

    [Theory]
    [MemberData(nameof(Unusable))]
    public void Unusable_answers_are_unavailable(string because, string json)
    {
        var v = DecisionsResponseParser.Parse(json, DecisionsFixtures.HobbitIds, "gpt-6-luna", 10);
        v.Status.ShouldBe(JudgeStatus.Unavailable, because);
        v.Probability.ShouldBeNull(because);
    }
}
