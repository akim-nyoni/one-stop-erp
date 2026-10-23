using System.Text;

namespace One4All.Application.Reporting;

/// <summary>
/// The text checks on a user-authored report query (brief §13.1): the statement is tokenised (comments and string literals dropped,
/// quoted identifiers read as identifiers) so that a reference to a protected schema cannot hide behind quoting, a comment or a literal;
/// it must be a single SELECT (or WITH ... SELECT); and it must bind the company with the exact fragment <c>company_id = @CompanyId</c>
/// (either way round, the column qualified or not) outside any literal. The checks are the first guard only: the statement then runs as
/// the read-only reporting role, which has no rights on the protected schemas at all, and its rows are cut to the company again when
/// they carry a <c>company_id</c> column.
/// </summary>
public static class SqlReportStatement
{
    /// <summary>Schemas a report query may never name: security, payroll, licensing, audit, system, offline sync and the catalogues.</summary>
    public static readonly IReadOnlySet<string> BlockedSchemas = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "sec", "pay", "lic", "audit", "sys", "sync", "public", "pg_catalog", "information_schema", "pg_temp", "pg_toast",
    };

    /// <summary>Functions and commands a report query may never call: file and link access, COPY, and the settings calls that could drop the reporting role.</summary>
    public static readonly IReadOnlySet<string> BlockedFunctions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "dblink", "dblink_connect", "dblink_exec", "lo_import", "lo_export", "lo_get", "lo_put", "copy", "set_config", "current_setting",
        "query_to_xml", "query_to_xmlschema", "query_to_xml_and_xmlschema", "cursor_to_xml", "cursor_to_xmlschema",
    };

    /// <summary>The name of the company column the query must bind, and that the rows are cut by.</summary>
    public const string CompanyColumn = "company_id";

    /// <summary>The company parameter the query must bind.</summary>
    public const string CompanyParameter = "@CompanyId";

    /// <summary>What a token is.</summary>
    public enum TokenKind
    {
        /// <summary>An unquoted word: a keyword, identifier or function name.</summary>
        Word,

        /// <summary>A quoted identifier, with the quotes removed.</summary>
        QuotedIdentifier,

        /// <summary>A named parameter, with its leading @.</summary>
        Parameter,

        /// <summary>A number.</summary>
        Number,

        /// <summary>A string literal (its text is dropped).</summary>
        Literal,

        /// <summary>An operator or punctuation character sequence.</summary>
        Symbol,
    }

    /// <summary>One token of the statement.</summary>
    /// <param name="Kind">What it is.</param>
    /// <param name="Text">Its text (empty for a literal).</param>
    public readonly record struct Token(TokenKind Kind, string Text)
    {
        /// <summary>Whether the token names this identifier (unquoted words compare without case; quoted ones exactly, and also without case so a quoted spelling cannot slip past a check).</summary>
        /// <param name="name">The name.</param>
        /// <returns>True when it does.</returns>
        public bool Is(string name) => Kind is TokenKind.Word or TokenKind.QuotedIdentifier && string.Equals(Text, name, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Splits a statement into tokens: comments gone, string literals (plain, E'...' and dollar-quoted) reduced to a marker, quoted identifiers read as identifiers.</summary>
    /// <param name="sql">The statement.</param>
    /// <returns>The tokens.</returns>
    public static IReadOnlyList<Token> Tokenise(string sql)
    {
        ArgumentNullException.ThrowIfNull(sql);
        List<Token> tokens = [];
        int i = 0;
        while (i < sql.Length)
        {
            char c = sql[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            // Comments: -- to the end of the line, /* ... */ nested as PostgreSQL nests them.
            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                while (i < sql.Length && sql[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                int depth = 1;
                i += 2;
                while (i < sql.Length && depth > 0)
                {
                    if (sql[i] == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
                    {
                        depth++;
                        i += 2;
                    }
                    else if (sql[i] == '*' && i + 1 < sql.Length && sql[i + 1] == '/')
                    {
                        depth--;
                        i += 2;
                    }
                    else
                    {
                        i++;
                    }
                }

                continue;
            }

            // String literals: 'text' with '' inside, E'text' with backslash escapes, $tag$text$tag$.
            bool escaped = (c == 'E' || c == 'e') && i + 1 < sql.Length && sql[i + 1] == '\'';
            if (c == '\'' || escaped)
            {
                i += escaped ? 2 : 1;
                while (i < sql.Length)
                {
                    if (escaped && sql[i] == '\\')
                    {
                        i += 2;
                        continue;
                    }

                    if (sql[i] == '\'')
                    {
                        if (i + 1 < sql.Length && sql[i + 1] == '\'')
                        {
                            i += 2;
                            continue;
                        }

                        i++;
                        break;
                    }

                    i++;
                }

                tokens.Add(new Token(TokenKind.Literal, string.Empty));
                continue;
            }

            if (c == '$' && DollarTag(sql, i) is string tag)
            {
                int close = sql.IndexOf(tag, i + tag.Length, StringComparison.Ordinal);
                i = close < 0 ? sql.Length : close + tag.Length;
                tokens.Add(new Token(TokenKind.Literal, string.Empty));
                continue;
            }

            // Quoted identifiers: "name" with "" inside.
            if (c == '"')
            {
                StringBuilder name = new();
                i++;
                while (i < sql.Length)
                {
                    if (sql[i] == '"')
                    {
                        if (i + 1 < sql.Length && sql[i + 1] == '"')
                        {
                            name.Append('"');
                            i += 2;
                            continue;
                        }

                        i++;
                        break;
                    }

                    name.Append(sql[i]);
                    i++;
                }

                tokens.Add(new Token(TokenKind.QuotedIdentifier, name.ToString()));
                continue;
            }

            if (c == '@' && i + 1 < sql.Length && (char.IsLetter(sql[i + 1]) || sql[i + 1] == '_'))
            {
                int start = i++;
                while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] == '_'))
                {
                    i++;
                }

                tokens.Add(new Token(TokenKind.Parameter, sql[start..i]));
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] == '_' || sql[i] == '$'))
                {
                    i++;
                }

                tokens.Add(new Token(TokenKind.Word, sql[start..i]));
                continue;
            }

            if (char.IsDigit(c))
            {
                int start = i;
                while (i < sql.Length && (char.IsLetterOrDigit(sql[i]) || sql[i] == '.' || sql[i] == '_'))
                {
                    i++;
                }

                tokens.Add(new Token(TokenKind.Number, sql[start..i]));
                continue;
            }

            // Everything else is a symbol, one character at a time (so "=" and "." are found whatever surrounds them).
            tokens.Add(new Token(TokenKind.Symbol, c.ToString()));
            i++;
        }

        return tokens;
    }

    /// <summary>Checks a statement, saying why it is refused or null when it may run.</summary>
    /// <param name="sql">The statement.</param>
    /// <returns>The refusal, or null.</returns>
    public static string? Refusal(string sql)
    {
        IReadOnlyList<Token> tokens = Tokenise(sql ?? string.Empty);
        if (tokens.Count == 0 || tokens[0].Kind != TokenKind.Word || !(tokens[0].Is("select") || tokens[0].Is("with")))
        {
            return "Only a SELECT (or WITH ... SELECT) query may be used.";
        }

        if (tokens.Any(t => t.Kind == TokenKind.Symbol && t.Text == ";"))
        {
            return "Only one statement may be used.";
        }

        for (int i = 0; i < tokens.Count; i++)
        {
            Token token = tokens[i];
            if (token.Kind is not (TokenKind.Word or TokenKind.QuotedIdentifier))
            {
                continue;
            }

            bool qualifies = i + 1 < tokens.Count && tokens[i + 1].Kind == TokenKind.Symbol && tokens[i + 1].Text == ".";
            if (qualifies && BlockedSchemas.Contains(token.Text))
            {
                return $"The query may not read {token.Text.ToLowerInvariant()}.";
            }

            if (token.Text.StartsWith("pg_", StringComparison.OrdinalIgnoreCase) || BlockedFunctions.Contains(token.Text))
            {
                return $"The query may not use {token.Text.ToLowerInvariant()}.";
            }
        }

        return BindsCompany(tokens) ? null : $"The query must filter by the company with the exact condition {CompanyColumn} = {CompanyParameter} so it only reads this company's data.";
    }

    /// <summary>Whether the tokens hold <c>company_id = @CompanyId</c> (or <c>@CompanyId = company_id</c>), the column qualified or not, outside any literal.</summary>
    /// <param name="tokens">The tokens.</param>
    /// <returns>True when they do.</returns>
    public static bool BindsCompany(IReadOnlyList<Token> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        for (int i = 0; i + 2 < tokens.Count; i++)
        {
            if (tokens[i + 1].Kind != TokenKind.Symbol || tokens[i + 1].Text != "=")
            {
                continue;
            }

            bool columnFirst = tokens[i].Is(CompanyColumn) && IsCompanyParameter(tokens[i + 2]);
            bool parameterFirst = IsCompanyParameter(tokens[i]) && tokens[i + 2].Is(CompanyColumn);
            if (columnFirst || parameterFirst)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCompanyParameter(Token token) => token.Kind == TokenKind.Parameter && string.Equals(token.Text, CompanyParameter, StringComparison.OrdinalIgnoreCase);

    // The $tag$ opening a dollar-quoted string at this position, or null when the $ is something else (a positional parameter, say).
    private static string? DollarTag(string sql, int at)
    {
        int end = at + 1;
        while (end < sql.Length && (char.IsLetterOrDigit(sql[end]) || sql[end] == '_'))
        {
            end++;
        }

        if (end >= sql.Length || sql[end] != '$' || (end > at + 1 && char.IsDigit(sql[at + 1])))
        {
            return null;
        }

        return sql[at..(end + 1)];
    }
}
