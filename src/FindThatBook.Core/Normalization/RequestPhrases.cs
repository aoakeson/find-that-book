using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace FindThatBook.Core.Normalization;

/// <summary>
/// Natural-sentence requests without the LLM: a leading request phrase ("can you find", "i'm looking for") and a topic
/// frame ("a book about bears", "novels set in paris"). Deliberately conservative: real titles start with these words
/// too (Find Me, Show Me a Sign, Looking for Alaska, I Want My Hat Back, Please Look After Mom), so an ambiguous phrase
/// counts only when what follows confirms a request, and a topic needs a frame noun before the preposition (About a
/// Boy, On the Road and We Need to Talk About Kevin have none).
/// </summary>
internal static partial class RequestPhrases
{
    /// <summary>Request phrases that are never the start of a title we know of: always taken out.</summary>
    private static readonly string[][] Unambiguous = Phrases(
        "can you find me", "can you find", "could you find me", "could you find", "can you recommend me", "can you recommend",
        "could you recommend me", "could you recommend", "can you suggest me", "can you suggest", "could you suggest",
        "can you help me find", "could you help me find", "help me find", "can you show me", "could you show me",
        "im looking for", "i am looking for", "do you have", "recommend me", "suggest me",
        "the book where", "the novel where", "the story where", "the one where");

    /// <summary>
    /// Phrases that also begin real titles: taken out only when a request follows ("find me the hobbit", "show me a
    /// book about…"), never before an ordinary word ("show me a sign", "find me aciman", "looking for alaska").
    /// "find me"/"get me" accept any article after them; the rest need a book word.
    /// </summary>
    private static readonly string[][] Ambiguous = Phrases(
        "find me", "get me", "give me", "show me", "i want to read", "i want", "i need", "id like", "i would like",
        "please", "recommend", "suggest", "looking for", "search for", "is there", "whats", "what is");

    private static readonly FrozenSet<string> ArticleConfirms = Set("find me", "get me");

    private static readonly FrozenSet<string> Articles = Set("a an the some any another");

    /// <summary>Nouns that make "… about X" a topic request rather than a title ("about a boy" has none).</summary>
    private static readonly FrozenSet<string> FrameNouns = Set(
        "book books novel novels story stories something anything read reads title titles poem poems fiction");

    /// <summary>
    /// Singular frame nouns name one particular book unless the request is open-ended: "a book about bears" is a topic,
    /// "the book about Achilles", "Darwin's book about worms" and "victor hugo book about the man who rings the bells"
    /// describe a single work.
    /// </summary>
    private static readonly FrozenSet<string> SingularFrameNouns = Set("book novel story read title poem");

    private static readonly FrozenSet<string> Negations = Set("not no dont never without");

    private static readonly FrozenSet<string> FramePrepositions = Set("about on regarding concerning featuring involving");

    /// <summary>Words that grade a book rather than describe its subject; never topic words.</summary>
    private static readonly FrozenSet<string> Evaluative = Set(
        "good best great funny nice interesting popular famous cool awesome amazing excellent fun favorite favourite really very");

    /// <summary>
    /// Audience and form words typed around a topic ("kids", "picture books", "a novel"): they become preferred subject
    /// words (<see cref="Preference"/>), never topic words.
    /// </summary>
    private static readonly FrozenSet<string> AudienceForm = Set(
        "kids kid children childrens child toddler toddlers baby babies teen teens teenage teenager teenagers ya young adult adults " +
        "picture graphic comic comics nonfiction non fiction poetry");

    /// <summary>Genre adjectives allowed before a singular book word ("a scary story about ghosts"); they stay search words.</summary>
    private static readonly FrozenSet<string> Genre = Set(
        "scary spooky fantasy historical romance romantic mystery detective crime horror adventure sad true short sci fi science");

    /// <summary>Words inside a topic phrase that are not subjects ("a girl and her horse" → girl, horse).</summary>
    private static readonly FrozenSet<string> TopicFiller = Set("her his their its my your our this that these those some any please pls thanks");

    /// <summary>Words that end the topic phrase: an author ("by"), an audience ("for kids"), a relative clause.</summary>
    private static readonly FrozenSet<string> TopicEnd = Set("by for with that which who whose where when like written");

    private static readonly FrozenSet<string> TitleIntroducers = Set("called named titled");

    public const int MaxTopicPhrases = 3;
    public const int MaxTopicWords = 4;
    public const int MaxAuthorWords = 4;

