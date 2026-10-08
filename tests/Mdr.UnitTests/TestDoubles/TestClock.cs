namespace Mdr.UnitTests.TestDoubles;

/// <summary>Controllable <see cref="TimeProvider"/> for deterministic tests.</summary>
public sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now;

    public TestClock(DateTime utcNow) => _now = new DateTimeOffset(utcNow, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}
