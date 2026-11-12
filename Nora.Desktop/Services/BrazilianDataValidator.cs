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

    public static string FormatCpf(string? cpf)
    {
        cpf ??= "";
        return cpf.Length == 11 ? $"{cpf[..3]}.{cpf[3..6]}.{cpf[6..9]}-{cpf[9..]}" : cpf;
    }

    /// <summary>Formato de e-mail como a API confere: endereço simples, com domínio, até 254 caracteres.</summary>
    public static bool IsValidEmail(string? value)
    {
        var email = value?.Trim() ?? "";
        return email.Length is > 0 and <= 254 && MailAddress.TryCreate(email, out var parsed) && parsed.Address == email && parsed.Host.Contains('.');
    }

    /// <summary>Telefone: 8 a 15 dígitos, com + opcional no início; espaços, parênteses, hífen e ponto são ignorados.</summary>
    public static bool IsValidPhone(string? value)
    {
        var phone = value?.Trim() ?? "";
        if (phone.Length == 0) return false;
        var digits = 0;
        for (var index = 0; index < phone.Length; index++)
        {
            var c = phone[index];
            if (char.IsAsciiDigit(c)) digits++;
            else if (!(c == '+' && index == 0) && c is not (' ' or '(' or ')' or '-' or '.')) return false;
        }
        return digits is >= 8 and <= 15;
    }
}
