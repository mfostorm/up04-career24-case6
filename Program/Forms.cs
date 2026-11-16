using System.Data;
using System.Globalization;
using System.Net.Mail;
using MySql.Data.MySqlClient;

namespace Career24;

internal static class Ui
{
    public static readonly Color Navy = Color.FromArgb(28, 50, 76);
    public static readonly Color Accent = Color.FromArgb(34, 90, 145);
    public static readonly Color Background = Color.FromArgb(244, 247, 251);
    public static readonly Font DefaultFont = new("Segoe UI", 10);
    public static readonly Font TitleFont = new("Segoe UI", 17, FontStyle.Bold);

    public static Button Button(string text, Action action, bool primary = false)
    {
        var button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(90, 34),
            Padding = new Padding(8, 0, 8, 0), Margin = new Padding(0, 0, 8, 4), FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Navy };
        button.FlatAppearance.BorderColor = primary ? Accent : Color.FromArgb(191, 203, 215);
        button.Click += (_, _) => action();
        return button;
    }

    public static Label Label(string value) => new() { Text = value, AutoSize = true, ForeColor = Navy,
        Margin = new Padding(0, 6, 8, 6) };

    public static FlowLayoutPanel Bar() => new() { AutoSize = true, Dock = DockStyle.Top, WrapContents = true,
        Padding = new Padding(0, 2, 0, 6) };

    public static TextBox Text(string accessible, string hint = "", int max = 120) =>
        new() { AccessibleName = accessible, PlaceholderText = hint, MaxLength = max, MinimumSize = new Size(100, 0),
            Margin = new Padding(0, 4, 10, 4) };

    public static TextBox Password(string accessible, string hint) =>
        new() { AccessibleName = accessible, PlaceholderText = hint, MaxLength = 120, PasswordChar = '*', Dock = DockStyle.Fill };

    public static DataGridView Grid() => new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false, MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Color.White,
        BorderStyle = BorderStyle.FixedSingle, RowHeadersVisible = false, RowTemplate = { Height = 29 },
        AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(244, 248, 252) },
        DefaultCellStyle = new DataGridViewCellStyle { SelectionBackColor = Color.FromArgb(213, 230, 247),
            SelectionForeColor = Navy, NullValue = "—" }
    };

    public static void Bind(DataGridView grid, DataTable table)
    {
        grid.DataSource = table;
        if (grid.Columns.Contains("id"))
            grid.Columns["id"]!.Visible = false;
        foreach (DataGridViewColumn col in grid.Columns)
            if (col.ValueType == typeof(DateTime))
                col.DefaultCellStyle.Format = col.HeaderText == "Дата и время" ? "dd.MM.yyyy HH:mm" : "dd.MM.yyyy";
    }

    public static uint SelectedId(DataGridView grid) =>
        grid.CurrentRow?.DataBoundItem is DataRowView view ? Convert.ToUInt32(view.Row["id"]) :
        throw new InvalidOperationException("Сначала выберите строку.");

    public static void Status(Label target, string text, bool error = false)
    {
        target.ForeColor = error ? Color.FromArgb(148, 39, 39) : Navy;
        target.Text = text;
    }

    public static void Run(Label status, Action action)
    {
        try { action(); }
        catch (InvalidOperationException ex) { Status(status, ex.Message, true); }
        catch (MySqlException ex)
        {
            string message = ex.Number switch
            {
                1062 => "Обнаружена повторяющаяся запись. Проверьте введённые значения.",
                1451 => "Нельзя удалить запись, пока на неё ссылаются другие записи.",
                1452 => "Связанная запись недоступна. Обновите список и повторите действие.",
                3819 => "Запись не прошла проверку целостности. Проверьте значения.",
                _ => $"Ошибка MySQL ({ex.Number}). Проверьте подключение и повторите действие."
            };
            Status(status, message, true);
        }
    }
}

internal sealed class LoginForm : Form
{
    public event Action<Session>? SignedIn;

