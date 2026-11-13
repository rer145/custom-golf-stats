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
        // (hole_result_id, round_id) -> hole_results(id, round_id): the denormalised round id can't disagree.
        builder.HasOne(s => s.HoleResult).WithMany(h => h.Shots)
            .HasForeignKey(s => new { s.HoleResultId, s.RoundId })
            .HasPrincipalKey(h => new { h.Id, h.RoundId })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(s => s.Round).WithMany().HasForeignKey(s => s.RoundId).OnDelete(DeleteBehavior.Cascade);
    }
}
