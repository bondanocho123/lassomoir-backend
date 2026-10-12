using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Lassommoir.Infrastructure.Persistence.Converters;

/// <summary>
/// DateTime selalu UTC, disimpan sebagai TEXT ISO 8601 lebar tetap
/// (yyyy-MM-ddTHH:mm:ss.fffffffZ) sehingga urutan leksikografis = urutan waktu.
/// Local dikonversi ke UTC, Unspecified dianggap sudah UTC.
/// </summary>
public class UtcDateTimeToTextConverter() : ValueConverter<DateTime, string>(
    v => ToText(v),
    v => FromText(v))
{
    private const string Format = "yyyy-MM-ddTHH:mm:ss.fffffffZ";

    private static string ToText(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Local => value.ToUniversalTime(),
            DateTimeKind.Utc => value,
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

        return utc.ToString(Format, CultureInfo.InvariantCulture);
    }

    private static DateTime FromText(string value) =>
        DateTime.SpecifyKind(
            DateTime.ParseExact(value, Format, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal),
            DateTimeKind.Utc);
}
