using MapCorleon.Models;
using Microsoft.EntityFrameworkCore;
namespace MapCorleon.Data;

public interface IRepository<T> where T : class, IEntity { Task<T?> GetAsync(int id, CancellationToken ct = default); IQueryable<T> Query(); Task AddAsync(T value, CancellationToken ct = default); void Remove(T value); Task SaveAsync(CancellationToken ct = default); }
public sealed class EfRepository<T>(AppDbContext db) : IRepository<T> where T : class, IEntity { public Task<T?> GetAsync(int id, CancellationToken ct = default) => db.Set<T>().FirstOrDefaultAsync(x => x.Id == id, ct); public IQueryable<T> Query() => db.Set<T>().AsQueryable(); public async Task AddAsync(T value, CancellationToken ct = default) => await db.AddAsync(value, ct); public void Remove(T value) => db.Remove(value); public Task SaveAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct); }
