using BeijingClock.Core;
using System.Globalization;

var tests = new (string Name, Action Body)[]
{
    ("UTC to Beijing crosses date boundary", UtcToBeijingCrossesDateBoundary),
    ("UTC to Beijing crosses year boundary", UtcToBeijingCrossesYearBoundary),
    ("UTC to Beijing reaches leap day", UtcToBeijingReachesLeapDay),
    ("One second before midnight", OneSecondBeforeMidnight),
    ("Exactly at midnight", ExactlyAtMidnight),
    ("One second after midnight", OneSecondAfterMidnight),
    ("Exact minute boundary", ExactMinuteBoundary),
    ("Input offset does not change instant", InputOffsetDoesNotChangeInstant),
    ("Remaining fraction uses actual seconds", RemainingFractionUsesActualSeconds),
    ("Position round trip", PositionRoundTrip),
    ("Missing position returns null", MissingPositionReturnsNull),
    ("Bad JSON returns null and is preserved", BadJsonReturnsNullAndIsPreserved),
    ("Out-of-range position returns null", OutOfRangePositionReturnsNull),
    ("Out-of-range position cannot be saved", OutOfRangePositionCannotBeSaved),
    ("Save failure is reported", SaveFailureIsReported)
};

int failures = 0;
foreach ((string name, Action body) in tests)
{
    try
    {
        body();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
    }
}

Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
return failures == 0 ? 0 : 1;

static void UtcToBeijingCrossesDateBoundary()
{
    ClockSnapshot snapshot = BeijingTime.At(Parse("2026-10-05T16:00:00Z"));
    Equal("2026年10月06日", snapshot.DateText);
    Equal("00:00", snapshot.TimeText);
    Equal(TimeSpan.FromHours(8), snapshot.BeijingTime.Offset);
}

static void UtcToBeijingCrossesYearBoundary()
{
    ClockSnapshot snapshot = BeijingTime.At(Parse("2025-12-31T16:30:00Z"));
    Equal("2026年01月01日", snapshot.DateText);
    Equal("00:30", snapshot.TimeText);
}

static void UtcToBeijingReachesLeapDay()
{
    ClockSnapshot snapshot = BeijingTime.At(Parse("2024-02-28T16:00:00Z"));
    Equal("2024年02月29日", snapshot.DateText);
    Equal("00:00", snapshot.TimeText);
}

static void OneSecondBeforeMidnight()
{
    ClockSnapshot snapshot = BeijingTime.At(Parse("2026-10-06T15:59:59Z"));
    Equal("23:59", snapshot.TimeText);
    Equal("00时01分", snapshot.RemainingText);
    Equal(1, snapshot.RemainingMinutes);
    NearlyEqual(1d / 86_400d, snapshot.RemainingFraction);
}

static void ExactlyAtMidnight()
{
    ClockSnapshot snapshot = BeijingTime.At(Parse("2026-10-06T16:00:00Z"));
    Equal("00:00", snapshot.TimeText);
    Equal("24时00分", snapshot.RemainingText);
    Equal(1_440, snapshot.RemainingMinutes);
    NearlyEqual(1d, snapshot.RemainingFraction);
}

static void OneSecondAfterMidnight()
{
    ClockSnapshot snapshot = BeijingTime.At(Parse("2026-10-06T16:00:01Z"));
    Equal("00:00", snapshot.TimeText);
    Equal("24时00分", snapshot.RemainingText);
    Equal(1_440, snapshot.RemainingMinutes);
    NearlyEqual(86_399d / 86_400d, snapshot.RemainingFraction);
}

static void ExactMinuteBoundary()
{
    ClockSnapshot exact = BeijingTime.At(Parse("2026-10-06T07:42:00Z"));
    Equal("15:42", exact.TimeText);
    Equal("08时18分", exact.RemainingText);
    Equal(498, exact.RemainingMinutes);

    ClockSnapshot oneSecondEarlier = BeijingTime.At(Parse("2026-10-06T07:41:59Z"));
    Equal("08时19分", oneSecondEarlier.RemainingText);
    Equal(499, oneSecondEarlier.RemainingMinutes);
}

