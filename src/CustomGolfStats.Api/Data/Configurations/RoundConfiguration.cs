using CustomGolfStats.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomGolfStats.Api.Data.Configurations;

public sealed class RoundConfiguration : IEntityTypeConfiguration<Round>
{
    public void Configure(EntityTypeBuilder<Round> builder)
    {
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Notes).HasMaxLength(4000);
        builder.Property(r => r.WeatherConditions).HasMaxLength(100);
        builder.Property(r => r.LegacyWeather).HasMaxLength(500);
        builder.Property(r => r.TemperatureF).HasPrecision(5, 1);
        builder.Property(r => r.WindSpeedMph).HasPrecision(5, 1);
        builder.Property(r => r.DistanceMiles).HasPrecision(6, 2);
        builder.Property(r => r.Version).IsConcurrencyToken();

        builder.HasIndex(r => new { r.AccountId, r.ModifiedAt });
        builder.HasOne(r => r.Account).WithMany().HasForeignKey(r => r.AccountId).OnDelete(DeleteBehavior.Cascade);
        // A tee version that rounds use can't be deleted.
        builder.HasOne(r => r.TeeVersion).WithMany().HasForeignKey(r => r.TeeVersionId).OnDelete(DeleteBehavior.Restrict);
    }
}
