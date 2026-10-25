namespace FindThatBook.Core.Explanations;

/// <summary>Who each authorship or role claim in a sentence is about, and whether the facts agree (AI-36).</summary>
public static partial class GroundingCheck
{
    // ---------------------------------------------------------------- relations between people and claims

    private enum ItemKind { Mention, Claim, By, Negation, Linker, Boundary, Other }

    private enum ClaimKind { Author, Role, Generic }

    private sealed record Item(ItemKind Kind, string Word, HashSet<int>? Persons = null, bool IsCharacter = false, ClaimKind Claim = ClaimKind.Author, string? Role = null);

    private sealed class Relations
    {
        public bool PrimaryMentioned { get; private set; }
        public bool CharacterMentioned { get; private set; }
        public HashSet<int> RoleConfirmed { get; } = [];

        public static Relations Analyze(List<Token> tokens, FactModel model, List<string> problems)
        {
            var result = new Relations();
            var items = Items(tokens, model);
            foreach (var m in items.Where(i => i.Kind == ItemKind.Mention && i.Persons is not null))
                if (m.Persons!.Any(p => model.Persons[p].IsPrimary)) result.PrimaryMentioned = true;
            result.CharacterMentioned = items.Any(i => i is { Kind: ItemKind.Mention, IsCharacter: true });

            var segments = new List<(List<Item> Items, string Before, string After)>();
            var current = new List<Item>();
            var before = "";
            foreach (var item in items)
            {
                if (item.Kind != ItemKind.Boundary) { current.Add(item); continue; }
                segments.Add((current, before, item.Word));
                before = item.Word;
                current = [];
            }
            segments.Add((current, before, ""));

            // Claims stated inside each segment, then inherited by name-only neighbours ("Dixon and Deming are listed as adapters").
            var claims = segments.Select(s => ResolveClaims(s.Items, model, result, problems)).ToList();
            var inherited = claims.Select(c => c.Count > 0 ? c.Select(x => x.Claim).ToList() : []).ToList();
            for (var k = segments.Count - 1; k >= 0; k--)
            {
                if (!IsNameOnly(segments[k].Items)) continue;
                if (k + 1 < segments.Count && segments[k].After is "and" or "," && StartsWithMention(segments[k + 1].Items) && inherited[k + 1].Count > 0)
                    inherited[k] = inherited[k + 1];
            }
            for (var k = 0; k < segments.Count; k++)
            {
                if (!IsNameOnly(segments[k].Items)) continue;
                if (inherited[k].Count == 0 && k > 0 && segments[k].Before == "and" && segments[k - 1].Items.LastOrDefault()?.Kind == ItemKind.Mention && inherited[k - 1].Count > 0)
                    inherited[k] = inherited[k - 1];
                foreach (var mention in segments[k].Items.Where(i => i.Kind == ItemKind.Mention))
                    foreach (var claim in inherited[k])
                        Apply(claim, mention, model, result, problems);
            }

            foreach (var segment in segments)
                if (segment.Items.Any(i => i.Kind == ItemKind.Negation) && segment.Items.Any(i => i.Kind == ItemKind.Mention))
                    problems.Add("negated_claim");
            if (model.Facts.PrimaryAuthors.Count > 0)
                foreach (var segment in segments)
                    if (segment.Items.Any(i => i is { Kind: ItemKind.Claim, Claim: ClaimKind.Author })
                        && segment.Items.Any(i => i.Word is "is" or "was")
                        && !segment.Items.Any(i => i.Kind == ItemKind.Mention && i.Persons is not null))
                        problems.Add("author_claim_without_author");
            return result;
        }

        private static bool IsNameOnly(List<Item> items) =>
            items.Any(i => i.Kind == ItemKind.Mention) && items.All(i => i.Kind is ItemKind.Mention or ItemKind.Linker);

        private static bool StartsWithMention(List<Item> items) =>
            items.SkipWhile(i => i.Kind == ItemKind.Linker).FirstOrDefault()?.Kind == ItemKind.Mention;

