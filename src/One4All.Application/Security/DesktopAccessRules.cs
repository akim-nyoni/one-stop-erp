using One4All.Application.Modules;
using One4All.Core.Security;

namespace One4All.Application.Security;

/// <summary>
/// What a desktop console action needs (brief §5.1, enforced for the desktop as for the API): the modules whose menu carries the console
/// module, a licence for one of them, and a permission in one of them - any to look, one that changes data to run an action. Pure.
/// </summary>
public static class DesktopAccessRules
{
    /// <summary>The built-in modules whose menu carries a console module.</summary>
    /// <param name="consoleModule">The console module (e.g. "Fixed Assets").</param>
    /// <returns>The modules (empty when no module claims it: any signed-in user).</returns>
    public static IReadOnlyList<IModuleDescriptor> ModulesFor(string consoleModule)
        => [.. BuiltInModules.All.Where(m => m.MenuEntries.Contains(consoleModule, StringComparer.OrdinalIgnoreCase))];

    /// <summary>Why a user may not use a console module, or null when they may.</summary>
    /// <param name="permissions">The user's effective permissions.</param>
    /// <param name="consoleModule">The console module.</param>
    /// <param name="isWrite">Whether the action changes data (running it) rather than reading (its list, a printed document).</param>
    /// <param name="isLicensed">Whether a licence module is usable.</param>
    /// <returns>The refusal, or null.</returns>
    public static string? Refusal(EffectivePermissions permissions, string consoleModule, bool isWrite, Func<string, bool> isLicensed)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(isLicensed);
        IReadOnlyList<IModuleDescriptor> modules = ModulesFor(consoleModule);
        if (modules.Count == 0)
        {
            return null;
        }

        if (!modules.Any(m => isLicensed(m.LicenceModule)))
        {
            return $"{consoleModule} is not licensed.";
        }

        return ApiAccessRules.Allows(permissions, new ApiRequirement([.. modules.Select(m => m.Code)], isWrite))
            ? null
            : isWrite ? $"You have no permission to change data in {consoleModule}." : $"You have no permission to use {consoleModule}.";
    }
}
