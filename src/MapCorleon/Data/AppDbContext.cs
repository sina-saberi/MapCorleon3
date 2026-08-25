using MapCorleon.Models;
using Microsoft.EntityFrameworkCore;
namespace MapCorleon.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Game> Games => Set<Game>(); public DbSet<Map> Maps => Set<Map>(); public DbSet<MapConfig> MapConfigs => Set<MapConfig>(); public DbSet<Group> Groups => Set<Group>(); public DbSet<Category> Categories => Set<Category>(); public DbSet<Location> Locations => Set<Location>(); public DbSet<Media> Media => Set<Media>(); public DbSet<Timeline> Timelines => Set<Timeline>(); public DbSet<Pin> Pins => Set<Pin>(); public DbSet<MarkedLocation> MarkedLocations => Set<MarkedLocation>(); public DbSet<DownloadTask> DownloadTasks => Set<DownloadTask>(); public DbSet<AppLog> AppLogs => Set<AppLog>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Game>().ToTable("Games"); b.Entity<Map>().ToTable("Maps"); b.Entity<MapConfig>().ToTable("Map Config"); b.Entity<Group>().ToTable("Groups"); b.Entity<Category>().ToTable("Categories"); b.Entity<Location>().ToTable("Locations"); b.Entity<Media>().ToTable("Media"); b.Entity<Timeline>().ToTable("Timelines"); b.Entity<Pin>().ToTable("Pins"); b.Entity<MarkedLocation>().ToTable("Marked Locations"); b.Entity<DownloadTask>().ToTable("Download Tasks"); b.Entity<AppLog>().ToTable("Application Logs");
        b.Entity<Category>().Ignore(x => x.TotalLocationCount).Ignore(x => x.PickedLocationCount).Ignore(x => x.ProgressText);
        b.Entity<Location>().Ignore(x => x.HasDescription);
        b.Entity<DownloadTask>().Ignore(x => x.Percentage).Ignore(x => x.MapDisplayName).Ignore(x => x.DownloadKinds)
            .Ignore(x => x.ProgressText).Ignore(x => x.CanAct).Ignore(x => x.ActionText);
        b.Entity<Map>().HasOne(x => x.Game).WithMany(x => x.Maps).HasForeignKey(x => x.GameId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<DownloadTask>().HasOne(x => x.Map).WithMany(x => x.DownloadTasks).HasForeignKey(x => x.MapId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<MapConfig>().HasOne(x => x.Map).WithOne(x => x.Config).HasForeignKey<MapConfig>(x => x.MapId).OnDelete(DeleteBehavior.Cascade); b.Entity<MapConfig>().HasIndex(x => x.MapId).IsUnique();
        b.Entity<Group>().HasOne(x => x.Map).WithMany(x => x.Groups).HasForeignKey(x => x.MapId); b.Entity<Category>().HasOne(x => x.Group).WithMany(x => x.Categories).HasForeignKey(x => x.GroupId); b.Entity<Location>().HasOne(x => x.Category).WithMany(x => x.Locations).HasForeignKey(x => x.CategoryId); b.Entity<Location>().HasOne(x => x.Timeline).WithMany(x => x.CustomLocations).HasForeignKey(x => x.TimelineId).OnDelete(DeleteBehavior.Cascade); b.Entity<Media>().HasOne(x => x.Location).WithMany(x => x.Media).HasForeignKey(x => x.LocationId); b.Entity<Timeline>().HasOne(x => x.Map).WithMany(x => x.Timelines).HasForeignKey(x => x.MapId); b.Entity<Pin>().HasOne(x => x.Timeline).WithMany(x => x.Pins).HasForeignKey(x => x.TimelineId); b.Entity<MarkedLocation>().HasOne(x => x.Location).WithMany(x => x.Marks).HasForeignKey(x => x.LocationId); b.Entity<MarkedLocation>().HasOne(x => x.Timeline).WithMany(x => x.Marks).HasForeignKey(x => x.TimelineId);
        b.Entity<MarkedLocation>().HasIndex(x => new { x.TimelineId, x.LocationId }).IsUnique(); b.Entity<Media>().HasIndex(x => x.ContentHash); b.Entity<Location>().HasIndex(x => new { x.CategoryId, x.Title }); b.Entity<Timeline>().HasIndex(x => new { x.MapId, x.SortOrder });
        b.Entity<DownloadTask>().HasIndex(x => x.UpdatedAtUtc);
        b.Entity<AppLog>().HasIndex(x => new { x.Type, x.CreatedAtUtc });
        b.Entity<Map>().HasIndex(x => new { x.GameId, x.ExternalId }).IsUnique(); b.Entity<Group>().HasIndex(x => new { x.MapId, x.ExternalId }).IsUnique(); b.Entity<Category>().HasIndex(x => new { x.GroupId, x.ExternalId }).IsUnique(); b.Entity<Location>().HasIndex(x => new { x.CategoryId, x.ExternalId }).IsUnique(); b.Entity<Media>().HasIndex(x => new { x.LocationId, x.ExternalId }).IsUnique();
    }
}

