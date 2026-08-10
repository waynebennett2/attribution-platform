using System.Data;
using System.Text;
using Dapper;

namespace Attribution.Infrastructure.Data;

public sealed class OutboxWriter : IOutboxWriter
{
    public Task WriteAsync(IDbConnection connection, IDbTransaction transaction, string tableName, object row)
    {
        var properties = row.GetType().GetProperties();
        var columns = string.Join(", ", properties.Select(p => ToSnakeCase(p.Name)));
        var parameters = string.Join(", ", properties.Select(p => "@" + p.Name));
        var sql = $"INSERT INTO {tableName} ({columns}) VALUES ({parameters})";

        return connection.ExecuteAsync(sql, row, transaction);
    }

    private static string ToSnakeCase(string pascalCaseName)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < pascalCaseName.Length; i++)
        {
            var c = pascalCaseName[i];
            if (char.IsUpper(c))
            {
                if (i > 0)
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
