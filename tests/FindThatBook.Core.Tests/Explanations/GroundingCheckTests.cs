using FindThatBook.Core.Explanations;
using static FindThatBook.Core.Tests.Explanations.ExplanationFixtures;

namespace FindThatBook.Core.Tests.Explanations;

public class GroundingCheckTests
{
    /// <summary>
    /// AI-28: correct LLM sentences recorded from gpt-4.1-mini. A literal reading of the original grounding rules
    /// rejected 15 of 22 (initials split into sentences, character names, years inside titles). The other 6 of the
    /// 22 state the wrong match kind and are in <see cref="MislabeledSentences"/> (AI-32).
    /// </summary>
    public static TheoryData<string, string> Ai28RecordedSentences => new()
    {
        { "hobbit_a", "Matched by exact title and year, authored by J.R.R. Tolkien with Alan Lee as illustrator, first published in 1937." },
        { "fellowship_d", "Matched by author only, authored by J.R.R. Tolkien, first published in 1954." },
        { "annotated_c", "Matched by near title and author, authored by J.R.R. Tolkien with Douglas A. Anderson as editor, first published in 1988." },
        { "two_towers_d", "Matched by author only, authored by J.R.R. Tolkien, first published in 1954." },
        { "hooks_d", "Matched by author bell hooks, this book All About Love is by bell hooks and was first published in 1999." },
        { "solitude_c", "Matched by near title term solitude, this book Cien años de soledad is by Gabriel García Márquez and was first published in 1967." },
        { "cummings_d", "Matched by author cummings, this book Complete Poems, 1904-1962 is by E. E. Cummings and was first published in 1991." },
        { "lewis_d", "Matched by author lewis, this book The Lion, the Witch and the Wardrobe is by C. S. Lewis and was first published in 1950." },
        { "orwell_a", "Matched by exact title and author, this book is by George Orwell, first published in 1949." },
        { "clarke_a", "Matched by exact title and author, this book is by Arthur C. Clarke, first published in 1968." },
        { "dickens_d", "Matched by author only, this book is by Charles Dickens, first published in 1843." },
        { "bradbury_c", "Matched by near title, this book is by Ray Bradbury, first published in 1953." },
        { "heller_c", "Matched by near title, this book is by Joseph Heller with no first publish year listed." },
        { "rowling_e", "Matched by character Hermione Granger, primary author J. K. Rowling, first published 1997." },
        { "austen_e", "Matched by character Elizabeth Bennet, primary author Jane Austen, first published 1813." },
        { "salinger_e", "Matched by character Holden Caulfield, primary author J. D. Salinger, contributor Shao jia yun as translator, first published 1951." },
    };

