using Flexischools.Api.Infrastructure.Idempotency;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flexischools.Api.Infrastructure.Persistence.Configurations;

public class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.HasKey(r => r.Key);
        builder.Property(r => r.Key).HasMaxLength(256);
        builder.Property(r => r.ResponseBody).IsRequired();
        builder.Property(r => r.StatusCode).IsRequired();
        builder.HasIndex(r => r.ExpiresAtUtc); // For efficient TTL cleanup
    }
}