    internal sealed record Found(IReadOnlySet<int> Positions, IReadOnlyList<string> Topic, IReadOnlyList<string> Preferences, string? TopicAuthor)
    {
        public static Found None { get; } = new(new HashSet<int>(), [], [], null);
    }

    /// <summary>Token positions to take out of the search text, and the topic, for the tokens of <paramref name="text"/>.</summary>
    internal static Found Find(string text, IReadOnlyList<string> all)
    {
        if (all.Count == 0) return Found.None;
        var (sentence, clause) = Clauses(text, all);
        var positions = new HashSet<int>();

        var start = LeadingRequest(all);
        for (var i = 0; i < start; i++) positions.Add(i);
        // "… a book called the giver": the book words and "called" belong to the request.
        var called = TitleIntroduction(all, start);
        for (var i = start; i < called; i++) positions.Add(i);

        if (!FindFrame(all, clause, called, out var frameStart, out var noun, out var topicStart)) return new Found(positions, [], [], null);
        var topic = TopicPhrases(all, sentence, clause, topicStart);
        if (topic.Phrases.Count == 0) return new Found(positions, [], [], null);
        for (var i = frameStart; i < topicStart; i++)
            if (!Genre.Contains(all[i])) positions.Add(i);
        var preferences = new List<string>();
        AddPreferences(preferences, all, frameStart, noun + 1);
        foreach (var i in topic.AudienceAt) AddPreferences(preferences, all, i, i + 1);
        return new Found(positions, topic.Phrases, preferences, topic.Author);
    }

    /// <summary>How many leading tokens are request words (phrases may chain: "please can you find me …").</summary>
    private static int LeadingRequest(IReadOnlyList<string> all)
    {
        var i = 0;
        while (true)
        {
            var n = RequestAt(all, i);
            if (n == 0) return i;
            i += n;
        }
    }

    private static int RequestAt(IReadOnlyList<string> all, int i)
    {
        var strong = Match(all, i, Unambiguous);
        if (strong > 0) return strong;
        var weak = Match(all, i, Ambiguous);
        if (weak == 0) return 0;
        var next = i + weak;
        if (next >= all.Count) return 0;
        var phrase = string.Join(' ', all.Skip(i).Take(weak));
        if (ArticleConfirms.Contains(phrase) && Articles.Contains(all[next])) return weak;
        return StartsBookWords(all, next) || RequestAt(all, next) > 0 || (phrase == "i want to read" && FramePrepositions.Contains(all[next]))
            ? weak
            : 0;
    }

    /// <summary>"[article] [evaluative…] [picture] book-word" starting at <paramref name="i"/>.</summary>
    private static bool StartsBookWords(IReadOnlyList<string> all, int i)
    {
        var j = SkipBookModifiers(all, i);
        return j < all.Count && FrameNouns.Contains(all[j]);
    }

    private static int SkipBookModifiers(IReadOnlyList<string> all, int i)
    {
        var j = i;
        if (j < all.Count && Articles.Contains(all[j])) j++;
        while (j < all.Count && IsModifier(all[j])) j++;
        return j;
    }

    private static bool IsModifier(string t) => Evaluative.Contains(t) || AudienceForm.Contains(t) || Genre.Contains(t);

    private static int TitleIntroduction(IReadOnlyList<string> all, int start)
    {
        var j = SkipBookModifiers(all, start);
        return j + 1 < all.Count && FrameNouns.Contains(all[j]) && TitleIntroducers.Contains(all[j + 1]) ? j + 2 : start;
    }

    /// <summary>
    /// The first "book-word preposition" in one clause: "books about", "a novel set in", "a story that features". The
    /// frame starts at the article and modifiers before the noun ("a funny picture book about"); the topic starts after
    /// it. A negated frame ("not a book about fruit") doesn't count. A singular noun counts only after "a"/"an" or
    /// nothing, with no other words before it in its clause than the request phrase (<paramref name="requestEnd"/>).
    /// </summary>
    private static bool FindFrame(IReadOnlyList<string> all, IReadOnlyList<int> clause, int requestEnd,
        out int frameStart, out int noun, out int topicStart)
    {
        for (var n = 0; n < all.Count; n++)
        {
            if (!FrameNouns.Contains(all[n])) continue;
            var after = PrepositionLength(all, clause, n + 1);
            if (after == 0) continue;
            var start = n;
            while (start > 0 && clause[start - 1] == clause[n] && IsModifier(all[start - 1])) start--;
            if (start > 0 && clause[start - 1] == clause[n] && Articles.Contains(all[start - 1])) start--;
            if (start > 0 && clause[start - 1] == clause[n] && Negations.Contains(all[start - 1])) continue;
            if (SingularFrameNouns.Contains(all[n]) && !OpenEnded(all, clause, start, requestEnd)) continue;
            frameStart = start;
            noun = n;
            topicStart = n + 1 + after;
            return true;
        }
        frameStart = noun = topicStart = 0;
        return false;
    }