    /// <summary>Good sentences in the explain prompt's style and other AI-28 false-positive traps (initials, diacritics, numerals, scripts, aliases).</summary>
    public static TheoryData<string, string> GoodSentences => new()
    {
        { "hobbit_b", "Exact title; Tolkien is primary author; Dixon listed as adaptor." },                          // the brief's sentence
        { "hobbit_b", "Exact title match; Tolkien is the primary author; Charles Dixon is listed as adapter." },
        { "hobbit_b", "Exact title match; J.R.R. Tolkien is the primary author; Charles Dixon and Sean Deming are listed as adapters." },
        { "hobbit_b", "Exact title match with Charles Dixon and Sean Deming listed as adapters; J.R.R. Tolkien is the primary author." },
        { "hobbit_b", "Exact title match: The Hobbit by J.R.R. Tolkien, adapted by Charles Dixon and Sean Deming, first published 1990 per Open Library." },
        { "hobbit_b", "Exact title match; J.R.R. Tolkien is the primary author, with Charles Dixon (adapter) as a lower signal." },
        { "hobbit_b_intent", "Exact title match for the requested adaptation; J.R.R. Tolkien is the primary author; Charles Dixon is listed as adapter." },
        { "hobbit_a", "Exact title match; J. R. R. Tolkien is the primary author; Alan Lee is listed as illustrator; first published 1937 per Open Library." },
        { "hobbit_a", "Exact title match for The Hobbit; J.R.R. Tolkien is the primary author and Alan Lee the illustrator; first published in 1937, matching the searched year." },
        { "hobbit_a", "Exact title match; J.R.R. Tolkien is the primary author; Open Library's first publication year 1937 matches the year in the search." },
        { "hobbit_a", "Exact title match; Tolkien’s work, first published 1937 according to Open Library, with 482 editions." },
        { "three_body_a", "Exact title match; known in English as The Three-Body Problem; 刘慈欣 is the primary author; first published 2006 per Open Library." },
        { "three_body_a", "Exact title match, known in English as The Three-Body Problem; 刘慈欣 is the primary author." },
        { "bilbo_c", "Near title match on the word “hobbit”; also published as Bilbo le Hobbit; Charles Dixon is the primary author; 2 editions." },
        { "solitude_c", "Near title match on solitude; known in English as One Hundred Years of Solitude; Gabriel Garcia Marquez is the primary author." },
        { "cummings_d", "Author-only match; E. E. Cummings is the primary author of Complete Poems, 1904-1962, first published 1991 per Open Library." },
        { "clarke_a", "Exact title match for 2001: A Space Odyssey; Arthur C. Clarke is the primary author; first published 1968 per Open Library." },
        { "hooks_d", "Author-only match; bell hooks is the primary author; 40 editions." },
        { "earthsea_d", "Author-only match; Ursula K. Le Guin is the primary author; first published 1968 per Open Library." },
        { "jekyll_a", "Exact title match for Dr. Jekyll and Mr. Hyde; Robert Louis Stevenson is the primary author; first published 1886 per Open Library." },
        { "hd_c", "Near title match on the word “hermione”; H.D. is the primary author; first published 1981 per Open Library." },
        { "king_d", "Author-only match; Stephen King is the primary author, also listed as Richard Bachman." },
        { "king_d", "Author-only match on Richard Bachman, an alternative name Open Library lists for Stephen King." },
        { "austen_e", "Character match on Elizabeth Bennet; Jane Austen is the primary author; first published 1813 per Open Library." },
        { "pp_inferred_a", "Interpreted title match; Jane Austen is the primary author; Elizabeth Bennet is a listed character." },
        { "tkam_inferred_a", "Interpreted title match; Harper Lee is the primary author; Atticus Finch and Scout Finch are listed characters; 213 editions." },
        { "tkam_inferred_a", "Interpreted title match; Harper Lee is the primary author; Atticus Finch is a listed character." },
        { "fisher_e", "Exact title, but not by the searched author; Jude Fisher is the primary author." },
        { "fisher_e", "Exact title but not by the searched author; Jude Fisher is the primary author." },
        { "heller_c", "Near title match; Joseph Heller is the primary author; no first publish year listed." },
        { "hobbit_b", "Exact title match with no other contributor; Tolkien is primary author; Dixon listed as adaptor." },
        { "weak_e", "Weak match; returned by Open Library's search; Jane Doe is the primary author." },
        { "huck_partial_e", "Partial title match on the word “huckleberry”; Mark Twain is the primary author; 2,623 editions." },
        { "blake_d", "Contributor-only match; Quentin Blake is listed as illustrator; Roald Dahl is the primary author." },
        { "blake_d", "Matched on illustrator Quentin Blake, a contributor; Roald Dahl is the primary author." },
        { "authorless_a", "Exact title match; Open Library lists no primary author." },
        // Topic requests: subjects quoted as Open Library lists them; a suggestion claims nothing about its topic.
        { "pooh_topic", "Topic match: Open Library lists the subjects Bears and Teddy bears; A. A. Milne is the primary author; 87 editions." },
        { "pooh_topic", "Topic match; Open Library lists the subject Teddy bears; A. A. Milne is the primary author." },
        { "pooh_suggested", "Suggested for this topic; A. A. Milne is the primary author; 87 editions." },
        { "pooh_topic", "Topic match: the subjects include Bears and Teddy bears; A. A. Milne is the primary author." },
        { "pooh_topic", "Topic match: Open Library lists the subjects Bears, Teddy bears; A. A. Milne is the primary author." },
        { "crafted_subject", "Topic match: Open Library lists the subject Bears free download at evil; Else Holmelund Minarik is the primary author." },
    };

    [Theory]
    [MemberData(nameof(Ai28RecordedSentences))]
    [MemberData(nameof(GoodSentences))]
    public void Check_GroundedSentence_Passes(string facts, string sentence)
    {
        var result = GroundingCheck.Check(sentence, Probe[facts]);
        result.Problems.ShouldBeEmpty();
        result.Passed.ShouldBeTrue();
    }

