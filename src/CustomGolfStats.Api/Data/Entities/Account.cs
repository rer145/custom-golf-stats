using CustomGolfStats.Domain;
using Microsoft.AspNetCore.Identity;

namespace CustomGolfStats.Api.Data.Entities;

/// <summary>A golfer's login and profile. <see cref="Role"/> is the single source of truth for admin rights.</summary>
public class Account : IdentityUser<Guid>
{
    public Account()
    {
        Id = Guid.CreateVersion7();
        SecurityStamp = Guid.NewGuid().ToString();
    }

    public Role Role { get; set; } = Role.User;

    public Tier Tier { get; set; } = Tier.Free;

    public int TrialShotRoundsUsed { get; set; }

    public string FirstName { get; set; } = "";

    public string LastName { get; set; } = "";

    public Gender Gender { get; set; } = Gender.Unspecified;

    public string? GhinNumber { get; set; }

    public Guid? HomeCourseId { get; set; }

    public Course? HomeCourse { get; set; }

    public DistanceUnits Units { get; set; } = DistanceUnits.Yards;

    public DateTimeOffset? DisabledAt { get; set; }

    /// <summary>Stamped from <see cref="TimeProvider"/> by <see cref="AppDbContext"/> when left at default.</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