    public LoginForm(Database database)
    {
        Text = "Кадровое агентство «Карьера» — Вход";
        Font = Ui.DefaultFont;
        BackColor = Ui.Background;
        ClientSize = new Size(555, 350);
        MinimumSize = new Size(525, 372);
        StartPosition = FormStartPosition.CenterScreen;

        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoScroll = true,
            Padding = new Padding(35, 22, 35, 18) };
        var title = Ui.Label("Кадровое агентство «Карьера»");
        title.Font = Ui.TitleFont;
        var login = Ui.Text("Логин", "Логин", 32);
        login.Dock = DockStyle.Fill;
        var password = Ui.Password("Пароль", "Пароль");
        var status = Ui.Label("");
        status.MaximumSize = new Size(457, 0);
        var buttons = Ui.Bar();
        var signIn = Ui.Button("Войти", () => Ui.Run(status, () =>
        {
            var session = database.SignIn(login.Text, password.Text);
            password.Clear();
            SignedIn?.Invoke(session);
        }), true);
        buttons.Controls.Add(signIn);
        buttons.Controls.Add(Ui.Button("Регистрация", () =>
        {
            using var form = new RegistrationForm(database);
            if (form.ShowDialog(this) == DialogResult.OK)
            {
                login.Text = form.NewLogin;
                password.Clear();
                Ui.Status(status, "Запись создана. После активации администратором можно войти.");
            }
        }));
        content.Controls.Add(title);
        content.Controls.Add(Ui.Label("Вход для сотрудников агентства"));
        content.Controls.Add(Ui.Label("Логин"));
        content.Controls.Add(login);
        content.Controls.Add(Ui.Label("Пароль"));
        content.Controls.Add(password);
        content.Controls.Add(buttons);
        content.Controls.Add(status);
        Controls.Add(content);
        AcceptButton = signIn;
        ActiveControl = login;
    }
}

internal sealed class RegistrationForm : Form
{
    public string NewLogin { get; private set; } = "";

    public RegistrationForm(Database database)
    {
        Text = "Карьера — Регистрация сотрудника";
        StartPosition = FormStartPosition.CenterParent;
        Font = Ui.DefaultFont;
        BackColor = Ui.Background;
        ClientSize = new Size(545, 401);
        MinimumSize = new Size(535, 420);

        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 1,
            Padding = new Padding(27, 14, 27, 14) };
        var fullName = Ui.Text("Фамилия, имя и отчество", "Фамилия, имя и отчество", 100);
        var login = Ui.Text("Логин", "Например, recruiter1", 32);
        var password = Ui.Password("Пароль", "Не менее 10 символов");
        var confirmation = Ui.Password("Повтор пароля", "Введите пароль ещё раз");
        fullName.Dock = login.Dock = DockStyle.Fill;
        var status = Ui.Label("");
        status.MaximumSize = new Size(455, 0);
        foreach (var (label, control) in new (string, Control)[]{
            ("ФИО", fullName), ("Логин", login), ("Пароль", password), ("Повтор пароля", confirmation)})
        {
            panel.Controls.Add(Ui.Label(label));
            panel.Controls.Add(control);
        }
        var bar = Ui.Bar();
        var submit = Ui.Button("Зарегистрироваться", () => Ui.Run(status, () =>
        {
            database.Register(fullName.Text, login.Text, password.Text, confirmation.Text);
            NewLogin = Security.NormalizeLogin(login.Text);
            DialogResult = DialogResult.OK;
        }), true);
        var cancel = Ui.Button("Отмена", () => DialogResult = DialogResult.Cancel);
        bar.Controls.AddRange([submit, cancel]);
        panel.Controls.Add(bar);
        panel.Controls.Add(status);
        Controls.Add(panel);
        AcceptButton = submit;
        CancelButton = cancel;
    }
}

internal sealed class RecordForm : Form
{
    private readonly Dictionary<string, Control> inputs = [];
    private readonly Entity entity;
    private readonly DataRow? row;
    private readonly Label status = Ui.Label("");

    public IReadOnlyDictionary<string, object?> Values { get; private set; } = new Dictionary<string, object?>();

