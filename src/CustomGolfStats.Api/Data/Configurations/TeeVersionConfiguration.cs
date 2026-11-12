using CustomGolfStats.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomGolfStats.Api.Data.Configurations;

public sealed class TeeVersionConfiguration : IEntityTypeConfiguration<TeeVersion>
{
    public void Configure(EntityTypeBuilder<TeeVersion> builder)
    {
        builder.Property(v => v.Rating).HasPrecision(4, 1);
        builder.Property(v => v.FrontRating).HasPrecision(4, 1);
        builder.Property(v => v.BackRating).HasPrecision(4, 1);
        builder.HasIndex(v => new { v.TeeId, v.EffectiveFrom }).IsUnique();
        builder.HasOne(v => v.Tee).WithMany(t => t.Versions).HasForeignKey(v => v.TeeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(v => v.ChangedByAccount).WithMany()
            .HasForeignKey(v => v.ChangedByAccountId).OnDelete(DeleteBehavior.SetNull);
    }
}
