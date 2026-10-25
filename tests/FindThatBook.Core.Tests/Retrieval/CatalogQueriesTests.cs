using FindThatBook.Core.Model;
using FindThatBook.Core.Normalization;
using FindThatBook.Core.Ports;
using FindThatBook.Core.Retrieval;

namespace FindThatBook.Core.Tests.Retrieval;

public class CatalogQueriesTests
{
    private static QueryHypothesis Hyp(string? title = null, string? author = null, string? english = null, string? original = null) =>
        new(title, author, [], [], english, original);

    // ---- call 1 and DE-05 short routing ----

    [Theory]
    [InlineData("noise and year are not sent", "Tolkien Hobbit Illustrated Deluxe 1937", "broad", "q=tolkien hobbit&limit=20")]
    [InlineData("number words go as digits", "nineteen eighty four orwell", "broad", "q=1984 orwell&limit=20")]
    [InlineData("IN-044 soft year is sent", "the hobbit 1937", "broad", "q=hobbit 1937&limit=20")]
    [InlineData("DE-05 Han under 3 characters", "三体", "short_title", "title=三体&limit=20")]
    [InlineData("DE-05 / IN-050 it", "it", "short_title", "title=it&limit=20")]
    [InlineData("DE-05 / IN-051 us", "us", "short_title", "title=us&limit=20")]
    [InlineData("DE-05 v.", "v.", "short_title", "title=v&limit=20")]
    [InlineData("DE-05 ab", "ab", "short_title", "title=ab&limit=20")]
    [InlineData("IN-005 one letter", "x", "short_title", "title=x&limit=20")]
    [InlineData("the it → it", "the it", "short_title", "title=it&limit=20")]
    [InlineData("three characters is enough", "ice", "broad", "q=ice&limit=20")]
    [InlineData("the space counts, as Open Library counts it", "v x", "broad", "q=v x&limit=20")]
    [InlineData("IN-052 short title + author", "it stephen king", "broad", "q=it stephen king&limit=20")]
    [InlineData("DE-07 Japanese", "ノルウェイの森", "broad", "q=ノルウェイの森&limit=20")]
    [InlineData("Hangul sent as syllables", "채식주의자", "broad", "q=채식주의자&limit=20")]
    public void Broad(string @case, string raw, string step, string sent)
    {
        _ = @case;
        var request = CatalogQueries.Broad(QueryCleaner.Clean(raw));
        request.Step.ShouldBe(step);
        request.Query.ToString().ShouldBe(sent);
    }

    [Theory]
    // DA-065..068: nothing the user types can become Solr syntax
    [InlineData("DA-065 unbalanced fielded paren", "title:(hobbit", "q=title hobbit&limit=20")]
    [InlineData("DA-066 fielded AND injection", "title:(the hobbit) AND author:(tolkien", "q=title hobbit author tolkien&limit=20")]
    [InlineData("DA-067 leading minus negation", "-tolkien hobbit", "q=tolkien hobbit&limit=20")]
    [InlineData("DA-067 AND NOT", "hobbit AND NOT tolkien", "q=hobbit not tolkien&limit=20")]
    [InlineData("DA-067 hyphenated title", "Spider-Man", "q=spider man&limit=20")]
    [InlineData("DA-068 wildcard", "hob*", "q=hob&limit=20")]
    [InlineData("DA-068 bare OR (stop word fallback, lower case)", "OR", "short_title")]
    [InlineData("quotes, boosts, fuzzy, ranges", "\"hobbit\"^2 tolkien~ [a TO z] {x}", "q=hobbit 2 tolkien z x&limit=20")]
    [InlineData("escapes and slashes", "hob\\bit / tolkien", "q=hob bit tolkien&limit=20")]
    [InlineData("DA-069 colon in natural text", "dune: messiah", "q=dune messiah&limit=20")]
    public void Broad_UserTextNeverCarriesSolrSyntax(string @case, string raw, string sent)
    {
        _ = @case;
        var request = CatalogQueries.Broad(QueryCleaner.Clean(raw));
        if (sent == "short_title")
        {
            request.Step.ShouldBe(CatalogSteps.ShortTitle);
            request.Query.Title.ShouldBe("or");
            return;
        }
        request.Query.ToString().ShouldBe(sent);
        AssertNoSyntax(request.Query.Q!);
    }

    [Fact]
    public void Broad_EmptyQueryIsAProgrammingError()
    {
        Should.Throw<ArgumentException>(() => CatalogQueries.Broad(QueryCleaner.Clean("?!?")));
    }

