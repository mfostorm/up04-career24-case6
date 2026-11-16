using System.Data;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text.Json;
using MySql.Data.MySqlClient;

namespace Career24.Case5;

internal static class Verify
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web){WriteIndented=true};
    private static readonly List<string> Passed=[];
    private static ConnectionSettings settings=null!;
    private static DbAccount root=null!;
    private static DbAccount backup=null!;
    private static string folder="";
    private static string state="";

    [STAThread]
    public static int Main(string[] args)
    {
        state=args[1]; folder=args[2];
        Directory.CreateDirectory(folder);
        settings=JsonSerializer.Deserialize<ConnectionSettings>(File.ReadAllText(Path.Combine(state,"connection.json")),Json)!;
        using var secrets=JsonDocument.Parse(File.ReadAllText(Path.Combine(state,"credentials.json")));
        root=new("root",secrets.RootElement.GetProperty("admin").GetString()!);
        backup=new("c5_backup",secrets.RootElement.GetProperty("backup").GetString()!);
        if (args[0]=="restore")
        {
            string schema=args[3];
            if (!System.Text.RegularExpressions.Regex.IsMatch(schema,@"^career24_recovery_[a-zA-Z0-9_]+$"))
                return 2;
            CheckRecovery(schema);
            File.WriteAllText(Path.Combine(folder,"checks-restore.json"),JsonSerializer.Serialize(Passed,Json));
        }
        else if (args[0]=="recovered")
        {
            var db=new Database(settings);
            var manager=db.SignIn("manager","CareerDemo!24");
            var admin=db.SignIn("admin","CareerDemo!24");
            CheckRecovery(settings.Database);
            Expect(db.List(manager,Entities.Vacancies,"Бухгалтер").Rows.Count==3,
                "Менеджер вошёл в восстановленный экземпляр и нашёл три вакансии");
            Expect(db.Users(admin,"").Rows.Count==3,"Роль администратора и процедура восстановления прав доступны");
            Expect(Sql(root,"SHOW PROCEDURE STATUS WHERE Db='career24_up04' AND Name='activate_manager'").Rows.Count==1,
                "Процедура активации создана заново после импорта");
            File.WriteAllText(Path.Combine(folder,"checks-full-recovery.json"),JsonSerializer.Serialize(Passed,Json));
        }
        else if (args[0]=="ci")
        {
            CheckDac(false);
            CheckApplication(false);
            File.WriteAllText(Path.Combine(folder,"checks-ci.json"),JsonSerializer.Serialize(Passed,Json));
        }
        else if (args[0]=="measure")
        {
            MeasureQueries();
        }
        else
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args[0]=="contact-picture")
            {
                var db=new Database(settings);
                CaptureContact(db,db.SignIn("manager","CareerDemo!24"),"candidate01@example.invalid");
                return 0;
            }
            CheckDac(true);
            CheckApplication(true);
            File.WriteAllText(Path.Combine(folder,"checks-live.json"),JsonSerializer.Serialize(Passed,Json));
        }
        Console.WriteLine($"{Passed.Count} passed.");
        return 0;
    }

    private static DataTable Sql(DbAccount account,string sql,string? schema=null)
    {
        using var connection=new MySqlConnection(new MySqlConnectionStringBuilder{
            Server=settings.Server,Port=settings.Port,Database=schema??"",UserID=account.User,Password=account.Password,
            SslMode=MySqlSslMode.Required,CharacterSet="utf8mb4",Pooling=false
        }.ConnectionString);
        connection.Open();
        using var cmd=new MySqlCommand(sql,connection);
        using var reader=cmd.ExecuteReader();
        var table=new DataTable(); table.Load(reader);
        return table;
    }

    private static void CheckDac(bool screenshots)
    {
        var checks=new List<(string Subject,string Operation,string Result)>();
        void Denied(DbAccount a,string operation,string sql)
        {
            try { Sql(a,sql); }
            catch (MySqlException ex) when(ex.Number is 1142 or 1143 or 1370)
            {
                checks.Add((a.User,operation,$"Отказ MySQL {ex.Number}"));
                Pass($"{a.User}: отказ MySQL {ex.Number} для «{operation}»");
                return;
            }
            throw new InvalidOperationException("Ожидался отказ для "+operation);
        }

        const string candidates="SELECT id FROM career24_up04.candidates WHERE id=1";
        var guest=new DbAccount("c5_guest_lab",Guid.NewGuid().ToString("N"));
        Sql(root,$"CREATE USER '{guest.User}'@'127.0.0.1' IDENTIFIED BY '{guest.Password}' REQUIRE SSL");
        try
        {
            Denied(guest,"SELECT candidates до выдачи права",candidates);
            Sql(root,"GRANT SELECT ON career24_up04.candidates TO 'c5_guest_lab'@'127.0.0.1'");
            Expect(Sql(guest,candidates).Rows.Count==1,"Гость прочитал одну тестовую строку после GRANT");
            checks.Add((guest.User,"GRANT SELECT → SELECT candidates","Доступ: строка id=1"));
            Denied(guest,"UPDATE candidates после GRANT SELECT",
                "UPDATE career24_up04.candidates SET city='Проверка' WHERE id=0");
            Sql(root,"REVOKE SELECT ON career24_up04.candidates FROM 'c5_guest_lab'@'127.0.0.1'");
            Denied(guest,"REVOKE SELECT → SELECT candidates",candidates);
        }
        finally { Sql(root,"DROP USER 'c5_guest_lab'@'127.0.0.1'"); }

        Denied(settings.Manager,"SELECT users","SELECT login FROM career24_up04.users");
        Denied(settings.Administrator,"SELECT candidates",candidates);
        Denied(settings.Administrator,"SELECT password_hash","SELECT password_hash FROM career24_up04.users");
        Denied(settings.Administrator,"UPDATE users напрямую","UPDATE career24_up04.users SET is_active=FALSE WHERE id=0");
        Denied(settings.Identity,"UPDATE users напрямую","UPDATE career24_up04.users SET is_active=TRUE WHERE id=0");
        Denied(backup,"UPDATE candidates","UPDATE career24_up04.candidates SET city='Проверка' WHERE id=0");
        Expect(Sql(settings.Manager,candidates).Rows.Count==1,"Роль менеджера читает рабочую таблицу");
        var tls=Sql(settings.Manager,"SHOW SESSION STATUS LIKE 'Ssl_cipher'").Rows[0][1].ToString();
        string bind=Sql(root,"SELECT @@bind_address AS addr").Rows[0][0].ToString()!;
        Expect(!string.IsNullOrWhiteSpace(tls) && bind=="127.0.0.1","Проверены TLS и адрес loopback");
        File.WriteAllText(Path.Combine(folder,"audit.json"),JsonSerializer.Serialize(new {tls,bind},Json));

        if (!screenshots) return;
        var data=new DataTable();data.Columns.Add("Учётная запись");data.Columns.Add("Операция");data.Columns.Add("Результат");
        foreach(var (role,op,result) in checks) data.Rows.Add(role,op,result);
        using var form=new Form{Text="Кейс 5 — результаты проверок ролей MySQL",ClientSize=new Size(1100,420),
            Font=Ui.DefaultFont,BackColor=Color.White,StartPosition=FormStartPosition.CenterScreen};
        var grid=Ui.Grid();grid.DataSource=data;
        form.Controls.Add(grid);
        Capture(form,"access");
    }

    private static void CheckApplication(bool screenshots)
    {
        var db=new Database(settings);
        db.CheckConnection();
        var manager=db.SignIn("manager","CareerDemo!24");
        var admin=db.SignIn("admin","CareerDemo!24");
        string login="case5_"+DateTime.Now.ToString("HHmmssfff");
        db.Register("Учебный Сотрудник",login,"Case5Demo!2026","Case5Demo!2026");
        uint user=Convert.ToUInt32(db.Users(admin,login).Rows[0][0]);
        db.ActivateManager(admin,user);
        Expect(db.SignIn(login,"Case5Demo!2026").Role==Security.Manager,
            "Администратор активировал нового менеджера через процедуру");

        const string phone="70000002001";
        const string email="candidate01@example.invalid";
        var table=Sql(root,"SELECT phone,email FROM candidates WHERE id=1",settings.Database);
        var row=table.Rows[0];
        var rawPhone=row.Field<string>("phone")!;
        var rawEmail=row.Field<string>("email")!;
        Expect(rawPhone.StartsWith("K5P:") && rawEmail.StartsWith("K5E:") && rawPhone!=phone && rawEmail!=email,
            "MySQL возвращает шифртекст для двух полей карточки #1");
        var refs=db.FindContacts(manager,phone,email);
        Expect(refs.SequenceEqual(new[]{1u}),"Поиск дубликата по двум исходным контактам находит #1");
        var saved=db.Read(manager,Entities.Candidates,1);
        Expect(saved.Field<string>("phone")==phone && saved.Field<string>("email")==email,
           "Форма читает исходные контакты после преобразования");
        Expect(db.List(manager,Entities.Candidates,"02001").Rows.Count==1 &&
               db.List(manager,Entities.Candidates,"candidate01").Rows.Count==1,
            "Поиск по частям телефона и адреса сохранён");
        int untagged=Convert.ToInt32(Sql(root,"""
            SELECT COUNT(*) FROM candidates WHERE (phone IS NOT NULL AND LEFT(phone,4)<>'K5P:')
              OR (email IS NOT NULL AND LEFT(email,4)<>'K5E:')
            """,settings.Database).Rows[0][0]);
        Expect(untagged==0,"Во всех перенесённых карточках контакты хранятся с признаками шифртекста");

        string title="Учебная запись К5";
        const string trialPhone="70000005581";
        const string trialEmail="иван+проверка@example.invalid";
        var fields=new Dictionary<string,object?> {
            ["full_name"]=title,["desired_title"]="Бухгалтер",["city"]="Воронеж",["experience"]=2,
            ["phone"]=trialPhone,["email"]=trialEmail,["consent"]=true
        };
        uint id=db.Save(manager,Entities.Candidates,null,fields);
        try
        {
            var created=db.Read(manager,Entities.Candidates,id);
            Expect(created.Field<string>("email")==trialEmail && created.Field<string>("phone")==trialPhone,
                "C# сохранил и вернул контакты UTF-8, включая кириллицу в адресе");
            var values=Sql(root,$"SELECT phone,email FROM candidates WHERE id={id}",settings.Database).Rows[0];
            Expect(values.Field<string>("phone")!=trialPhone && values.Field<string>("email")!=trialEmail,
                "Новая карточка записана в MySQL в зашифрованном виде");
            fields["phone"]=null;
            fields["email"]="edited-case5@example.invalid";
            db.Save(manager,Entities.Candidates,id,fields);
            var changed=db.Read(manager,Entities.Candidates,id);
            Expect(changed.IsNull("phone") && changed.Field<string>("email")=="edited-case5@example.invalid",
                "Редактирование защищённого поля и сохранение NULL проверены");
        }
        finally { db.Delete(manager,Entities.Candidates,id); }

        if (!screenshots) return;
        using var display=new Form{Text="Содержимое MySQL — контакты учебной карточки",ClientSize=new Size(1100,185),Font=Ui.DefaultFont};
        var grid=Ui.Grid();grid.DataSource=table;
        display.Controls.Add(grid);
        Capture(display,"mysql-ciphertext");

        CaptureContact(db,manager,email);
        File.WriteAllText(Path.Combine(folder,"sample.json"),JsonSerializer.Serialize(new{
            record=1,phone,rawPhone,email,rawEmail
        },Json));
    }

    private static void MeasureQueries()
    {
        const int warmup=5, trials=40;
        var db=new Database(settings);
        var manager=db.SignIn("manager","CareerDemo!24");
        var measurements=new List<double>();
        for (int i=-warmup;i<trials;i++)
        {
            var start=Stopwatch.GetTimestamp();
            var rows=db.List(manager,Entities.Vacancies,"Бухгалтер");
            double elapsed=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (rows.Rows.Count!=3)
                throw new InvalidOperationException($"В запросе {i+1} ожидались три вакансии.");
            if (i>=0) measurements.Add(elapsed);
        }
        File.WriteAllText(Path.Combine(folder,"measurements.json"),JsonSerializer.Serialize(measurements,Json));
    }

    private static void CaptureContact(Database db,Session manager,string email)
    {
        using var form=new MainForm(db,manager,()=>{});
        form.ClientSize=new Size(1110,395);
        form.Show(); Pump();
        var tabs=Find<TabControl>(form).Single();
        tabs.SelectedTab=tabs.TabPages.Cast<TabPage>().Single(t=>t.Text=="Соискатели");
        var active=tabs.SelectedTab!;
        Find<TextBox>(active).Single().Text="candidate01";
        Find<Button>(active).Single(b=>b.Text=="Найти").PerformClick();
        var view=Find<DataGridView>(active).Single();
        Expect(view.Rows.Count==1 && view.Rows[0].Cells["Почта"].Value.ToString()==email,
            "Интерфейс отображает расшифрованное значение в результате поиска");
        view.Columns["Почта"].FillWeight=180;
        Capture(form,"readable-contact");
    }

    private static void CheckRecovery(string schema)
    {
        var sample=Sql(root,"SELECT phone,email FROM candidates WHERE id=1",schema).Rows[0];
        var cipher=new ContactCipher(settings.CipherKey);
        Expect(cipher.DecodePhone(sample.Field<string>("phone"))=="70000002001" &&
            cipher.DecodeEmail(sample.Field<string>("email"))=="candidate01@example.invalid",
            "Восстановленный шифртекст читается с ключом профиля");
        Expect(Convert.ToInt32(Sql(root,"SELECT COUNT(*) FROM users",schema).Rows[0][0])==3,
            "В копии восстановлены три учётные записи учебного стенда");
    }

    private static void Expect(bool state,string message)
    {
        if(!state) throw new InvalidOperationException(message);
        Pass(message);
    }
    private static void Pass(string message) { Passed.Add(message);Console.WriteLine(message); }
    private static IEnumerable<T> Find<T>(Control parent) where T:Control
    {
        foreach(Control c in parent.Controls)
        {
            if(c is T found) yield return found;
            foreach(var sub in Find<T>(c)) yield return sub;
        }
    }
    private static void Pump(){Application.DoEvents();Thread.Sleep(100);Application.DoEvents();}
    private static void Capture(Form form,string name)
    {
        if(!form.Visible) form.Show();
        Pump();
        using var bmp=new Bitmap(form.Width,form.Height,PixelFormat.Format24bppRgb);
        form.DrawToBitmap(bmp,new Rectangle(Point.Empty,bmp.Size));
        bmp.Save(Path.Combine(folder,name+".png"),ImageFormat.Png);
    }
}
