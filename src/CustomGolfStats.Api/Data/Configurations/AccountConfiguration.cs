using CustomGolfStats.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomGolfStats.Api.Data.Configurations;

public sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        // Identity's own index names (EmailIndex, UserNameIndex) renamed to match the snake_case convention.
        builder.HasIndex(a => a.NormalizedEmail).HasDatabaseName("ix_accounts_normalized_email");
        builder.HasIndex(a => a.NormalizedUserName).HasDatabaseName("ix_accounts_normalized_user_name").IsUnique();

        builder.Property(a => a.FirstName).HasMaxLength(100);
        builder.Property(a => a.LastName).HasMaxLength(100);
        builder.Property(a => a.GhinNumber).HasMaxLength(20);
        builder.HasOne(a => a.HomeCourse).WithMany().HasForeignKey(a => a.HomeCourseId).OnDelete(DeleteBehavior.SetNull);
    }
}
