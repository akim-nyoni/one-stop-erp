using One4All.Application.Navigation;
using One4All.Desktop.Operations;

namespace One4All.Desktop.Navigation;

/// <summary>One screen in the navigation: an action (opened in its own window) or a dedicated window.</summary>
public sealed class NavEntry
{
    /// <summary>Gets the name shown.</summary>
    public required string Title { get; init; }

    /// <summary>Gets one line on what it is for.</summary>
    public required string Purpose { get; init; }

    /// <summary>Gets where it sits in the Maintenance / Transactions / Enquiries / Utilities menus.</summary>
    public required MenuArea Area { get; init; }

    /// <summary>Gets the action, or null for a window.</summary>
    public Operation? Operation { get; init; }

    /// <summary>Gets the window's key, or null for an action.</summary>
    public string? WindowKey { get; init; }

    /// <summary>Gets the module it is under.</summary>
    public required string ModuleTitle { get; init; }

    /// <summary>Gets the group it is under.</summary>
    public required string GroupTitle { get; init; }

    /// <summary>Gets the screen's icon.</summary>
    public string Glyph { get; init; } = "📄";

    /// <summary>Gets its colour (the module's).</summary>
    public string Accent { get; init; } = "#2E7D32";

    /// <summary>Gets the kind of work in plain words (setting up, day-to-day, looking up, period end).</summary>
    public string AreaLabel => Area switch
    {
        MenuArea.Maintenance => "Setting up",
        MenuArea.Enquiries => "Look up",
        MenuArea.Utilities => "Period end / housekeeping",
        _ => "Day-to-day",
    };

    /// <summary>Gets the words a search looks through.</summary>
    public string SearchText => $"{Title} {Purpose} {ModuleTitle} {GroupTitle} {Operation?.Title}";
}

/// <summary>A group of screens in a module.</summary>
/// <param name="Title">The group's name.</param>
/// <param name="Entries">Its screens.</param>
public sealed record NavGroup(string Title, IReadOnlyList<NavEntry> Entries)
{
    /// <summary>Gets the group's icon.</summary>
    public string Glyph { get; init; } = "📁";

    /// <summary>Gets its colour (the module's).</summary>
    public string Accent { get; init; } = "#2E7D32";

    /// <summary>Gets how many screens it has.</summary>
    public int Count => Entries.Count;

    /// <summary>Gets "n screens" for the card.</summary>
    public string CountText => Entries.Count == 1 ? "1 screen" : $"{Entries.Count} screens";

    /// <summary>Gets the first few screens' names, as a hint of what is inside.</summary>
    public string Preview => string.Join("  ·  ", Entries.Take(3).Select(e => e.Title)) + (Entries.Count > 3 ? "  ·  …" : string.Empty);
}

/// <summary>A module the user may use, with the groups and screens they may use in it.</summary>
/// <param name="Title">The module's name.</param>
/// <param name="Glyph">Its icon.</param>
/// <param name="Accent">Its colour.</param>
/// <param name="Purpose">What it is for.</param>
/// <param name="Groups">Its groups.</param>
public sealed record NavModule(string Title, string Glyph, string Accent, string Purpose, IReadOnlyList<NavGroup> Groups)
{
    /// <summary>Gets every screen in the module.</summary>
    public IEnumerable<NavEntry> Entries => Groups.SelectMany(g => g.Entries);

    /// <summary>Gets how many screens it has.</summary>
    public int Count => Groups.Sum(g => g.Entries.Count);
}

