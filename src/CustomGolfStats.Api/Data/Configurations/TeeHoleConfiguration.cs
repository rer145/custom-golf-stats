using CustomGolfStats.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomGolfStats.Api.Data.Configurations;

public sealed class TeeHoleConfiguration : IEntityTypeConfiguration<TeeHole>
{
    public void Configure(EntityTypeBuilder<TeeHole> builder)
    {
        builder.HasIndex(h => new { h.TeeVersionId, h.Number }).IsUnique();
        builder.HasOne(h => h.TeeVersion).WithMany(v => v.Holes)
            .HasForeignKey(h => h.TeeVersionId).OnDelete(DeleteBehavior.Cascade);
    }
}
