using CustomGolfStats.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomGolfStats.Api.Data.Configurations;

public sealed class CourseConfiguration : IEntityTypeConfiguration<Course>
{
    public void Configure(EntityTypeBuilder<Course> builder)
    {
        builder.Property(c => c.Name).HasMaxLength(200);
        builder.Property(c => c.ClubName).HasMaxLength(200);
        builder.Property(c => c.Address).HasMaxLength(300);
        builder.Property(c => c.City).HasMaxLength(100);
        builder.Property(c => c.Region).HasMaxLength(100);
        builder.Property(c => c.Country).HasMaxLength(100);
        builder.Property(c => c.Phone).HasMaxLength(50);
        builder.Property(c => c.Website).HasMaxLength(300);
        builder.Property(c => c.ProviderCourseId).HasMaxLength(100);

        builder.HasIndex(c => new { c.Source, c.ProviderCourseId })
            .IsUnique()
            .HasFilter("provider_course_id IS NOT NULL");
        builder.HasIndex(c => c.Name);

        builder.HasOne(c => c.MergedIntoCourse).WithMany()
            .HasForeignKey(c => c.MergedIntoCourseId).OnDelete(DeleteBehavior.Restrict);
    }
}
