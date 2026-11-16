using MySql.Data.MySqlClient;

namespace Career24;

internal sealed record MigrationSettings(string Server, uint Port, string Database,
    string User, string Password, string CipherKey);

internal static class ContactMigration
{
    public static int Run(MigrationSettings settings)
    {
        var cipher = new ContactCipher(settings.CipherKey);
        var builder = new MySqlConnectionStringBuilder
        {
            Server=settings.Server, Port=settings.Port, Database=settings.Database,
            UserID=settings.User, Password=settings.Password, SslMode=MySqlSslMode.Required,
            CharacterSet="utf8mb4", ConnectionTimeout=5
        };
        using var connection = new MySqlConnection(builder.ConnectionString);
        connection.Open();

        using var transaction = connection.BeginTransaction();
        using var read = new MySqlCommand("SELECT id,phone,email FROM candidates FOR UPDATE", connection, transaction);
        var rows = new List<(uint Id,string? Phone,string? Email)>();
        using (var reader = read.ExecuteReader())
        {
            while (reader.Read())
                rows.Add((reader.GetUInt32(0), reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        using var write = new MySqlCommand("UPDATE candidates SET phone=@phone,email=@email WHERE id=@id",
            connection, transaction);
        write.Parameters.Add("@id", MySqlDbType.UInt32);
        write.Parameters.Add("@phone", MySqlDbType.VarChar);
        write.Parameters.Add("@email", MySqlDbType.VarChar);
        int updated=0;
        foreach (var row in rows)
        {
            var phone=ContactCipher.IsEncodedPhone(row.Phone) ? row.Phone : cipher.EncodePhone(row.Phone);
            var email=ContactCipher.IsEncodedEmail(row.Email) ? row.Email : cipher.EncodeEmail(row.Email);
            if (phone==row.Phone && email==row.Email) continue;
            write.Parameters["@id"].Value=row.Id;
            write.Parameters["@phone"].Value=(object?)phone ?? DBNull.Value;
            write.Parameters["@email"].Value=(object?)email ?? DBNull.Value;
            write.ExecuteNonQuery();
            updated++;
        }
        transaction.Commit();
        return updated;
    }
}
