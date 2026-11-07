# Architecture overview

MapCorleon is a .NET 8 WPF application organized by feature and layer. `Views` and `Dialogs` contain presentation only; `ViewModels` expose observable state and commands; `Services` own use cases; `Data` contains EF Core and repository abstractions; and `Models` defines the domain. The generic repository provides a replaceable persistence seam while purpose-built services prevent query logic leaking into controls. Microsoft DI wires the graph in `App.xaml.cs`. A small event bus is the cross-component selection channel.

## Persistence

SQLite is used through EF Core. The model maps exactly to `Games`, `Maps`, `Map Config`, `Groups`, `Categories`, `Locations`, `Media`, `Timelines`, `Pins`, and `Marked Locations`, with the required foreign keys, cascade behavior, uniqueness constraints, and lookup indexes. Mark/unmark and custom-location edits call `SaveChangesAsync` in their operation, so no global save exists. On startup the bundled catalog creates missing maps, each with an empty default timeline. Timeline replacement creates `Default` if the submitted collection is empty.

Settings are JSON outside the database. One `DataDirectory` setting owns `mapcorleon.db`, `tiles/`, and `storage/`; changing it takes effect after restart and never moves or deletes existing files automatically. Mark filtering and category visibility are persisted; search text is deliberately transient.

## Map rendering choice

The selected library is **Mapsui 5.0.2 for WPF**. Mapsui uses SkiaSharp for batched rendering and BruTile for raster tile scheduling, addressing and caching. This avoids the performance cost of one WPF `UIElement` per marker that affects GMap.NET-style marker overlays. Mapsui's maintained samples include a custom renderer for one million points, so 3,000 point features are comfortably within the intended rendering model; MapCorleon additionally reuses one immutable image style per category and coalesces collection notifications into one layer rebuild. MapGenie locations are `PointFeature` instances in a `MemoryLayer`, and Mapsui performs viewport querying, pan/zoom and hit testing. The WPF `MapControl` raises map-info taps which are mapped back to the domain `Location`, making every rendered sprite clickable.

The catalog carries category icon fonts and their glyph layers, or sprite atlases and rectangles for games that use sprites. The sidebar and location details render those assets directly from the packaged catalog. Mapsui uses a bitmap region for map markers when an atlas is available.

## Offline tiles

Mapsui owns tile calculation and fetch scheduling. Its BruTile `HttpTileSource` receives the provider's `{z}/{x}/{y}` template, while the actual HTTP request is delegated to the selected `IMapProvider`. Tiles are isolated by a hash of the template under `tiles/mapsui/{providerHash}/{zoom}/{x}/{y}.tile`. Cached tiles never expire and are always loaded locally unless the user explicitly removes them.

## Media

Uploads are SHA-256 hashed while streaming. User-added media uses `storage/blobs/{hash}.{ext}` and location-scoped copies. Imported provider media initially retains its remote URL so it remains available on demand. When offline-media download is selected, a background job stores it under `storage/providers/{provider}/media` and replaces the database URL with the local path.

## UI composition and performance

`HeaderView`, `LeftSidebarView`, `MapView`, and `RightSidebarView` are independent controls bound to a shell state coordinator. Dialogs keep high-density actions out of the header. Empty result collections render no containers or messages. Async commands and the global busy overlay provide loading feedback. Sidebars handle pointer events, and marker previews mark events handled, preventing clicks from bubbling to coordinate-pick map behavior. The native `Window` is retained so minimize, maximize, close, snapping, accessibility, and theme decorations remain controlled by Windows.

## Catalog and download providers

The developer-only exporter under `tools/MapCorleon.CatalogExport` reads the rendered MapGenie map page with Chrome or Edge and produces a portable `CatalogMap` JSON document. The published desktop app reads those documents from the `Catalog` folder. The MapGenie provider in the desktop app fetches tiles and the local KCD2 provider can still read Sedletz from an installed game archive.

Catalog maps apply in a SQLite transaction. The provider map ID resolves the existing map, and stable provider location IDs preserve local progress even if a location changes category. An installed revision is recorded with the update and the same revision is skipped on subsequent launches. Omitted locations, custom locations, timelines, pins, marks, and launch settings are retained.

Download Manager uses the same game and map picker as the main app. It queues media and tiles separately for the selected map. Download state and cached files live in the user's data directory.
