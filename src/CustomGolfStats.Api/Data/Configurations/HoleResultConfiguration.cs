using CustomGolfStats.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomGolfStats.Api.Data.Configurations;

public sealed class HoleResultConfiguration : IEntityTypeConfiguration<HoleResult>
{
    public void Configure(EntityTypeBuilder<HoleResult> builder)
    {
        builder.HasIndex(h => new { h.RoundId, h.EntrySequence }).IsUnique();
        // Target of the shots' composite foreign key, so a shot's round is always its hole result's round.
        builder.HasAlternateKey(h => new { h.Id, h.RoundId });
        builder.HasOne(h => h.Round).WithMany(r => r.HoleResults).HasForeignKey(h => h.RoundId).OnDelete(DeleteBehavior.Cascade);
    }
}
