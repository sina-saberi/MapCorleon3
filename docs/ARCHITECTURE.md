# Architecture overview

MapCorleon is a .NET 8 WPF application organized by feature and layer. `Views` and `Dialogs` contain presentation only; `ViewModels` expose observable state and commands; `Services` own use cases; `Data` contains EF Core and repository abstractions; and `Models` defines the domain. The generic repository provides a replaceable persistence seam while purpose-built services prevent query logic leaking into controls. Microsoft DI wires the graph in `App.xaml.cs`. A small event bus is the cross-component selection channel.

## Persistence

SQLite is used through EF Core. The model maps exactly to `Games`, `Maps`, `Map Config`, `Groups`, `Categories`, `Locations`, `Media`, `Timelines`, `Pins`, and `Marked Locations`, with the required foreign keys, cascade behavior, uniqueness constraints, and lookup indexes. Mark/unmark and custom-location edits call `SaveChangesAsync` in their operation, so no global save exists. On first start a minimal map and mandatory default timeline are seeded. Timeline replacement creates `Default` if the submitted collection is empty.

Settings are JSON outside the database so the database directory can be changed safely. Saving settings creates new directories but never deletes or truncates old data. Mark filtering and category visibility are persisted; search text is deliberately transient.

## Map rendering choice

The selected library is **Mapsui 5.0.2 for WPF**. Mapsui uses SkiaSharp for batched rendering and BruTile for raster tile scheduling, addressing and caching. This avoids the performance cost of one WPF `UIElement` per marker that affects GMap.NET-style marker overlays. Mapsui's maintained samples include a custom renderer for one million points, so 3,000 point features are comfortably within the intended rendering model; MapCorleon additionally reuses one immutable image style per category and coalesces collection notifications into one layer rebuild. MapGenie locations are `PointFeature` instances in a `MemoryLayer`, and Mapsui performs viewport querying, pan/zoom and hit testing. The WPF `MapControl` raises map-info taps which are mapped back to the domain `Location`, making every rendered sprite clickable.

MapGenie's PNG sprite atlas is rendered with Mapsui v5 `ImageStyle` and `BitmapRegion`. The imported category rectangle (`x`, `y`, `width`, `height`, `pixelRatio`) selects the correct sprite for every location; there is no generic circle unless a category genuinely has no sprite metadata. Remote atlases are fetched once through the proxy-aware HTTP client, stored under the configured storage directory, and then supplied to Mapsui as a local file source.

## Offline tiles

Mapsui owns tile calculation and fetch scheduling. Its BruTile `HttpTileSource` receives the imported MapGenie `{z}/{x}/{y}` template, the proxy-aware shared `HttpClient`, and a MapCorleon `IPersistentCache<byte[]>` adapter. Tiles are isolated by a hash of the provider template under `tiles/mapsui/{providerHash}/{zoom}/{x}/{y}.tile`. The default TTL is 30 days and expired tiles are lazily refetched.

## Media

Uploads are SHA-256 hashed while streaming. Canonical blobs live in `storage/blobs/{hash}.{ext}` and are copied into `storage/locations/{locationId}/` for location-scoped organization. The hash index and existence checks deduplicate identical bytes across locations. A future migration can replace local copies with hard links without affecting database paths or `IMediaService`.

## UI composition and performance

`HeaderView`, `LeftSidebarView`, `MapView`, and `RightSidebarView` are independent controls bound to a shell state coordinator. Dialogs keep high-density actions out of the header. Empty result collections render no containers or messages. Async commands and the global busy overlay provide loading feedback. Sidebars handle pointer events, and marker previews mark events handled, preventing clicks from bubbling to coordinate-pick map behavior. The native `Window` is retained so minimize, maximize, close, snapping, accessibility, and theme decorations remain controlled by Windows.

## MapGenie import

`MapCorleon.Import` is a UI- and database-independent class library. `MapGenieImportProvider` validates the three import inputs, deserializes the MapGenie document and sprite-atlas JSON, maps them to normalized import records, derives the adjacent/local or CDN sprite PNG, and reports progress. It delegates persistence through `IMapImportSink`; the WPF application supplies `EfMapImportSink`.

The EF sink performs a transactionally consistent upsert using MapGenie external IDs scoped to their parents. Existing games, maps, groups, categories, locations, and media are updated; missing records are added; user marks and custom records are retained. Legacy rows without external IDs are matched by stable names/coordinates before adding new records. Schema initialization adds the nullable import identity and sprite metadata columns without removing data. On success, the shell reloads and selects the imported map so sidebars, locations, timelines, counts, and filters immediately reflect the result.
