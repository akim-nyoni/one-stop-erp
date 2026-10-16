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

    /// <summary>Gets a value indicating whether the screen has a list of records (opened in its own window).</summary>
    public bool HasList => Operation?.HasList == true;

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

    /// <summary>Gets the Explorer colour of the kind of work: green setting up, red day-to-day, blue looking up, purple period end.</summary>
    public System.Windows.Media.Brush AreaBrush => NavCategory.BrushFor(Area);

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

/// <summary>A catalogue report shown under its module (opens in the Report Viewer).</summary>
/// <param name="Code">The report code.</param>
/// <param name="Name">Its name.</param>
public sealed record NavReport(string Code, string Name)
{
    /// <summary>Gets the tile icon.</summary>
    public string Glyph => "📊";
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

/// <summary>
/// One kind of work in a module — Maintenance (setting up), Transactions (day-to-day), Enquiries (looking up) or Utilities (period end and
/// housekeeping) — with the module's screens of that kind, still in their groups.
/// </summary>
public sealed class NavCategory
{
    /// <summary>Gets the kind of work.</summary>
    public required MenuArea Area { get; init; }

    /// <summary>Gets its name (Maintenance, Transactions, Enquiries, Utilities).</summary>
    public required string Title { get; init; }

    /// <summary>Gets its icon.</summary>
    public required string Glyph { get; init; }

    /// <summary>Gets what it is for, in plain words.</summary>
    public required string Purpose { get; init; }

    /// <summary>Gets its colour (the module's).</summary>
    public required string Accent { get; init; }

    /// <summary>Gets the module it is in.</summary>
    public required string ModuleTitle { get; init; }

    /// <summary>Gets the module's groups, each with only its screens of this kind.</summary>
    public required IReadOnlyList<NavGroup> Groups { get; init; }

    /// <summary>Gets every screen of this kind.</summary>
    public IReadOnlyList<NavEntry> Entries => [.. Groups.SelectMany(g => g.Entries)];

    /// <summary>Gets the Explorer colour of this kind of work.</summary>
    public System.Windows.Media.Brush AreaBrush => BrushFor(Area);

    /// <summary>The Explorer colour of a kind of work.</summary>
    /// <param name="area">The kind.</param>
    /// <returns>Green for Maintenance, red for Transactions, blue for Enquiries, purple for Utilities.</returns>
    public static System.Windows.Media.Brush BrushFor(MenuArea area) => area switch
    {
        MenuArea.Maintenance => Frozen(0x4C, 0xAF, 0x50),
        MenuArea.Transactions => Frozen(0xE0, 0x5A, 0x5A),
        MenuArea.Enquiries => Frozen(0x3C, 0x7D, 0xD9),
        _ => Frozen(0x8E, 0x6A, 0xC8),
    };

