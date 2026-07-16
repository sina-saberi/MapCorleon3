using System.IO;
using System.Net.Http;
using System.Net;
using System.Net.Http.Headers;
using System.Windows;
using MapCorleon.Data;
using MapCorleon.Contracts;
using MapCorleon.MapGenie;
using MapCorleon.Services;
using MapCorleon.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MapCorleon;

public partial class App : Application
{
    public static IServiceProvider Services => ((App)Current)._host.Services;
    private readonly IHost _host = Host.CreateDefaultBuilder().ConfigureServices(services =>
    {
        services.AddSingleton<ISettingsService, JsonSettingsService>();
        services.AddDbContextFactory<AppDbContext>((provider, options) =>
            options.UseSqlite($"Data Source={provider.GetRequiredService<ISettingsService>().Current.DatabasePath}"));
        services.AddSingleton(CreateImportHttpClient());
        services.AddSingleton<IEventBus, EventBus>();
        services.AddSingleton<IMapPickService, MapPickService>();
        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
        services.AddScoped<ILocationService, LocationService>(); services.AddScoped<ITimelineService, TimelineService>();
        services.AddSingleton<IFilterService, FilterService>(); services.AddSingleton<ISearchService, SearchService>();
        services.AddSingleton<IMediaService, MediaService>();
        services.AddSingleton<IMediaPathResolver, MediaPathResolver>();
        services.AddSingleton<ITileCacheService, TileCacheService>();
        services.AddSingleton<IProviderStorage, ProviderStorage>();
        services.AddSingleton<IMapImportSink, EfMapImportSink>();
        services.AddSingleton<IMapProvider, MapGenieProvider>();
        services.AddSingleton<IMapProviderResolver, MapProviderResolver>();
        services.AddSingleton<IImportTaskService, ImportTaskService>();
        services.AddSingleton<ShellViewModel>(); services.AddSingleton<MainWindow>();
    }).Build();

    private static HttpClient CreateImportHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = true,
            Proxy = HttpClient.DefaultProxy,
            DefaultProxyCredentials = CredentialCache.DefaultCredentials,
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(30)
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(2) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/150.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9,fa;q=0.8");
        return client;
    }
    protected override async void OnStartup(StartupEventArgs e)
    {
        DarkWindowChrome.Register();
        await _host.StartAsync();
        await using var scope = _host.Services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        await DbSchemaInitializer.InitializeAsync(db);
        await DbSeeder.SeedAsync(db);
        _host.Services.GetRequiredService<MainWindow>().Show(); base.OnStartup(e);
    }
    protected override async void OnExit(ExitEventArgs e) { await _host.StopAsync(); _host.Dispose(); base.OnExit(e); }
}
