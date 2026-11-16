using System.Data;

namespace Career24;

internal sealed class MainForm : Form
{
    private readonly Database database;
    private readonly Session session;
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill, Padding = new Point(17, 7), AccessibleName = "Разделы" };
    private readonly Dictionary<TabPage, Action> refreshers = [];
    private readonly EntityPage? recruitments;
    private readonly TabPage? recruitmentTab;

    public MainForm(Database database, Session session, Action signOut)
    {
        this.database = database;
        this.session = session;
        Text = "Кадровое агентство «Карьера» — " + RoleTitle;
        StartPosition = FormStartPosition.CenterScreen;
        Font = Ui.DefaultFont;
        BackColor = Ui.Background;
        ClientSize = new Size(1120, 674);
        MinimumSize = new Size(890, 545);

        var header = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 75, Padding = new Padding(15, 7, 10, 6),
            BackColor = Color.White, WrapContents = false };
        var title = Ui.Label("Кадровое агентство «Карьера»");
        title.Font = Ui.TitleFont;
        title.Margin = new Padding(0, 9, 22, 4);
        var person = Ui.Label(session.FullName + "  |  " + RoleTitle);
        person.Margin = new Padding(0, 16, 22, 4);
        var outButton = Ui.Button("Выйти", signOut);
        outButton.Margin = new Padding(5, 9, 4, 4);
        header.Controls.AddRange([title, person, outButton]);
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(13, 7, 13, 13) };
        body.Controls.Add(tabs);
        Controls.Add(body);
        Controls.Add(header);

        if (session.Role == Security.Administrator)
        {
            AddUsers();
        }
        else
        {
            foreach (Entity entity in Entities.Business)
            {
                var page = new EntityPage(database, session, entity);
                var tab = AddPage(entity.Caption, page, page.Reload);
                if (entity == Entities.Recruitments)
                {
                    recruitments = page;
                    recruitmentTab = tab;
                }
            }
            AddSelection();
        }
        tabs.SelectedIndexChanged += (_, _) => RefreshTab();
        Shown += (_, _) => RefreshTab();
    }

    private string RoleTitle => session.Role == Security.Administrator ? "Администратор" : "Менеджер";

    private TabPage AddPage(string name, Control child, Action refresh)
    {
        var tab = new TabPage(name) { BackColor = Color.White };
        tab.Controls.Add(child);
        refreshers.Add(tab, refresh);
        tabs.TabPages.Add(tab);
        return tab;
    }

    private void RefreshTab()
    {
        if (tabs.SelectedTab is { } tab && refreshers.TryGetValue(tab, out var refresh)) refresh();
    }

    private void AddUsers()
    {
        var layout = new EntityLayout();
        void Reload()
        {
            Ui.Run(layout.Status, () =>
            {
                var rows = database.Users(session, layout.Search.Text);
                Ui.Bind(layout.Grid, rows);
                Ui.Status(layout.Status, $"Учётных записей: {rows.Rows.Count}");
            });
        }

        layout.Toolbar.Controls.AddRange([
            layout.Search,
            Ui.Button("Найти", Reload, true),
            Ui.Button("Все записи", () => { layout.Search.Clear(); Reload(); }),
            Ui.Button("Активировать как менеджера", () => Ui.Run(layout.Status, () =>
            {
                uint id = Ui.SelectedId(layout.Grid);
                database.ActivateManager(session, id);
                Reload();
                Ui.Status(layout.Status, "Запись активирована. Пользователь может войти как менеджер.");
            }))
        ]);
        layout.BindEnter(Reload);
        AddPage("Пользователи", layout, Reload);
    }

    private void AddSelection()
    {
        var layout = new EntityLayout();
        var choices = new ComboBox { Width = 440, DropDownWidth = 520, DropDownStyle = ComboBoxStyle.DropDownList,
            AccessibleName = "Активная вакансия", Margin = new Padding(0, 6, 10, 0) };

        void Reload() => Ui.Run(layout.Status, () =>
        {
            var previous = (choices.SelectedItem as Choice)?.Id;
            var active = database.Lookup(session, "activeVacancies");
            choices.Items.Clear();
            choices.Items.AddRange(active.Cast<object>().ToArray());
            if (previous.HasValue)
                choices.SelectedItem = active.Find(c => c.Id == previous.Value);
            layout.Grid.DataSource = null;
            Ui.Status(layout.Status, "Выберите вакансию и нажмите «Найти соискателей».");
        });

        void Find() => Ui.Run(layout.Status, () =>
        {
            if (choices.SelectedItem is not Choice vacancy)
                throw new InvalidOperationException("Выберите активную вакансию.");
            var rows = database.SelectCandidates(session, vacancy.Id);
            Ui.Bind(layout.Grid, rows);
            Ui.Status(layout.Status, rows.Rows.Count == 0
                ? "Подходящих соискателей нет."
                : $"Найдено по должности, городу и опыту: {rows.Rows.Count}");
        });

        layout.Toolbar.Controls.AddRange([
            choices,
            Ui.Button("Найти соискателей", Find, true),
            Ui.Button("Выбрать для подбора", () => Ui.Run(layout.Status, () =>
            {
                if (choices.SelectedItem is not Choice vacancy)
                    throw new InvalidOperationException("Выберите активную вакансию.");
                uint candidate = Ui.SelectedId(layout.Grid);
                uint id = database.Save(session, Entities.Recruitments, null, new Dictionary<string, object?>
                {
                    ["vacancy_id"] = vacancy.Id, ["candidate_id"] = candidate, ["status"] = "На рассмотрении"
                });
                tabs.SelectedTab = recruitmentTab;
                recruitments!.Highlight(id, "Соискатель добавлен в подбор.");
            }))
        ]);
        choices.SelectedIndexChanged += (_, _) =>
        {
            layout.Grid.DataSource = null;
            Ui.Status(layout.Status, "Нажмите «Найти соискателей» для выбранной вакансии.");
        };
        AddPage("Отбор по вакансии", layout, Reload);
    }
}