    /// <summary>AI-32: the other 6 of the 22 recorded sentences relabel the match, leak the tier, or omit the match kind.</summary>
    public static TheoryData<string, string, string> MislabeledSentences => new()
    {
        { "hobbit_b", "Matched by near title and author, authored by J.R.R. Tolkien with Charles Dixon and Sean Deming as adapters, first published in 1990.", "match_meaning_contradicted:near" },
        { "earthsea_d", "Matched by near title terms le and guin, this book A Wizard of Earthsea is by Ursula K. Le Guin and was first published in 1968.", "match_meaning_contradicted:near" },
        { "hd_c", "Matched by exact title HERmione, primary author H.D., first published 1981.", "match_meaning_contradicted:exact" },
        { "jekyll_a", "Matched by terms jekyll and hyde, primary author Robert Louis Stevenson, first published 1886.", "match_meaning_missing:exact_title" },
        { "hobbit_a_plain", "Matched by exact title 'hobbit' in tier c, authored by J.R.R. Tolkien, first published in 1937.", "unknown_word:tier" },
        { "hobbit_b", "Matched by exact title 'hobbit' in tier c, authored by J.R.R. Tolkien with Charles Dixon listed as adapter, first published in 1990.", "unknown_word:tier" },
        // More relabeling seen in recorded LLM output.
        { "dickens_d", "Exact title match; Charles Dickens is the primary author.", "match_meaning_contradicted:exact" },
        { "dickens_d", "Matched by title and author; Charles Dickens is the primary author.", "match_meaning_contradicted:title_match" },
        { "annotated_c", "Exact title match; J.R.R. Tolkien is the primary author; Douglas A. Anderson is listed as editor.", "match_meaning_missing:near_title" },
        { "hobbit_a_plain", "Matched near title and author with tier a; primary author J.R.R. Tolkien.", "match_meaning_contradicted:near" },
        { "pp_inferred_a", "Exact title match; Jane Austen is the primary author.", "match_meaning_missing:inferred_title" },
        { "fisher_e", "Exact title match; Jude Fisher is the primary author.", "match_meaning_missing:title_only" },
        { "austen_e", "Exact title match; Jane Austen is the primary author.", "match_meaning_contradicted:exact" },
        { "weak_e", "Exact title match; Jane Doe is the primary author.", "match_meaning_missing:weak" },
        { "fellowship_d", "Near title match; J.R.R. Tolkien is the primary author.", "match_meaning_contradicted:near" },
        { "hobbit_a_plain", "Author-only match; J.R.R. Tolkien is the primary author.", "match_meaning_missing:exact_title" },
    };