        private static List<Item> Items(List<Token> tokens, FactModel model)
        {
            var items = new List<Item>();
            for (var k = 0; k < tokens.Count; k++)
            {
                var t = tokens[k];
                switch (t.Kind)
                {
                    case TokenKind.Boundary:
                        items.Add(new Item(ItemKind.Boundary, t.Folded));
                        continue;
                    case TokenKind.Period:
                        // The dot of an initial ("J. K. Rowling") is part of the name, not a boundary.
                        if (k > 0 && tokens[k - 1] is { Kind: TokenKind.Word, Folded.Length: 1 }) continue;
                        items.Add(new Item(ItemKind.Boundary, "."));
                        continue;
                    case TokenKind.Number:
                        items.Add(new Item(ItemKind.Other, t.Folded));
                        continue;
                    case TokenKind.Fact:
                        var fact = model.Texts[t.Fact];
                        items.Add(fact.Kind switch
                        {
                            FactKind.Person => new Item(ItemKind.Mention, t.Folded, Persons: [fact.Person]),
                            FactKind.Character => new Item(ItemKind.Mention, t.Folded, IsCharacter: true),
                            _ => new Item(ItemKind.Other, "title"),
                        });
                        continue;
                }

                var word = StripPossessive(t.Folded);
                var next = k + 1 < tokens.Count && tokens[k + 1].Kind == TokenKind.Word ? tokens[k + 1].Folded : "";
                if (word.Length == 1 && (t.Capitalized || word != "a")) continue;             // an initial
                var persons = PersonsNamed(word, t.Capitalized, model);
                if (persons.Count > 0) { items.Add(new Item(ItemKind.Mention, word, Persons: persons)); continue; }
                if (BoundaryWords.Contains(word)) { items.Add(new Item(ItemKind.Boundary, word)); continue; }
                if (Negations.Contains(word)) { items.Add(new Item(ItemKind.Negation, word)); continue; }
                if (word == "by") { items.Add(new Item(ItemKind.By, word)); continue; }
                if (AuthorWords.Contains(word) && !(word == "author" && next == "only")) { items.Add(new Item(ItemKind.Claim, word, Claim: ClaimKind.Author)); continue; }
                if (WriterWords.Contains(word))
                {
                    var afterRole = items.Count > 0 && items[^1] is { Kind: ItemKind.Claim, Claim: ClaimKind.Role };
                    items.Add(afterRole ? new Item(ItemKind.Other, word) : new Item(ItemKind.Claim, word, Claim: ClaimKind.Author));
                    continue;
                }
                if (RoleLexicon.CategoryOfWord(word) is { } category && !model.TitleWords.Contains(word))
                {
                    items.Add(new Item(ItemKind.Claim, word, Claim: category == RoleLexicon.Generic ? ClaimKind.Generic : ClaimKind.Role, Role: category));
                    continue;
                }
                items.Add(new Item(Linkers.Contains(word) ? ItemKind.Linker : ItemKind.Other, word));
            }

            // "Charles" + "Dixon" → one mention of Charles Dixon; an ambiguous surname narrows to the shared person.
            var merged = new List<Item>();
            foreach (var item in items)
            {
                if (item is { Kind: ItemKind.Mention, Persons: not null } && merged.Count > 0 && merged[^1] is { Kind: ItemKind.Mention, Persons: { } prev }
                    && prev.Overlaps(item.Persons))
                {
                    merged[^1] = merged[^1] with { Persons = [.. prev.Intersect(item.Persons)] };
                    continue;
                }
                merged.Add(item);
            }
            return merged;
        }

        private static HashSet<int> PersonsNamed(string word, bool capitalized, FactModel model)
        {
            var found = new HashSet<int>();
            if (word.Length < 2 || NameParticles.Contains(word) || (ExplanationWords.Contains(word) && !capitalized)) return found;
            for (var p = 0; p < model.Persons.Count; p++)
                if (model.Persons[p].NameTokens.Contains(word)) found.Add(p);
            return found;
        }

