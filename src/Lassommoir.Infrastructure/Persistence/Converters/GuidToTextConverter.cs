using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Lassommoir.Infrastructure.Persistence.Converters;

/// <summary>Guid disimpan sebagai TEXT format "D" huruf kecil.</summary>
public class GuidToTextConverter() : ValueConverter<Guid, string>(
    v => v.ToString("D", CultureInfo.InvariantCulture),
    v => Guid.Parse(v, CultureInfo.InvariantCulture));
