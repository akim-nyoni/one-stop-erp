using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using One4All.Desktop.Navigation;
using One4All.Desktop.Operations;
using One4All.Reporting;
using One4All.Reporting.Model;

// One4All.DocGen: writes the documentation that is derived from the system itself (brief §12 deliverables) so it never drifts:
//   docs/manual/<module>.md     the user manual, one per module as the Explorer shows it (screens, what each does, its fields, its reports)
//   docs/demos/<module>.md      a demo script per module: the screens to walk through in order, with what to enter and what to expect
//   docs/acceptance/ACCEPTANCE-PACK.md   the acceptance test pack: every automated scenario by area, as a checklist with its test
//   docs/TRAINING-DATA.md       how to load the training data set and what it contains
// Run from the repository root after a build:  dotnet run --project tools/One4All.DocGen -- .
string root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
if (!Directory.Exists(Path.Combine(root, "docs")) || !Directory.Exists(Path.Combine(root, "tests")))
{
    Console.Error.WriteLine($"{root} is not the One4All repository root (no docs/ and tests/).");
    return 1;
}

IReadOnlyList<Operation> operations = OperationCatalogue.All();
Dictionary<string, Operation> byKey = operations.GroupBy(o => $"{o.Module}: {o.Title}", StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
List<ReportDefinition> reports = [.. SystemReportCatalogue.All()];
int written = 0;

// ---- user manual per module
string manualDir = Path.Combine(root, "docs", "manual");
Directory.CreateDirectory(manualDir);
StringBuilder index = new();
index.AppendLine("# One4All user manual");
index.AppendLine();
index.AppendLine("One chapter per module, as the Explorer shows them. Generated from the screens and reports themselves by `tools/One4All.DocGen`; regenerate after adding a screen or a report.");
index.AppendLine();
index.AppendLine("| Module | What it covers | Screens | Reports |");
index.AppendLine("| --- | --- | ---: | ---: |");
foreach (NavModuleSpec module in NavigationMap.Modules)
{
    string slug = Slug(module.Title);
    StringBuilder text = new();
    text.AppendLine(CultureInfo.InvariantCulture, $"# {module.Title}");
    text.AppendLine();
    text.AppendLine(module.Purpose);
    text.AppendLine();
    if (module.Takes.Count > 0)
    {
        text.AppendLine(CultureInfo.InvariantCulture, $"Works with: {string.Join(", ", module.Takes)}.");
        text.AppendLine();
    }

    text.AppendLine("Open the module from the Explorer on the left; each group below is a heading in its menu. Every form has OK and Cancel; every list has add, change and delete where the record allows it. The Reports entry of each module lists the reports named at the end of this chapter.");
    int screens = 0;
    foreach (NavGroupSpec group in module.Groups)
    {
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"## {group.Title}");
        foreach (NavItemSpec item in group.Items)
        {
            screens++;
            if (item.Operation is string operationTitle && item.ConsoleModule is string consoleModule && byKey.TryGetValue($"{consoleModule}: {operationTitle}", out Operation? operation))
            {
                text.AppendLine();
                text.AppendLine(CultureInfo.InvariantCulture, $"### {item.Title ?? operation.Title}");
                text.AppendLine();
                text.AppendLine(operation.Description);
                if (operation.Fields.Count > 0)
                {
                    text.AppendLine();
                    text.AppendLine("| Field | Required | Notes |");
                    text.AppendLine("| --- | --- | --- |");
                    foreach (OperationField field in operation.Fields)
                    {
                        string notes = field.Choices is { Count: > 0 } choices ? "One of: " + string.Join(", ", choices) : field.Kind.ToString();
                        if (field.Default is string def && def.Length > 0 && field.Kind != FieldKind.Date)
                        {
                            notes += $"; default {def}";
                        }

                        text.AppendLine(CultureInfo.InvariantCulture, $"| {field.Label} | {(field.Required ? "yes" : "no")} | {notes} |");
                    }
                }

                if (operation.ListTitle is string listTitle)
                {
                    text.AppendLine();
                    text.AppendLine(CultureInfo.InvariantCulture, $"The list beside the form shows: {listTitle}.");
                }
            }
            else
            {
                text.AppendLine();
                text.AppendLine(CultureInfo.InvariantCulture, $"### {item.Title ?? item.Operation ?? item.Window}");
                text.AppendLine();
                text.AppendLine(item.Purpose ?? (item.Window is not null ? "A window of its own; see the screen's help." : "See the screen's description."));
            }
        }
    }

    List<ReportDefinition> own = [.. reports.Where(r => NavigationMap.ReportModules.TryGetValue(r.Module ?? string.Empty, out string? nav) && nav == module.Title).OrderBy(r => r.Code, StringComparer.Ordinal)];
    if (own.Count > 0)
    {
        text.AppendLine();
        text.AppendLine("## Reports");
        text.AppendLine();
        text.AppendLine("| Code | Report | What it shows |");
        text.AppendLine("| --- | --- | --- |");
        foreach (ReportDefinition report in own)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"| {report.Code} | {report.Name} | {report.Description} |");
        }
    }

    File.WriteAllText(Path.Combine(manualDir, slug + ".md"), text.ToString().Replace("\r\n", "\n", StringComparison.Ordinal), new UTF8Encoding(false));
    written++;
    index.AppendLine(CultureInfo.InvariantCulture, $"| [{module.Title}]({slug}.md) | {module.Purpose} | {screens} | {own.Count} |");
}