static void InputOffsetDoesNotChangeInstant()
{
    DateTimeOffset utc = Parse("2026-01-01T08:00:00Z");
    DateTimeOffset sameInstantAtDifferentOffset = Parse("2026-01-01T10:00:00+02:00");

    ClockSnapshot first = BeijingTime.At(utc);
    ClockSnapshot second = BeijingTime.At(sameInstantAtDifferentOffset);

    Equal(first, second);
    Equal("16:00", first.TimeText);
}

static void RemainingFractionUsesActualSeconds()
{
    ClockSnapshot noon = BeijingTime.At(Parse("2026-10-06T04:00:00Z"));
    NearlyEqual(0.5d, noon.RemainingFraction);

    ClockSnapshot halfSecondBeforeMidnight = BeijingTime.At(Parse("2026-10-06T15:59:59.5000000Z"));
    NearlyEqual(0.5d / 86_400d, halfSecondBeforeMidnight.RemainingFraction);
    Equal(1, halfSecondBeforeMidnight.RemainingMinutes);
}

static void PositionRoundTrip()
{
    string path = NewArtifactPath("round-trip.json");
    var store = new PositionStore(path);
    var initial = new WindowPosition(20, 40);
    var expected = new WindowPosition(-420, 315);

    store.Save(initial);
    store.Save(expected);

    Equal(expected, store.Load());
    string directory = Path.GetDirectoryName(path)
        ?? throw new InvalidOperationException("Test path has no parent directory.");
    Equal(0, Directory.GetFiles(directory, "*.tmp").Length);
}

static void MissingPositionReturnsNull()
{
    string path = NewArtifactPath("missing.json");
    var store = new PositionStore(path);
    Equal<WindowPosition?>(null, store.Load());
}

static void BadJsonReturnsNullAndIsPreserved()
{
    string path = NewArtifactPath("bad.json");
    const string invalidJson = "{ definitely not valid JSON";
    File.WriteAllText(path, invalidJson);
    var store = new PositionStore(path);

    Equal<WindowPosition?>(null, store.Load());
    Equal(invalidJson, File.ReadAllText(path));
}

static void OutOfRangePositionReturnsNull()
{
    string path = NewArtifactPath("out-of-range.json");
    File.WriteAllText(path, "{\"X\":100001,\"Y\":0}");
    var store = new PositionStore(path);

    Equal<WindowPosition?>(null, store.Load());
    True(File.Exists(path), "Out-of-range position file should be preserved.");
}

static void OutOfRangePositionCannotBeSaved()
{
    string path = NewArtifactPath("invalid-save.json");
    var store = new PositionStore(path);

    Throws<ArgumentOutOfRangeException>(() => store.Save(new WindowPosition(0, -100_001)));
    True(!File.Exists(path), "Invalid position should not create a settings file.");
}

static void SaveFailureIsReported()
{
    string path = NewArtifactPath("directory-cannot-be-replaced-by-file");
    Directory.CreateDirectory(path);
    var store = new PositionStore(path);

    ThrowsOneOf<IOException, UnauthorizedAccessException>(
        () => store.Save(new WindowPosition(10, 20)));
}

static DateTimeOffset Parse(string value) =>
    DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

static string NewArtifactPath(string fileName)
{
    string artifactDirectory = Path.Combine(
        AppContext.BaseDirectory,
        "..",
        "..",
        "..",
        "artifacts",
        Guid.NewGuid().ToString("N"));
    artifactDirectory = Path.GetFullPath(artifactDirectory);
    Directory.CreateDirectory(artifactDirectory);
    return Path.Combine(artifactDirectory, fileName);
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected <{expected}> but got <{actual}>.");
    }
}

static void NearlyEqual(double expected, double actual, double tolerance = 1e-12)
{
    if (Math.Abs(expected - actual) > tolerance)
    {
        throw new InvalidOperationException($"Expected <{expected:R}> but got <{actual:R}>.");
    }
}

static void True(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void Throws<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name} to be thrown.");
}

static void ThrowsOneOf<TFirst, TSecond>(Action action)
    where TFirst : Exception
    where TSecond : Exception
{
    try
    {
        action();
    }
    catch (TFirst)
    {
        return;
    }
    catch (TSecond)
    {
        return;
    }

    throw new InvalidOperationException(
        $"Expected {typeof(TFirst).Name} or {typeof(TSecond).Name} to be thrown.");
}
