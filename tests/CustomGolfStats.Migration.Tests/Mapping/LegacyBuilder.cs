using CustomGolfStats.Migration.Source;

namespace CustomGolfStats.Migration.Tests.Mapping;

/// <summary>Builds in-memory <see cref="LegacyData"/> for mapper tests. Ids are unique across all tables.</summary>
internal sealed class LegacyBuilder
{
    public const int GolferId = 7;

    private readonly List<LegacyCourse> _courses = [];
    private readonly List<LegacyTee> _tees = [];
    private readonly List<LegacyHole> _holes = [];
    private readonly List<LegacyRound> _rounds = [];
    private readonly List<LegacyRoundDetail> _details = [];
    private readonly List<LegacyHandicap> _handicaps = [];
    private readonly Dictionary<int, string> _roundTypes = [];
    private readonly Dictionary<int, string> _weather = [];
    private readonly Dictionary<int, string> _wind = [];
    private readonly Dictionary<int, string> _temperatures = [];
    private int _nextId = 100;

    public LegacyCourse Course(string name = "Test Links") =>
        Add(_courses, new LegacyCourse(NextId(), name, "1 Fairway Dr", null, "Springfield", "12345", "555-0100", null, null, null));

    public LegacyCourse Add(LegacyCourse course) => Add(_courses, course);

    public LegacyTee Tee(LegacyCourse course, string name = "White", double? rating = 70.1, int? slope = 125) =>
        Add(_tees, new LegacyTee(NextId(), course.Id, name, "M", 72, rating, slope, 36, 35.0, 124, 36, 35.1, 126));

    public LegacyTee Add(LegacyTee tee) => Add(_tees, tee);

    /// <summary>Holes 1..<paramref name="count"/>, par 4, 400 yards, stroke index = number unless given.</summary>
    public IReadOnlyList<LegacyHole> Holes(LegacyTee tee, int count = 18, Func<int, int>? strokeIndex = null, Func<int, int>? par = null) =>
        Enumerable.Range(1, count)
            .Select(n => Add(_holes, new LegacyHole(NextId(), tee.Id, n, par?.Invoke(n) ?? 4, 400, strokeIndex?.Invoke(n) ?? n)))
            .ToList();

    public LegacyHole Add(LegacyHole hole) => Add(_holes, hole);

    public LegacyRound Round(
        LegacyTee tee,
        DateTime? playedOn = null,
        int? roundTypeId = null,
        int? weatherId = null,
        int? windId = null,
        int? temperatureId = null,
        string? duration = null,
        decimal? distance = null,
        string? notes = null) =>
        Add(_rounds, new LegacyRound(
            NextId(), GolferId, tee.Id, roundTypeId, weatherId, windId, temperatureId, 18, 90, duration, distance,
            playedOn ?? new DateTime(2024, 6, 1, 9, 30, 0), notes));

    public LegacyRoundDetail Detail(
        LegacyRound round,
        LegacyHole hole,
        int? sequence = null,
        int score = 5,
        int putts = 2,
        bool gir = false) =>
        Add(_details, new LegacyRoundDetail(NextId(), round.Id, hole.Id, sequence, score, putts, false, false, false, false, false, gir, 0, 0));

    public LegacyRoundDetail Add(LegacyRoundDetail detail) => Add(_details, detail);

    /// <summary>One detail per hole, in the given order, with null sequence numbers and the given scores (default 5).</summary>
    public IReadOnlyList<LegacyRoundDetail> Details(LegacyRound round, IEnumerable<LegacyHole> holes, Func<int, int>? score = null) =>
        holes.Select((hole, i) => Detail(round, hole, score: score?.Invoke(i) ?? 5)).ToList();

    public LegacyHandicap Handicap(DateTime date, decimal index) =>
        Add(_handicaps, new LegacyHandicap(NextId(), GolferId, date, index));

    public int RoundType(string name) => AddLookup(_roundTypes, name);

    public int Weather(string name) => AddLookup(_weather, name);

    public int Wind(string name) => AddLookup(_wind, name);

    public int Temperature(string name) => AddLookup(_temperatures, name);

    public int NextId() => _nextId++;

    public LegacyData Build() =>
        new(GolferId, _courses, _tees, _holes, _rounds, _details, _handicaps, _roundTypes, _weather, _wind, _temperatures);

    /// <summary>A course, an 18-hole tee and one round on it with one detail per hole.</summary>
    public (LegacyCourse Course, LegacyTee Tee, IReadOnlyList<LegacyHole> Holes, LegacyRound Round) StandardRound(
        DateTime? playedOn = null)
    {
        var course = Course();
        var tee = Tee(course);
        var holes = Holes(tee);
        var round = Round(tee, playedOn);
        Details(round, holes);
        return (course, tee, holes, round);
    }

    private int AddLookup(Dictionary<int, string> lookup, string name)
    {
        var id = NextId();
        lookup[id] = name;
        return id;
    }

    private static T Add<T>(List<T> list, T item)
    {
        list.Add(item);
        return item;
    }
}
