using Flexischools.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flexischools.Api.Infrastructure.Idempotency;

/// <summary>
/// Persisted record of a previously completed idempotent request.
/// </summary>
public class IdempotencyRecord
{
    public string Key { get; set; } = string.Empty;       // The Idempotency-Key header value
    public string ResponseBody { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}

/// <summary>
/// Stores and retrieves idempotency records within the same DB transaction as the order creation.
/// TTL: 24 hours.
/// </summary>
public class IdempotencyService
{
    private readonly AppDbContext _db;
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    public IdempotencyService(AppDbContext db) => _db = db;

    public async Task<IdempotencyRecord?> GetAsync(string key, CancellationToken ct = default)
    {
        var record = await _db.IdempotencyRecords
            .FirstOrDefaultAsync(r => r.Key == key, ct);

        if (record is null) return null;
        if (record.ExpiresAtUtc < DateTime.UtcNow)
        {
            _db.IdempotencyRecords.Remove(record);
            await _db.SaveChangesAsync(ct);
            return null;
        }

        return record;
    }

    public async Task SaveAsync(string key, int statusCode, string responseBody, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        _db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Key = key,
            StatusCode = statusCode,
            ResponseBody = responseBody,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.Add(Ttl)
        });
        await _db.SaveChangesAsync(ct);
    }
}