    [Theory]
    [InlineData("DE-05 422 on q= re-plans to title=", "tolkien hobbit", "title=tolkien hobbit&limit=20")]
    [InlineData("already title=: nothing else to try", "it", null)]
    [InlineData("nothing searchable", "###", null)]
    public void AfterRejected(string @case, string raw, string? sent)
    {
        _ = @case;
        var request = CatalogQueries.AfterRejected(QueryCleaner.Clean(raw));
        if (sent is null)
        {
            request.ShouldBeNull();
            return;
        }
        request.ShouldNotBeNull();
        request.Step.ShouldBe(CatalogSteps.ShortTitle);
        request.Query.ToString().ShouldBe(sent);
    }

    // ---- hypothesis calls ----

    [Theory]
    [InlineData("LLM's own words, numerals never converted", "A Tale of Two Cities", "Charles Dickens", "title=tale two cities&author=dickens&limit=20")]
    [InlineData("main title only", "The Hobbit: or There and Back Again", "J.R.R. Tolkien", "title=hobbit&author=tolkien&limit=20")]
    [InlineData("folded", "Les Misérables", "Victor Hugo", "title=les miserables&author=hugo&limit=20")]
    [InlineData("no author", "Nineteen Eighty-Four", null, "title=nineteen eighty four&limit=20")]
    [InlineData("author only", null, "Kurt Vonnegut Jr.", "author=vonnegut&limit=20")]
    [InlineData("DA-066 LLM series decoration is stripped", "Dune (Dune Chronicles, #1)", "Frank Herbert", "title=dune&author=herbert&limit=20")]
    [InlineData("LLM title with Solr syntax", "Spider-Man: Homecoming*", null, "title=spider man&limit=20")]
    [InlineData("Hangul title stays syllables", "채식주의자", "Han Kang", "title=채식주의자&author=kang&limit=20")]
    [InlineData("stop-word title keeps its words", "The", null, "title=the&limit=20")]
    public void HypothesisTitle(string @case, string? title, string? author, string sent)
    {
        _ = @case;
        var query = CatalogQueries.HypothesisTitle(Hyp(title, author));
        query.ShouldNotBeNull();
        query.ToString().ShouldBe(sent);
    }

    [Fact]
    public void HypothesisTitle_NothingToSend_IsNull()
    {
        CatalogQueries.HypothesisTitle(Hyp()).ShouldBeNull();
        CatalogQueries.HypothesisTitle(Hyp("   ", " ")).ShouldBeNull();
    }

    [Fact]
    public void HypothesisTitle_AsksForCharacterNames_WhenTheModelKeptTypedCharacters()
    {
        var withCharacters = new QueryHypothesis("To Kill a Mockingbird", "Harper Lee", ["Scout Finch", "Atticus Finch"], [], null, null);
        CatalogQueries.HypothesisTitle(withCharacters)!.ToString().ShouldBe("title=kill mockingbird&author=lee&limit=20&person");
        CatalogQueries.HypothesisTitle(Hyp("To Kill a Mockingbird", "Harper Lee"))!.IncludePersons.ShouldBeFalse();
    }

    [Fact]
    public void HypothesisTitle_PromotesTheOriginalThenTheEnglishTitle_WhenTheLlmGaveNoTitle()
    {
        CatalogQueries.HypothesisTitle(Hyp(null, "Liu Cixin", english: "The Three-Body Problem", original: "三体"))!
            .ToString().ShouldBe("title=三体&author=cixin&limit=20");
        CatalogQueries.HypothesisTitle(Hyp(null, null, english: "The Three-Body Problem"))!
            .ToString().ShouldBe("title=three body problem&limit=20");
        // promoted, so the original-title call has nothing new to send
        CatalogQueries.OriginalTitle(Hyp(null, "Liu Cixin", original: "三体")).ShouldBeNull();
    }

