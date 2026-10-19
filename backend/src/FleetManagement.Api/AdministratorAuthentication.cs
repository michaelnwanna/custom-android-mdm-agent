using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace FleetManagement.Api;

public static class AdministratorAuthentication
{
    public const string Scheme = "administrator";
    public const string ProvisionPolicy = "provision-enrollment";
    public const string EntraEnrollmentPermission = "Enrollments.Create";

    public static void AddAdministratorAuthentication(this WebApplicationBuilder builder)
    {
        var authority = builder.Configuration["Administrator:Authority"];
        var audience = builder.Configuration["Administrator:Audience"];
        var profile = builder.Configuration["Administrator:TokenProfile"];
        var entra = string.Equals(profile, "Entra", StringComparison.Ordinal);
        if (!string.IsNullOrEmpty(profile) && !entra && !string.Equals(profile, "Generic", StringComparison.Ordinal))
            throw new InvalidOperationException("Administrator token profile must be Generic or Entra.");
        if (!string.IsNullOrWhiteSpace(authority) || !string.IsNullOrWhiteSpace(audience))
        {
            if (!Uri.TryCreate(authority, UriKind.Absolute, out var address) ||
                address.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(address.UserInfo) ||
                !string.IsNullOrEmpty(address.Query) || !string.IsNullOrEmpty(address.Fragment) ||
                string.IsNullOrWhiteSpace(audience))
                throw new InvalidOperationException("Administrator authentication requires an HTTPS authority and API audience.");
        }
        builder.Services.AddAuthentication().AddJwtBearer(Scheme, options =>
        {
            options.Authority = authority;
            options.Audience = audience;
            options.MapInboundClaims = false;
            options.RequireHttpsMetadata = true;
            options.IncludeErrorDetails = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateLifetime = true,
                RequireExpirationTime = true,
                RequireSignedTokens = true,
                ValidateIssuerSigningKey = true,
                ClockSkew = TimeSpan.FromSeconds(30),
                // Entra ID access tokens use typ JWT; audience and the required scp claim reject ID tokens.
                ValidTypes = entra ? ["JWT"] : ["at+jwt"],
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256, SecurityAlgorithms.EcdsaSha256]
            };
            options.Events = new JwtBearerEvents
            {
                OnChallenge = async context =>
                {
                    context.HandleResponse();
                    context.Response.Headers.WWWAuthenticate = "Bearer";
                    await FleetApi.Problem(401).ExecuteAsync(context.HttpContext);
                },
                OnForbidden = context => FleetApi.Problem(403).ExecuteAsync(context.HttpContext)
            };
        });
        builder.Services.AddAuthorization(options => options.AddPolicy(ProvisionPolicy, policy =>
        {
            policy.AddAuthenticationSchemes(Scheme).RequireAuthenticatedUser();
            if (entra)
            {
                policy.RequireAssertion(context =>
                    HasEntraValue(context.User, "scp", ' ') && HasEntraValue(context.User, "roles", null));
            }
            else
            {
                policy.RequireClaim("permission", "enrollments:create");
            }
            policy.RequireAssertion(context =>
                context.User.FindAll("sub").Count() == 1 &&
                !string.IsNullOrWhiteSpace(context.User.FindFirst("sub")!.Value) &&
                context.User.FindFirst("sub")!.Value is { Length: > 0 and <= 256 } &&
                context.User.FindAll("iss").Count() == 1 &&
                context.User.FindFirst("iss")!.Value is { Length: > 0 and <= 2048 });
        }));
    }

    private static bool HasEntraValue(System.Security.Claims.ClaimsPrincipal user, string type, char? separator) =>
        user.FindAll(type).Any(claim => separator is { } value
            ? claim.Value.Split(value, StringSplitOptions.RemoveEmptyEntries).Contains(EntraEnrollmentPermission, StringComparer.Ordinal)
            : string.Equals(claim.Value, EntraEnrollmentPermission, StringComparison.Ordinal));
}
