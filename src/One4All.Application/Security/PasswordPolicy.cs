namespace One4All.Application.Security;

/// <summary>The password rules (brief §5.2): at least eight characters with letters and digits, and not containing the user name.</summary>
public static class PasswordPolicy
{
    /// <summary>The shortest password allowed.</summary>
    public const int MinimumLength = 8;

    /// <summary>Why a password is not acceptable, or null when it is.</summary>
    /// <param name="password">The password.</param>
    /// <param name="username">The user it is for.</param>
    /// <returns>The reason, or null.</returns>
    public static string? Problem(string? password, string username)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinimumLength)
        {
            return $"A password needs at least {MinimumLength} characters.";
        }

        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
        {
            return "A password needs both letters and digits.";
        }

        return !string.IsNullOrWhiteSpace(username) && password.Contains(username.Trim(), StringComparison.OrdinalIgnoreCase)
            ? "A password may not contain the user name."
            : null;
    }
}
