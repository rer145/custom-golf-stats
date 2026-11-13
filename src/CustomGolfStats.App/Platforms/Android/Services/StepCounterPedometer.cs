using Android;
using Android.Content;
using Android.Hardware;
using CustomGolfStats.App.Platform;

namespace CustomGolfStats.App.Platforms.Android.Services;

/// <summary>
/// Android's step counter only reports a running total since boot, and only while the app listens. So once the golfer allows
/// activity recognition (asked when a round screen opens) this samples the total while the app runs and keeps the samples in
/// private preferences (three days), and <see cref="ReadAsync"/> adds up the increases between the samples around the round.
/// A drop in the total means the phone restarted; counting resumes from zero. Distance is not measured (null).
/// </summary>
public sealed class StepCounterPedometer : Java.Lang.Object, IPedometer, IMotionPermission, ISensorEventListener
{
    private const string PreferencesName = "cgs.steps";
    private const string SamplesKey = "samples";
    private static readonly TimeSpan Keep = TimeSpan.FromDays(3);
    private static readonly TimeSpan SampleEvery = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan BaselineTolerance = TimeSpan.FromMinutes(15);

    private readonly Context _context;
    private readonly object _lock = new();
    private List<(long Ticks, long Count)>? _samples;
    private bool _listening;
    private (long Ticks, long Count)? _lastStored;

    public StepCounterPedometer(Context context)
    {
        _context = context;
        if (HasPermission())
            StartListening();
    }

    private static string? Permission => OperatingSystem.IsAndroidVersionAtLeast(29) ? Manifest.Permission.ActivityRecognition : null;

    private static bool HasPermission() => Permission is not { } p || AndroidPermissions.IsGranted(p);

    public async Task PrepareAsync()
    {
        if (Permission is { } p && !await AndroidPermissions.RequestAsync(p))
            return;
        StartListening();
    }

    public Task<PedometerReading?> ReadAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        lock (_lock)
        {
            var samples = Samples().Where(s => s.Ticks <= to.UtcTicks).OrderBy(s => s.Ticks).ToList();
            var start = samples.FindLastIndex(s => s.Ticks <= from.UtcTicks);
            if (start < 0)
            {
                // No sample before the round: accept the first one shortly after it started (a few steps may be missed).
                start = samples.FindIndex(s => s.Ticks >= from.UtcTicks && s.Ticks - from.UtcTicks <= BaselineTolerance.Ticks);
                if (start < 0)
                    return Task.FromResult<PedometerReading?>(null);
            }
            if (start == samples.Count - 1)
                return Task.FromResult<PedometerReading?>(null);

            long steps = 0;
            for (var i = start + 1; i < samples.Count; i++)
            {
                var delta = samples[i].Count - samples[i - 1].Count;
                steps += delta >= 0 ? delta : samples[i].Count; // restarted: the counter began again at zero
            }
            return Task.FromResult<PedometerReading?>(new PedometerReading((int)Math.Min(steps, int.MaxValue), null));
        }
    }

    private void StartListening()
    {
        lock (_lock)
        {
            if (_listening)
                return;
            var manager = (SensorManager?)_context.GetSystemService(Context.SensorService);
            var sensor = manager?.GetDefaultSensor(SensorType.StepCounter);
            if (manager is null || sensor is null)
                return;
            _listening = manager.RegisterListener(this, sensor, SensorDelay.Normal);
        }
    }

    public void OnSensorChanged(SensorEvent? e)
    {
        if (e?.Values is not { Count: > 0 } values)
            return;
        var now = DateTimeOffset.UtcNow.UtcTicks;
        var count = (long)values[0];
        lock (_lock)
        {
            if (_lastStored is { } last && now - last.Ticks < SampleEvery.Ticks && count >= last.Count)
                return;
            var samples = Samples();
            samples.Add((now, count));
            samples.RemoveAll(s => now - s.Ticks > Keep.Ticks);
            _lastStored = (now, count);
            Save(samples);
        }
    }

    public void OnAccuracyChanged(Sensor? sensor, SensorStatus accuracy)
    {
    }

    private List<(long Ticks, long Count)> Samples()
    {
        if (_samples is not null)
            return _samples;
        var text = _context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!.GetString(SamplesKey, "") ?? "";
        _samples = text.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split(':'))
            .Where(p => p.Length == 2 && long.TryParse(p[0], out _) && long.TryParse(p[1], out _))
            .Select(p => (long.Parse(p[0]), long.Parse(p[1])))
            .ToList();
        return _samples;
    }

    private void Save(List<(long Ticks, long Count)> samples) =>
        _context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!.Edit()!
            .PutString(SamplesKey, string.Join(';', samples.Select(s => $"{s.Ticks}:{s.Count}")))!.Apply();
}