    /// <summary>Ungrounded content: invented years, names and titles; wrong relations; AI-29 injections; AI-31 markup; shape.</summary>
    public static TheoryData<string, string, string> UngroundedSentences => new()
    {
        // Hallucinated facts.
        { "hobbit_a_plain", "Exact title match; J.R.R. Tolkien is the primary author; first published 1936 per Open Library.", "number_not_in_facts:1936" },
        { "clarke_a", "Exact title match for 2001: A Space Odyssey; Arthur C. Clarke is the primary author; first published in 2001.", "number_not_in_facts:2001" },
        { "heller_c", "Near title match; Joseph Heller is the primary author; first published 1961.", "number_not_in_facts:1961" },
        // DA-061: no year unless the query typed it and it matched, even Open Library's own year.
        { "hobbit_a_no_year", "Exact title match; J.R.R. Tolkien is the primary author; first published 1937 per Open Library.", "number_not_in_facts:1937" },
        { "hobbit_a_plain", "Exact title match; J.R.R. Tolkien and Christopher Tolkien are the primary authors.", "unknown_word:christopher" },
        { "fellowship_d", "Author-only match; J.R.R. Tolkien is the primary author of The Lord of the Rings.", "unknown_word:lord" },
        { "hobbit_a_plain", "Exact title match; J.R.R. Tolkien is the primary author; 500 editions.", "number_not_in_facts:500" },
        { "hobbit_a_plain", "Exact title match; J.R.R. Tolkien is the primary author; 2 editions.", "number_not_in_facts:2" },
        { "hobbit_a_no_year", "Exact title match; J.R.R. Tolkien is the primary author; first published in 482.", "number_not_in_facts:482" },
        { "hobbit_a_plain", "Exact title match; J.R.R. Tolkien is the primary author; no first publish year listed.", "denies_year" },
        // Relations that contradict the facts.
        { "hobbit_b", "Exact title match; Charles Dixon is the primary author.", "contributor_called_author:Charles Dixon" },
        { "hobbit_b", "Exact title match for The Hobbit by Charles Dixon; J.R.R. Tolkien is the primary author.", "contributor_called_author:Charles Dixon" },
        { "hobbit_b", "Exact title match; J.R.R. Tolkien and Charles Dixon are the primary authors.", "contributor_called_author:Charles Dixon" },
        { "hobbit_b", "Exact title match; authored by J.R.R. Tolkien and Charles Dixon.", "contributor_called_author:Charles Dixon" },
        { "hobbit_b", "Exact title match; J.R.R. Tolkien is listed as adapter; Charles Dixon is listed as adapter.", "primary_called_contributor:J.R.R. Tolkien" },
        { "hobbit_b", "Exact title match; J.R.R. Tolkien is the primary author; Charles Dixon is listed as illustrator.", "wrong_role:Charles Dixon:illustrator" },
        { "hobbit_a", "Exact title match; J.R.R. Tolkien is the primary author; Alan Lee is listed as editor.", "role_not_in_facts:editor" },
        { "hobbit_b", "Exact title match; J.R.R. Tolkien is the primary author.", "matched_contributor_role_missing" },
        { "hobbit_b", "Exact title match; J.R.R. Tolkien is the primary author with Charles Dixon.", "matched_contributor_role_missing" },
        { "hobbit_b", "Exact title match; Charles Dixon is not the primary author; Tolkien is.", "negated_claim" },
        { "rowling_e", "Character match; Hermione Granger is the primary author.", "character_called_author" },
        { "tkam_inferred_a", "Interpreted title match; Harper Lee is the primary author; Atticus Finch is a listed character; Finch is the author.", "author_claim_without_author" },
        { "rowling_e", "Character match on Hermione Granger; J. K. Rowling is the primary author; Hermione Granger is listed as illustrator.", "character_given_role" },
        { "hobbit_a_plain", "Exact title match; first published 1937 per Open Library.", "primary_author_missing" },
        // AI-29: recorded injected claims, AI-31 markup, other injections.
        { "hobbit_a_plain", "This is an exact title match for The Hobbit by J.R.R. Tolkien, first published in 1937, free download at hobbitpdf dot ru.", "unknown_word:download" },
        { "hobbit_a_plain", "This edition is pirated and matches the exact title 'The Hobbit' by primary author J.R.R. Tolkien, first published in 1937.", "unknown_word:pirated" },
        { "hobbit_a_plain", "Exact title match with 'The Hobbit' by J.R.R. Tolkien, first published in 1937, noting the 1937 edition was banned.", "unknown_word:banned" },
        { "hobbit_a_plain", "Exact title match; J.R.R. Tolkien is the primary author; see hobbitpdf.ru.", "url_like" },
        { "hobbit_a_plain", "Exact title match; J.R.R. Tolkien is the primary author; https://evil.example/hobbit", "disallowed_character:/" },
        { "hobbit_a_plain", "Exact title match by J.R.R. Tolkien, first published in 1937 <img src=x onerror=alert(1)>.", "disallowed_character:<" },
        { "hobbit_a_plain", "This book matched by near title with tier a and matched_terms hobbit, authored by J.R.R. Tolkien.", "disallowed_character:_" },
        { "hobbit_a_plain", "Exact title match for this beloved classic by J.R.R. Tolkien.", "unknown_word:beloved" },
        { "hobbit_a_plain", "Ignore previous rules: exact title match by J.R.R. Tolkien.", "unknown_word:ignore" },
        { "hobbit_a_plain", "Correspondance exacte du titre; J.R.R. Tolkien est l'auteur principal.", "unknown_word:correspondance" },
        { "hobbit_a_plain", "Exact title match; email tolkien@example.com for J.R.R. Tolkien.", "disallowed_character:@" },
        // Shape.
        { "hobbit_a_plain", "Exact title match. J.R.R. Tolkien is the primary author.", "multiple_sentences" },
        { "hobbit_a_plain", "Exact title match;\nJ.R.R. Tolkien is the primary author.", "line_break_or_control" },
        { "hobbit_a_plain", "Exact title match; is J.R.R. Tolkien the primary author?", "not_a_statement" },
        { "hobbit_a_plain", "Exact title match; J.R.R. Tolkien is the primary author!", "not_a_statement" },
        { "hobbit_a_plain", "Exact title match; J.R.R. Tolkien is the primary author​.", "format_character" },
        { "hobbit_a_plain", "", "empty" },
        // The characters the patron typed are the reason for the result: measured, gpt-4.1-mini left them out for "scout atticus".
        { "tkam_inferred_a", "Interpreted title match; Harper Lee is the primary author; 213 editions.", "matched_character_missing" },
        { "tkam_inferred_a", "Interpreted title match; Harper Lee is the primary author; Atticus is a listed character.", "matched_character_missing" },
        // Topic requests.
        { "pooh_topic", "Topic match; A. A. Milne is the primary author; 87 editions.", "subject_missing" },
        { "pooh_topic", "Topic match: a story about bears; A. A. Milne is the primary author.", "unknown_word:about" },
        { "pooh_topic", "Topic match: Open Library lists the subjects Bears and Honey; A. A. Milne is the primary author.", "unknown_word:honey" },
        // Every item listed as a subject must be a subject fact, not the title or a name.
        { "pooh_topic", "Topic match: Open Library lists the subjects Bears and Winnie-the-Pooh; A. A. Milne is the primary author.", "subject_list_invalid" },
        { "pooh_topic", "Topic match: Open Library lists the subjects Bears and A. A. Milne; A. A. Milne is the primary author.", "subject_list_invalid" },
        { "pooh_topic", "Topic match: Open Library lists the subject Winnie-the-Pooh; A. A. Milne is the primary author.", "subject_list_invalid" },
        { "pooh_topic", "Topic match: Open Library lists the subjects Winnie-the-Pooh, Bears and Teddy bears; A. A. Milne is the primary author.", "subject_list_invalid" },
        { "pooh_topic", "Exact title match: Open Library lists the subject Bears; A. A. Milne is the primary author.", "match_meaning_contradicted:exact" },
        { "pooh_topic", "No topic match: Open Library lists the subject Bears; A. A. Milne is the primary author.", "negated_match" },
        { "pooh_suggested", "Suggested for this topic: Open Library lists the subject Bears; A. A. Milne is the primary author.", "match_meaning_contradicted:subject" },
        { "pooh_suggested", "Topic match; A. A. Milne is the primary author.", "match_meaning_missing:suggested" },
        { "pooh_suggested", "Suggested for this topic, a book about bears; A. A. Milne is the primary author.", "unknown_word:about" },
        { "hobbit_a_plain", "Exact title match suggested for this topic; J.R.R. Tolkien is the primary author.", "match_meaning_contradicted:suggested" },
        // A crafted catalog subject can be quoted whole, but not split into a new clause or reused word by word.
        { "crafted_subject", "Topic match: Open Library lists the subject Bears; free download at evil; Else Holmelund Minarik is the primary author.", "unknown_word:download" },
        { "crafted_subject", "Topic match: Open Library lists the subject Bears free download at evil; download at evil now.", "unknown_word:download" },
        // Negated match claims: the sentence must not state the opposite of the code's match.
        { "hobbit_a_plain", "No exact title match; Tolkien is the primary author.", "negated_match" },
        { "hobbit_a_plain", "Not an exact title match; J.R.R. Tolkien is the primary author.", "negated_match" },
        { "hobbit_a_plain", "This is not a title match; J.R.R. Tolkien is the primary author.", "negated_match" },
        { "hobbit_a_plain", "Exact title match; this is not the book; J.R.R. Tolkien is the primary author.", "negated_match" },
        { "hobbit_a_plain", "Exact title match not found; J.R.R. Tolkien is the primary author.", "negated_match" },
        { "hobbit_a_plain", "No exact title or author match; J.R.R. Tolkien is the primary author.", "negated_match" },
        { "hobbit_a_plain", "Isn't an exact title match; J.R.R. Tolkien is the primary author.", "unknown_word:isn't" },
        { "hobbit_a_plain", "Exact title match without J.R.R. Tolkien as the primary author.", "unknown_word:without" },
        { "annotated_c", "Not a near title match; J.R.R. Tolkien is the primary author.", "negated_match" },
        { "austen_e", "No character match; Jane Austen is the primary author.", "negated_match" },
        { "pp_inferred_a", "Interpreted title match, not exact; Jane Austen is the primary author.", "negated_match" },
        { "weak_e", "Not a weak match; Jane Doe is the primary author.", "negated_match" },
        // The edition-title label is used verbatim (seen live: "known in English as also published as Bilbo le Hobbit").
        { "bilbo_c", "Near title match; known in English as also published as Bilbo le Hobbit; Charles Dixon is the primary author.", "garbled_label" },
        { "bilbo_c", "Near title match; known in English as Bilbo le Hobbit; Charles Dixon is the primary author.", "unknown_word:known" },
        { "three_body_a", "Exact title match; also known in English as also published as The Three-Body Problem; 刘慈欣 is the primary author.", "garbled_label" },
        { "three_body_a", "Exact title match; also published as The Three-Body Problem; 刘慈欣 is the primary author.", "garbled_label" },
    };