    public RecordForm(Database database, Session session, Entity entity, DataRow? row)
    {
        this.entity = entity;
        this.row = row;
        Text = $"{entity.Caption} — {(row is null ? "Добавление" : "Изменение")}";
        Font = Ui.DefaultFont;
        BackColor = Ui.Background;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(655, Math.Min(520, 161 + entity.Fields.Length * 42));
        MinimumSize = new Size(630, 325);

        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true,
            Padding = new Padding(22, 19, 22, 12) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 205));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var field in entity.Fields)
        {
            var value = row?[field.Key];
            var control = MakeInput(database, session, field, value is DBNull ? null : value);
            control.Dock = DockStyle.Top;
            control.AccessibleName = field.Label;
            inputs.Add(field.Key, control);
            panel.Controls.Add(Ui.Label(field.Label));
            panel.Controls.Add(control);
        }
        var bar = Ui.Bar();
        var save = Ui.Button("Сохранить", () => Ui.Run(status, ValidateFields), true);
        var cancel = Ui.Button("Отмена", () => DialogResult = DialogResult.Cancel);
        bar.Controls.AddRange([save, cancel]);
        panel.Controls.Add(bar, 0, entity.Fields.Length);
        panel.SetColumnSpan(bar, 2);
        status.MaximumSize = new Size(580, 0);
        panel.Controls.Add(status, 0, entity.Fields.Length + 1);
        panel.SetColumnSpan(status, 2);
        Controls.Add(panel);
        AcceptButton = save;
        CancelButton = cancel;
    }

    private static Control MakeInput(Database db, Session session, Field field, object? value)
    {
        switch (field.Kind)
        {
            case FieldKind.Number:
            case FieldKind.Money:
                return new NumericUpDown { Maximum = field.Maximum,
                    DecimalPlaces = field.Kind == FieldKind.Money ? 2 : 0, ThousandsSeparator = true,
                    Value = value is null ? 0 : Convert.ToDecimal(value, CultureInfo.InvariantCulture) };
            case FieldKind.Check:
                return new CheckBox { Text = "Да", AutoSize = true,
                    Checked = value is not null && Convert.ToBoolean(value), Margin = new Padding(4, 4, 0, 3) };
            case FieldKind.Date:
                return new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd.MM.yyyy HH:mm",
                    ShowUpDown = true, Value = value is DateTime time ? time : DateTime.Today.AddDays(1).AddHours(10) };
            case FieldKind.Reference:
            case FieldKind.Choice:
                var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, DropDownWidth = 480 };
                if (field.Kind == FieldKind.Reference)
                {
                    var choices = db.Lookup(session, field.Lookup!);
                    combo.Items.AddRange(choices.Cast<object>().ToArray());
                    if (value is not null)
                        combo.SelectedItem = choices.Find(c => c.Id == Convert.ToUInt32(value));
                }
                else
                {
                    combo.Items.AddRange(field.Choices!);
                    combo.SelectedItem = value?.ToString() ?? field.Choices![0];
                }
                return combo;
            default:
                var input = Ui.Text(field.Label, max: field.Maximum);
                input.Text = value?.ToString() ?? "";
                if (field.Maximum > 200) { input.Multiline = true; input.Height = 62; input.ScrollBars = ScrollBars.Vertical; }
                return input;
        }
    }

    private void ValidateFields()
    {
        var values = new Dictionary<string, object?>();
        foreach (var field in entity.Fields)
        {
            var control = inputs[field.Key];
            object? value = control switch
            {
                TextBox t => t.Text.Trim().Length > 0 ? t.Text.Trim() : null,
                ComboBox c when c.SelectedItem is Choice choice => choice.Id,
                ComboBox c => c.SelectedItem,
                NumericUpDown n => n.Value,
                DateTimePicker d => new DateTime(d.Value.Year, d.Value.Month, d.Value.Day, d.Value.Hour, d.Value.Minute, 0),
                CheckBox c => c.Checked,
                _ => null
            };
            if (value is null && !field.Optional)
            {
                control.Focus();
                throw new InvalidOperationException($"Заполните поле «{field.Label}».");
            }

            if (field.Key is "phone" or "contact_phone" && value is string phone)
            {
                string digits = string.Concat(phone.Where(c => c is >= '0' and <= '9'));
                if (digits.Length is < 10 or > 15 || phone.Any(c => !char.IsAsciiDigit(c) && !"+ ()-".Contains(c)))
                    throw new InvalidOperationException("В телефоне должно быть от 10 до 15 цифр.");
                if (digits.Length == 11 && digits[0] == '8') digits = "7" + digits[1..];
                value = digits;
            }
            if (field.Key == "email" && value is string email)
            {
                if (!MailAddress.TryCreate(email, out var address) || address.Address != email || !email.Contains('.'))
                    throw new InvalidOperationException("Проверьте адрес электронной почты.");
                value = email.ToLowerInvariant();
            }
            values[field.Key] = value;
        }

        if (entity == Entities.Candidates && (values["phone"] is null && values["email"] is null))
            throw new InvalidOperationException("Укажите хотя бы один контакт.");
        if (entity == Entities.Candidates && row is null && !(bool)values["consent"]!)
            throw new InvalidOperationException("Отметьте полученное согласие.");

        if (entity == Entities.Interviews && (row is null || (string)values["status"]! != "Отменено") &&
            (!(bool)values["candidate_confirmed"]! || !(bool)values["employer_confirmed"]!))
            throw new InvalidOperationException("Для сохранения нужны подтверждения обеих сторон.");
        if (entity == Entities.Interviews && row is null && (DateTime)values["scheduled_at"]! < DateTime.Now)
            throw new InvalidOperationException("Для новой встречи выберите дату и время в будущем.");

        Values = values;
        DialogResult = DialogResult.OK;
    }
}
