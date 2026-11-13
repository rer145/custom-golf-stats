using System.Globalization;
using CustomGolfStats.Api.Data.Entities;
using CustomGolfStats.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomGolfStats.Api.Data.Configurations;

public sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> builder)
    {
        builder.HasKey(s => s.Key);
        builder.Property(s => s.Key).HasMaxLength(100);
        builder.Property(s => s.Value).HasMaxLength(1000);
        builder.HasData(new AppSetting
        {
            Key = AppSettingKeys.TrialShotRounds,
            Value = Entitlements.DefaultTrialShotRounds.ToString(CultureInfo.InvariantCulture),
        });
    }
}