/// <summary>Builds the navigation the signed-in user sees from <see cref="NavigationMap"/>, the console's actions and what they may use.</summary>
public static class NavigationBuilder
{
    /// <summary>Builds the modules, leaving out what the user's licence and permissions do not cover and any module left empty.</summary>
    /// <param name="operations">Every console action.</param>
    /// <param name="available">Whether a console module (the gate) may be used; null gates are always open.</param>
    /// <returns>The modules.</returns>
    public static IReadOnlyList<NavModule> Build(IReadOnlyList<Operation> operations, Func<string, bool> available)
    {
        Dictionary<(string Module, string Title), Operation> byKey = [];
        foreach (Operation operation in operations)
        {
            byKey.TryAdd((operation.Module, operation.Title), operation);
        }

        HashSet<Operation> placed = [];
        List<NavModule> modules = [];
        foreach (NavModuleSpec spec in NavigationMap.Modules)
        {
            List<NavGroup> groups = [];
            foreach (NavGroupSpec group in spec.Groups)
            {
                string groupGlyph = GroupGlyph(group.Title, spec.Glyph);
                List<NavEntry> entries = [];
                foreach (NavItemSpec item in group.Items)
                {
                    if (item.Operation is string title)
                    {
                        if (!byKey.TryGetValue((item.ConsoleModule!, title), out Operation? operation) || !placed.Add(operation) || !available(operation.Module))
                        {
                            continue;
                        }

                        entries.Add(Entry(operation, item.Title, spec, group.Title, groupGlyph));
                    }
                    else if (item.ConsoleModule is null || available(item.ConsoleModule))
                    {
                        entries.Add(new NavEntry
                        {
                            Title = item.Title!, Purpose = item.Purpose ?? string.Empty, Area = item.Area ?? MenuArea.Transactions, WindowKey = item.Window,
                            ModuleTitle = spec.Title, GroupTitle = group.Title, Glyph = EntryGlyph(item.Title!, groupGlyph), Accent = spec.Accent,
                        });
                    }
                }

                if (entries.Count > 0)
                {
                    groups.Add(new NavGroup(group.Title, entries) { Glyph = groupGlyph, Accent = spec.Accent });
                }
            }

            modules.Add(new NavModule(spec.Title, spec.Glyph, spec.Accent, spec.Purpose, groups));
        }

        // Anything the map does not name goes to the "More" group of the module that takes its console module, so no action is ever unreachable.
        foreach (IGrouping<string, Operation> stray in operations.Where(o => !placed.Contains(o) && available(o.Module)).GroupBy(o => o.Module))
        {
            int index = Math.Max(0, NavigationMap.Modules.ToList().FindIndex(m => m.Takes.Contains(stray.Key)));
            NavModule home = modules[index];
            NavModuleSpec homeSpec = NavigationMap.Modules[index];
            List<NavEntry> more = [.. stray.Select(o => Entry(o, null, homeSpec, "More", home.Glyph))];
            NavGroup? existing = home.Groups.FirstOrDefault(g => g.Title == "More");
            List<NavGroup> groups = [.. home.Groups.Where(g => g != existing), new NavGroup("More", [.. existing?.Entries ?? [], .. more]) { Glyph = "➕", Accent = home.Accent }];
            modules[index] = home with { Groups = groups };
        }

        return [.. modules.Where(m => m.Groups.Count > 0)];
    }

    /// <summary>The actions the map names that do not exist (a renamed action), for checking the map.</summary>
    /// <param name="operations">Every console action.</param>
    /// <returns>Each missing "module: title".</returns>
    public static IReadOnlyList<string> Unresolved(IReadOnlyList<Operation> operations)
        => [.. NavigationMap.Modules.SelectMany(m => m.Groups).SelectMany(g => g.Items).Where(i => i.Operation is not null)
            .Where(i => !operations.Any(o => o.Module == i.ConsoleModule && o.Title == i.Operation)).Select(i => $"{i.ConsoleModule}: {i.Operation}")];

    private static NavEntry Entry(Operation operation, string? title, NavModuleSpec module, string group, string groupGlyph) => new()
    {
        Title = title ?? operation.Title, Purpose = FirstSentence(operation.Description), Area = operation.MenuArea, Operation = operation, ModuleTitle = module.Title,
        GroupTitle = group, Glyph = EntryGlyph(title ?? operation.Title, groupGlyph), Accent = module.Accent,
    };