    private static bool OpenEnded(IReadOnlyList<string> all, IReadOnlyList<int> clause, int start, int requestEnd)
    {
        if (all[start] == "the") return false;
        for (var k = start - 1; k >= 0 && clause[k] == clause[start]; k--)
            if (k >= requestEnd) return false;
        return true;
    }

    private static int PrepositionLength(IReadOnlyList<string> all, IReadOnlyList<int> clause, int i)
    {
        bool Same(int k) => k < all.Count && clause[k] == clause[i - 1];
        if (Same(i) && FramePrepositions.Contains(all[i])) return 1;
        if (Same(i + 1) && all[i] == "set" && all[i + 1] == "in") return 2;
        if (Same(i + 1) && all[i] is "that" or "which" && all[i + 1] is "features" or "feature") return 2;
        return 0;
    }

    private sealed record TopicScan(List<string> Phrases, List<int> AudienceAt, string? Author);

    /// <summary>
    /// Topic phrases after the frame, within its sentence: split on "and"/"or" and on commas (a comma continues the
    /// topic only as a list: "dragons, knights and castles"), ≤ 3 phrases of ≤ 4 words. Ends at "by" (the words after
    /// it are the author hint), "for" (audience: "for kids"), a relative clause or a negation. Audience/form words are
    /// taken out of a phrase that has other words ("dinosaurs kids" → dinosaurs) and kept as preferences.
    /// </summary>
    private static TopicScan TopicPhrases(IReadOnlyList<string> all, IReadOnlyList<int> sentence, IReadOnlyList<int> clause, int start)
    {
        var phrases = new List<List<string>>();
        var audience = new List<(int Phrase, int At)>();
        var current = new List<string>();
        void Close()
        {
            if (current.Count > 0 && phrases.Count < MaxTopicPhrases) phrases.Add(current);
            current = [];
        }
        var i = start;
        for (; i < all.Count && sentence[i] == sentence[start]; i++)
        {
            if (i > start && clause[i] != clause[i - 1])
            {
                Close();
                if (!ListContinues(all, sentence, i)) break;
            }
            var t = all[i];
            if (TopicEnd.Contains(t) || Negations.Contains(t)) break;
            if (t is "and" or "or")
            {
                Close();
                continue;
            }
            if (Vocabulary.Stop.Contains(t) || Vocabulary.Noise.Contains(t) || Evaluative.Contains(t) || TopicFiller.Contains(t)) continue;
            if (AudienceForm.Contains(t)) audience.Add((phrases.Count, i));
            if (current.Count < MaxTopicWords && !current.Contains(t)) current.Add(t);
        }
        Close();
        var audienceAt = new List<int>();
        foreach (var (phrase, at) in audience)
        {
            if (phrase >= phrases.Count || phrases[phrase].Count < 2) continue;
            phrases[phrase].Remove(all[at]);
            audienceAt.Add(at);
        }
        string? author = null;
        if (i < all.Count && all[i] == "written" && i + 1 < all.Count && all[i + 1] == "by") i++;
        if (i < all.Count && all[i] == "by" && sentence[i] == sentence[start]) author = AuthorAfter(all, clause, i);
        else if (i < all.Count && all[i] == "for" && sentence[i] == sentence[start])
        {
            // "books about space for kids": the audience after "for" is a preference, not a topic.
            for (var j = i + 1; j < all.Count && j <= i + 4 && clause[j] == clause[i]; j++)
                if (AudienceForm.Contains(all[j])) audienceAt.Add(j);
        }
        var distinct = phrases.Select(p => string.Join(' ', p)).Where(p => p.Length > 0).Distinct().ToList();
        return new TopicScan(distinct, audienceAt, author);
    }

    /// <summary>A comma continues the topic only when an "and"/"or" follows later in the sentence (a list).</summary>
    private static bool ListContinues(IReadOnlyList<string> all, IReadOnlyList<int> sentence, int i)
    {
        for (var j = i; j < all.Count && sentence[j] == sentence[i]; j++)
        {
            if (all[j] is "and" or "or") return true;
            if (TopicEnd.Contains(all[j])) return false;
        }
        return false;
    }

