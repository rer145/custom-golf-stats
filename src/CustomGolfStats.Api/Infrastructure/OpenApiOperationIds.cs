using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace CustomGolfStats.Api.Infrastructure;

/// <summary>
/// Gives the operations that the framework maps without a <c>WithName</c> (the Identity API routes under <c>/auth</c>)
/// a stable operation id, so every operation in the OpenAPI document has a unique one and the generated client gets
/// readable method names.
/// </summary>
public static class OpenApiOperationIds
{
    private static readonly Dictionary<(string Method, string Path), string> IdentityOperations = new()
    {
        [("POST", "/auth/register")] = "AuthRegister",
        [("POST", "/auth/login")] = "AuthLogin",
        [("POST", "/auth/refresh")] = "AuthRefresh",
        [("GET", "/auth/confirmEmail")] = "AuthConfirmEmail",
        [("POST", "/auth/resendConfirmationEmail")] = "AuthResendConfirmationEmail",
        [("POST", "/auth/forgotPassword")] = "AuthForgotPassword",
        [("POST", "/auth/resetPassword")] = "AuthResetPassword",
        [("POST", "/auth/manage/2fa")] = "AuthManageTwoFactor",
        [("GET", "/auth/manage/info")] = "AuthGetManageInfo",
        [("POST", "/auth/manage/info")] = "AuthUpdateManageInfo",
    };

    /// <summary>
    /// Enums travel as names (global <c>JsonStringEnumConverter</c>) but the generated schemas list the names without a
    /// type; stating <c>"type": "string"</c> lets client generators emit string enums.
    /// </summary>
    public static OpenApiOptions AddStringEnumTypes(this OpenApiOptions options) =>
        options.AddSchemaTransformer((schema, context, _) =>
        {
            if (context.JsonTypeInfo.Type.IsEnum || Nullable.GetUnderlyingType(context.JsonTypeInfo.Type)?.IsEnum == true)
            {
                // A nullable enum's schema also lists null as a value; nullability belongs to the property, not the enum.
                schema.Type = JsonSchemaType.String;
                if (schema.Enum is { } values)
                {
                    for (var i = values.Count - 1; i >= 0; i--)
                    {
                        if (values[i] is null || values[i].GetValueKind() == System.Text.Json.JsonValueKind.Null)
                        {
                            values.RemoveAt(i);
                        }
                    }
                }
            }

            return Task.CompletedTask;
        });

    /// <summary>Adds the operation transformer to the document options (<c>AddOpenApi(options =&gt; ...)</c>).</summary>
    public static OpenApiOptions AddOperationIds(this OpenApiOptions options) =>
        options.AddOperationTransformer((operation, context, _) =>
        {
            var path = "/" + context.Description.RelativePath?.TrimStart('/');
            var method = context.Description.HttpMethod ?? "";
            if (IdentityOperations.TryGetValue((method.ToUpperInvariant(), path), out var id))
            {
                operation.OperationId = id;
            }

            return Task.CompletedTask;
        });
}
