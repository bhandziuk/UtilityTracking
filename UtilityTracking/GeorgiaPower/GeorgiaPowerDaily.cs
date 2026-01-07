namespace UtilityTracking.GeorgiaPower
{
    public record GeorgiaPowerDaily(DateTime Date, double? Cost, double? LowTemp, double? HighTemp, double? Usage);
}
