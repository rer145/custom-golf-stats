using CustomGolfStats.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomGolfStats.Api.Data.Configurations;

public sealed class CourseFlagConfiguration : IEntityTypeConfiguration<CourseFlag>
{
    public void Configure(EntityTypeBuilder<CourseFlag> builder)
    {
        builder.Property(f => f.Note).HasMaxLength(4000);
        builder.HasIndex(f => f.Status);
        builder.HasOne(f => f.Course).WithMany().HasForeignKey(f => f.CourseId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(f => f.Tee).WithMany().HasForeignKey(f => f.TeeId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(f => f.Reporter).WithMany().HasForeignKey(f => f.ReporterAccountId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(f => f.ResolvedBy).WithMany().HasForeignKey(f => f.ResolvedByAccountId).OnDelete(DeleteBehavior.SetNull);
    }
}