    // An icon for a group from the words in its name, so the cards are easy to tell apart; otherwise the module's own icon.
    private static readonly (string Word, string Glyph)[] GroupGlyphs =
    [
        ("report", "📊"), ("enquir", "🔎"), ("look", "🔎"), ("setup", "⚙️"), ("set up", "⚙️"), ("setting", "⚙️"), ("rule", "⚙️"), ("polic", "📜"),
        ("import", "📥"), ("export", "📤"), ("period", "📅"), ("year", "📅"), ("calendar", "📅"), ("leave", "🌴"), ("attendance", "⏱️"), ("time", "⏱️"),
        ("payroll", "💵"), ("pay", "💳"), ("receipt", "🧾"), ("invoice", "🧾"), ("bill", "🧾"), ("statement", "📄"), ("document", "📄"), ("letter", "✉️"),
        ("message", "✉️"), ("customer", "👥"), ("supplier", "🚚"), ("employee", "👔"), ("people", "👥"), ("applicant", "🧑"), ("waiting", "⏳"),
        ("product", "📦"), ("stock", "🏬"), ("warehouse", "🏬"), ("count", "🔢"), ("production", "🏭"), ("price", "🏷️"), ("promotion", "🏷️"),
        ("loyalty", "⭐"), ("gift", "🎁"), ("refund", "↩️"), ("return", "↩️"), ("till", "🖥️"), ("table", "🍽️"), ("kitchen", "🍳"), ("order", "📝"),
        ("quote", "📝"), ("requisition", "📝"), ("bank", "🏦"), ("cash", "💰"), ("cheque", "💰"), ("journal", "📒"), ("ledger", "📒"), ("account", "📒"),
        ("tax", "🏛️"), ("currenc", "💱"), ("budget", "📈"), ("asset", "🏢"), ("depreciat", "📉"), ("maintenance", "🔧"), ("fault", "🔧"),
        ("land", "🗺️"), ("stand", "🗺️"), ("allocation", "🔑"), ("title", "📜"), ("ownership", "📜"), ("rental", "🏠"), ("scheme", "🏗️"),
        ("property", "🏠"), ("meter", "📟"), ("tariff", "🏷️"), ("licen", "📜"), ("clearance", "✅"), ("approv", "✅"), ("contract", "📑"),
        ("certificate", "📜"), ("weigh", "⚖️"), ("student", "🎓"), ("class", "🏫"), ("fee", "💳"), ("bursar", "🎓"), ("lead", "🎯"),
        ("opportunit", "🎯"), ("activit", "📌"), ("user", "👤"), ("security", "🔒"), ("permission", "🔒"), ("company", "🏢"), ("data", "🗄️"),
        ("schedul", "⏰"), ("audit", "🕵️"), ("design", "✏️"), ("training", "📚"), ("disciplin", "⚠️"), ("grievance", "⚠️"),
    ];

