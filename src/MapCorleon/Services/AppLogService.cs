using MapCorleon.Data;
using MapCorleon.Models;
using Microsoft.EntityFrameworkCore;

namespace MapCorleon.Services;

public interface IAppLogService
{
    Task WriteAsync(string type, string level, string message, string? details = null);
    Task<IReadOnlyList<AppLog>> QueryAsync(string? type = null, int take = 250, CancellationToken ct = default);
}

public sealed class AppLogService(IDbContextFactory<AppDbContext> factory) : IAppLogService
{
    public async Task WriteAsync(string type, string level, string message, string? details = null)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync();
            db.AppLogs.Add(new AppLog
            {
                Type = type,
                Level = level,
                Message = message,
                Details = details,
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        catch
        {
            // Diagnostics must never break the operation they are recording.
        }
    }

    public async Task<IReadOnlyList<AppLog>> QueryAsync(string? type = null, int take = 250, CancellationToken ct = default)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var query = db.AppLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(type)) query = query.Where(x => x.Type == type);
        return await query.OrderByDescending(x => x.CreatedAtUtc)
            .Take(Math.Clamp(take, 1, 2000)).ToListAsync(ct);
    }
}
