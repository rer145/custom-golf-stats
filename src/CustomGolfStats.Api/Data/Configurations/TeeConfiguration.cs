using CustomGolfStats.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CustomGolfStats.Api.Data.Configurations;

public sealed class TeeConfiguration : IEntityTypeConfiguration<Tee>
{
    public void Configure(EntityTypeBuilder<Tee> builder)
    {
        builder.Property(t => t.Name).HasMaxLength(100);
        builder.HasOne(t => t.Course).WithMany(c => c.Tees).HasForeignKey(t => t.CourseId).OnDelete(DeleteBehavior.Cascade);
    }
}
