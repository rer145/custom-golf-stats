using CustomGolfStats.App.Core.Storage.Rows;
using CustomGolfStats.Domain.Holes;
using SQLite;

namespace CustomGolfStats.App.Core.Storage.Repositories;

/// <summary>Courses with their tees, tee versions and holes. Saving replaces the whole course tree in one transaction.</summary>
public sealed class CourseRepository(SQLiteAsyncConnection db)
{
    /// <summary>Stores the course as given (its PendingSync flag is kept) and preserves an existing favorite mark.</summary>
    public Task SaveAsync(CourseRecord record) => db.RunInTransactionAsync(c => Write(c, record));

    /// <summary>Stores a copy received from the server: not pending, favorite mark preserved.</summary>
    public Task SaveFromServerAsync(CourseRecord record)
    {
        record.Course.PendingSync = false;
        return SaveAsync(record);
    }

    /// <summary>Stores a course created on this device (offline manual course), pending until pushed.</summary>
    public Task SaveLocalAsync(CourseRecord record)
    {
        record.Course.PendingSync = true;
        return SaveAsync(record);
    }

    public async Task<CourseRecord?> GetAsync(Guid courseId)
    {
        var course = await db.FindAsync<CourseRow>(courseId);
        return course is null ? null : await LoadAsync(course);
    }

    /// <summary>Courses matching the text in name, club, city or region (empty text matches all), favorites first then by name.
    /// Courses merged into another are left out.</summary>
    public async Task<List<CourseRow>> SearchAsync(string text)
    {
        var all = await db.Table<CourseRow>().Where(c => c.MergedIntoCourseId == null).ToListAsync();
        var term = text.Trim();
        return all
            .Where(c => term.Length == 0 || new[] { c.Name, c.ClubName, c.City, c.Region }
                .Any(f => f is not null && f.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(c => c.IsFavorite)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<List<CourseRecord>> GetPendingAsync()
    {
        var rows = await db.Table<CourseRow>().Where(c => c.PendingSync).ToListAsync();
        var result = new List<CourseRecord>(rows.Count);
        foreach (var row in rows)
            result.Add(await LoadAsync(row));
        return result;
    }

    public async Task SetFavoriteAsync(Guid courseId, bool favorite)
    {
        var row = await db.FindAsync<CourseRow>(courseId);
        if (row is null)
            return;
        row.IsFavorite = favorite;
        await db.UpdateAsync(row);
    }

    /// <summary>The tee version in effect on <paramref name="asOf"/> (latest one effective on or before it), or null.</summary>
    public async Task<TeeVersionRow?> GetVersionInEffectAsync(Guid teeId, DateTime asOf)
    {
        var versions = await db.Table<TeeVersionRow>().Where(v => v.TeeId == teeId && v.EffectiveFrom <= asOf)
            .OrderByDescending(v => v.EffectiveFrom).Take(1).ToListAsync();
        return versions.FirstOrDefault();
    }

    /// <summary>The hole layout of a tee version as domain <see cref="HoleInfo"/>, by hole number.</summary>
    public async Task<IReadOnlyList<HoleInfo>> GetHoleInfosAsync(Guid teeVersionId)
    {
        var holes = await db.Table<TeeHoleRow>().Where(h => h.TeeVersionId == teeVersionId).OrderBy(h => h.Number).ToListAsync();
        return holes.Select(h => h.ToHoleInfo()).ToList();
    }

    private async Task<CourseRecord> LoadAsync(CourseRow course)
    {
        var tees = await db.Table<TeeRow>().Where(t => t.CourseId == course.Id).OrderBy(t => t.Name).ToListAsync();
        var teeRecords = new List<TeeRecord>();
        foreach (var tee in tees)
        {
            var versions = await db.Table<TeeVersionRow>().Where(v => v.TeeId == tee.Id).OrderBy(v => v.EffectiveFrom).ToListAsync();
            var versionRecords = new List<TeeVersionRecord>();
            foreach (var version in versions)
            {
                var holes = await db.Table<TeeHoleRow>().Where(h => h.TeeVersionId == version.Id).OrderBy(h => h.Number).ToListAsync();
                versionRecords.Add(new TeeVersionRecord(version, holes));
            }
            teeRecords.Add(new TeeRecord(tee, versionRecords));
        }
        return new CourseRecord(course, teeRecords);
    }

    private static void Write(SQLiteConnection c, CourseRecord record)
    {
        var id = record.Course.Id;
        var existing = c.Find<CourseRow>(id);
        if (existing is not null)
            record.Course.IsFavorite = existing.IsFavorite;
        c.InsertOrReplace(record.Course);

        // Replace the whole tree so tees/versions/holes the server no longer has do not linger.
        foreach (var tee in c.Table<TeeRow>().Where(t => t.CourseId == id).ToList())
        {
            foreach (var version in c.Table<TeeVersionRow>().Where(v => v.TeeId == tee.Id).ToList())
                c.Execute("DELETE FROM TeeHoleRow WHERE TeeVersionId = ?", version.Id);
            c.Execute("DELETE FROM TeeVersionRow WHERE TeeId = ?", tee.Id);
        }
        c.Execute("DELETE FROM TeeRow WHERE CourseId = ?", id);

        foreach (var tee in record.Tees)
        {
            tee.Tee.CourseId = id;
            c.Insert(tee.Tee);
            foreach (var version in tee.Versions)
            {
                version.Version.TeeId = tee.Tee.Id;
                c.Insert(version.Version);
                foreach (var hole in version.Holes)
                {
                    hole.Id = 0;
                    hole.TeeVersionId = version.Version.Id;
                    c.Insert(hole);
                }
            }
        }
    }
}
