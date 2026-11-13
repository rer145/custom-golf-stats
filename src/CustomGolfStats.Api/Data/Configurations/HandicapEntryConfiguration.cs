using CustomGolfStats.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomGolfStats.Api.Data.Configurations;

public sealed class HandicapEntryConfiguration : IEntityTypeConfiguration<HandicapEntry>
{
    public void Configure(EntityTypeBuilder<HandicapEntry> builder)
    {
        builder.Property(h => h.Index).HasPrecision(4, 1);
        builder.HasOne(h => h.Account).WithMany().HasForeignKey(h => h.AccountId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(h => new { h.AccountId, h.EffectiveOn });
    }
}
