using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Net.Http;
using System.Net;
using System.Net.Http.Headers;
using System.Windows;
using MapCorleon.Data;
using MapCorleon.Contracts;
using MapCorleon.Games.Fallout4;
using MapCorleon.Games.Kcd2;
using MapCorleon.Games.Skyrim;
using MapCorleon.MapGenie;
using MapCorleon.Services;
using MapCorleon.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MapCorleon;

public partial class App : Application
{
    public static IServiceProvider Services => ((App)Current).host.Value.Services;

    private InstanceActivation? instance;
    private readonly Lazy<IHost> host = new(CreateHost);

    private static IHost CreateHost() => Host.CreateDefaultBuilder().ConfigureServices(services =>
    {
        services.AddSingleton<ISettingsService, JsonSettingsService>();
        services.AddSingleton<MapSessionStore>();
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
        services.AddSingleton<IAppLogService, AppLogService>();
        services.AddSingleton<IGameProcessDetector, WindowsGameProcessDetector>();
        services.AddSingleton<IProcessMemoryReader, WindowsProcessMemoryReader>();
        services.AddSingleton<IProcessStarter, SystemProcessStarter>();
        services.AddSkyrimGame();
        services.AddFallout4Game();
        services.AddKcd2Game();
        services.AddSingleton<PlayerLocationCoordinator>();
        services.AddSingleton<IPlayerLocationCoordinator>(provider => provider.GetRequiredService<PlayerLocationCoordinator>());
        services.AddHostedService(provider => provider.GetRequiredService<PlayerLocationCoordinator>());
        services.AddSingleton<IMapSettingsService, MapSettingsService>();
        services.AddSingleton<IMediaDownloadService, MediaDownloadService>();
        services.AddSingleton<EfMapImportSink>();
        services.AddSingleton<BundledMapCatalog>();
        services.AddSingleton<IMapProvider, MapGenieProvider>();
        services.AddSingleton<IMapProvider, Kcd2MonasteryMapProvider>();
        services.AddSingleton<IMapProviderResolver, MapProviderResolver>();
        services.AddSingleton<IImportTaskService, ImportTaskService>();
        services.AddSingleton<ITrayService, TrayService>();
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
        using var process = Process.GetCurrentProcess();
        using var user = WindowsIdentity.GetCurrent();
        instance = new InstanceActivation($"MapCorleon-{process.SessionId}-{user.User}");
        if (!instance.IsPrimary)
        {
            await instance.RequestActivationAsync();
            Shutdown();
            return;
        }

        _ = instance.ListenAsync(() => Dispatcher.BeginInvoke(() =>
        {
            if (MainWindow is not null)
                host.Value.Services.GetRequiredService<ITrayService>().ShowWindow();
        }));

        DarkWindowChrome.Register();
        await host.Value.StartAsync();
        await using var scope = host.Value.Services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        await DbSchemaInitializer.InitializeAsync(db);
        await Task.Run(() => host.Value.Services.GetRequiredService<BundledMapCatalog>().SeedAsync());
        await DbSeeder.SeedAsync(db);
        await host.Value.Services.GetRequiredService<IImportTaskService>().InitializeAsync();
        host.Value.Services.GetRequiredService<MainWindow>().Show(); base.OnStartup(e);
    }
    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            if (host.IsValueCreated)
            {
                host.Value.Services.GetService<ITrayService>()?.Dispose();
                await host.Value.StopAsync();
                host.Value.Dispose();
            }
        }
        finally
        {
            instance?.Dispose();
            base.OnExit(e);
        }
    }
}
