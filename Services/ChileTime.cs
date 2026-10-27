namespace CampeonatoMagic.Services;

public static class ChileTime
{
    // Windows without ICU (including NLS mode) cannot resolve IANA identifiers.
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "Pacific SA Standard Time" : "America/Santiago");

    public static DateTime Now => FromUtc(DateTime.UtcNow);

    public static DateTime FromUtc(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, Zone);
}
