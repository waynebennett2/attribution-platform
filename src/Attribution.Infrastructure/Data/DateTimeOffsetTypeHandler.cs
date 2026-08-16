using System.Data;
using Dapper;

namespace Attribution.Infrastructure.Data;

// MySQL's DATETIME columns carry no timezone/offset information, and this platform always
// treats every stored instant as UTC (every DateTimeOffset the application constructs uses
// DateTimeOffset.UtcNow or a UTC-anchored arithmetic derivative of it). Without this
// handler, Dapper's default DateTime -> DateTimeOffset coercion on read constructs
// `new DateTimeOffset(rawValue)` from a Kind=Unspecified DateTime, which .NET interprets
// as the *local system* timezone — silently shifting every value read back from the
// database by the host machine's current UTC offset. That's invisible wherever a
// timestamp is only ever compared inside a SQL WHERE clause (both sides go through the
// same write path, so a shared shift is a no-op), but it corrupts the very first place
// this codebase reads a timestamp back and compares it in C# for exact equality:
// Call.ApplyRestatement's FR-045 change-detection, which otherwise reports a genuine,
// unchanged re-ingestion as "changed" the moment the host isn't itself on UTC.
public sealed class DateTimeOffsetTypeHandler : SqlMapper.TypeHandler<DateTimeOffset>
{
    public override void SetValue(IDbDataParameter parameter, DateTimeOffset value) => parameter.Value = value.UtcDateTime;

    public override DateTimeOffset Parse(object value) => new(DateTime.SpecifyKind((DateTime)value, DateTimeKind.Utc));
}
