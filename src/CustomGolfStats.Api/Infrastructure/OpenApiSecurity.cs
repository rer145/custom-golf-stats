using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace CustomGolfStats.Api.Infrastructure;

/// <summary>
/// Declares the bearer scheme (the Identity bearer tokens from <c>/auth/login</c>, <c>/auth/refresh</c> and
/// <c>/auth/external</c>) and marks every operation that requires authorization with it, so generated clients know which
/// calls need the token.
/// </summary>
public static class OpenApiSecurity
{
    public const string BearerSchemeName = "Bearer";

    public static OpenApiOptions AddBearerSecurity(this OpenApiOptions options)
    {
        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes[BearerSchemeName] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                Description = "Access token from /auth/login, /auth/refresh or /auth/external.",
            };
            return Task.CompletedTask;
        });

        options.AddOperationTransformer((operation, context, _) =>
        {
            var metadata = context.Description.ActionDescriptor.EndpointMetadata;
            if (RequiresAuthorization(metadata))
            {
                operation.Security ??= [];
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(BearerSchemeName, context.Document)] = [],
                });
            }

            return Task.CompletedTask;
        });

        return options;
    }

    /// <summary>Same rule the authorization middleware applies: some authorize data and no allow-anonymous.</summary>
    public static bool RequiresAuthorization(IEnumerable<object> metadata)
    {
        var list = metadata as IReadOnlyCollection<object> ?? metadata.ToList();
        return list.OfType<IAuthorizeData>().Any() && !list.OfType<IAllowAnonymous>().Any();
    }
}
