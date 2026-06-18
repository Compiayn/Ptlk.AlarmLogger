using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Ptlk.AlarmLogger.Components;
using Ptlk.AlarmLogger.Configuration;
using Ptlk.AlarmLogger.Data;
using Ptlk.AlarmLogger.Services.Logging;
using Ptlk.AlarmLogger.Services.Query;
using Ptlk.AlarmLogger.Services.Redis;
using Ptlk.AlarmLogger.Services.Startup;
using Ptlk.AlarmLogger.Services.Status;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services.AddAlarmLoggerOptions(builder.Configuration);

var historyConnection = builder.Configuration.GetConnectionString("HistoryConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:HistoryConnection is required.");
if (string.IsNullOrWhiteSpace(historyConnection))
{
    throw new InvalidOperationException("ConnectionStrings:HistoryConnection is required.");
}

var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"]
    ?? (builder.Environment.IsDevelopment() ? "data-protection-keys" : "/data/data-protection-keys");
Directory.CreateDirectory(dataProtectionKeysPath);

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));

builder.Services.AddDbContextFactory<HistoryDbContext>((serviceProvider, options) =>
{
    options.UseNpgsql(historyConnection)
        .UseSnakeCaseNamingConvention();
});

builder.Services.AddSingleton<AlarmLoggerRuntimeSnapshotService>();
builder.Services.AddSingleton<AlarmLoggerUiEventHub>();
builder.Services.AddSingleton<AlarmEventQueue>();
builder.Services.AddSingleton<RedisConnectionFactory>();
builder.Services.AddSingleton<AlarmHistoryWriter>();

builder.Services.AddScoped<AlarmLoggerStatusQueryService>();
builder.Services.AddScoped<AlarmHistoryQueryService>();

builder.Services.AddHostedService<StartupGateService>();
builder.Services.AddHostedService<RedisAlarmEventSubscriptionService>();
builder.Services.AddHostedService<AlarmEventProcessorHostedService>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var historyDbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<HistoryDbContext>>();
    await using var historyDb = await historyDbFactory.CreateDbContextAsync();
    await historyDb.Database.MigrateAsync();
    await HistoryDatabaseInitializer.InitializeTimescaleAsync(
        historyDb,
        scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AlarmLoggerOptions>>());
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/healthz", (AlarmLoggerStatusQueryService status) => Results.Ok(status.GetSnapshot()));

app.MapGet("/api/alarm-logger/status", (AlarmLoggerStatusQueryService status) => Results.Ok(status.GetSnapshot()));

app.MapGet("/api/alarm-logger/history/range", (
    string? begin,
    string? end,
    string? order,
    string? time_zone,
    AlarmHistoryQueryService query,
    CancellationToken cancellationToken) => query.QueryRangeHttpAsync(begin, end, order, time_zone, cancellationToken));

app.MapGet("/api/alarm-logger/history/page", (
    int? skip,
    int? take,
    string? order,
    string? time_zone,
    AlarmHistoryQueryService query,
    CancellationToken cancellationToken) => query.QueryPageHttpAsync(skip, take, order, time_zone, cancellationToken));

app.Run();