    // Words that say what a single screen is about (checked before the group words), then what it does.
    private static readonly (string Word, string Glyph)[] ScreenGlyphs =
    [
        ("standard", "💲"), ("standing order", "🔁"), ("salary", "💵"), ("overtime", "⏱️"), ("shift", "🔄"), ("roster", "🗓️"), ("appraisal", "⭐"), ("medical", "🩺"), ("increment", "📈"),
        ("establishment", "🪑"), ("contact", "📇"), ("photo", "📇"), ("expiry", "🔔"), ("alert", "🔔"), ("reminder", "🔔"), ("discount", "🏷️"),
        ("transfer", "🔁"), ("adjust", "🔧"), ("print", "🖨️"), ("email", "✉️"), ("sms", "💬"), ("calculat", "🧮"), ("interest", "🧮"),
        ("instalment", "📆"), ("deposit", "💰"), ("serial", "🔖"), ("batch", "🔖"), ("barcode", "🔖"), ("unit", "📏"), ("recipe", "📋"),
        ("reservation", "📅"), ("tip", "💁"), ("delivery", "🚚"), ("receiv", "📥"), ("despatch", "📤"), ("dispatch", "📤"), ("credit note", "↩️"),
        ("debit note", "🧾"), ("write off", "✖️"), ("write-off", "✖️"), ("valuation", "💲"), ("revaluation", "💲"), ("reconcil", "⚖️"),
        ("consolidat", "🧩"), ("close", "🔒"), ("reopen", "🔓"), ("lock", "🔒"), ("password", "🔑"), ("two-factor", "🔑"), ("role", "🛡️"),
        ("health", "🩺"), ("history", "🕘"), ("trail", "🕵️"), ("notification", "🔔"), ("desk", "🛎️"), ("touch screen", "🖥️"), ("terminal", "🖥️"),
        ("float", "💰"), ("cash-up", "🧮"), ("parked", "🅿️"), ("event", "📌"), ("dashboard", "📊"), ("analytics", "📊"), ("pipeline", "📜"),
        ("map", "🗺️"), ("survey", "📐"), ("inspection", "🔍"), ("valuation roll", "📜"), ("water", "💧"), ("electric", "⚡"), ("token", "⚡"),
        ("refuse", "🗑️"), ("cemetery", "🪦"), ("grave", "🪦"), ("hall", "🏛️"), ("vehicle", "🚗"), ("fuel", "⛽"), ("concrete", "🧱"),
        ("load", "🚛"), ("ticket", "🎫"), ("wallet", "👛"), ("sibling", "👪"), ("dependant", "👪"), ("loan", "🤝"), ("advance", "🤝"),
        ("gratuity", "🎁"), ("bonus", "🎁"), ("final pay", "👋"), ("leaves", "👋"), ("termination", "👋"), ("recruit", "🧲"), ("vacanc", "🧲"),
    ];

    // What a screen does, when nothing more specific matched.
    private static readonly (string Word, string Glyph)[] VerbGlyphs =
    [
        ("add", "➕"), ("create", "➕"), ("new", "➕"), ("register", "➕"), ("record", "📝"), ("enter", "📝"), ("capture", "📝"), ("change", "✏️"),
        ("edit", "✏️"), ("update", "✏️"), ("set", "⚙️"), ("approve", "✅"), ("reject", "✖️"), ("cancel", "✖️"), ("reverse", "↩️"), ("run", "▶️"),
        ("post", "📤"), ("send", "📤"), ("view", "🔎"), ("find", "🔎"), ("search", "🔎"), ("check", "🔍"), ("list", "📋"), ("show", "🔎"),
    ];

    private static string GroupGlyph(string title, string fallback) => Match(title, GroupGlyphs) ?? fallback;

    // A screen's icon: what it is about, else what its group is about, else what it does, else the group's icon.
    private static string EntryGlyph(string title, string groupGlyph)
    {
        string? glyph = Match(title, ScreenGlyphs) ?? Match(title, GroupGlyphs);
        if (glyph is not null)
        {
            return glyph;
        }

        string first = title.Split(' ', 2)[0];
        return Match(first, VerbGlyphs) ?? groupGlyph;
    }

    private static string? Match(string title, (string Word, string Glyph)[] words)
    {
        // A word matches only at the start of a word in the title ("tip" is not found in "multiple").
        string lower = title.ToLowerInvariant();
        foreach ((string word, string glyph) in words)
        {
            for (int at = lower.IndexOf(word, StringComparison.Ordinal); at >= 0; at = lower.IndexOf(word, at + 1, StringComparison.Ordinal))
            {
                if (at == 0 || !char.IsLetter(lower[at - 1]))
                {
                    return glyph;
                }
            }
        }

        return null;
    }

    // The description's first sentence, kept short for a card.
    private static string FirstSentence(string text)
    {
        int stop = text.IndexOfAny(['.', ';', ':']);
        string first = stop > 20 ? text[..stop] : text;
        return first.Length > 150 ? first[..147].TrimEnd() + "…" : first;
    }
}
