using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using BruTile;
using BruTile.Cache;
using BruTile.Predefined;
using BruTile.Web;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling.Layers;
using MapCorleon.Models;
using MapCorleon.Services;
using MapCorleon.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows.Threading;

namespace MapCorleon.Views;

public partial class MapView
{
    private ShellViewModel? viewModel;
    private MemoryLayer? locationLayer;
    private GlobalSphericalMercator? tileSchema;
    private string? spriteSource;
    private CancellationTokenSource? spriteCancellation;
    private readonly DispatcherTimer markerRefreshTimer;

    public MapView()
    {
        InitializeComponent();
        MapControl.MapTapped += MapTapped;
        markerRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        markerRefreshTimer.Tick += (_, _) => { markerRefreshTimer.Stop(); RebuildLocations(); };
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        DataContextChanged += (_, _) => Attach();
    }

    private void Attach()
    {
        Detach();
        viewModel = DataContext as ShellViewModel;
        if (viewModel is null) return;
        viewModel.PropertyChanged += ViewModelChanged;
        viewModel.VisibleLocations.CollectionChanged += LocationsChanged;
        RebuildMap();
    }

    private void Detach()
    {
        if (viewModel is not null)
        {
            viewModel.PropertyChanged -= ViewModelChanged;
            viewModel.VisibleLocations.CollectionChanged -= LocationsChanged;
        }
        spriteCancellation?.Cancel(); spriteCancellation?.Dispose(); spriteCancellation = null;
        viewModel = null;
    }