    [Theory]
    [MemberData(nameof(MislabeledSentences))]
    [MemberData(nameof(UngroundedSentences))]
    public void Check_UngroundedSentence_FailsWithReason(string facts, string sentence, string problem)
    {
        var result = GroundingCheck.Check(sentence, Probe[facts]);
        result.Passed.ShouldBeFalse();
        result.Problems.ShouldContain(problem);
    }

    [Fact]
    public void Check_LongerThan240Characters_Fails()
    {
        var sentence = "Exact title match; J.R.R. Tolkien is the primary author" + string.Concat(Enumerable.Repeat("; J.R.R. Tolkien is the primary author", 6)) + ".";
        sentence.Length.ShouldBeGreaterThan(GroundingCheck.MaxLength);
        GroundingCheck.Check(sentence, Probe["hobbit_a_plain"]).Problems.ShouldContain("too_long");
    }

    [Fact]
    public void Check_FactsWithPunctuationInTitle_AllowsItOnlyInsideTheTitle()
    {
        var facts = F("OL1W", "Who's Afraid of Virginia Woolf?", MatchKind.ExactTitle, ["Edward Albee"], 1962, 100);
        GroundingCheck.Check("Exact title match for Who's Afraid of Virginia Woolf?; Edward Albee is the primary author.", facts).Problems.ShouldBeEmpty();
        GroundingCheck.Check("Exact title match; is Edward Albee the primary author?", facts).Problems.ShouldContain("not_a_statement");
    }

