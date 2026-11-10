using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using PvAutopilot.Sources.HubSpot;
using PvAutopilot.Registries.Mastr;
using PvAutopilot.Netzbetreiber.NetzeDuisburg;
using PvAutopilot.Sources.Reonic;
using PvAutopilot.Sources.SharePoint;
using PvAutopilot.Core;
using PvAutopilot.Host.Endpoints;
using PvAutopilot.Infrastructure;
using PvAutopilot.Infrastructure.Jobs;
using PvAutopilot.Infrastructure.Logging;
using PvAutopilot.Infrastructure.Persistence;
using Serilog;
using Serilog.Formatting.Compact;

// Im Docker-Build: Browser für Playwright installieren, ohne die App zu starten.
if (args is ["playwright-install", ..])
    return Microsoft.Playwright.Program.Main(["install", "--with-deps", "chromium"]);

var builder = WebApplication.CreateBuilder(args);
AddInstanceConfiguration(builder, args);

builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Instance", builder.Configuration["Instance:Name"] ?? "unbekannt")
    .Destructure.With<PersonalDataDestructuringPolicy>()
    .WriteTo.Console(new RenderedCompactJsonFormatter()));

builder.Services
    .AddAutopilotCore(builder.Configuration)
    .AddAutopilotInfrastructure(builder.Configuration)
    .AddHubSpot(builder.Configuration)
    .AddReonic(builder.Configuration)
    .AddSharePoint(builder.Configuration)
    .AddNetzeDuisburg(builder.Configuration)
    .AddMastr(builder.Configuration);

var connectionString = builder.Configuration.GetConnectionString(PvAutopilot.Infrastructure.DependencyInjection.ConnectionStringName)!;
builder.Services.AddHangfire(config => config
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(connectionString)));
builder.Services.AddHangfireServer();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    // Railway terminiert TLS am Proxy; ohne das stimmt die URI für die Webhook-Signatur nicht.
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseSerilogRequestLogging();

await using (var scope = app.Services.CreateAsyncScope())
    await scope.ServiceProvider.GetRequiredService<AutopilotDbContext>().Database.MigrateAsync();
app.Services.ScheduleRecurringJobs();

if (app.Environment.IsDevelopment())
    app.MapHangfireDashboard("/hangfire");

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapHubSpotWebhooks();
app.MapReonicWebhooks();
app.MapAdminEndpoints();

app.Run();
return 0;

// Reihenfolge: appsettings → Instanz-Datei (config/instances/<name>/instance.json) → appsettings.Local.json
// → Umgebungsvariablen → Kommandozeile. Secrets kommen nur aus den letzten drei.
static void AddInstanceConfiguration(WebApplicationBuilder builder, string[] args)
{
    var configuration = builder.Configuration;
    if (configuration["Instance:ConfigFile"] is { Length: > 0 } instanceFile)
        configuration.AddJsonFile(Path.GetFullPath(instanceFile, builder.Environment.ContentRootPath), optional: false);

    configuration.AddJsonFile("appsettings.Local.json", optional: true);
    configuration.AddEnvironmentVariables();
    configuration.AddCommandLine(args);
}
