namespace Career24;

internal enum FieldKind { Text, Number, Money, Choice, Reference, Date, Check }

internal sealed record Field(string Key, string Label, FieldKind Kind, int Maximum = 0, bool Optional = false,
    string[]? Choices = null, string? Lookup = null);
internal sealed record Entity(string Table, string Caption, string From, string Columns, string Search, Field[] Fields);
internal sealed record Choice(uint Id, string Label)
{
    public override string ToString() => Label;
}

internal static class Entities
{
    private static Field Text(string key, string label, int limit, bool optional = false) =>
        new(key, label, FieldKind.Text, limit, optional);
    private static Field Ref(string key, string label, string lookup) => new(key, label, FieldKind.Reference, Lookup: lookup);
    private static Field State(params string[] values) => new("status", "Статус", FieldKind.Choice, Choices: values);

    public static readonly Entity Employers = new("employers", "Работодатели",
        "employers t",
        "t.id, t.name AS 'Предприятие', t.city AS 'Город', t.contact_name AS 'Контактное лицо', t.contact_phone AS 'Телефон'",
        "t.name,t.city,t.contact_name,t.contact_phone",
        [Text("name", "Предприятие", 120), Text("city", "Город", 60),
         Text("contact_name", "Контактное лицо", 100), Text("contact_phone", "Телефон", 20)]);

    public static readonly Entity Vacancies = new("vacancies", "Вакансии",
        "vacancies t JOIN employers e ON e.id=t.employer_id",
        "t.id, " + VacancyNumber + " AS 'Номер', t.title AS 'Должность', e.name AS 'Предприятие', t.city AS 'Город', t.min_experience AS 'Опыт от', t.salary_from AS 'Зарплата от', t.status AS 'Статус'",
        VacancyNumber + ",t.title,e.name,t.city,t.status",
        [Ref("employer_id", "Работодатель", "employers"), Text("title", "Должность", 100),
         Text("city", "Город работы", 60), new("min_experience", "Мин. опыт, лет", FieldKind.Number, 60),
         new("salary_from", "Зарплата от, руб.", FieldKind.Money, 99999999), Text("requirements", "Требования", 400),
         State("В работе","Приостановлена","Закрыта")]);

    public static readonly Entity Candidates = new("candidates", "Соискатели",
        "candidates t",
        "t.id, t.full_name AS 'ФИО', t.desired_title AS 'Должность', t.city AS 'Город', t.experience AS 'Опыт, лет', t.phone AS 'Телефон', t.email AS 'Почта'",
        "t.full_name,t.desired_title,t.city,t.phone,t.email",
        [Text("full_name","ФИО",100), Text("desired_title","Желаемая должность",100), Text("city","Город",60),
         new("experience","Опыт, лет",FieldKind.Number,60), Text("phone","Телефон",20,true),
         Text("email","Электронная почта",128,true), new("consent","Согласие получено",FieldKind.Check)]);

    public static readonly Entity Recruitments = new("recruitments", "Подбор",
        "recruitments t JOIN vacancies v ON v.id=t.vacancy_id JOIN candidates c ON c.id=t.candidate_id",
        "t.id, v.title AS 'Вакансия', c.full_name AS 'Соискатель', t.status AS 'Статус'",
        "v.title,c.full_name,t.status,t.id",
        [Ref("vacancy_id","Вакансия","vacancies"), Ref("candidate_id","Соискатель","candidates"),
         State("На рассмотрении","Отложен","Завершён")]);

    public static readonly Entity Interviews = new("interviews", "Собеседования",
        "interviews t JOIN recruitments r ON r.id=t.recruitment_id JOIN vacancies v ON v.id=r.vacancy_id JOIN candidates c ON c.id=r.candidate_id",
        "t.id, " + InterviewNumber + " AS 'Номер', v.title AS 'Вакансия', c.full_name AS 'Соискатель', t.scheduled_at AS 'Дата и время', t.status AS 'Статус'",
        InterviewNumber + ",v.title,c.full_name,t.status",
        [Ref("recruitment_id","Запись подбора","recruitments"), new("scheduled_at","Дата и время",FieldKind.Date),
         new("candidate_confirmed","Соискатель подтвердил",FieldKind.Check),
         new("employer_confirmed","Работодатель подтвердил",FieldKind.Check),
         State("Назначено","Проведено","Отменено")]);

    public static readonly Entity[] Business = [Employers,Vacancies,Candidates,Recruitments,Interviews];
    public const string VacancyNumber = "CONCAT('VAC-',LPAD(t.id,GREATEST(6,CHAR_LENGTH(t.id)),'0'))";
    public const string InterviewNumber = "CONCAT('INT-',LPAD(t.id,GREATEST(6,CHAR_LENGTH(t.id)),'0'))";
}
