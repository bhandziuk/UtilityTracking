using UtilityTracking.GeorgiaPower;

namespace UtilityTracking.GeorgiaPower.ForgeRock;

/// <summary>Adapts OCC usage readings to the legacy result record shapes.</summary>
public static class ForgeRockUsageResultMapper
{
    public static HourlyPowerUsageResult ToHourlyPowerUsageResult(
        IEnumerable<ForgeRockHourlyUsage> readings)
    {
        var ordered = readings.OrderBy(reading => reading.Date).ToArray();
        var series = new AllHourlySeries(
            ToHourlySeries(ordered, reading => reading.Cost),
            ToHourlySeries(ordered, reading => reading.Usage),
            EmptySeries(),
            EmptySeries(),
            ToHourlySeries(ordered, reading => reading.Temperature),
            EmptySeries(),
            EmptySeries());

        return new HourlyPowerUsageResult(
            new XAxis(ordered.Select(reading => reading.Date).ToArray()),
            series);
    }

    public static DailyPowerUsageResult ToDailyPowerUsageResult(
        IEnumerable<ForgeRockDailyUsage> readings)
    {
        var ordered = readings.OrderBy(reading => reading.Date).ToArray();
        var weekdays = ordered.Where(reading => !IsWeekend(reading.Date)).ToArray();
        var weekends = ordered.Where(reading => IsWeekend(reading.Date)).ToArray();

        var series = new AllDailySeries(
            ToSeries(weekdays, reading => reading.Cost),
            ToSeries(weekends, reading => reading.Cost),
            ToSeries(weekdays, reading => reading.Usage),
            ToSeries(weekends, reading => reading.Usage),
            EmptySeries(),
            EmptySeries(),
            EmptySeries(),
            ToSeries(ordered, reading => reading.HighTemperature),
            ToSeries(ordered, reading => reading.LowTemperature),
            EmptySeries(),
            EmptySeries(),
            EmptySeries(),
            EmptySeries());

        return new DailyPowerUsageResult(
            new XAxis(ordered.Select(reading => reading.Date).ToArray()),
            series,
            "OCC");
    }

    private static Series ToSeries(
        IEnumerable<ForgeRockDailyUsage> readings,
        Func<ForgeRockDailyUsage, double?> valueSelector)
    {
        var points = readings
            .Select(reading => (Reading: reading, Value: valueSelector(reading)))
            .Where(item => item.Value.HasValue)
            .Select(item => new DataPoint(
                ToUnixMilliseconds(item.Reading.Date),
                item.Value!.Value,
                item.Reading.Date,
                "Daily"))
            .ToArray();

        return new Series(points);
    }

    private static Series ToHourlySeries(
        IEnumerable<ForgeRockHourlyUsage> readings,
        Func<ForgeRockHourlyUsage, double?> valueSelector)
    {
        var points = readings
            .Select(reading => (Reading: reading, Value: valueSelector(reading)))
            .Where(item => item.Value.HasValue)
            .Select(item => new DataPoint(
                ToUnixMilliseconds(item.Reading.Date),
                item.Value!.Value,
                item.Reading.Date,
                "Hourly"))
            .ToArray();

        return new Series(points);
    }

    private static Series EmptySeries() => new(Array.Empty<DataPoint>());

    private static bool IsWeekend(DateTime date) =>
        date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    private static double ToUnixMilliseconds(DateTime date)
    {
        var utcDate = date.Kind == DateTimeKind.Utc
            ? date
            : DateTime.SpecifyKind(date, DateTimeKind.Utc);
        return new DateTimeOffset(utcDate).ToUnixTimeMilliseconds();
    }
}
