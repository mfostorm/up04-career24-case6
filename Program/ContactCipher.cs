using System.Text;

namespace Career24;

internal sealed class ContactCipher
{
    private const string PhoneTag = "K5P:";
    private const string EmailTag = "K5E:";
    private const int PhoneShift = 3;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly byte[] keyword;

    public ContactCipher(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Для контактов не настроен ключ шифрования.");
        keyword = Utf8.GetBytes(key);
    }

    public string? EncodePhone(string? value) =>
        value is null ? null : PhoneTag + RotateDigits(value, PhoneShift);

    public string? DecodePhone(string? value) =>
        value?.StartsWith(PhoneTag, StringComparison.Ordinal) == true
            ? RotateDigits(value[PhoneTag.Length..], 10 - PhoneShift)
            : value;

    private static string RotateDigits(string text, int step) =>
        string.Concat(text.Select(c => char.IsAsciiDigit(c)
            ? (char)('0' + (c - '0' + step) % 10)
            : c));

    public string? EncodeEmail(string? value)
    {
        if (value is null) return null;
        byte[] bytes = Utf8.GetBytes(value);
        ApplyKeyword(bytes, encrypt: true);
        return EmailTag + Convert.ToBase64String(bytes);
    }

    public string? DecodeEmail(string? value)
    {
        if (!IsEncodedEmail(value)) return value;
        try
        {
            var bytes = Convert.FromBase64String(value![EmailTag.Length..]);
            ApplyKeyword(bytes, encrypt: false);
            return Utf8.GetString(bytes);
        }
        catch (Exception ex) when (ex is FormatException or DecoderFallbackException)
        {
            throw new InvalidOperationException("Не удалось прочитать адрес. Проверьте ключ и целостность копии.", ex);
        }
    }

    private void ApplyKeyword(byte[] bytes, bool encrypt)
    {
        for (int i = 0; i < bytes.Length; i++)
        {
            int step = keyword[i % keyword.Length];
            bytes[i] = (byte)((bytes[i] + (encrypt ? step : 256 - step)) % 256);
        }
    }

    public static bool IsEncodedPhone(string? value) =>
        value?.StartsWith(PhoneTag, StringComparison.Ordinal) == true;

    public static bool IsEncodedEmail(string? value) =>
        value?.StartsWith(EmailTag, StringComparison.Ordinal) == true && !value.Contains('@');
}
