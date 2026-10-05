using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Reparatio.Repairs.Api;

public static class LocalIdentity
{
    public static async Task RegisterClientAsync(IServiceProvider services, IConfiguration configuration)
    {
        await using var scope = services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var descriptor = new OpenIddictApplicationDescriptor {
            ClientId = "reparatio-web", ClientType = ClientTypes.Public, ConsentType = ConsentTypes.Implicit,
            DisplayName = "Reparatio web",
            RedirectUris = { new(configuration["Identity:RedirectUri"] ?? "https://localhost:4200/auth/callback") },
            Permissions = { Permissions.Endpoints.Authorization, Permissions.Endpoints.Token,
                Permissions.GrantTypes.AuthorizationCode, Permissions.ResponseTypes.Code,
                Permissions.Scopes.Profile, Permissions.Prefixes.Scope + "reparatio.api" },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange }
        };
        var application = await manager.FindByClientIdAsync(descriptor.ClientId);
        if (application is null) await manager.CreateAsync(descriptor);
        else await manager.UpdateAsync(application, descriptor);
    }

    public static void Map(WebApplication app)
    {
        app.MapMethods("/connect/authorize", ["GET", "POST"], async (HttpContext context, UserManager<IdentityUser> users) => {
            var request = context.GetOpenIddictServerRequest() ?? throw new InvalidOperationException();
            var authentication = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
            var expired = request.MaxAge is not null && authentication.Properties?.IssuedUtc is { } issued &&
                DateTimeOffset.UtcNow - issued > TimeSpan.FromSeconds(request.MaxAge.Value);
            if (!authentication.Succeeded || request.HasPromptValue(PromptValues.Login) || expired)
            {
                if (request.HasPromptValue(PromptValues.None))
                    return Results.Forbid(new AuthenticationProperties(new Dictionary<string, string?> {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.LoginRequired
                    }), [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
                var parameters = context.Request.HasFormContentType
                    ? (await context.Request.ReadFormAsync()).ToDictionary(x => x.Key, x => (string?)x.Value.ToString())
                    : context.Request.Query.ToDictionary(x => x.Key, x => (string?)x.Value.ToString());
                parameters["prompt"] = string.Join(" ", request.GetPromptValues().Where(x => x != PromptValues.Login));
                parameters.Remove("max_age");
                return Results.Challenge(new AuthenticationProperties { RedirectUri = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(
                    context.Request.PathBase + context.Request.Path, parameters) }, [IdentityConstants.ApplicationScheme]);
            }
            var user = await users.GetUserAsync(authentication.Principal!);
            if (user is null || !await users.IsEmailConfirmedAsync(user) || await users.IsLockedOutAsync(user))
                return Results.Forbid(authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
            var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
            identity.SetClaim(Claims.Subject, user.Id);
            identity.SetClaim(Claims.Name, user.UserName);
            identity.SetClaim("security_stamp", await users.GetSecurityStampAsync(user));
            var principal = new ClaimsPrincipal(identity);
            principal.SetScopes(request.GetScopes());
            principal.SetResources("reparatio-api");
            identity.SetDestinations(c => c.Type == Claims.Name && c.Subject!.HasScope(Scopes.Profile) ? [Destinations.AccessToken, Destinations.IdentityToken] : []);
            return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }).AllowAnonymous();

        app.MapPost("/connect/token", async (HttpContext context, UserManager<IdentityUser> users) => {
            var authentication = await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            var principal = authentication.Principal;
            var user = principal is null ? null : await users.FindByIdAsync(principal.GetClaim(Claims.Subject)!);
            if (user is null || !await users.IsEmailConfirmedAsync(user) || await users.IsLockedOutAsync(user) ||
                principal!.GetClaim("security_stamp") != await users.GetSecurityStampAsync(user))
                return Results.Forbid(authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
            return Results.SignIn(principal!, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }).AllowAnonymous();
        app.MapRazorPages();
    }
}