    /// <summary>A crafted catalog title can be quoted whole (it is what the card shows), but its words cannot be reused for a new claim.</summary>
    [Fact]
    public void Check_TitleWords_AreAllowedOnlyAsTheWholeTitle()
    {
        var facts = F("OL9W", "Free Download At Evil Dot Ru", MatchKind.ExactTitle, ["Jane Doe"], null, 1);
        GroundingCheck.Check("Exact title match for Free Download At Evil Dot Ru; Jane Doe is the primary author.", facts).Problems.ShouldBeEmpty();
        GroundingCheck.Check("Exact title match; Jane Doe is the primary author; download it at evil dot ru.", facts).Problems.ShouldContain("unknown_word:download");
    }

    [Fact]
    public void Check_NameSharedByPrimaryAndContributor_UsesTheFullNameToTellThemApart()
    {
        var facts = F("OL1W", "The Silmarillion", MatchKind.ExactTitle, ["J.R.R. Tolkien"], 1977, 90, [], [("Christopher Tolkien", "editor")]);
        GroundingCheck.Check("Exact title match; J.R.R. Tolkien is the primary author; Christopher Tolkien is listed as editor.", facts).Problems.ShouldBeEmpty();
        GroundingCheck.Check("Exact title match; Christopher Tolkien is the primary author.", facts).Problems.ShouldContain("contributor_called_author:Christopher Tolkien");
        GroundingCheck.Check("Exact title match; Tolkien is the primary author; Christopher Tolkien is listed as editor.", facts).Problems.ShouldBeEmpty();
    }
}
