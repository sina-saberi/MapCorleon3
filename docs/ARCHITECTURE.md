# Architecture overview

MapCorleon is a .NET 8 WPF application organized by feature and layer. `Views` and `Dialogs` contain presentation only; `ViewModels` expose observable state and commands; `Services` own use cases; `Data` contains EF Core and repository abstractions; and `Models` defines the domain. The generic repository provides a replaceable persistence seam while purpose-built services prevent query logic leaking into controls. Microsoft DI wires the graph in `App.xaml.cs`. A small event bus is the cross-component selection channel.

## Persistence

SQLite is used through EF Core. The model maps exactly to `Games`, `Maps`, `Map Config`, `Groups`, `Categories`, `Locations`, `Media`, `Timelines`, `Pins`, and `Marked Locations`, with the required foreign keys, cascade behavior, uniqueness constraints, and lookup indexes. Mark/unmark and custom-location edits call `SaveChangesAsync` in their operation, so no global save exists. On first start a minimal map and mandatory default timeline are seeded. Timeline replacement creates `Default` if the submitted collection is empty.

Settings are JSON outside the database so the database directory can be changed safely. Saving settings creates new directories but never deletes or truncates old data. Mark filtering and category visibility are persisted; search text is deliberately transient.

## Map rendering choice

The selected library is **Mapsui 5.0.2 for WPF**. Mapsui uses SkiaSharp for batched rendering and BruTile for raster tile scheduling, addressing and caching. This avoids the performance cost of one WPF `UIElement` per marker that affects GMap.NET-style marker overlays. Mapsui's maintained samples include a custom renderer for one million points, so 3,000 point features are comfortably within the intended rendering model; MapCorleon additionally reuses one immutable image style per category and coalesces collection notifications into one layer rebuild. MapGenie locations are `PointFeature` instances in a `MemoryLayer`, and Mapsui performs viewport querying, pan/zoom and hit testing. The WPF `MapControl` raises map-info taps which are mapped back to the domain `Location`, making every rendered sprite clickable.

Provider sprite atlases are rendered with Mapsui v5 `ImageStyle` and `BitmapRegion`. The imported category rectangle (`x`, `y`, `width`, `height`, `pixelRatio`) selects the correct sprite for every location. Providers derive and download the atlas image and metadata from the map webpage, store them below `storage/providers/{provider}/sprites`, and persist only the local image path.

## Offline tiles

Mapsui owns tile calculation and fetch scheduling. Its BruTile `HttpTileSource` receives the provider's `{z}/{x}/{y}` template, while the actual HTTP request is delegated to the selected `IMapProvider`. Tiles are isolated by a hash of the template under `tiles/mapsui/{providerHash}/{zoom}/{x}/{y}.tile`; once downloaded they are always loaded locally unless the cache is explicitly cleared.

## Media

Uploads are SHA-256 hashed while streaming. User-added media uses `storage/blobs/{hash}.{ext}` and location-scoped copies. Imported provider media initially retains its remote URL so it remains available on demand. When offline-media download is selected, a background job stores it under `storage/providers/{provider}/media` and replaces the database URL with the local path.

## UI composition and performance

`HeaderView`, `LeftSidebarView`, `MapView`, and `RightSidebarView` are independent controls bound to a shell state coordinator. Dialogs keep high-density actions out of the header. Empty result collections render no containers or messages. Async commands and the global busy overlay provide loading feedback. Sidebars handle pointer events, and marker previews mark events handled, preventing clicks from bubbling to coordinate-pick map behavior. The native `Window` is retained so minimize, maximize, close, snapping, accessibility, and theme decorations remain controlled by Windows.

## Provider architecture

`MapCorleon.Contracts` contains provider-neutral import records plus `IMapProvider`, `IMapProviderResolver`, `IProviderStorage`, and `IMapImportSink`. `MapCorleon.MapGenie` contains every MapGenie URL, HTTP header, DTO, import rule, media/sprite acquisition rule, and tile downloader. The WPF application selects a provider by the key persisted in `MapConfig`; adding another provider means implementing `IMapProvider` in another class library and registering it with dependency injection.

The MapGenie importer accepts a public map webpage URL. Because branded pages may populate locations only after JavaScript execution, the provider opens the supplied page in installed Chrome or Edge, waits for the page itself to finish populating `window.mapData`, and then reads `window.mapData` and `window.game` from the rendered page context. It does not implement or call MapGenie data APIs directly. Optional media and complete bounded-tile downloads run after the database import through a singleton task coordinator. The header displays its status and disables new imports until it finishes.

The EF sink performs a transactionally consistent upsert using MapGenie external IDs scoped to their parents. Existing games, maps, groups, categories, locations, and media are updated; missing records are added; user marks and custom records are retained. Legacy rows without external IDs are matched by stable names/coordinates before adding new records. Schema initialization adds the nullable import identity and sprite metadata columns without removing data. On success, the shell reloads and selects the imported map so sidebars, locations, timelines, counts, and filters immediately reflect the result.