public static class DbSchemaInitializer
{
    public static async Task InitializeAsync(AppDbContext db, CancellationToken ct = default)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON;", ct);
            await db.Database.ExecuteSqlRawAsync(SchemaSql, ct);
            await EnsureImportColumnsAsync(db, ct);
            await EnsureTimelineMarksAsync(db, ct);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static async Task EnsureTimelineMarksAsync(AppDbContext db, CancellationToken ct)
    {
        await using var check = db.Database.GetDbConnection().CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Marked Locations') WHERE name = 'TimelineId'";
        if (Convert.ToInt32(await check.ExecuteScalarAsync(ct)) == 0)
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Marked Locations\" ADD COLUMN \"TimelineId\" INTEGER NULL;", ct);
        await db.Database.ExecuteSqlRawAsync("""
UPDATE "Locations" SET "TimelineId" = (SELECT t."Id" FROM "Timelines" t JOIN "Groups" g ON g."MapId"=t."MapId" JOIN "Categories" c ON c."GroupId"=g."Id" WHERE c."Id"="Locations"."CategoryId" ORDER BY t."SortOrder" LIMIT 1) WHERE "IsCustom"=1 AND "TimelineId" IS NULL;
UPDATE "Marked Locations" SET "TimelineId" = COALESCE(
 (SELECT t."Id" FROM "Timelines" t JOIN "Maps" m ON m."Id"=t."MapId" JOIN "Groups" g ON g."MapId"=m."Id" JOIN "Categories" c ON c."GroupId"=g."Id" JOIN "Locations" l ON l."CategoryId"=c."Id" WHERE l."Id"="Marked Locations"."LocationId" ORDER BY t."SortOrder" LIMIT 1),
 (SELECT "Id" FROM "Timelines" ORDER BY "Id" LIMIT 1)) WHERE "TimelineId" IS NULL;
DROP INDEX IF EXISTS "IX_Marked_Locations_LocationId";
CREATE UNIQUE INDEX IF NOT EXISTS "IX_Marked_Locations_TimelineId_LocationId" ON "Marked Locations" ("TimelineId", "LocationId");
""", ct);
    }

    private static async Task EnsureImportColumnsAsync(AppDbContext db, CancellationToken ct)
    {
        var columns = new (string Table, string Name, string Definition)[]
        {
            ("Maps", "ExternalId", "INTEGER NULL"), ("Groups", "ExternalId", "INTEGER NULL"),
            ("Categories", "ExternalId", "INTEGER NULL"), ("Categories", "IconKey", "TEXT NULL"),
            ("Categories", "IconX", "INTEGER NULL"), ("Categories", "IconY", "INTEGER NULL"),
            ("Categories", "IconWidth", "INTEGER NULL"), ("Categories", "IconHeight", "INTEGER NULL"),
            ("Categories", "IconPixelRatio", "REAL NULL"), ("Locations", "ExternalId", "INTEGER NULL"),
            ("Locations", "TimelineId", "INTEGER NULL"),
            ("Media", "ExternalId", "INTEGER NULL"), ("Map Config", "TilePattern", "TEXT NULL"),
            ("Media", "SourceUrl", "TEXT NULL"),
            ("Map Config", "Provider", "TEXT NOT NULL DEFAULT 'mapgenie'"),
            ("Map Config", "TileBoundsJson", "TEXT NULL"),
            ("Map Config", "MinZoom", "INTEGER NULL"), ("Map Config", "MaxZoom", "INTEGER NULL"),
            ("Map Config", "SpriteImagePath", "TEXT NULL"),
            ("Map Config", "LaunchExecutablePath", "TEXT NULL"),
            ("Map Config", "LaunchArguments", "TEXT NULL"),
            ("Map Config", "LaunchWorkingDirectory", "TEXT NULL"),
            ("Download Tasks", "SourcePageUrl", "TEXT NULL")
        };
        foreach (var column in columns)
        {
            await using var check = db.Database.GetDbConnection().CreateCommand();
            check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{column.Table.Replace("'", "''")}') WHERE name = $name";
            var parameter = check.CreateParameter(); parameter.ParameterName = "$name"; parameter.Value = column.Name; check.Parameters.Add(parameter);
            if (Convert.ToInt32(await check.ExecuteScalarAsync(ct)) == 0)
            {
                await using var alter = db.Database.GetDbConnection().CreateCommand();
                alter.CommandText = $"ALTER TABLE \"{column.Table}\" ADD COLUMN \"{column.Name}\" {column.Definition};";
                await alter.ExecuteNonQueryAsync(ct);
            }
        }
        await db.Database.ExecuteSqlRawAsync("""
CREATE UNIQUE INDEX IF NOT EXISTS "IX_Maps_GameId_ExternalId" ON "Maps" ("GameId", "ExternalId");
CREATE UNIQUE INDEX IF NOT EXISTS "IX_Groups_MapId_ExternalId" ON "Groups" ("MapId", "ExternalId");
CREATE UNIQUE INDEX IF NOT EXISTS "IX_Categories_GroupId_ExternalId" ON "Categories" ("GroupId", "ExternalId");
CREATE UNIQUE INDEX IF NOT EXISTS "IX_Locations_CategoryId_ExternalId" ON "Locations" ("CategoryId", "ExternalId");
CREATE UNIQUE INDEX IF NOT EXISTS "IX_Media_LocationId_ExternalId" ON "Media" ("LocationId", "ExternalId");
UPDATE "Media" SET "SourceUrl"="RelativePath" WHERE "SourceUrl" IS NULL AND ("RelativePath" LIKE 'http://%' OR "RelativePath" LIKE 'https://%');
""", ct);
    }

    // EnsureCreated does not evolve an existing database. This script is
    // intentionally idempotent and never removes or replaces user data.
    private const string SchemaSql = """
CREATE TABLE IF NOT EXISTS "Games" ("Id" INTEGER NOT NULL CONSTRAINT "PK_Games" PRIMARY KEY AUTOINCREMENT, "Name" TEXT NOT NULL);
CREATE TABLE IF NOT EXISTS "Maps" ("Id" INTEGER NOT NULL CONSTRAINT "PK_Maps" PRIMARY KEY AUTOINCREMENT, "GameId" INTEGER NOT NULL, "Name" TEXT NOT NULL, CONSTRAINT "FK_Maps_Games_GameId" FOREIGN KEY ("GameId") REFERENCES "Games" ("Id") ON DELETE CASCADE);
CREATE TABLE IF NOT EXISTS "Map Config" ("Id" INTEGER NOT NULL CONSTRAINT "PK_Map_Config" PRIMARY KEY AUTOINCREMENT, "MapId" INTEGER NOT NULL, "MinLatitude" REAL NOT NULL, "MaxLatitude" REAL NOT NULL, "MinLongitude" REAL NOT NULL, "MaxLongitude" REAL NOT NULL, "DefaultZoom" REAL NOT NULL, "LaunchExecutablePath" TEXT NULL, "LaunchArguments" TEXT NULL, "LaunchWorkingDirectory" TEXT NULL, CONSTRAINT "FK_Map_Config_Maps_MapId" FOREIGN KEY ("MapId") REFERENCES "Maps" ("Id") ON DELETE CASCADE);
CREATE TABLE IF NOT EXISTS "Groups" ("Id" INTEGER NOT NULL CONSTRAINT "PK_Groups" PRIMARY KEY AUTOINCREMENT, "MapId" INTEGER NOT NULL, "Name" TEXT NOT NULL, "IsVisible" INTEGER NOT NULL DEFAULT 1, CONSTRAINT "FK_Groups_Maps_MapId" FOREIGN KEY ("MapId") REFERENCES "Maps" ("Id") ON DELETE CASCADE);
CREATE TABLE IF NOT EXISTS "Categories" ("Id" INTEGER NOT NULL CONSTRAINT "PK_Categories" PRIMARY KEY AUTOINCREMENT, "GroupId" INTEGER NOT NULL, "Name" TEXT NOT NULL, "IsVisible" INTEGER NOT NULL DEFAULT 1, CONSTRAINT "FK_Categories_Groups_GroupId" FOREIGN KEY ("GroupId") REFERENCES "Groups" ("Id") ON DELETE CASCADE);
CREATE TABLE IF NOT EXISTS "Locations" ("Id" INTEGER NOT NULL CONSTRAINT "PK_Locations" PRIMARY KEY AUTOINCREMENT, "CategoryId" INTEGER NOT NULL, "TimelineId" INTEGER NULL, "Title" TEXT NOT NULL, "DescriptionMarkdown" TEXT NOT NULL DEFAULT '', "Latitude" REAL NOT NULL, "Longitude" REAL NOT NULL, "IsCustom" INTEGER NOT NULL DEFAULT 0, CONSTRAINT "FK_Locations_Categories_CategoryId" FOREIGN KEY ("CategoryId") REFERENCES "Categories" ("Id") ON DELETE CASCADE, CONSTRAINT "FK_Locations_Timelines_TimelineId" FOREIGN KEY ("TimelineId") REFERENCES "Timelines" ("Id") ON DELETE CASCADE);
CREATE TABLE IF NOT EXISTS "Media" ("Id" INTEGER NOT NULL CONSTRAINT "PK_Media" PRIMARY KEY AUTOINCREMENT, "LocationId" INTEGER NOT NULL, "ContentHash" TEXT NOT NULL, "RelativePath" TEXT NOT NULL, "SourceUrl" TEXT NULL, "Caption" TEXT NULL, CONSTRAINT "FK_Media_Locations_LocationId" FOREIGN KEY ("LocationId") REFERENCES "Locations" ("Id") ON DELETE CASCADE);
CREATE TABLE IF NOT EXISTS "Timelines" ("Id" INTEGER NOT NULL CONSTRAINT "PK_Timelines" PRIMARY KEY AUTOINCREMENT, "MapId" INTEGER NOT NULL, "Name" TEXT NOT NULL, "SortOrder" INTEGER NOT NULL, CONSTRAINT "FK_Timelines_Maps_MapId" FOREIGN KEY ("MapId") REFERENCES "Maps" ("Id") ON DELETE CASCADE);
CREATE TABLE IF NOT EXISTS "Pins" ("Id" INTEGER NOT NULL CONSTRAINT "PK_Pins" PRIMARY KEY AUTOINCREMENT, "TimelineId" INTEGER NOT NULL, "LocationId" INTEGER NULL, "Latitude" REAL NOT NULL, "Longitude" REAL NOT NULL, "Title" TEXT NULL, CONSTRAINT "FK_Pins_Timelines_TimelineId" FOREIGN KEY ("TimelineId") REFERENCES "Timelines" ("Id") ON DELETE CASCADE, CONSTRAINT "FK_Pins_Locations_LocationId" FOREIGN KEY ("LocationId") REFERENCES "Locations" ("Id"));
CREATE TABLE IF NOT EXISTS "Marked Locations" ("Id" INTEGER NOT NULL CONSTRAINT "PK_Marked_Locations" PRIMARY KEY AUTOINCREMENT, "LocationId" INTEGER NOT NULL, "TimelineId" INTEGER NOT NULL, "MarkedAtUtc" TEXT NOT NULL, CONSTRAINT "FK_Marked_Locations_Locations_LocationId" FOREIGN KEY ("LocationId") REFERENCES "Locations" ("Id") ON DELETE CASCADE, CONSTRAINT "FK_Marked_Locations_Timelines_TimelineId" FOREIGN KEY ("TimelineId") REFERENCES "Timelines" ("Id") ON DELETE CASCADE);
CREATE TABLE IF NOT EXISTS "Download Tasks" ("Id" INTEGER NOT NULL CONSTRAINT "PK_Download_Tasks" PRIMARY KEY AUTOINCREMENT, "MapId" INTEGER NOT NULL, "Provider" TEXT NOT NULL, "SourcePageUrl" TEXT NULL, "DownloadMedia" INTEGER NOT NULL, "DownloadTiles" INTEGER NOT NULL, "Status" TEXT NOT NULL, "CompletedItems" INTEGER NOT NULL, "TotalItems" INTEGER NOT NULL, "FailedItems" INTEGER NOT NULL, "Error" TEXT NULL, "CreatedAtUtc" TEXT NOT NULL, "UpdatedAtUtc" TEXT NOT NULL, CONSTRAINT "FK_Download_Tasks_Maps_MapId" FOREIGN KEY ("MapId") REFERENCES "Maps" ("Id") ON DELETE CASCADE);
CREATE TABLE IF NOT EXISTS "Application Logs" ("Id" INTEGER NOT NULL CONSTRAINT "PK_Application_Logs" PRIMARY KEY AUTOINCREMENT, "Type" TEXT NOT NULL, "Level" TEXT NOT NULL, "Message" TEXT NOT NULL, "Details" TEXT NULL, "CreatedAtUtc" TEXT NOT NULL);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_Map_Config_MapId" ON "Map Config" ("MapId");
CREATE INDEX IF NOT EXISTS "IX_Maps_GameId" ON "Maps" ("GameId");
CREATE INDEX IF NOT EXISTS "IX_Groups_MapId" ON "Groups" ("MapId");
CREATE INDEX IF NOT EXISTS "IX_Categories_GroupId" ON "Categories" ("GroupId");
CREATE INDEX IF NOT EXISTS "IX_Locations_CategoryId_Title" ON "Locations" ("CategoryId", "Title");
CREATE INDEX IF NOT EXISTS "IX_Media_ContentHash" ON "Media" ("ContentHash");
CREATE INDEX IF NOT EXISTS "IX_Media_LocationId" ON "Media" ("LocationId");
CREATE INDEX IF NOT EXISTS "IX_Timelines_MapId_SortOrder" ON "Timelines" ("MapId", "SortOrder");
CREATE INDEX IF NOT EXISTS "IX_Pins_TimelineId" ON "Pins" ("TimelineId");
CREATE INDEX IF NOT EXISTS "IX_Pins_LocationId" ON "Pins" ("LocationId");
CREATE INDEX IF NOT EXISTS "IX_Download_Tasks_UpdatedAtUtc" ON "Download Tasks" ("UpdatedAtUtc");
CREATE INDEX IF NOT EXISTS "IX_Application_Logs_Type_CreatedAtUtc" ON "Application Logs" ("Type", "CreatedAtUtc");
""";
}

public static class DbSeeder { public static async Task SeedAsync(AppDbContext db) { if (await db.Games.AnyAsync()) return; var game = new Game { Name = "Demo Game" }; var map = new Map { Name = "World Map", Game = game }; map.Config = new MapConfig { Map = map, MinLatitude = -85, MaxLatitude = 85, MinLongitude = -180, MaxLongitude = 180, DefaultZoom = 2 }; var group = new Group { Name = "Discoveries", Map = map }; var category = new Category { Name = "Landmarks", Group = group }; category.Locations.Add(new Location { Title = "Welcome Point", DescriptionMarkdown = "Select a location to see its **details**.", Latitude = 35.6892, Longitude = 51.389, Category = category }); map.Timelines.Add(new Timeline { Name = "Default", SortOrder = 0 }); db.Add(game); await db.SaveChangesAsync(); } }
