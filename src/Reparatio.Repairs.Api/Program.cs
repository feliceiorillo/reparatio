using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using OpenIddict.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Reparatio.Repairs.Api;
using Reparatio.Repairs.Application;
using Reparatio.Repairs.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
if (OperatingSystem.IsWindows()) builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Codex", ".secrets", "reparatio", "cookies")))
    .ProtectKeysWithDpapi();
builder.Services.AddDbContext<IdentityStore>(o => o.UseSqlServer(SqlConnectionSettings.ReadRequired(),
    x => x.MigrationsHistoryTable("__IdentityMigrations", "identity")).UseOpenIddict());
builder.Services.AddDefaultIdentity<Microsoft.AspNetCore.Identity.IdentityUser>(o => {
    o.SignIn.RequireConfirmedAccount = true;
    o.User.RequireUniqueEmail = true;
    o.Password.RequiredLength = 12;
    o.Lockout.MaxFailedAccessAttempts = 5;
}).AddEntityFrameworkStores<IdentityStore>();
builder.Services.AddRazorPages();
builder.Services.AddTransient<Microsoft.AspNetCore.Identity.UI.Services.IEmailSender, IdentityEmailSender>();
builder.Services.ConfigureApplicationCookie(o => { o.Cookie.SecurePolicy = CookieSecurePolicy.Always; o.Cookie.HttpOnly = true; });
builder.Services.AddOpenIddict().AddCore(o => o.UseEntityFrameworkCore().UseDbContext<IdentityStore>())
    .AddServer(o => {
        o.SetIssuer(new Uri(builder.Configuration["Identity:Issuer"] ?? "https://localhost:7240/"));
        o.SetAuthorizationEndpointUris("connect/authorize").SetTokenEndpointUris("connect/token");
        o.AllowAuthorizationCodeFlow().RequireProofKeyForCodeExchange();
        o.RegisterScopes("reparatio.api", "profile");
        o.SetAccessTokenLifetime(TimeSpan.FromMinutes(10));
        if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
            throw new InvalidOperationException("Configure production signing and encryption certificates before deployment.");
        o.AddSigningCertificate(LocalSigningCertificate.Load("signing"));
        o.AddEncryptionCertificate(LocalSigningCertificate.Load("encryption"));
        o.UseAspNetCore().EnableAuthorizationEndpointPassthrough().EnableTokenEndpointPassthrough();
    }).AddValidation(o => { o.UseLocalServer(); o.AddAudiences("reparatio-api"); o.EnableTokenEntryValidation(); o.UseAspNetCore(); });
builder.Services.AddAuthorization(o => o.AddPolicy("RepairApi", new AuthorizationPolicyBuilder(
    OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
    .RequireAuthenticatedUser().RequireAssertion(c => c.User.HasScope("reparatio.api")).Build()));
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<IDbContextFactory<RepairsDbContext>>(_ => new RepairsDbContextFactory(SqlConnectionSettings.ReadRequired()));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<SqlRepairStore>();
builder.Services.AddScoped<IRepairOpeningStore>(s => s.GetRequiredService<SqlRepairStore>());
builder.Services.AddScoped<ITechnicianAvailabilityStore>(s => s.GetRequiredService<SqlRepairStore>());
builder.Services.AddScoped<IRepairLifecycleStore>(s => s.GetRequiredService<SqlRepairStore>());
builder.Services.AddScoped<StaffAccess>();
builder.Services.AddScoped<IRepairAccess>(s => s.GetRequiredService<StaffAccess>());
builder.Services.AddScoped<ITechnicianAvailabilityAccess>(s => s.GetRequiredService<StaffAccess>());
builder.Services.AddScoped<IRepairLifecycleAccess>(s => s.GetRequiredService<StaffAccess>());
builder.Services.AddScoped<OpenRepairHandler>();
builder.Services.AddScoped<SetTechnicianAvailabilityHandler>();
builder.Services.AddScoped<RepairLifecycleHandler>();
builder.Services.ConfigureHttpJsonOptions(o => {
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    o.SerializerOptions.RespectRequiredConstructorParameters = true;
});
var app = builder.Build();
app.UseHttpsRedirection();
app.Use(async (context, next) => {
    try { await next(context); }
    catch (UnauthorizedAccessException) { await Results.Problem(statusCode: 403, title: "Accesso non consentito.").ExecuteAsync(context); }
    catch (RepairNotFoundException) { await Results.Problem(statusCode: 404, title: "Pratica non trovata.").ExecuteAsync(context); }
    catch (ArgumentException) { await Results.Problem(statusCode: 400, title: "Dati non validi.").ExecuteAsync(context); }
    catch (InvalidOperationException exception) { app.Logger.LogWarning(exception, "Invalid operation."); await Results.Problem(statusCode: 409, title: "Operazione incompatibile con lo stato corrente o con la richiesta precedente.").ExecuteAsync(context); }
    catch (Exception exception) when (!context.Response.HasStarted && exception is not OperationCanceledException) {
        app.Logger.LogError(exception, "Request failed.");
        await Results.Problem(statusCode: 500, title: "Errore durante l'operazione.").ExecuteAsync(context);
    }
});
app.UseAuthentication();
app.UseAuthorization();
await LocalIdentity.RegisterClientAsync(app.Services, app.Configuration);
LocalIdentity.Map(app);
RepairEndpoints.Map(app);
app.Run();
public partial class Program;