    private static System.Windows.Media.Brush Frozen(byte r, byte g, byte b)
    {
        System.Windows.Media.SolidColorBrush brush = new(System.Windows.Media.Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>Gets "n screens" for the card.</summary>
    public string CountText => Entries.Count == 1 ? "1 screen" : $"{Entries.Count} screens";

    /// <summary>Gets the first few screens' names.</summary>
    public string Preview => string.Join("  ·  ", Entries.Take(4).Select(e => e.Title)) + (Entries.Count > 4 ? "  ·  …" : string.Empty);

    /// <summary>The four kinds of work, in menu order, with their icons and plain descriptions.</summary>
    public static readonly (MenuArea Area, string Title, string Glyph, string Purpose)[] Kinds =
    [
        (MenuArea.Maintenance, "Maintenance", "⚙️", "Setting things up: records, codes, rates, rules and policies."),
        (MenuArea.Transactions, "Transactions", "📝", "The day-to-day work: documents, payments, movements and postings."),
        (MenuArea.Enquiries, "Enquiries", "🔎", "Looking things up: balances, statements, histories and positions."),
        (MenuArea.Utilities, "Utilities", "🧰", "Period-end runs, imports, approvals and housekeeping."),
    ];
}

/// <summary>A module the user may use, with the groups and screens they may use in it.</summary>
/// <param name="Title">The module's name.</param>
/// <param name="Glyph">Its icon.</param>
/// <param name="Accent">Its colour.</param>
/// <param name="Purpose">What it is for.</param>
/// <param name="Groups">Its groups.</param>
public sealed record NavModule(string Title, string Glyph, string Accent, string Purpose, IReadOnlyList<NavGroup> Groups) : System.ComponentModel.INotifyPropertyChanged
{
    private IReadOnlyList<NavCategory>? _categories;

    /// <summary>Gets the catalogue reports of this module, for the Reports menu and the module page.</summary>
    public IReadOnlyList<NavReport> Reports { get; init; } = [];

    /// <summary>Gets a value indicating whether the module has reports.</summary>
    public bool HasReports => Reports.Count > 0;
    private bool _isActive;
    private bool _isExpanded;

    /// <inheritdoc />
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets every screen in the module.</summary>
    public IEnumerable<NavEntry> Entries => Groups.SelectMany(g => g.Entries);

    /// <summary>Gets how many screens it has.</summary>
    public int Count => Groups.Sum(g => g.Entries.Count);

    /// <summary>Gets the module's screens by kind of work (only the kinds it has), built once.</summary>
    public IReadOnlyList<NavCategory> Categories => _categories ??= [.. NavCategory.Kinds.Select(k => new NavCategory
    {
        Area = k.Area, Title = k.Title, Glyph = k.Glyph, Purpose = k.Purpose, Accent = Accent, ModuleTitle = Title,
        Groups = [.. Groups.Select(g => g with { Entries = [.. g.Entries.Where(e => e.Area == k.Area)] }).Where(g => g.Entries.Count > 0)],
    }).Where(c => c.Groups.Count > 0)];

    /// <summary>Gets or sets a value indicating whether the module is the one shown (lit in the module list).</summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive != value)
            {
                _isActive = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsActive)));
            }
        }
    }

    /// <summary>Gets or sets a value indicating whether the module is open in the module list.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsExpanded)));
            }
        }
    }

    /// <summary>Whether this is the same module (by reference: its lit/open state changes, so it must not take part in equality).</summary>
    /// <param name="other">The other module.</param>
    /// <returns>True when it is the same object.</returns>
    public bool Equals(NavModule? other) => ReferenceEquals(this, other);

    /// <inheritdoc />
    public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);

    /// <summary>The category a screen is in.</summary>
    /// <param name="entry">The screen.</param>
    /// <returns>Its category, or null when it is not in this module.</returns>
    public NavCategory? CategoryOf(NavEntry entry) => Categories.FirstOrDefault(c => c.Groups.Any(g => g.Entries.Contains(entry)));
}

/// <summary>Builds the navigation the signed-in user sees from <see cref="NavigationMap"/>, the console's actions and what they may use.</summary>
public static class NavigationBuilder
{
    /// <summary>Builds the modules, leaving out what the user's licence and permissions do not cover and any module left empty.</summary>
    /// <param name="operations">Every console action.</param>
    /// <param name="available">Whether a console module (the gate) may be used; null gates are always open.</param>
    /// <param name="canOpen">Whether a screen with a feature may be shown (its licence and the user's flags on it); it replaces the module gate for such screens. Null: the module gate alone.</param>
    /// <returns>The modules.</returns>
    public static IReadOnlyList<NavModule> Build(IReadOnlyList<Operation> operations, Func<string, bool> available, Func<Operation, bool>? canOpen = null)
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
                        if (!byKey.TryGetValue((item.ConsoleModule!, title), out Operation? operation) || !placed.Add(operation)
                            || !(canOpen is not null && operation.PermissionCode is not null ? canOpen(operation) : available(operation.Module)))
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

            modules.Add(new NavModule(spec.Title, spec.Glyph, spec.Accent, spec.Purpose, groups) { Reports = ReportsOf(spec.Title) });
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

    // The catalogue's reports, by the module they belong to (the catalogue's module names mapped to the Explorer's).
    private static readonly Lazy<ILookup<string, NavReport>> Catalogue = new(() => One4All.Reporting.SystemReportCatalogue.All()
        .Where(r => r.Module is not null && NavigationMap.ReportModules.ContainsKey(r.Module))
        .ToLookup(r => NavigationMap.ReportModules[r.Module!], r => new NavReport(r.Code, r.Name)));

    private static IReadOnlyList<NavReport> ReportsOf(string module) => [.. Catalogue.Value[module].OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase)];

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
        // A full stop ends the sentence only when a space or the end follows it ("e.g." and "1.5" do not end one).
        int stop = text.IndexOfAny([';', ':']);
        for (int at = text.IndexOf('.'); at >= 0; at = text.IndexOf('.', at + 1))
        {
            if ((at + 1 == text.Length || text[at + 1] == ' ') && (stop < 0 || at < stop))
            {
                stop = at;
                break;
            }
        }

        string first = stop > 20 ? text[..stop] : text;
        return first.Length > 150 ? first[..147].TrimEnd() + "…" : first;
    }
}