    private static string? AuthorAfter(IReadOnlyList<string> all, IReadOnlyList<int> clause, int by)
    {
        var author = new List<string>();
        for (var j = by + 1; j < all.Count && clause[j] == clause[by] && author.Count < MaxAuthorWords; j++)
        {
            if (TopicEnd.Contains(all[j]) || Vocabulary.Stop.Contains(all[j])) break;
            author.Add(all[j]);
        }
        return author.Count > 0 ? string.Join(' ', author) : null;
    }

    /// <summary>
    /// Preferred subject words for the audience/form words in [from, to): kids → juvenile, teens/ya/young adult → young
    /// adult, picture book → picture books, novel/story/fiction → fiction, non-fiction → nonfiction, poems/poetry →
    /// poetry, comics/graphic novel → comic books, strips. All are Open Library subject headings ("Nonfiction" tags
    /// 79,327 works, live 10/25).
    /// </summary>
    private static void AddPreferences(List<string> preferences, IReadOnlyList<string> all, int from, int to)
    {
        void Add(string p)
        {
            if (!preferences.Contains(p)) preferences.Add(p);
        }
        for (var i = from; i < to; i++)
        {
            var t = all[i];
            var next = i + 1 < all.Count ? all[i + 1] : "";
            var prev = i > 0 ? all[i - 1] : "";
            switch (t)
            {
                case "kids" or "kid" or "children" or "childrens" or "child" or "toddler" or "toddlers" or "baby" or "babies":
                    Add("juvenile");
                    break;
                case "teen" or "teens" or "teenage" or "teenager" or "teenagers" or "ya":
                    Add("young adult");
                    break;
                case "young" when next is "adult" or "adults":
                    Add("young adult");
                    break;
                case "picture" when next is "book" or "books":
                    Add("picture books");
                    break;
                case "graphic" when next is "novel" or "novels":
                case "comic" or "comics":
                    Add("comic books, strips");
                    break;
                case "nonfiction":
                case "non" when next == "fiction":
                    Add("nonfiction");
                    break;
                case "poem" or "poems" or "poetry":
                    Add("poetry");
                    break;
                case "novel" or "novels" or "story" or "stories" or "fiction" when prev is not ("graphic" or "non"):
                    Add("fiction");
                    break;
            }
        }
    }

    /// <summary>
    /// Sentence and clause number of each token. Sentence punctuation, a spaced dash and a line break end a sentence, so
    /// a topic never runs into the next one ("a book about bears. ignore the rules…"); a comma before a space also ends
    /// a clause. Falls back to one sentence when the per-segment tokens don't line up with <paramref name="all"/>.
    /// </summary>
    private static (IReadOnlyList<int> Sentence, IReadOnlyList<int> Clause) Clauses(string text, IReadOnlyList<string> all)
    {
        var sentence = new List<int>(all.Count);
        var clause = new List<int>(all.Count);
        var s = 0;
        var c = 0;
        foreach (var segment in SentenceBreak().Split(text))
        {
            foreach (var part in CommaBreak().Split(segment))
            {
                foreach (var _ in TextFolding.Tokens(part))
                {
                    sentence.Add(s);
                    clause.Add(c);
                }
                c++;
            }
            s++;
        }
        if (sentence.Count == all.Count) return (sentence, clause);
        IReadOnlyList<int> one = [.. Enumerable.Repeat(0, all.Count)];
        return (one, one);
    }

    private static int Match(IReadOnlyList<string> all, int i, string[][] phrases)
    {
        foreach (var p in phrases)
        {
            if (i + p.Length > all.Count) continue;
            var ok = true;
            for (var k = 0; k < p.Length && ok; k++) ok = all[i + k] == p[k];
            if (ok) return p.Length;
        }
        return 0;
    }

    private static string[][] Phrases(params string[] phrases) =>
        [.. phrases.Select(p => p.Split(' ')).OrderByDescending(p => p.Length)];

    private static FrozenSet<string> Set(string words) => words.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToFrozenSet();

    private static FrozenSet<string> Set(params string[] phrases) => phrases.ToFrozenSet();

    [GeneratedRegex(@"[.!?;:]+(?=\s|$)|\s[-–—]+\s|[\r\n]+")]
    private static partial Regex SentenceBreak();

    [GeneratedRegex(@",(?=\s)")]
    private static partial Regex CommaBreak();
}