File.WriteAllText(Path.Combine(manualDir, "README.md"), index.ToString().Replace("\r\n", "\n", StringComparison.Ordinal), new UTF8Encoding(false));
written++;

// ---- demo scripts per module
string demoDir = Path.Combine(root, "docs", "demos");
Directory.CreateDirectory(demoDir);
StringBuilder demoIndex = new();
demoIndex.AppendLine("# Demo scripts");
demoIndex.AppendLine();
demoIndex.AppendLine("One walkthrough per module. Before any of them: start the desktop, sign in as `admin`, and on a fresh company run Administration › Set up a company for an industry, then Load sample data for testing (see ../TRAINING-DATA.md). Generated by `tools/One4All.DocGen`.");
demoIndex.AppendLine();
foreach (NavModuleSpec module in NavigationMap.Modules)
{
    string slug = Slug(module.Title);
    StringBuilder text = new();
    text.AppendLine(CultureInfo.InvariantCulture, $"# Demo: {module.Title}");
    text.AppendLine();
    text.AppendLine(CultureInfo.InvariantCulture, $"Goal: show that {module.Purpose.TrimEnd('.').ToLowerInvariant()} work end to end. Allow 15 to 20 minutes. Sample data loaded; signed in as admin.");
    int step = 0;
    foreach (NavGroupSpec group in module.Groups)
    {
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"## {group.Title}");
        foreach (NavItemSpec item in group.Items.Take(3))
        {
            step++;
            if (item.Operation is string operationTitle && item.ConsoleModule is string consoleModule && byKey.TryGetValue($"{consoleModule}: {operationTitle}", out Operation? operation))
            {
                string required = string.Join(", ", operation.Fields.Where(f => f.Required).Select(f => f.Label));
                text.AppendLine(step.ToString(CultureInfo.InvariantCulture) + ". Open **" + (item.Title ?? operation.Title) + "**. " + FirstSentence(operation.Description)
                    + (required.Length == 0 ? string.Empty : " Fill in: " + required + ".") + " Press OK and point out the result line and the list on the right.");
            }
            else
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"{step}. Open **{item.Title ?? item.Window}**. {item.Purpose ?? "Walk through what it shows."}");
            }
        }
    }

    List<ReportDefinition> own = [.. reports.Where(r => NavigationMap.ReportModules.TryGetValue(r.Module ?? string.Empty, out string? nav) && nav == module.Title).OrderBy(r => r.Code, StringComparer.Ordinal).Take(3)];
    if (own.Count > 0)
    {
        text.AppendLine();
        text.AppendLine("## Reports to end on");
        foreach (ReportDefinition report in own)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"- {report.Code} {report.Name}: {FirstSentence(report.Description)} Show the drill-through by clicking a document or account.");
        }
    }

    File.WriteAllText(Path.Combine(demoDir, slug + ".md"), text.ToString().Replace("\r\n", "\n", StringComparison.Ordinal), new UTF8Encoding(false));
    written++;
    demoIndex.AppendLine(CultureInfo.InvariantCulture, $"- [{module.Title}]({slug}.md)");
}

File.WriteAllText(Path.Combine(demoDir, "README.md"), demoIndex.ToString().Replace("\r\n", "\n", StringComparison.Ordinal), new UTF8Encoding(false));
written++;

