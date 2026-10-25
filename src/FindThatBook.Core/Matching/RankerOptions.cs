namespace FindThatBook.Core.Matching;

/// <summary>
/// Rules added on top of the measured baseline ranker, each switchable so the eval can measure it. Production uses
/// <see cref="Default"/> (all on); <see cref="Baseline"/> turns every addition off, which is the measured reference the
/// parity tests compare against.
/// </summary>
internal sealed record RankerOptions
{
    public static RankerOptions Default { get; } = new();

    public static RankerOptions Baseline { get; } = new()
    {
        SoftTokens = false,
        HypothesisAuthorCheck = false,
        RequiredTitleWords = false,
        Mashup = false,
        LoneSurname = false,
        CharacterWord = false,
        NamedContributor = false,
        SeriesName = false,
        TinyRecord = false,
        VolumeMarkers = false,
        NoneGuard = false,
        AuthoredExact = false,
        IntentRivals = false,
        PerformerRoles = false,
        ExactOverPartial = false,
        TypedAuthorTiny = false,
        SubtitleWorks = false,
        SameTitleRival = false,
        Topics = false,
    };

    /// <summary>IN-044: soft (kept) year tokens are optional for title exactness and can earn the year bonus.</summary>
    public bool SoftTokens { get; init; } = true;

    /// <summary>R3-0366: an interpreter-only exact title on a work by a different author than the interpreter's is not tier A;
    /// the interpreter's author also matches through alternative names.</summary>
    public bool HypothesisAuthorCheck { get; init; } = true;

    /// <summary>R3-0390 / R3-0283: a typed hypothesis subtitle and a typed volume number are required title words.</summary>
    public bool RequiredTitleWords { get; init; } = true;

    /// <summary>R3-0195: typed title words split across two works by one author → both shown first, status capped.</summary>
    public bool Mashup { get; init; } = true;

    /// <summary>R3-0025: with no interpreter, one typed surname of a &lt; 3-edition author is not enough for a match.</summary>
    public bool LoneSurname { get; init; } = true;

    /// <summary>DE-01 / AI-43: a one-word character query is not a match unless the word is the typed title of the interpreter's book.</summary>
    public bool CharacterWord { get; init; } = true;

    /// <summary>Golden contrib-03: a work leaving a typed contributor unexplained never outranks the record that explains it.</summary>
    public bool NamedContributor { get; init; } = true;

    /// <summary>IN-058 / DA-047: a typed series name ("harry potter") is never a match on one of its books.</summary>
    public bool SeriesName { get; init; } = true;

    /// <summary>DA-009 / DA-016: a ≤ 2-edition top never matches over a 10× larger work whose title it contains.</summary>
    public bool TinyRecord { get; init; } = true;

    /// <summary>IN-035: a volume-marked part ("(下)", "vol. 2") is penalised like an omnibus when the query asks for no volume.</summary>
    public bool VolumeMarkers { get; init; } = true;

    /// <summary>R3-0390: "none" is not reported while an exact title with author evidence is in the top 5.</summary>
    public bool NoneGuard { get; init; } = true;

    /// <summary>R3-0390: an exact title with a typed author never ranks below a tier-E work (keyword overlap only).</summary>
    public bool AuthoredExact { get; init; } = true;

    /// <summary>An edition-intent top (the graphic novel asked for) is compared, for status, only with other intent hits.</summary>
    public bool IntentRivals { get; init; } = true;

    /// <summary>DA-006: a narrator/reader credit never demotes one of a work's ≤ 3 listed authors to contributor.</summary>
    public bool PerformerRoles { get; init; } = true;

    /// <summary>IN-050 ("it"): an exact title outranks a much more popular partial title by another author unless it is tiny next to it.</summary>
    public bool ExactOverPartial { get; init; } = true;

    /// <summary>DA-009: the tiny-record rule never overrules an author the user typed with another author's bigger record.</summary>
    public bool TypedAuthorTiny { get; init; } = true;

    /// <summary>A title match that needed its subtitle doesn't fold into the main title's work ("… : The Two Towers"), and a
    /// representative that takes the group's tier shows the evidence that earned it.</summary>
    public bool SubtitleWorks { get; init; } = true;

    /// <summary>DA-003: with no typed author, a same-title work by another author with ≥ 25% of the top's editions keeps the status ambiguous.</summary>
    public bool SameTitleRival { get; init; } = true;

    /// <summary>Topic requests ("find me a book about bears"): subject evidence, topic order and status, request words optional.</summary>
    public bool Topics { get; init; } = true;
}