        private sealed record Resolved(Item Claim);

        /// <summary>Attaches each claim word in a segment to the person it is about, and checks it.</summary>
        private static List<Resolved> ResolveClaims(List<Item> items, FactModel model, Relations result, List<string> problems)
        {
            var resolved = new List<Resolved>();
            var consumed = new HashSet<int>();
            for (var k = 0; k < items.Count; k++)
            {
                if (items[k].Kind != ItemKind.By) continue;
                var prev = k > 0 ? items[k - 1] : null;
                if (prev is not null && MatchVerbs.Contains(prev.Word)) continue;             // "matched by author …"
                var target = items.Skip(k + 1).SkipWhile(i => i.Kind == ItemKind.Linker).FirstOrDefault();
                Item claim;
                if (prev is { Kind: ItemKind.Claim }) { claim = prev; consumed.Add(k - 1); }  // "adapted by", "written by"
                else claim = new Item(ItemKind.Claim, "by", Claim: ClaimKind.Author);       // "a book by X" is authorship
                resolved.Add(new Resolved(claim));
                if (target?.Kind == ItemKind.Mention) Apply(claim, target, model, result, problems);
            }

            for (var k = 0; k < items.Count; k++)
            {
                if (items[k].Kind != ItemKind.Claim || consumed.Contains(k)) continue;
                resolved.Add(new Resolved(items[k]));
                if (Target(items, k) is { } target) Apply(items[k], target, model, result, problems);
            }
            return resolved;
        }

        /// <summary>"Dixon is listed as adapter" (left), "adapter Charles Dixon" (right), else the single nearest name.</summary>
        private static Item? Target(List<Item> items, int k)
        {
            for (var j = k - 1; j >= 0; j--)
            {
                if (items[j].Kind == ItemKind.Mention) return items[j];
                if (items[j].Kind is not (ItemKind.Linker or ItemKind.Claim)) break;
            }
            for (var j = k + 1; j < items.Count; j++)
            {
                if (items[j].Kind == ItemKind.Mention) return items[j];
                if (items[j].Kind is not (ItemKind.Linker or ItemKind.Claim)) break;
            }
            var nearest = items.Select((item, index) => (item, distance: Math.Abs(index - k)))
                .Where(x => x.item.Kind == ItemKind.Mention)
                .OrderBy(x => x.distance)
                .Take(2)
                .ToList();
            return nearest.Count == 1 || (nearest.Count == 2 && nearest[0].distance < nearest[1].distance) ? nearest[0].item : null;
        }

        private static void Apply(Item claim, Item mention, FactModel model, Relations result, List<string> problems)
        {
            if (mention.IsCharacter)
            {
                problems.Add(claim.Claim == ClaimKind.Author ? "character_called_author" : "character_given_role");
                return;
            }
            var persons = mention.Persons!.Select(p => (Index: p, Person: model.Persons[p])).ToList();
            var name = persons[0].Person.Name;
            switch (claim.Claim)
            {
                case ClaimKind.Author:
                    if (!persons.Any(p => p.Person.IsPrimary)) problems.Add($"contributor_called_author:{name}");
                    break;
                case ClaimKind.Generic:
                    var any = persons.Where(p => p.Person.HasRole).ToList();
                    if (any.Count == 0) problems.Add($"primary_called_contributor:{name}");
                    foreach (var p in any) result.RoleConfirmed.Add(p.Index);
                    break;
                case ClaimKind.Role:
                    var right = persons.Where(p => p.Person.Roles.Contains(claim.Role!)).ToList();
                    if (right.Count > 0) { foreach (var p in right) result.RoleConfirmed.Add(p.Index); }
                    else if (persons.All(p => !p.Person.HasRole)) problems.Add($"primary_called_contributor:{name}");
                    else problems.Add($"wrong_role:{name}:{claim.Role}");
                    break;
            }
        }
    }
}