    private void ViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.CurrentMapConfig)) RebuildMap();
        else if (e.PropertyName == nameof(ShellViewModel.SelectedLocation) && viewModel?.SelectedLocation is { } location)
        {
            var point = SphericalMercator.FromLonLat(location.Longitude, location.Latitude);
            MapControl.Map?.Navigator.CenterOn(new MPoint(point.x, point.y), 350);
        }
    }

    private void LocationsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        markerRefreshTimer.Stop(); markerRefreshTimer.Start();
    }

    private void RebuildMap()
    {
        var config = viewModel?.CurrentMapConfig;
        if (config is null || string.IsNullOrWhiteSpace(config.TilePattern)) return;
        spriteSource = null;
        var minimumZoom = config.MinZoom ?? 0;
        var maximumZoom = config.MaxZoom ?? 19;
        tileSchema = new GlobalSphericalMercator(YAxis.OSM, minimumZoom, maximumZoom, "png");
        var template = config.TilePattern.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? config.TilePattern
            : $"https://tiles.mapgenie.io/games/{config.TilePattern.TrimStart('/')}";
        var settings = App.Services.GetRequiredService<ISettingsService>().Current;
        var cacheName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(template)))[..16];
        var cache = new MapTileCache(Path.Combine(settings.TileDirectory, "mapsui", cacheName), settings.TileMaxAge);
        var tileSource = new HttpTileSource(tileSchema, template, name: "MapGenie", persistentCache: cache,
            configureHttpRequestMessage: request =>
            {
                request.Headers.Referrer = new Uri("https://mapgenie.io/");
                request.Headers.TryAddWithoutValidation("Accept", "image/avif,image/webp,image/apng,image/*,*/*;q=0.8");
            });
        var map = new Mapsui.Map { CRS = "EPSG:3857", BackColor = new Color(17, 25, 35) };
        map.Layers.Add(new TileLayer(tileSource, httpClient: App.Services.GetRequiredService<HttpClient>()) { Name = "MapGenie Tiles" });
        MapControl.Map = map;
        RebuildLocations();
        spriteCancellation?.Cancel(); spriteCancellation = new CancellationTokenSource();
        _ = PrepareSpriteAsync(config.SpriteImagePath, spriteCancellation.Token);

        var latitude = (config.MinLatitude + config.MaxLatitude) / 2;
        var longitude = (config.MinLongitude + config.MaxLongitude) / 2;
        var projectedCenter = SphericalMercator.FromLonLat(longitude, latitude);
        var center = new MPoint(projectedCenter.x, projectedCenter.y);
        var zoom = Math.Clamp((int)Math.Round(config.DefaultZoom), minimumZoom, maximumZoom);
        map.Navigator.CenterOnAndZoomTo(center, tileSchema.Resolutions[zoom].UnitsPerPixel, 0);
        StatusText.Text = $"Mapsui • {viewModel?.VisibleLocations.Count ?? 0} locations";
    }

    private void RebuildLocations()
    {
        var vm = viewModel;
        var map = MapControl.Map;
        if (vm is null || map is null) return;
        if (locationLayer is not null) map.Layers.Remove(locationLayer);
        var styles = new Dictionary<(int CategoryId, bool Marked), IStyle>();
        var features = new List<IFeature>(vm.VisibleLocations.Count);
        foreach (var location in vm.VisibleLocations)
        {
            var coordinates = SphericalMercator.FromLonLat(location.Longitude, location.Latitude);
            var feature = new PointFeature(new MPoint(coordinates.x, coordinates.y)) { Data = location };
            var marked = location.Marks.Count > 0;
            if (!styles.TryGetValue((location.CategoryId, marked), out var style))
            {
                style = CreateLocationStyle(location.Category, spriteSource, marked);
                styles[(location.CategoryId, marked)] = style;
            }
            feature.Styles.Add(style);
            features.Add(feature);
        }
        locationLayer = new MemoryLayer("Locations") { Features = features, Style = null };
        map.Layers.Add(locationLayer);
        map.RefreshData();
        StatusText.Text = $"Mapsui • {features.Count} locations";
    }

    private async Task PrepareSpriteAsync(string? location, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(location)) return;
        try
        {
            if (!Uri.TryCreate(location, UriKind.Absolute, out var remote) || remote.IsFile)
            {
                var localPath = remote?.LocalPath ?? Path.GetFullPath(location);
                if (File.Exists(localPath)) spriteSource = new Uri(localPath).AbsoluteUri;
            }
            else
            {
                var settings = App.Services.GetRequiredService<ISettingsService>().Current;
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(location)))[..20];
                var localPath = Path.Combine(settings.StorageDirectory, "sprites", hash + ".png");
                if (!File.Exists(localPath))
                {
                    var response = await FetchSpriteAsync(location, ct);
                    using (response)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                        await using var output = File.Create(localPath);
                        await response.Content.CopyToAsync(output, ct);
                    }
                }
                spriteSource = new Uri(localPath).AbsoluteUri;
            }
            ct.ThrowIfCancellationRequested();
            RebuildLocations();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { StatusText.Text = $"Sprite error: {ex.Message}"; }
    }

    private static async Task<HttpResponseMessage> FetchSpriteAsync(string location, CancellationToken ct)
    {
        var http = App.Services.GetRequiredService<HttpClient>();
        var response = await SendAsync(location);
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden && Uri.TryCreate(location, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Query))
        {
            response.Dispose(); response = await SendAsync(new UriBuilder(uri) { Query = string.Empty }.Uri.ToString());
        }
        response.EnsureSuccessStatusCode(); return response;

        Task<HttpResponseMessage> SendAsync(string url)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Referrer = new Uri("https://mapgenie.io/");
            request.Headers.Accept.ParseAdd("image/avif,image/webp,image/apng,image/*,*/*;q=0.8");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "image");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "no-cors");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "cross-site");
            return http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
    }

    private static IStyle CreateLocationStyle(Category? category, string? spritePath, bool marked)
    {
        if (category?.IconX is int x && category.IconY is int y && category.IconWidth is int width && category.IconHeight is int height && !string.IsNullOrWhiteSpace(spritePath))
        {
            var source = Uri.TryCreate(spritePath, UriKind.Absolute, out var uri) && uri.IsFile ? uri.AbsoluteUri
                : Uri.TryCreate(spritePath, UriKind.Absolute, out _) ? spritePath : new Uri(Path.GetFullPath(spritePath)).AbsoluteUri;
            return new ImageStyle
            {
                Image = new Mapsui.Styles.Image { Source = source, BitmapRegion = new BitmapRegion(x, y, width, height) },
                SymbolScale = 1d / Math.Max(category.IconPixelRatio ?? 1, 1),
                Offset = new Offset(0, -height / 2d),
                Opacity = marked ? .32f : 1f
            };
        }
        return new SymbolStyle { SymbolScale = .45, Fill = new Brush(new Color(238, 99, 82)), Outline = new Pen(Color.White, 2), Opacity = marked ? .32f : 1f };
    }

    private void MapTapped(object? sender, MapEventArgs e)
    {
        var picker = App.Services.GetRequiredService<IMapPickService>();
        if (picker.IsPicking)
        {
            var coordinates = SphericalMercator.ToLonLat(e.WorldPosition.X, e.WorldPosition.Y);
            picker.Complete(coordinates.lat, coordinates.lon); e.Handled = true; return;
        }
        if (locationLayer is null || viewModel is null) return;
        var info = e.GetMapInfo([locationLayer]);
        if (info?.Feature?.Data is not Location location) return;
        viewModel.SelectedLocation = location;
        e.Handled = true;
    }

    private sealed class MapTileCache(string directory, TimeSpan maxAge) : IPersistentCache<byte[]>
    {
        public void Add(TileIndex index, byte[] tile)
        {
            var path = GetPath(index); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (!File.Exists(path)) File.WriteAllBytes(path, tile);
        }
        public byte[]? Find(TileIndex index)
        {
            var path = GetPath(index);
            if (!File.Exists(path) || DateTime.UtcNow - File.GetLastWriteTimeUtc(path) >= maxAge) return null;
            return File.ReadAllBytes(path);
        }
        public void Remove(TileIndex index) { var path = GetPath(index); if (File.Exists(path)) File.Delete(path); }
        private string GetPath(TileIndex index) => Path.Combine(directory, index.Level.ToString(), index.Col.ToString(), $"{index.Row}.tile");
    }
}
