using System.Globalization;
using System.Threading.RateLimiting;
using CampeonatoMagic.Models;
using CampeonatoMagic.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var dataPath = Path.GetFullPath(builder.Configuration["DataDirectory"] ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data"));
Directory.CreateDirectory(dataPath);
var admin = new AdminAccount(dataPath);
if (args.Contains("--set-admin"))
{
    Console.Write("Usuario administrador: ");
    var username = Console.ReadLine() ?? "";
    string ReadSecret()
    {
        if (Console.IsInputRedirected) return Console.ReadLine() ?? "";
        var secret = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); return secret.ToString(); }
            if (key.Key == ConsoleKey.Backspace) { if (secret.Length > 0) secret.Length--; }
            else if (!char.IsControl(key.KeyChar)) secret.Append(key.KeyChar);
        }
    }
    Console.Write("Contraseña (mínimo 12 caracteres): ");
    var password = ReadSecret();
    Console.Write("Repite la contraseña: ");
    if (password != ReadSecret()) throw new ArgumentException("Las contraseñas no coinciden.");
    admin.Set(username, password);
    Console.WriteLine("Administrador configurado. Se invalidaron las sesiones anteriores.");
    return;
}
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("es-CL");
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("es-CL");
builder.Services.AddSingleton(admin);
builder.Services.AddDbContext<LeagueDb>(o => o.UseSqlite(new SqliteConnectionStringBuilder { DataSource = Path.Combine(dataPath, "league.db"), ForeignKeys = true }.ToString()));
builder.Services.AddControllersWithViews(o => o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
var protection = builder.Services.AddDataProtection().SetApplicationName("CampeonatoMagic").PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataPath, "keys")));
if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi(protectToLocalMachine: true);
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.LoginPath = "/cuenta/entrar";
    o.AccessDeniedPath = "/cuenta/entrar";
    o.Cookie.Name = "CampeonatoMagic.Admin";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.SlidingExpiration = true;
    o.Events.OnValidatePrincipal = async context =>
    {
        if (context.Principal?.FindFirst("stamp")?.Value != admin.Read()?.Stamp)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync();
        }
    };
});
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
    { PermitLimit = 6, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LeagueDb>();
    await db.Database.MigrateAsync();
}
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; style-src 'self'; script-src 'self'; img-src 'self' data:; font-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    context.Response.Headers.CacheControl = "no-store";
    await next();
});
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapGet("/health/ready", async (LeagueDb db, IConfiguration configuration, ILogger<Program> logger, CancellationToken cancellationToken) =>
{
    try
    {
        if (!await db.Database.CanConnectAsync(cancellationToken)) return Results.StatusCode(503);
        return Results.Ok(new { status = "ready", commit = configuration["Deployment:CommitSha"] ?? "local" });
    }
    catch (Exception exception)
    {
        logger.LogError(exception, "Database readiness check failed");
        return Results.StatusCode(503);
    }
});
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
app.Run();

public partial class Program;