// ---- acceptance test pack from the automated scenarios
string acceptanceDir = Path.Combine(root, "docs", "acceptance");
Directory.CreateDirectory(acceptanceDir);
StringBuilder pack = new();
pack.AppendLine("# Acceptance test pack");
pack.AppendLine();
pack.AppendLine("Every automated scenario, as a checklist by area: tick a scenario when it passes on the acceptance environment (`dotnet test One4All.slnx` runs them all; the accounting scenarios need a PostgreSQL server). Generated by `tools/One4All.DocGen` from the test sources.");
Regex summary = new(@"///\s*<summary>(?<text>.*?)///\s*</summary>\s*(\[[^\]]*\]\s*)*public\s+(sealed\s+)?class\s+(?<name>\w+)", RegexOptions.Singleline | RegexOptions.Compiled);
Regex test = new(@"\[(SkippableFact|Fact|Theory|SkippableTheory)[^\]]*\]\s*(\[[^\]]*\]\s*)*public\s+(async\s+)?(Task|void)\s+(?<name>\w+)\s*\(", RegexOptions.Compiled);
int scenarios = 0;
foreach (string project in Directory.GetDirectories(Path.Combine(root, "tests")).OrderBy(p => p, StringComparer.Ordinal))
{
    List<(string Area, string Class, string Summary, List<string> Tests)> classes = [];
    foreach (string file in Directory.GetFiles(project, "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")).OrderBy(f => f, StringComparer.Ordinal))
    {
        string source = File.ReadAllText(file);
        List<string> names = [.. test.Matches(source).Select(m => m.Groups["name"].Value)];
        if (names.Count == 0)
        {
            continue;
        }

        Match found = summary.Match(source);
        string text = found.Success ? Regex.Replace(found.Groups["text"].Value, @"\s*///\s*", " ").Replace("<c>", "`").Replace("</c>", "`").Trim() : string.Empty;
        string area = Path.GetRelativePath(project, Path.GetDirectoryName(file)!);
        classes.Add((area == "." ? "General" : area.Replace('\\', '/'), Path.GetFileNameWithoutExtension(file), text, names));
    }

    pack.AppendLine();
    pack.AppendLine(CultureInfo.InvariantCulture, $"## {Path.GetFileName(project)} ({classes.Sum(c => c.Tests.Count)} scenarios)");
    foreach (IGrouping<string, (string Area, string Class, string Summary, List<string> Tests)> area in classes.GroupBy(c => c.Area).OrderBy(g => g.Key, StringComparer.Ordinal))
    {
        pack.AppendLine();
        pack.AppendLine(CultureInfo.InvariantCulture, $"### {area.Key}");
        foreach ((_, string cls, string text, List<string> tests) in area)
        {
            pack.AppendLine();
            pack.AppendLine("**" + Words(cls.EndsWith("Tests", StringComparison.Ordinal) ? cls[..^5] : cls) + "**" + (text.Length == 0 ? string.Empty : " - " + text));
            foreach (string name in tests)
            {
                scenarios++;
                pack.AppendLine(CultureInfo.InvariantCulture, $"- [ ] {Words(name)} (`{cls}.{name}`)");
            }
        }
    }
}

string packText = pack.ToString();
packText = packText.Insert(packText.IndexOf('\n', packText.IndexOf("Generated by", StringComparison.Ordinal)) + 1, "\n" + scenarios.ToString(CultureInfo.InvariantCulture) + " scenarios in all.\n");
pack.Clear().Append(packText);
File.WriteAllText(Path.Combine(acceptanceDir, "ACCEPTANCE-PACK.md"), pack.ToString().Replace("\r\n", "\n", StringComparison.Ordinal), new UTF8Encoding(false));
written++;

// ---- training data set
Operation? sample = operations.FirstOrDefault(o => o.Title == "Load sample data for testing");
Operation? industry = operations.FirstOrDefault(o => o.Title == "Set up a company for an industry");
StringBuilder training = new();
training.AppendLine("# Training data set");
training.AppendLine();
training.AppendLine("A training or demonstration company is built in two steps from the Administration module (both screens are also in the user manual); nothing is typed by hand. Generated by `tools/One4All.DocGen`.");
training.AppendLine();
training.AppendLine("## 1. Set up a company for an industry");
training.AppendLine();
training.AppendLine(industry?.Description ?? "Creates the company with the industry's chart of accounts, tax codes, pay elements, tariffs and document layouts.");
training.AppendLine();
training.AppendLine("Industry templates shipped (chart of accounts in `docs/coa/`): " + string.Join(", ", Directory.GetFiles(Path.Combine(root, "docs", "coa"), "*.csv").Select(f => Path.GetFileNameWithoutExtension(f)).OrderBy(n => n, StringComparer.Ordinal)) + ".");
training.AppendLine();
training.AppendLine("## 2. Load sample data for testing");
training.AppendLine();
training.AppendLine(sample?.Description ?? "Fills the company with realistic sample records across the modules so every screen has something to show.");
training.AppendLine();
training.AppendLine("Every sample record is marked as sample data; the load is refused on a company that already holds sample records, so a training company is reset by creating a new company and loading again. Opening balances for a real site go through Administration › Import (see the import templates there).");
File.WriteAllText(Path.Combine(root, "docs", "TRAINING-DATA.md"), training.ToString().Replace("\r\n", "\n", StringComparison.Ordinal), new UTF8Encoding(false));
written++;

Console.WriteLine($"Wrote {written} document(s): {NavigationMap.Modules.Count} manual chapters, {NavigationMap.Modules.Count} demo scripts, the acceptance pack ({scenarios} scenarios) and the training data note.");
return 0;

static string Slug(string title) => Regex.Replace(title.ToLowerInvariant().Replace("&", "and", StringComparison.Ordinal), "[^a-z0-9]+", "-").Trim('-');

static string FirstSentence(string text)
{
    int end = text.IndexOf(". ", StringComparison.Ordinal);
    return end < 0 ? text : text[..(end + 1)];
}

static string Words(string identifier)
{
    string spaced = identifier.Replace('_', ' ');
    spaced = Regex.Replace(spaced, "(?<=[a-z0-9])(?=[A-Z])", " ");
    return spaced.Length == 0 ? spaced : char.ToUpperInvariant(spaced[0]) + spaced[1..];
}