internal sealed class EntityLayout : UserControl
{
    public DataGridView Grid { get; } = Ui.Grid();
    public TextBox Search { get; } = Ui.Text("Поиск", "Введите текст для поиска");
    public Label Status { get; } = Ui.Label("");
    public FlowLayoutPanel Toolbar { get; } = Ui.Bar();

    public EntityLayout()
    {
        Dock = DockStyle.Fill;
        Padding = new Padding(13, 12, 13, 10);
        BackColor = Color.White;
        Search.Width = 239;
        Search.Margin = new Padding(0, 7, 10, 0);
        Status.Dock = DockStyle.Bottom;
        Status.AutoSize = false;
        Status.Height = 32;
        Status.TextAlign = ContentAlignment.MiddleLeft;
        Controls.Add(Grid);
        Controls.Add(Status);
        Controls.Add(Toolbar);
    }

    public void BindEnter(Action action) => Search.KeyDown += (_, e) =>
    {
        if (e.KeyCode != Keys.Enter) return;
        e.SuppressKeyPress = true;
        action();
    };
}

internal sealed class EntityPage : UserControl
{
    private readonly Database database;
    private readonly Session session;
    private readonly Entity entity;
    private readonly EntityLayout layout = new();

    public EntityPage(Database database, Session session, Entity entity)
    {
        this.database = database;
        this.session = session;
        this.entity = entity;
        Dock = DockStyle.Fill;
        Controls.Add(layout);

        layout.Toolbar.Controls.AddRange([
            layout.Search,
            Ui.Button("Найти", Reload, true),
            Ui.Button("Все записи", () => { layout.Search.Clear(); Reload(); }),
            Ui.Button("Добавить", () => Edit(false)),
            Ui.Button("Изменить", () => Edit(true)),
            Ui.Button("Удалить", Delete)
        ]);
        layout.BindEnter(Reload);
        layout.Grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Edit(true); };
    }

    public void Reload() => Ui.Run(layout.Status, () =>
    {
        var list = database.List(session, entity, layout.Search.Text);
        Ui.Bind(layout.Grid, list);
        Ui.Status(layout.Status, $"Записей: {list.Rows.Count}");
    });

    private void Edit(bool existing) => Ui.Run(layout.Status, () =>
    {
        uint? id = existing ? Ui.SelectedId(layout.Grid) : null;
        var row = id.HasValue ? database.Read(session, entity, id.Value) : null;
        using var edit = new RecordForm(database, session, entity, row);
        if (edit.ShowDialog(this) != DialogResult.OK) return;
        if (!id.HasValue && entity == Entities.Candidates)
        {
            var matches = database.FindContacts(session, edit.Values["phone"], edit.Values["email"]);
            if (matches.Count > 1)
                throw new InvalidOperationException("Телефон и почта относятся к разным карточкам. Уточните контакты.");
            if (matches.Count == 1)
            {
                if (MessageBox.Show(this, "Найдена карточка с таким контактом. Обновить её введёнными сведениями?",
                        "Проверка дубликатов", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                        MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                id = matches[0];
            }
        }
        uint savedId = database.Save(session, entity, id, edit.Values);
        string suffix = entity == Entities.Vacancies ? $" Номер VAC-{savedId:D6}." :
            entity == Entities.Interviews ? $" Номер INT-{savedId:D6}." : "";
        Highlight(savedId, "Запись сохранена." + suffix);
    });

    private void Delete() => Ui.Run(layout.Status, () =>
    {
        uint id = Ui.SelectedId(layout.Grid);
        if (MessageBox.Show(this, "Удалить выбранную запись?", "Подтверждение удаления",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        database.Delete(session, entity, id);
        Reload();
        Ui.Status(layout.Status, "Запись удалена.");
    });

    public void Highlight(uint id, string message)
    {
        layout.Search.Clear();
        Reload();
        foreach (DataGridViewRow row in layout.Grid.Rows)
        {
            if (row.DataBoundItem is not DataRowView value || Convert.ToUInt32(value.Row["id"]) != id) continue;
            layout.Grid.CurrentCell = row.Cells.Cast<DataGridViewCell>().First(c => c.Visible);
            break;
        }
        Ui.Status(layout.Status, message);
    }
}
