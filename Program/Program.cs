using System.Text.Json;
using MySql.Data.MySqlClient;

namespace Career24;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args is ["--migrate-contacts", var migrationFile, var migrationLog])
            return Migrate(migrationFile, migrationLog);

        ApplicationConfiguration.Initialize();
        try
        {
            string settingsPath = args.FirstOrDefault()
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Career24-UP04-Case5", "connection.json");
            var settings = JsonSerializer.Deserialize<ConnectionSettings>(
                File.ReadAllText(settingsPath), new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidOperationException("Файл подключения пуст.");
            var database = new Database(settings);
            database.CheckConnection();

            Application.Run(new CareerContext(database));
            return 0;
        }
        catch (Exception ex) when (ex is IOException or JsonException or MySqlException or InvalidOperationException)
        {
            MessageBox.Show("Не удалось открыть приложение. Запустите «Запустить Карьеру.cmd» и проверьте подключение MySQL.\n\n"
                + ex.Message, "Кадровое агентство «Карьера»", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static int Migrate(string settingsPath, string logPath)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<MigrationSettings>(
                File.ReadAllText(settingsPath), new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidOperationException("Migration settings are empty.");
            int updated = ContactMigration.Run(settings);
            File.WriteAllText(logPath, $"Rows migrated: {updated}");
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(logPath, "Contact migration failed: " + ex.GetType().Name);
            return 1;
        }
    }
}

internal sealed class CareerContext : ApplicationContext
{
    private readonly Database database;
    private Form? current;

    public CareerContext(Database database)
    {
        this.database = database;
        OpenLogin();
    }

    public void OpenLogin()
    {
        var login = new LoginForm(database);
        login.SignedIn += session => ShowWindow(new MainForm(database, session, OpenLogin));
        ShowWindow(login);
    }

    private void ShowWindow(Form next)
    {
        Form? previous = current;
        current = next;
        MainForm = next;
        next.Show();
        previous?.Close();
    }
}
