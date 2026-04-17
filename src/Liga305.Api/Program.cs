using AspNet.Security.OpenId.Steam;
using Liga305.Infrastructure;
using Liga305.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddLiga305Infrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<Liga305.Api.Auth.CurrentUserAccessor>();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

builder.Services
    .AddAuthentication(options =>
    {
        // Cookie is the default for everything (authenticate + challenge + forbid).
        // /auth/steam/login explicitly calls Challenge() with the Steam scheme to kick off OpenID.
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.Cookie.Name = "liga305.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    })
    .AddSteam(options =>
    {
        options.ApplicationKey = builder.Configuration["Steam:WebApiKey"] ?? "";
    });

builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<Liga305DbContext>();
    try
    {
        await db.Database.MigrateAsync();
        var seeder = scope.ServiceProvider.GetRequiredService<Liga305.Infrastructure.Persistence.DatabaseSeeder>();
        await seeder.EnsureActiveSeasonAsync();
        await seeder.EnsureBootstrapAdminAsync();
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Database migration/seeding on startup failed (Postgres not running?). The app will continue, but DB-backed endpoints will return errors.");
    }
}

app.UseStaticFiles();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "Liga305.Api" }));

app.MapGet("/health/db", async (Liga305DbContext db) =>
{
    try
    {
        var canConnect = await db.Database.CanConnectAsync();
        if (!canConnect) return Results.Problem("Cannot connect to database", statusCode: 503);

        var pending = await db.Database.GetPendingMigrationsAsync();
        var applied = await db.Database.GetAppliedMigrationsAsync();

        return Results.Ok(new
        {
            status = "ok",
            connected = true,
            appliedMigrations = applied,
            pendingMigrations = pending
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message, statusCode: 503);
    }
});

app.MapControllers();

app.Run();
