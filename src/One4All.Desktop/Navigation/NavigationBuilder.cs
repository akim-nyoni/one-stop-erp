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

    /// <summary>Gets the words a search looks through.</summary>
    public string SearchText => $"{Title} {Purpose} {ModuleTitle} {GroupTitle} {Operation?.Title}";
}

/// <summary>A group of screens in a module.</summary>
/// <param name="Title">The group's name.</param>
/// <param name="Entries">Its screens.</param>
public sealed record NavGroup(string Title, IReadOnlyList<NavEntry> Entries);

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
                List<NavEntry> entries = [];
                foreach (NavItemSpec item in group.Items)
                {
                    if (item.Operation is string title)
                    {
                        if (!byKey.TryGetValue((item.ConsoleModule!, title), out Operation? operation) || !placed.Add(operation) || !available(operation.Module))
                        {
                            continue;
                        }

                        entries.Add(Entry(operation, item.Title, spec.Title, group.Title));
                    }
                    else if (item.ConsoleModule is null || available(item.ConsoleModule))
                    {
                        entries.Add(new NavEntry
                        {
                            Title = item.Title!, Purpose = item.Purpose ?? string.Empty, Area = item.Area ?? MenuArea.Transactions, WindowKey = item.Window,
                            ModuleTitle = spec.Title, GroupTitle = group.Title,
                        });
                    }
                }

                if (entries.Count > 0)
                {
                    groups.Add(new NavGroup(group.Title, entries));
                }
            }

            modules.Add(new NavModule(spec.Title, spec.Glyph, spec.Accent, spec.Purpose, groups));
        }

        // Anything the map does not name goes to the "More" group of the module that takes its console module, so no action is ever unreachable.
        foreach (IGrouping<string, Operation> stray in operations.Where(o => !placed.Contains(o) && available(o.Module)).GroupBy(o => o.Module))
        {
            int index = Math.Max(0, NavigationMap.Modules.ToList().FindIndex(m => m.Takes.Contains(stray.Key)));
            NavModule home = modules[index];
            List<NavEntry> more = [.. stray.Select(o => Entry(o, null, home.Title, "More"))];
            NavGroup? existing = home.Groups.FirstOrDefault(g => g.Title == "More");
            List<NavGroup> groups = [.. home.Groups.Where(g => g != existing), new NavGroup("More", [.. existing?.Entries ?? [], .. more])];
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

    private static NavEntry Entry(Operation operation, string? title, string module, string group) => new()
    {
        Title = title ?? operation.Title, Purpose = FirstSentence(operation.Description), Area = operation.MenuArea, Operation = operation, ModuleTitle = module, GroupTitle = group,
    };

    // The description's first sentence, kept short for a card.
    private static string FirstSentence(string text)
    {
        int stop = text.IndexOfAny(['.', ';', ':']);
        string first = stop > 20 ? text[..stop] : text;
        return first.Length > 150 ? first[..147].TrimEnd() + "…" : first;
    }
}
