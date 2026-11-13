using CustomGolfStats.Api.Data;
using CustomGolfStats.Api.Data.Entities;
using CustomGolfStats.Api.Features.Admin;
using CustomGolfStats.Api.Features.Auth;
using CustomGolfStats.Api.Features.Courses;
using CustomGolfStats.Api.Features.Me;
using CustomGolfStats.Api.Features.Rounds;
using CustomGolfStats.Api.Features.Stats;
using CustomGolfStats.Api.Infrastructure;
using CustomGolfStats.Domain;
using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

// Composition root only: features register themselves through their Add*/Map* extensions.
var builder = WebApplication.CreateBuilder(args);

// The build-time OpenAPI generator (GetDocument.Insider) starts the app without a database or secrets.
var generatingDocument = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

// The Compose stack passes these lists as one comma-separated environment variable each (deploy/compose.yaml).
builder.Configuration.ExpandCommaSeparatedList("Auth:Apple:ClientIds");
builder.Configuration.ExpandCommaSeparatedList("Auth:Google:ClientIds");

builder.Services.AddProblemDetails();
// Enums travel as their names everywhere (requests, responses and the OpenAPI document).
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi(options => options.AddOperationIds().AddStringEnumTypes().AddBearerSecurity());
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddDbContext<AppDbContext>((services, options) => options.UseAppDatabase(
    services.GetRequiredService<IConfiguration>().GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.")));

// Keys live in the database, so tokens issued by one instance (or before a restart) stay valid on every instance.
// The fixed application name keeps them valid across images too (the default is the content root path).
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("CustomGolfStats");
if (!generatingDocument)
{
    dataProtection.PersistKeysToDbContext<AppDbContext>();
}

builder.Services.AddIdentityApiEndpoints<Account>()
    .AddUserValidator<UniqueEmailValidator>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddClaimsPrincipalFactory<AccountClaimsPrincipalFactory>();
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.Admin, policy => policy.RequireAuthenticatedUser().RequireRole(nameof(Role.Admin)));

// Behind Caddy: trust X-Forwarded-For/Proto from the proxy network only. Caddy terminates TLS, so there is no HTTPS redirect.
builder.Services.AddReverseProxySupport();

builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();
builder.Services.AddScoped<AppSettingsReader>();

builder.Services
    .AddAuthFeature()
    .AddMeFeature()
    .AddCoursesFeature()
    .AddRoundsFeature()
    .AddStatsFeature()
    .AddAdminFeature();

var app = builder.Build();

// Production migrates only through the migrations bundle (the `migrate` service in deploy/compose.yaml), never on start.
if (!generatingDocument && (app.Environment.IsDevelopment() || app.Environment.IsEnvironment(AppEnvironments.Testing)))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi().AllowAnonymous();
app.MapHealthChecks("/health").AllowAnonymous();

app.MapAuthEndpoints();
app.MapMeEndpoints();
app.MapCoursesEndpoints();
app.MapRoundsEndpoints();
app.MapStatsEndpoints();
app.MapAdminEndpoints();

await app.RunAsync();

/// <summary>Visible to the integration tests (<c>WebApplicationFactory&lt;Program&gt;</c>).</summary>
public partial class Program;
