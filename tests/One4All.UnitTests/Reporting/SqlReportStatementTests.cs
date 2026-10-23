using FluentAssertions;
using One4All.Application.Reporting;
using Xunit;

namespace One4All.UnitTests.Reporting;

/// <summary>
/// Covers the token checks on user-authored report SQL: comments, string literals (plain, escaped and dollar-quoted) and quoted identifiers are
/// read for what they are, so a protected schema cannot hide behind quoting or a comment; only one SELECT or WITH may run; the file, link and
/// settings functions are refused; and the company must be bound with the exact condition company_id = @CompanyId.
/// </summary>
public sealed class SqlReportStatementTests
{
    [Theory]
    [InlineData("SELECT account_code FROM gl.account WHERE company_id = @CompanyId", null)]
    [InlineData("select a.account_code from gl.account a where a.company_id = @companyid", null)]
    [InlineData("WITH t AS (SELECT * FROM gl.account WHERE @CompanyId = company_id) SELECT * FROM t", null)]
    [InlineData("SELECT * FROM gl.account WHERE company_id = @CompanyId -- sec.user is only a comment", null)]
    [InlineData("SELECT 'sec.user; drop' AS note, $$pay.employee$$ AS tag, E'\\'sec.x' AS e FROM gl.account WHERE company_id = @CompanyId", null)]
    [InlineData("SELECT * FROM \"gl\".\"account\" WHERE \"company_id\" = @CompanyId", null)]
    [InlineData("SELECT * FROM gl.account", "company_id = @CompanyId")]
    [InlineData("SELECT * FROM gl.account WHERE company_id = 1 AND @CompanyId > 0", "company_id = @CompanyId")]
    [InlineData("SELECT * FROM gl.account WHERE company_id = 'company_id = @CompanyId'", "company_id = @CompanyId")]
    [InlineData("DELETE FROM gl.account WHERE company_id = @CompanyId", "SELECT")]
    [InlineData("SELECT 1 WHERE company_id = @CompanyId; DROP TABLE gl.account", "one statement")]
    [InlineData("SELECT * FROM sec.user_account WHERE company_id = @CompanyId", "sec")]
    [InlineData("SELECT * FROM \"sec\".\"user\" WHERE company_id = @CompanyId", "sec")]
    [InlineData("SELECT * FROM \"SEC\".\"user\" WHERE company_id = @CompanyId", "sec")]
    [InlineData("SELECT * FROM pay . employee WHERE company_id = @CompanyId", "pay")]
    [InlineData("SELECT * FROM /* x */ audit.change_log WHERE company_id = @CompanyId", "audit")]
    [InlineData("SELECT * FROM sys.company WHERE company_id = @CompanyId", "sys")]
    [InlineData("SELECT * FROM information_schema.tables WHERE company_id = @CompanyId", "information_schema")]
    [InlineData("SELECT pg_read_file('x') FROM gl.account WHERE company_id = @CompanyId", "pg_read_file")]
    [InlineData("SELECT set_config('role', 'postgres', true) FROM gl.account WHERE company_id = @CompanyId", "set_config")]
    [InlineData("SELECT query_to_xml('select 1', true, false, '') FROM gl.account WHERE company_id = @CompanyId", "query_to_xml")]
    [InlineData("SELECT \"pg_read_file\"('x') FROM gl.account WHERE company_id = @CompanyId", "pg_read_file")]
    [InlineData("", "SELECT")]
    public void Statements_are_checked_on_their_tokens(string sql, string? refusal)
    {
        string? actual = SqlReportStatement.Refusal(sql);
        if (refusal is null)
        {
            actual.Should().BeNull();
        }
        else
        {
            actual.Should().Contain(refusal);
        }
    }

    [Fact]
    public void Tokens_drop_comments_and_literals_and_read_quoted_identifiers()
    {
        IReadOnlyList<SqlReportStatement.Token> tokens = SqlReportStatement.Tokenise("SELECT \"Odd \"\"name\"\"\", 'it''s' /* c /* nested */ */ FROM x.\"y\" -- tail\nWHERE n = 1.5 AND p = @P");
        tokens.Select(t => t.Kind).Should().Equal(
            SqlReportStatement.TokenKind.Word, SqlReportStatement.TokenKind.QuotedIdentifier, SqlReportStatement.TokenKind.Symbol, SqlReportStatement.TokenKind.Literal,
            SqlReportStatement.TokenKind.Word, SqlReportStatement.TokenKind.Word, SqlReportStatement.TokenKind.Symbol, SqlReportStatement.TokenKind.QuotedIdentifier,
            SqlReportStatement.TokenKind.Word, SqlReportStatement.TokenKind.Word, SqlReportStatement.TokenKind.Symbol, SqlReportStatement.TokenKind.Number,
            SqlReportStatement.TokenKind.Word, SqlReportStatement.TokenKind.Word, SqlReportStatement.TokenKind.Symbol, SqlReportStatement.TokenKind.Parameter);
        tokens[1].Text.Should().Be("Odd \"name\"");
        tokens[3].Text.Should().BeEmpty();
        tokens[^1].Text.Should().Be("@P");
        SqlReportStatement.BindsCompany(tokens).Should().BeFalse();
        SqlReportStatement.BindsCompany(SqlReportStatement.Tokenise("WHERE t.company_id=@CompanyId")).Should().BeTrue();
        SqlReportStatement.BindsCompany(SqlReportStatement.Tokenise("WHERE @CompanyId = \"company_id\"")).Should().BeTrue();
        SqlReportStatement.BindsCompany(SqlReportStatement.Tokenise("WHERE company_id <> @CompanyId")).Should().BeFalse();
    }
}
