namespace CustomGolfStats.App.Core.Sync;

public enum SyncNoticeKind { UpdatedFromAnotherDevice, CouldNotSync }

/// <summary>Something the golfer should be told about. <see cref="Message"/> never contains tokens or server response text.</summary>
public sealed record SyncNotice(SyncNoticeKind Kind, Guid? RoundId, string Message)
{
    /// <summary>Set when the notice is about a course (a manual course the server rejected).</summary>
    public Guid? CourseId { get; init; }
}

/// <summary>The outcome of one <see cref="SyncEngine.SyncAsync"/> run.</summary>
public sealed record SyncResult
{
    public int PushedRounds { get; init; }
    public int PushedDeletes { get; init; }
    public int PushedCourses { get; init; }
    /// <summary>409 responses where the server's copy replaced the local round.</summary>
    public int Conflicts { get; init; }
    public int PulledRounds { get; init; }
    /// <summary>Pulled rounds not stored: dirty locally (unsent edits win until pushed) or older than the stored copy.</summary>
    public int SkippedPulledRounds { get; init; }
    public int PulledCourses { get; init; }
    public int PulledHandicapEntries { get; init; }
    /// <summary>Rounds left dirty because their course has not reached the server yet.</summary>
    public int DeferredRounds { get; init; }
    /// <summary>True when the pull finished and the cursor was stored.</summary>
    public bool PullCompleted { get; init; }
    /// <summary>The network was unavailable; everything unsent stays dirty.</summary>
    public bool Offline { get; init; }
    /// <summary>The server rejected the session (401 after a refresh); sign in again.</summary>
    public bool SignedOut { get; init; }
    public IReadOnlyList<SyncNotice> Notices { get; init; } = [];
    /// <summary>Per-item failures (status codes only), each left dirty/pending for the next sync.</summary>
    public IReadOnlyList<string> Failures { get; init; } = [];

    public bool Succeeded => PullCompleted && Failures.Count == 0 && !Offline && !SignedOut;
}
