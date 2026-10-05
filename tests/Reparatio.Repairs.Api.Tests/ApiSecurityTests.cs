using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Reparatio.Repairs.Api;
using Reparatio.Repairs.Infrastructure;
using Xunit;

namespace Reparatio.Repairs.Api.Tests;

public sealed class ApiSecurityTests
{
    [SqlFact]
    public async Task Real_login_pkce_and_database_permissions_protect_api()
    {
        await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Testing"));
        using var client = app.CreateClient(new() { BaseAddress = new("https://localhost:7240"), AllowAutoRedirect = false });
        var tenant = Guid.NewGuid(); var site = Guid.NewGuid(); var actor = Guid.NewGuid();
        var technician1 = Guid.NewGuid(); var technician2 = Guid.NewGuid();
        var email = $"test-{Guid.NewGuid():N}@example.com";
        string userId;
        using (var scope = app.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
            Assert.True((await users.CreateAsync(user, "Test-Password2026!")).Succeeded);
            userId = user.Id;
        }
        var factory = app.Services.GetRequiredService<IDbContextFactory<RepairsDbContext>>();
        try
        {
            await using (var db = factory.CreateDbContext())
            {
                db.Sites.Add(new() { TenantId = tenant, SiteId = site });
                db.Technicians.AddRange(new TechnicianRow { TenantId = tenant, SiteId = site, Id = technician1, IsAvailable = true },
                    new TechnicianRow { TenantId = tenant, SiteId = site, Id = technician2, IsAvailable = true });
                db.StaffIdentities.Add(new() { Id = actor, Issuer = "https://localhost:7240/", Subject = userId });
                db.StaffGrants.Add(new() { Id = Guid.NewGuid(), UserId = actor, TenantId = tenant, Role = StaffRole.TenantAdministrator });
                await db.SaveChangesAsync();
            }
            var route = $"/api/tenants/{tenant}/sites/{site}/repairs";
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(route)).StatusCode);
            var authorizationPost = await client.PostAsync("/connect/authorize", new FormUrlEncodedContent(new Dictionary<string, string> {
                ["client_id"] = "reparatio-web", ["redirect_uri"] = "https://localhost:4200/auth/callback", ["response_type"] = "code",
                ["scope"] = "openid reparatio.api", ["code_challenge"] = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32)), ["code_challenge_method"] = "S256" }));
            Assert.Equal(HttpStatusCode.Redirect, authorizationPost.StatusCode);
            var returnUrl = QueryHelpers.ParseQuery(authorizationPost.Headers.Location!.Query)["ReturnUrl"].ToString();
            Assert.Contains("client_id=reparatio-web", returnUrl);
            var login = await client.GetStringAsync("/Identity/Account/Login");
            var csrf = WebUtility.HtmlDecode(Regex.Match(login, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
            Assert.NotEmpty(csrf);
            var signIn = await client.PostAsync("/Identity/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string> {
                ["__RequestVerificationToken"] = csrf, ["Input.Email"] = email, ["Input.Password"] = "Test-Password2026!", ["Input.RememberMe"] = "false" }));
            Assert.Equal(HttpStatusCode.Redirect, signIn.StatusCode);
            // Identity cookies must never authenticate the repairs API.
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(route)).StatusCode);
            var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            var challenge = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            var authorize = QueryHelpers.AddQueryString("/connect/authorize", new Dictionary<string, string?> {
                ["client_id"] = "reparatio-web", ["redirect_uri"] = "https://localhost:4200/auth/callback", ["response_type"] = "code",
                ["scope"] = "openid profile reparatio.api", ["code_challenge"] = challenge, ["code_challenge_method"] = "S256", ["state"] = "test-state" });
            var authorization = await client.GetAsync(authorize);
            Assert.Equal(HttpStatusCode.Redirect, authorization.StatusCode);
            var code = QueryHelpers.ParseQuery(authorization.Headers.Location!.Query)["code"].ToString();
            Assert.NotEmpty(code);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(authorize.Replace("https%3A%2F%2Flocalhost%3A4200%2Fauth%2Fcallback", "https%3A%2F%2Fevil.example%2Fcallback"))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(QueryHelpers.AddQueryString("/connect/authorize", new Dictionary<string,string?> {
                ["client_id"] = "reparatio-web", ["redirect_uri"] = "https://localhost:4200/auth/callback", ["response_type"] = "code", ["scope"] = "openid reparatio.api" }))).StatusCode);
            var fields = new Dictionary<string, string> { ["grant_type"] = "authorization_code", ["client_id"] = "reparatio-web",
                ["redirect_uri"] = "https://localhost:4200/auth/callback", ["code"] = code, ["code_verifier"] = verifier };
            var exchange = await client.PostAsync("/connect/token", new FormUrlEncodedContent(fields));
            var tokenJson = await exchange.Content.ReadAsStringAsync();
            Assert.True(exchange.IsSuccessStatusCode, tokenJson);
            var bearer = JsonDocument.Parse(tokenJson).RootElement.GetProperty("access_token").GetString();
            client.DefaultRequestHeaders.Authorization = new("Bearer", bearer);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(route)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/tenants/{Guid.NewGuid()}/sites/{site}/repairs")).StatusCode);
            var command = new { RequestId = Guid.NewGuid(), RepairId = Guid.NewGuid() };
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(route, command)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(route, command)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(route, new { command.RequestId, command.RepairId, ActorId = actor })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/tenants/{tenant}/sites/{site}/technicians/{technician1}/availability",
                new { RequestId = Guid.NewGuid() })).StatusCode);
            Guid target;
            await using (var db = factory.CreateDbContext())
                target = (await db.Repairs.SingleAsync(x => x.TenantId == tenant)).TechnicianId == technician1 ? technician2 : technician1;
            var repairRoute = route + "/" + command.RepairId;
            var reassign = new { RequestId = Guid.NewGuid(), TechnicianId = target, Reason = "Cambio banco" };
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(repairRoute + "/reassign", reassign)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(repairRoute + "/reassign", reassign)).StatusCode);
            await using (var db = factory.CreateDbContext())
            {
                Assert.Equal(actor, (await db.Reassignments.SingleAsync(x => x.TenantId == tenant)).ActorId);
                // Quote/payment authorization is outside this API increment: seed the starting work state only.
                await db.Repairs.Where(x => x.TenantId == tenant).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, Reparatio.Repairs.Domain.RepairStatus.InProgress));
            }
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(repairRoute + "/submit-testing", new { RequestId = Guid.NewGuid() })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(repairRoute + "/testing", new { RequestId = Guid.NewGuid(), Passed = true, Notes = "Verificato" })).StatusCode);
            await using (var db = factory.CreateDbContext())
            {
                Assert.Equal(0, (await db.Technicians.SingleAsync(x => x.TenantId == tenant && x.Id == target)).ActiveRepairCount);
                Assert.All(await db.RepairTransitions.Where(x => x.TenantId == tenant).ToArrayAsync(), x => Assert.Equal(actor, x.ActorId));
            }
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(repairRoute + "/return-work", new { RequestId = Guid.NewGuid(), Reason = "Ulteriore verifica" })).StatusCode);
            await using (var db = factory.CreateDbContext())
            {
                await db.StaffGrants.Where(x => x.UserId == actor).ExecuteDeleteAsync();
                Assert.Equal(1, await db.Repairs.CountAsync(x => x.TenantId == tenant));
            }
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(route, command)).StatusCode);
            client.DefaultRequestHeaders.Authorization = new("Bearer", bearer + "corrupted");
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(route)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/connect/token", new FormUrlEncodedContent(fields))).StatusCode);
        }
        finally
        {
            await using var db = factory.CreateDbContext();
            await db.StaffGrants.Where(x => x.UserId == actor).ExecuteDeleteAsync();
            await db.StaffIdentities.Where(x => x.Id == actor).ExecuteDeleteAsync();
            await db.Receipts.Where(x => x.TenantId == tenant).ExecuteDeleteAsync();
            await db.Reassignments.Where(x => x.TenantId == tenant).ExecuteDeleteAsync();
            await db.RepairTransitions.Where(x => x.TenantId == tenant).ExecuteDeleteAsync();
            await db.Repairs.Where(x => x.TenantId == tenant).ExecuteDeleteAsync();
            await db.Technicians.Where(x => x.TenantId == tenant).ExecuteDeleteAsync();
            await db.Sites.Where(x => x.TenantId == tenant).ExecuteDeleteAsync();
            using var scope = app.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = await users.FindByIdAsync(userId);
            if (user is not null) await users.DeleteAsync(user);
            var identityDb = scope.ServiceProvider.GetRequiredService<IdentityStore>();
            await identityDb.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [identity].[OpenIddictTokens] WHERE [Subject] = {userId}");
            await identityDb.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [identity].[OpenIddictAuthorizations] WHERE [Subject] = {userId}");
        }
    }
}
