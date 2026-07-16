using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
using MapCorleon.Contracts;
using MapCorleon.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System.Windows.Threading;

namespace MapCorleon.Views;

public partial class MapView
{
    private const string TileUserAgent = "MapCorleon/1.0 (desktop map client)";
    private ShellViewModel? viewModel;
    private MemoryLayer? locationLayer;
    private GlobalSphericalMercator? tileSchema;
    private string? spriteSource;
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
        var template = config.TilePattern;
        var settings = App.Services.GetRequiredService<ISettingsService>().Current;
        var cacheName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(template)))[..16];
        var cache = new MapTileCache(Path.Combine(settings.TileDirectory, "mapsui", cacheName));
        var provider = App.Services.GetRequiredService<IMapProviderResolver>().Get(config.Provider);
        var tileSource = new HttpTileSource(
            tileSchema,
            template,
            name: provider.Key,
            persistentCache: cache,
            configureHttpRequestMessage: request =>
            {
                request.Headers.UserAgent.ParseAdd(TileUserAgent);
                request.Headers.Accept.ParseAdd("image/avif,image/webp,image/apng,image/*,*/*;q=0.8");
            });
        var tileBounds = string.IsNullOrWhiteSpace(config.TileBoundsJson)
            ? null
            : JsonSerializer.Deserialize<Dictionary<int, MapCorleon.Contracts.TileRange>>(config.TileBoundsJson);
        if (tileBounds is not null && tileBounds.TryGetValue(minimumZoom, out var minimumBounds))
            tileSchema.Extent = TileBoundsToExtent(tileSchema, minimumZoom, minimumBounds);
        var tileHttpClient = new HttpClient(new ProviderTileHandler(provider, template, tileBounds));
        tileHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(TileUserAgent);
        var map = new Mapsui.Map { CRS = "EPSG:3857", BackColor = new Color(17, 25, 35) };
        map.Layers.Add(new TileLayer(tileSource, httpClient: tileHttpClient) { Name = $"{provider.Key} Tiles" });
        MapControl.Map = map;
        RebuildLocations();
        if (!string.IsNullOrWhiteSpace(config.SpriteImagePath) && File.Exists(config.SpriteImagePath))
            spriteSource = new Uri(Path.GetFullPath(config.SpriteImagePath)).AbsoluteUri;
        RebuildLocations();

        var latitude = (config.MinLatitude + config.MaxLatitude) / 2;
        var longitude = (config.MinLongitude + config.MaxLongitude) / 2;
        var projectedCenter = SphericalMercator.FromLonLat(longitude, latitude);
        var center = new MPoint(projectedCenter.x, projectedCenter.y);
        var zoom = Math.Clamp((int)Math.Round(config.DefaultZoom), minimumZoom, maximumZoom);
        map.Navigator.CenterOnAndZoomTo(center, tileSchema.Resolutions[zoom].UnitsPerPixel, 0);
        StatusText.Text = $"Mapsui • {viewModel?.VisibleLocations.Count ?? 0} locations";
    }

    private static Extent TileBoundsToExtent(
        GlobalSphericalMercator schema,
        int zoom,
        MapCorleon.Contracts.TileRange bounds)
    {
        var resolution = schema.Resolutions[zoom].UnitsPerPixel;
        var tileSpan = resolution * 256d;
        var minX = schema.OriginX + bounds.MinX * tileSpan;
        var maxX = schema.OriginX + (bounds.MaxX + 1) * tileSpan;
        var maxY = schema.OriginY - bounds.MinY * tileSpan;
        var minY = schema.OriginY - (bounds.MaxY + 1) * tileSpan;
        return new Extent(minX, minY, maxX, maxY);
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

    private sealed class MapTileCache(string directory) : IPersistentCache<byte[]>
    {
        public void Add(TileIndex index, byte[] tile)
        {
            var path = GetPath(index); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (!File.Exists(path)) File.WriteAllBytes(path, tile);
        }
        public byte[]? Find(TileIndex index)
        {
            var path = GetPath(index);
            if (!File.Exists(path)) return null;
            return File.ReadAllBytes(path);
        }
        public void Remove(TileIndex index) { var path = GetPath(index); if (File.Exists(path)) File.Delete(path); }
        private string GetPath(TileIndex index) => Path.Combine(directory, index.Level.ToString(), index.Col.ToString(), $"{index.Row}.tile");
    }

    private sealed class ProviderTileHandler : HttpMessageHandler
    {
        private static readonly byte[] TransparentTile = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M/wHwAF/gL+Xw4AAAAASUVORK5CYII=");
        private readonly IMapProvider provider;
        private readonly IReadOnlyDictionary<int, MapCorleon.Contracts.TileRange>? bounds;
        private readonly Regex tileUrl;

        public ProviderTileHandler(IMapProvider provider, string template, IReadOnlyDictionary<int, MapCorleon.Contracts.TileRange>? bounds)
        {
            this.provider = provider;
            this.bounds = bounds;
            tileUrl = new Regex(
                "^" + Regex.Escape(template)
                    .Replace("\\{z}", "(?<z>\\d+)")
                    .Replace("\\{x}", "(?<x>\\d+)")
                    .Replace("\\{y}", "(?<y>\\d+)") + "$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri is null) return TileResponse(TransparentTile);
            var url = request.RequestUri.ToString();
            var match = tileUrl.Match(url);
            if (bounds is not null && match.Success &&
                int.TryParse(match.Groups["z"].Value, out var zoom) &&
                int.TryParse(match.Groups["x"].Value, out var x) &&
                int.TryParse(match.Groups["y"].Value, out var y) &&
                (!bounds.TryGetValue(zoom, out var range) ||
                 x < range.MinX || x > range.MaxX || y < range.MinY || y > range.MaxY))
                return TileResponse(TransparentTile);

            var bytes = await provider.DownloadTileAsync(url, ct);
            return bytes is null
                ? TileResponse(TransparentTile)
                : TileResponse(bytes);
        }

        private static HttpResponseMessage TileResponse(byte[] bytes) => new(System.Net.HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
            {
                Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png") }
            }
        };
    }
}
