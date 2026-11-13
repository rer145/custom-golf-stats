using CustomGolfStats.Migration.Cli;

namespace CustomGolfStats.Migration.Tests.Cli;

public sealed class SecretRedactorTests
{
    [Fact]
    public void Masks_the_connection_strings_their_passwords_and_any_password_setting()
    {
        const string sql = "Server=localhost,1433;Database=golftrack;User Id=sa;Password=Canary-Sql-1;TrustServerCertificate=True";
        const string pg = "Host=localhost;Database=cgs;Username=postgres;Pwd='Canary;Pg-2'";
        var redactor = new SecretRedactor([sql, pg, null, " "]);

        var text = redactor.Redact($"a {sql} b Canary-Sql-1 c Canary;Pg-2 d password = Other-Secret; e PWD=x");

        Assert.Equal("a *** b *** c *** d password = ***; e PWD=***", text);
    }

    [Fact]
    public void Describes_inner_exceptions_without_stack_traces()
    {
        var exception = new InvalidOperationException("outer", new TimeoutException("inner"));

        Assert.Equal("InvalidOperationException: outer ---> TimeoutException: inner", SecretRedactor.Describe(exception));
    }
}
