using CustomGolfStats.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomGolfStats.Api.Data.Configurations;

public sealed class ShotEntityConfiguration : IEntityTypeConfiguration<ShotEntity>
{
    public void Configure(EntityTypeBuilder<ShotEntity> builder)
    {
        builder.ToTable("shots");
        builder.HasIndex(s => new { s.HoleResultId, s.Sequence }).IsUnique();
        builder.HasOne(s => s.HoleResult).WithMany(h => h.Shots).HasForeignKey(s => s.HoleResultId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(s => s.Round).WithMany().HasForeignKey(s => s.RoundId).OnDelete(DeleteBehavior.Cascade);
    }
}