    [Theory]
    [InlineData("non-Latin: no author", "The Vegetarian", "Han Kang", "채식주의자", "title=채식주의자&limit=20")]
    [InlineData("Cyrillic, NFC not folded", "Anna Karenina", "Leo Tolstoy", "Анна Каренина", "title=анна каренина&limit=20")]
    [InlineData("Latin-script title gets the author", "The Little Prince", "Antoine de Saint-Exupéry", "Le Petit Prince", "title=le petit prince&author=exupery&limit=20")]
    [InlineData("accents kept (not folded), still Latin", "The Stranger", "Albert Camus", "L'Étranger", "title=étranger&author=camus&limit=20")]
    [InlineData("only the part before ':' or ';'", "Childhood", "Leo Tolstoy", "Детство; Отрочество; Юность", "title=детство&limit=20")]
    [InlineData("Japanese", "Norwegian Wood", "Haruki Murakami", "ノルウェイの森", "title=ノルウェイの森&limit=20")]
    [InlineData("Greek keeps its accents", "The Odyssey", "Homer", "Ὀδύσσεια", "title=ὀδύσσεια&limit=20")]
    public void OriginalTitle(string @case, string title, string author, string original, string sent)
    {
        _ = @case;
        var query = CatalogQueries.OriginalTitle(Hyp(title, author, original: original));
        query.ShouldNotBeNull();
        query.ToString().ShouldBe(sent);
    }

    [Fact]
    public void OriginalTitle_R3_0366_AuthorCanBeRequestedForAnyScript()
    {
        var h = Hyp("Childhood", "Maxim Gorky", original: "Детство");
        CatalogQueries.OriginalTitle(h).ShouldNotBeNull().Author.ShouldBeNull();
        CatalogQueries.OriginalTitle(h, OriginalTitleAuthor.Always)!.ToString().ShouldBe("title=детство&author=gorky&limit=20");
        CatalogQueries.OriginalTitle(Hyp("The Little Prince", "Saint-Exupéry", original: "Le Petit Prince"), OriginalTitleAuthor.Never)!
            .Author.ShouldBeNull();
    }

    [Fact]
    public void OriginalTitle_NullWhenMissingOrTheSameTitle()
    {
        CatalogQueries.OriginalTitle(Hyp("The Hobbit", "Tolkien")).ShouldBeNull();
        CatalogQueries.OriginalTitle(Hyp("The Hobbit", "Tolkien", original: "The Hobbit")).ShouldBeNull();
        CatalogQueries.OriginalTitle(Hyp("Le Petit Prince", null, original: "Le petit prince")).ShouldBeNull();
        CatalogQueries.OriginalTitle(Hyp("The Hobbit", null, original: "!!!")).ShouldBeNull();
    }

    // ---- person: and OR calls ----

    [Theory]
    [InlineData("all names required", new[] { "scout", "atticus" }, true, "q=person:(scout AND atticus)&limit=20&person")]
    [InlineData("any name (retry)", new[] { "scout", "atticus" }, false, "q=person:(scout OR atticus)&limit=20&person")]
    [InlineData("at most 3 distinct tokens", new[] { "harry", "harry", "ron", "hermione", "draco" }, true, "q=person:(harry AND ron AND hermione)&limit=20&person")]
    [InlineData("tokens under 3 letters dropped when a longer one remains", new[] { "jo", "march", "amy" }, true, "q=person:(march AND amy)&limit=20&person")]
    [InlineData("a lone short name still searches", new[] { "jo" }, true, "q=person:(jo)&limit=20&person")]
    [InlineData("digits are not names", new[] { "123", "jo" }, true, "q=person:(jo)&limit=20&person")]
    [InlineData("caller text is re-tokenized", new[] { "Bella Swan", "-edward*" }, true, "q=person:(bella AND swan AND edward)&limit=20&person")]
    [InlineData("CJK 2-character names allowed", new[] { "哈利" }, true, "q=person:(哈利)&limit=20&person")]
    public void Persons(string @case, string[] tokens, bool all, string sent)
    {
        _ = @case;
        var query = CatalogQueries.Persons(tokens, all);
        query.ToString().ShouldBe(sent);
        query.IncludePersons.ShouldBeTrue();
    }

    [Fact]
    public void Persons_NoUsableToken_Throws()
    {
        CatalogQueries.PersonTerms(["(", "***", "42"]).ShouldBeEmpty();
        Should.Throw<ArgumentException>(() => CatalogQueries.Persons(["(", "-"], true));
    }

    [Theory]
    [InlineData("typo rescue", new[] { "tolkein", "hobbit" }, "q=tolkein OR hobbit&limit=20")]
    [InlineData("at most 8", new[] { "a1", "b2", "c3", "d4", "e5", "f6", "g7", "h8", "i9", "j10" }, "q=a1 OR b2 OR c3 OR d4 OR e5 OR f6 OR g7 OR h8&limit=20")]
    [InlineData("repeated tokens once", new[] { "hobbit", "hobbit", "tolkien" }, "q=hobbit OR tolkien&limit=20")]
    [InlineData("a lone short token goes as title=", new[] { "it" }, "title=it&limit=20")]
    [InlineData("caller tokens can't inject syntax", new[] { "OR", "*", "hobbit)" }, "q=or OR hobbit&limit=20")]
    public void AnyOf(string @case, string[] tokens, string sent)
    {
        _ = @case;
        CatalogQueries.AnyOf(tokens).ToString().ShouldBe(sent);
    }

