using System.Text.Json.Nodes;
using FindThatBook.Eval.Datasets;
using FindThatBook.Eval.Hosting;

namespace FindThatBook.Api.Tests.Golden;

/// <summary>Dataset versions and the held-out seal.</summary>
public sealed class DatasetTests
{
    private static JsonArray Cases(string file) => JsonNode.Parse(File.ReadAllText(Path.Combine(RepoPaths.Eval, file)))!["cases"]!.AsArray();

    [Fact]
    public void GoldenV2_DiffersFromV1OnlyInNone04AndTheTuningFlags()
    {
        var v1 = Cases("golden.json");
        var v2 = Cases("golden.v2.json");

        v2.Count.ShouldBe(v1.Count);
        for (var i = 0; i < v1.Count; i++)
        {
            var a = v1[i]!.AsObject();
            var b = v2[i]!.DeepClone().AsObject();
            b["in_sample"]!.GetValue<bool>().ShouldBeTrue();
            b.Remove("in_sample");
            b.Remove("in_hard_set");
            if (a["id"]!.GetValue<string>() == "none-04")
            {
                b["expected"]!.ToJsonString().ShouldBe("""{"status":["rejected"],"error_code":"validation_failed","http_status":400}""");
                a.Remove("expected"); a.Remove("note"); b.Remove("expected"); b.Remove("note");
            }
            JsonNode.DeepEquals(a, b).ShouldBeTrue($"row {a["id"]} changed between v1 and v2");
        }
    }

    [Fact]
    public void GoldenV2_HeaderRecordsVersionChangelogAndInSampleTuning()
    {
        var root = JsonNode.Parse(File.ReadAllText(Path.Combine(RepoPaths.Eval, "golden.v2.json")))!;

        root["version"]!.GetValue<int>().ShouldBe(2);
        root["changelog"]!.ToJsonString().ShouldContain("DE-05");
        root["tuning"]!["status"]!.GetValue<string>().ShouldBe("in_sample");
        Cases("golden.v2.json").Count(c => c!["in_hard_set"]!.GetValue<bool>()).ShouldBe(30);
    }

    [Fact]
    public void Load_GoldenDefault_IsV2With57AnswerableRowsAndOneRejectedRow()
    {
        var d = DatasetLoader.Load("golden", allowHeldOut: false);

        d.Version.ShouldBe(2);
        d.HeldOut.ShouldBeFalse();
        d.Cases.Count.ShouldBe(60);
        d.Cases.Count(c => c.Expected.Answerable).ShouldBe(57);
        d.Cases.Single(c => c.Expected.IsRejection).Id.ShouldBe("none-04");
        d.Cases.Count(c => c.Expected.IsAuthorRow).ShouldBe(8); // 5 author_only + dickens + meyer + hermione
        d.Cases.ShouldAllBe(c => c.InSample);
    }

    [Fact]
    public void Load_GoldenV1_KeepsNone04AsAPlainNoneRow()
    {
        var none04 = DatasetLoader.Load("golden-v1", allowHeldOut: false).Cases.Single(c => c.Id == "none-04");

        none04.Expected.Statuses.ShouldBe(["none"]);
        none04.Expected.IsRejection.ShouldBeFalse();
    }

    [Fact]
    public void Load_HeldOutWithoutTheFlag_IsRefused()
    {
        Should.Throw<HeldOutRefusedException>(() => DatasetLoader.Load("heldout", allowHeldOut: false));
        Should.Throw<HeldOutRefusedException>(() => DatasetLoader.Load(Path.Combine(RepoPaths.Eval, "heldout.json"), allowHeldOut: false));
    }

    [Fact]
    public void Load_HeldOutWithTheFlag_IsLabelledHeldOutAndNotInSample()
    {
        var d = DatasetLoader.Load("heldout", allowHeldOut: true);

        d.HeldOut.ShouldBeTrue();
        d.Label!.ShouldContain("HELD-OUT");
        d.Cases.ShouldAllBe(c => !c.InSample && c.Expected.WrongMatchIsStatusError);
    }

    [Fact]
    public void Load_JsonlSets_ReadTheirMetaLine()
    {
        var hard = DatasetLoader.Load("hard", allowHeldOut: false);
        var synthetic = DatasetLoader.Load("synthetic", allowHeldOut: false);

        hard.Cases.Count.ShouldBe(46);
        hard.Cases.ShouldAllBe(c => c.InSample);
        hard.Tuning.ShouldStartWith("in_sample");
        synthetic.Cases.Count.ShouldBe(2000);
        synthetic.Label!.ShouldContain("not Open Library's ranking");
        synthetic.Cases.ShouldAllBe(c => !c.InSample && c.Expected.Statuses.Count == 0);
    }
}
