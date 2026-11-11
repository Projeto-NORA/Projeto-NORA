using System.Net.Mail;

namespace Nora.Desktop.Services;

public static class BrazilianDataValidator
{
    public static string NormalizeCpf(string? value)
    {
        value ??= "";
        return value.Any(c => !char.IsAsciiDigit(c) && c is not ('.' or '-' or ' ')) ? "" : new(value.Where(char.IsAsciiDigit).ToArray());
    }

    public static bool IsValidCpf(string? value)
    {
        var cpf = NormalizeCpf(value);
        if (cpf.Length != 11 || cpf.Distinct().Count() == 1) return false;
        for (var count = 9; count <= 10; count++)
        {
            var sum = 0;
            for (var index = 0; index < count; index++) sum += (cpf[index] - '0') * (count + 1 - index);
            var digit = sum * 10 % 11;
            if (digit == 10) digit = 0;
            if (cpf[count] - '0' != digit) return false;
        }
        return true;
    }

    public static bool IsValidLoginIdentifier(string? value)
    {
        var identifier = value?.Trim() ?? "";
        if (identifier.Contains('@')) return MailAddress.TryCreate(identifier, out var email) && email.Address == identifier;
        return IsValidCpf(identifier);
    }

    public static string NormalizeCep(string? value)
    {
        value ??= "";
        return value.Any(c => !char.IsAsciiDigit(c) && c is not ('-' or ' ')) ? "" : new(value.Where(char.IsAsciiDigit).ToArray());
    }

    public static bool IsCompleteCep(string? value) => NormalizeCep(value).Length == 8;
}