    // ---- topic requests ----

    [Theory]
    [InlineData("one subject", new[] { "bears" }, null, "q=subject:(bears)&limit=20&subject")]
    [InlineData("multi-word terms: every word required", new[] { "french revolution", "paris (france)" }, null, "q=subject:(french AND revolution AND paris AND france)&limit=20&subject")]
    [InlineData("stop words dropped", new[] { "the history of science" }, null, "q=subject:(history AND science)&limit=20&subject")]
    [InlineData("author hint", new[] { "cats" }, "Doris Lessing", "q=subject:(cats)&author=doris lessing&limit=20&subject")]
    [InlineData("at most 3 terms", new[] { "a1", "b2", "c3", "d4" }, null, "q=subject:(a1 AND b2 AND c3)&limit=20&subject")]
    [InlineData("injection is just words", new[] { "bears) OR title:(twilight", "*" }, null, "q=subject:(bears AND title AND twilight)&limit=20&subject")]
    [InlineData("non-Latin subject", new[] { "ours", "Медведи" }, null, "q=subject:(ours AND медведи)&limit=20&subject")]
    public void Topic(string @case, string[] terms, string? author, string sent)
    {
        _ = @case;
        var query = CatalogQueries.Topic(terms, author);
        query.ShouldNotBeNull();
        query.ToString().ShouldBe(sent);
        query.IncludeSubjects.ShouldBeTrue();
        query.Sort.ShouldBe(CatalogSort.Relevance);
    }

    [Theory]
    [InlineData("one term", new[] { "bears" }, "q=subject:(bears)&limit=20&subject")]
    [InlineData("any term, words within a term required", new[] { "french revolution", "napoleon" }, "q=subject:(french AND revolution) OR subject:(napoleon)&limit=20&subject")]
    [InlineData("repeated terms once", new[] { "bears", "Bears" }, "q=subject:(bears)&limit=20&subject")]
    [InlineData("a single phrase falls back to any of its words", new[] { "french revolution" }, "q=subject:(french) OR subject:(revolution)&limit=20&subject")]
    public void TopicAny(string @case, string[] terms, string sent)
    {
        _ = @case;
        CatalogQueries.TopicAny(terms)!.ToString().ShouldBe(sent);
    }

    [Fact]
    public void TopicAny_CarriesTheAuthorHint()
    {
        CatalogQueries.TopicAny(["cats", "dogs"], "Doris Lessing")!.ToString()
            .ShouldBe("q=subject:(cats) OR subject:(dogs)&author=doris lessing&limit=20&subject");
        CatalogQueries.TopicAny(["cats"], "?!")!.Author.ShouldBeNull();
    }

    [Fact]
    public void Topic_NothingSearchable_IsNull()
    {
        CatalogQueries.Topic([], null).ShouldBeNull();
        CatalogQueries.Topic(["the", "?!", " "], "by").ShouldBeNull();
        CatalogQueries.TopicAny(["of", "*"]).ShouldBeNull();
        CatalogQueries.Topic(["bears"], "!!!")!.Author.ShouldBeNull();
    }

    [Fact]
    public void Suggestions_OneCallForUpToFiveTitles()
    {
        var query = CatalogQueries.Suggestions(
        [
            new("Winnie-the-Pooh", "A. A. Milne"), new("A Bear Called Paddington", "Michael Bond"), new("Corduroy", null),
            new("Winnie-the-Pooh", "Milne"), new("We're Going on a Bear Hunt", "Michael Rosen"), new("Little Bear", null),
            new("The Bear (Dune Chronicles, #1): extra", null),
        ]);
        query.ShouldNotBeNull();
        query.ToString().ShouldBe("q=title:(winnie pooh) OR title:(bear called paddington) OR title:(corduroy) OR title:(were going bear hunt) "
            + "OR title:(little bear)&limit=20&subject");
        query.IncludeSubjects.ShouldBeTrue();
    }

