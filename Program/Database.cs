using System.Data;
using System.Globalization;
using MySql.Data.MySqlClient;

namespace Career24;

internal sealed record DbAccount(string User, string Password);
internal sealed record ConnectionSettings(string Server, uint Port, string Database,
    DbAccount Identity, DbAccount Manager, DbAccount Administrator, string CipherKey);

internal sealed class Database(ConnectionSettings settings)
{
    private readonly ContactCipher cipher = new(settings.CipherKey);
    private static readonly CompareInfo SearchCompare = CultureInfo.GetCultureInfo("ru-RU").CompareInfo;

    private MySqlConnection Open(DbAccount account)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = settings.Server,
            Port = settings.Port,
            Database = settings.Database,
            UserID = account.User,
            Password = account.Password,
            CharacterSet = "utf8mb4",
            ConnectionTimeout = 5,
            DefaultCommandTimeout = 8,
            SslMode = MySqlSslMode.Required
        };
        var connection = new MySqlConnection(builder.ConnectionString);
        try
        {
            connection.Open();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static MySqlCommand Command(MySqlConnection c, string sql, params (string Name, object? Value)[] parameters)
    {
        var command = new MySqlCommand(sql, c);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    private DataTable Query(DbAccount account, string sql, params (string, object?)[] parameters)
    {
        using var connection = Open(account);
        using var command = Command(connection, sql, parameters);
        using var reader = command.ExecuteReader();
        var table = new DataTable();
        table.Load(reader);
        return table;
    }

    private int Execute(DbAccount account, string sql, params (string, object?)[] parameters)
    {
        using var connection = Open(account);
        using var command = Command(connection, sql, parameters);
        return command.ExecuteNonQuery();
    }

    public void CheckConnection() => Query(settings.Identity, "SELECT COUNT(*) FROM roles");

    public Session SignIn(string input, string password)
    {
        string login = Security.NormalizeLogin(input);
        var records = Query(settings.Identity, """
            SELECT u.full_name, u.password_hash, u.password_salt, u.password_iterations, u.is_active, r.code
            FROM users u LEFT JOIN roles r ON r.id = u.role_id
            WHERE u.login = @login
            """, ("@login", login));
        if (records.Rows.Count != 1)
            throw new InvalidOperationException("Неверный логин или пароль.");
        var row = records.Rows[0];
        if (!Security.Verify(password, (byte[])row[1], (byte[])row[2], Convert.ToInt32(row[3])))
            throw new InvalidOperationException("Неверный логин или пароль.");
        if (!Convert.ToBoolean(row[4]) || row[5] is not string role)
            throw new InvalidOperationException("Учётная запись ожидает активации администратором.");
        return new Session((string)row[0], role);
    }

    public void Register(string fullName, string input, string password, string confirmation)
    {
        string login = Security.NormalizeLogin(input);
        fullName = fullName.Trim();
        if (fullName.Length is < 2 or > 100 || !Security.ValidLogin(login))
            throw new InvalidOperationException("Укажите ФИО и логин из 4–32 латинских букв, цифр или символов _ . -.");
        if (password.Length is < 10 or > 120 || password.All(char.IsWhiteSpace))
            throw new InvalidOperationException("Пароль должен содержать от 10 до 120 символов.");
        if (password != confirmation)
            throw new InvalidOperationException("Пароли не совпадают.");
        var digest = Security.CreateDigest(password);
        try
        {
            Execute(settings.Identity, """
                INSERT INTO users (full_name,login,password_hash,password_salt,password_iterations)
                VALUES (@name,@login,@hash,@salt,@iterations)
                """, ("@name", fullName), ("@login", login), ("@hash", digest.Hash),
                ("@salt", digest.Salt), ("@iterations", digest.Iterations));
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            throw new InvalidOperationException("Этот логин уже занят.");
        }
    }

    public DataTable Users(Session session, string search)
    {
        Require(session, Security.Administrator);
        return Query(settings.Administrator, """
            SELECT u.id, u.login AS 'Логин', u.full_name AS 'ФИО', COALESCE(r.title,'—') AS 'Роль',
              IF(u.is_active,'Активна','Ожидает активации') AS 'Статус', u.registered_at AS 'Зарегистрирована'
            FROM users u LEFT JOIN roles r ON r.id=u.role_id
            WHERE @search='' OR LOCATE(@search,CONCAT_WS(' ',u.login,u.full_name))>0
            ORDER BY u.id DESC
            """, ("@search", search.Trim()));
    }

    public void ActivateManager(Session session, uint userId)
    {
        Require(session, Security.Administrator);
        var result = Query(settings.Administrator, "CALL activate_manager(@id)", ("@id", userId));
        int updated = Convert.ToInt32(result.Rows[0]["activated"]);
        if (updated != 1)
            throw new InvalidOperationException("Выберите учётную запись, которая ожидает активации.");
    }

    public DataTable List(Session session, Entity entity, string search)
    {
        Require(session, Security.Manager);
        if (entity == Entities.Candidates)
        {
            var rows = Query(settings.Manager, $"SELECT {entity.Columns} FROM {entity.From} ORDER BY t.id DESC");
            DecodeContacts(rows, "Телефон", "Почта");
            string needle = search.Trim();
            if (needle.Length == 0) return rows;

            var matched = rows.Clone();
            foreach (DataRow row in rows.Rows)
            {
                string text = string.Join(" ", new[] {"ФИО", "Должность", "Город", "Телефон", "Почта"}
                    .Select(k => row.Field<string>(k) ?? ""));
                if (SearchCompare.IndexOf(text, needle, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0)
                    matched.ImportRow(row);
            }
            return matched;
        }
        // Имена таблиц и столбцов берутся из описаний форм. Поисковый текст передаётся параметром.
        return Query(settings.Manager, $"SELECT {entity.Columns} FROM {entity.From} WHERE @search='' OR " +
            $"LOCATE(@search, CONCAT_WS(' ',{entity.Search}))>0 ORDER BY t.id DESC", ("@search", search.Trim()));
    }

    public DataRow Read(Session session, Entity entity, uint id)
    {
        Require(session, Security.Manager);
        var records = Query(settings.Manager, $"SELECT * FROM {entity.Table} WHERE id=@id", ("@id", id));
        if (entity == Entities.Candidates)
            DecodeContacts(records, "phone", "email");
        return records.Rows.Count == 1 ? records.Rows[0] : throw new InvalidOperationException("Запись уже удалена.");
    }

    public List<Choice> Lookup(Session session, string kind)
    {
        Require(session, Security.Manager);
        string sql = kind switch
        {
            "employers" => "SELECT id, CONCAT(name,' · ',city) FROM employers ORDER BY name",
            "vacancies" => "SELECT t.id, CONCAT(" + Entities.VacancyNumber +
                ",' · ',t.title,' · ',e.name) FROM vacancies t JOIN employers e ON e.id=t.employer_id ORDER BY t.id DESC",
            "activeVacancies" => "SELECT t.id, CONCAT(" + Entities.VacancyNumber +
                ",' · ',t.title,' · ',e.name) FROM vacancies t JOIN employers e ON e.id=t.employer_id WHERE t.status='В работе' ORDER BY t.id DESC",
            "candidates" => "SELECT id, CONCAT(full_name,' · ',city,' [',id,']') FROM candidates ORDER BY full_name",
            "recruitments" => """
                SELECT r.id, CONCAT('#',r.id,' · ',v.title,' · ',c.full_name)
                FROM recruitments r JOIN vacancies v ON r.vacancy_id=v.id JOIN candidates c ON r.candidate_id=c.id
                ORDER BY r.id DESC
                """,
            _ => throw new InvalidOperationException("Неизвестный справочник.")
        };
        return Query(settings.Manager, sql).Rows.Cast<DataRow>().Select(r => new Choice(Convert.ToUInt32(r[0]), (string)r[1])).ToList();
    }

    public DataTable SelectCandidates(Session session, uint vacancy)
    {
        Require(session, Security.Manager);
        return Query(settings.Manager, """
            SELECT c.id, c.full_name AS 'ФИО', c.desired_title AS 'Должность', c.city AS 'Город', c.experience AS 'Опыт, лет'
            FROM vacancies v JOIN candidates c
              ON c.desired_title=v.title AND c.city=v.city AND c.experience>=v.min_experience
            WHERE v.id=@id AND v.status='В работе' AND c.consent=TRUE
            ORDER BY c.experience DESC, c.full_name
            """, ("@id", vacancy));
    }

    public List<uint> FindContacts(Session session, object? phone, object? email)
    {
        Require(session, Security.Manager);
        return Query(settings.Manager, """
            SELECT id FROM candidates
            WHERE (@phone IS NOT NULL AND phone=@phone) OR (@email IS NOT NULL AND email=@email)
            """, ("@phone", cipher.EncodePhone(phone as string)), ("@email", cipher.EncodeEmail(email as string))).Rows.Cast<DataRow>()
            .Select(row => Convert.ToUInt32(row[0])).ToList();
    }

    public uint Save(Session session, Entity entity, uint? id, IReadOnlyDictionary<string, object?> values)
    {
        Require(session, Security.Manager);
        var columns = entity.Fields.Select(f => f.Key).ToArray();
        var parameters = columns.Select(k => ("@" + k, ForStorage(entity, k, values[k]))).ToArray();
        using var connection = Open(settings.Manager);
        if (id.HasValue)
        {
            string setters = string.Join(",", columns.Select(k => $"{k}=@{k}"));
            using var command = Command(connection, $"UPDATE {entity.Table} SET {setters} WHERE id=@id",
                [..parameters, ("@id", id.Value)]);
            if (command.ExecuteNonQuery() == 0)
                throw new InvalidOperationException("Запись уже удалена. Обновите список.");
            return id.Value;
        }

        string names = string.Join(",", columns);
        string placeholders = string.Join(",", columns.Select(k => "@" + k));
        using var insert = Command(connection, $"INSERT INTO {entity.Table} ({names}) VALUES ({placeholders})", parameters);
        insert.ExecuteNonQuery();
        return checked((uint)insert.LastInsertedId);
    }

    public void Delete(Session session, Entity entity, uint id)
    {
        Require(session, Security.Manager);
        Execute(settings.Manager, $"DELETE FROM {entity.Table} WHERE id=@id", ("@id", id));
    }

    private object? ForStorage(Entity entity, string key, object? value)
    {
        if (entity != Entities.Candidates) return value;
        return key switch
        {
            "phone" => cipher.EncodePhone(value as string),
            "email" => cipher.EncodeEmail(value as string),
            _ => value
        };
    }

    private void DecodeContacts(DataTable table, string phone, string email)
    {
        table.Columns[phone]!.ReadOnly = false;
        table.Columns[email]!.ReadOnly = false;
        foreach (DataRow row in table.Rows)
        {
            row[phone] = (object?)cipher.DecodePhone(row.Field<string>(phone)) ?? DBNull.Value;
            row[email] = (object?)cipher.DecodeEmail(row.Field<string>(email)) ?? DBNull.Value;
        }
    }

    private static void Require(Session session, string role)
    {
        if (session.Role != role)
            throw new InvalidOperationException("У текущей роли нет доступа к этой операции.");
    }
}
