using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Lassommoir.Infrastructure.Persistence;

public class SqlitePragmaInterceptor(int busyTimeoutMs) : DbConnectionInterceptor
{
    private readonly string _pragmaSql =
        $"PRAGMA journal_mode=WAL; PRAGMA busy_timeout={busyTimeoutMs}; PRAGMA foreign_keys=ON; PRAGMA synchronous=NORMAL;";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = connection.CreateCommand();
        command.CommandText = _pragmaSql;
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = _pragmaSql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