    [Fact]
    public void Suggestions_TitlesAreRebuiltFromWords()
    {
        CatalogQueries.Suggestions([new("title:(x) OR *:*", null), new("-Spider-Man*", null)])!.ToString()
            .ShouldBe("q=title:(title) OR title:(spider man)&limit=20&subject");
        CatalogQueries.Suggestions([]).ShouldBeNull();
        CatalogQueries.Suggestions([new("!!!", "x"), new("  ", null)]).ShouldBeNull();
    }

    [Fact]
    public void Topic_FromTheCleaner()
    {
        var q = QueryCleaner.Clean("books about cats by doris lessing");
        CatalogQueries.Topic(q.Topic, q.TopicAuthor)!.ToString().ShouldBe("q=subject:(cats)&author=doris lessing&limit=20&subject");
        var phrases = QueryCleaner.Clean("a book about the french revolution and napoleon");
        CatalogQueries.Topic(phrases.Topic, null)!.ToString().ShouldBe("q=subject:(french AND revolution AND napoleon)&limit=20&subject");
        CatalogQueries.TopicAny(phrases.Topic)!.ToString().ShouldBe("q=subject:(french AND revolution) OR subject:(napoleon)&limit=20&subject");
    }

    // ---- author calls ----

    [Theory]
    [InlineData("OL24638A", "author_key=OL24638A&sort=editions&limit=10")]
    [InlineData("/authors/OL24638A", "author_key=OL24638A&sort=editions&limit=10")]
    public void AuthorTopWorks(string key, string sent) => CatalogQueries.AuthorTopWorks(key).ToString().ShouldBe(sent);

    [Theory]
    [InlineData("OL24638W")]
    [InlineData("OL1A) OR (x")]
    [InlineData("")]
    public void AuthorTopWorks_RejectsAnythingButAnAuthorKey(string key) =>
        Should.Throw<ArgumentException>(() => CatalogQueries.AuthorTopWorks(key));

    [Theory]
    [InlineData("Charles Dickens", "author=charles dickens&sort=editions&limit=10")]
    [InlineData("Tolkien, J. R. R. 1892-1973", "author=j r r tolkien&sort=editions&limit=10")]
    [InlineData("Gabriel García Márquez", "author=gabriel garcia marquez&sort=editions&limit=10")]
    [InlineData("村上春樹", "author=村上春樹&sort=editions&limit=10")]
    [InlineData("author:(x) OR *", "author=author x or&sort=editions&limit=10")]
    public void AuthorByName(string name, string sent) => CatalogQueries.AuthorByName(name).ToString().ShouldBe(sent);

    [Fact]
    public void Defaults()
    {
        CatalogQueries.AnyOf(["hobbit", "tolkien"]).Limit.ShouldBe(20);
        CatalogQueries.AnyOf(["hobbit", "tolkien"]).Sort.ShouldBe(CatalogSort.Relevance);
        CatalogQueries.AuthorByName("Dickens").Sort.ShouldBe(CatalogSort.Editions);
    }

    [Theory]
    [InlineData("hob*bit")]
    [InlineData("title:(the hobbit) AND author:(tolkien")]
    [InlineData("\"quoted\" ^ ~ ? \\ / + - ! { } [ ] : ( )")]
    [InlineData("-x OR y NOT z && w || v")]
    public void Sanitizer_NoBuilderOutputContainsAMetacharacter(string text)
    {
        var tokens = new[] { text };
        var q = QueryCleaner.Clean(text);
        if (!q.IsEmpty) AssertNoSyntax(CatalogQueries.Broad(q).Query.Q ?? CatalogQueries.Broad(q).Query.Title!);
        AssertNoSyntax(CatalogQueries.AnyOf(tokens).Q!.Replace(" OR ", " "));
        AssertNoSyntax(CatalogQueries.HypothesisTitle(Hyp(text, text))!.Title!);
        AssertNoSyntax(CatalogQueries.OriginalTitle(Hyp("x", text, original: text))!.Title!);
        AssertNoSyntax(CatalogQueries.AuthorByName(text).Author!);
        var person = CatalogQueries.Persons(tokens, true).Q!;
        AssertNoSyntax(person["person:(".Length..^1].Replace(" AND ", " "));
    }

    private static void AssertNoSyntax(string value)
    {
        value.IndexOfAny("()[]{}\"':^~*?\\/+-!&|".ToCharArray()).ShouldBe(-1, value);
        value.Split(' ').Intersect(["AND", "OR", "NOT"]).ShouldBeEmpty(value);
        value.ShouldNotStartWith(" ");
        value.ShouldNotContain("  ");
    }
}
