using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Nora.Api.DAOs;
using Nora.Api.Models;

namespace Nora.Api.Services;

public sealed class AccountService(AccountDao accounts, IPasswordHasher<Account> passwords, TimeProvider time)
{
    public Task<List<PatientInfo>> PatientsAsync(CancellationToken ct) => accounts.PatientsAsync(time.GetUtcNow(), ct);
    public Task<PatientInfo> CreatePatientAsync(CreatePatientRequest request, CancellationToken ct) => ProvisionAsync("patient", "patient", request, ct);

    /// <summary>
    /// Cria uma conta. Paciente exige CPF válido. Contas de equipe (<c>obs</c> e <c>municipal</c>) não têm CPF (P-053): entram só por e-mail e senha, então o CPF de <paramref name="request"/> é ignorado e nada dele é gravado.
    /// </summary>
    public Task<PatientInfo> ProvisionAsync(string scope, string role, CreatePatientRequest request, CancellationToken ct = default)
    {
        if (!((scope == "obs" && role is "administrator" or "attendant") || (scope == "municipal" && role == "manager") || (scope == "patient" && role == "patient"))) throw new ApiInputException();
        var staff = scope != "patient";
        var fullName = request.FullName?.Trim() ?? "";
        var cpf = staff ? "" : NormalizeCpf(request.Cpf);
        var email = request.Email?.Trim().ToLowerInvariant() ?? "";
        if (fullName.Length is < 2 or > 160 || (!staff && !ValidCpf(cpf)) || email.Length > 254 || !MailAddress.TryCreate(email, out var address) || address.Address != email || request.Password is null || request.Password.Length is < 8 or > 256)
            throw new ApiInputException();
        var normalized = new CreatePatientRequest(fullName, cpf, email, request.Password, NormalizePhone(request.Phone), NormalizeAddress(request.Address));
        var account = new Account(new(Guid.Empty, fullName, role, scope), cpf, email, "");
        return accounts.CreateAsync(scope, role, normalized, passwords.HashPassword(account, request.Password), ct);
    }

    /// <summary>Conta de equipe sem CPF (o cadastro inicial pelo terminal e as contas de teste): nome, e-mail e senha.</summary>
    public Task<PatientInfo> ProvisionStaffAsync(string scope, string role, string fullName, string email, string password, CancellationToken ct = default) =>
        scope == "patient" ? throw new ApiInputException() : ProvisionAsync(scope, role, new(fullName, "", email, password), ct);

    internal static string? NormalizePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (value.Length > 50 || value.Any(c => !char.IsAsciiDigit(c) && c is not ('+' or '(' or ')' or ' ' or '-' or '.')) || value.Count(c => c == '+') > 1 || (value.Contains('+') && value[0] != '+'))
            throw new ApiInputException();
        var digits = new string(value.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length is < 8 or > 15) throw new ApiInputException();
        return value[0] == '+' ? "+" + digits : digits;
    }

    public static string NormalizeCep(string? cep)
    {
        var value = cep ?? "";
        return value.Any(c => !char.IsAsciiDigit(c) && c is not ('-' or ' ')) ? "" : new(value.Where(char.IsAsciiDigit).ToArray());
    }

    internal static AddressInfo? NormalizeAddress(AddressInfo? address)
    {
        if (address is null) return null;
        var cep = NormalizeCep(address.Cep);
        var street = address.Street?.Trim() ?? "";
        var number = NormalizeAddressPart(address.Number, 20);
        var complement = NormalizeAddressPart(address.Complement, 160);
        var neighborhood = NormalizeAddressPart(address.Neighborhood, 120);
        var city = address.City?.Trim() ?? "";
        var state = address.State?.Trim().ToUpperInvariant() ?? "";
        var ibge = NormalizeAddressPart(address.Ibge, 10);
        if (cep.Length != 8 || street.Length is < 1 or > 160 || city.Length is < 2 or > 120 || state.Length != 2 || !state.All(char.IsAsciiLetter))
            throw new ApiInputException();
        return new(cep, street, number, complement, neighborhood, city, state, ibge);
    }

    private static string? NormalizeAddressPart(string? value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (value.Length > maximum || value.Any(char.IsControl)) throw new ApiInputException();
        return value;
    }

    public static string NormalizeCpf(string? cpf)
    {
        var value=cpf??"";
        return value.Any(c=>!char.IsAsciiDigit(c) && c is not ('.' or '-' or ' ')) ? "" : new(value.Where(char.IsAsciiDigit).ToArray());
    }
    public static string NormalizeIdentifier(string? identifier)
    {
        var value = identifier?.Trim() ?? "";
        return value.Contains('@') ? value.ToLowerInvariant() : NormalizeCpf(value);
    }
    public static bool ValidCpf(string cpf)
    {
        if (cpf.Length != 11 || cpf.Any(c => !char.IsAsciiDigit(c)) || cpf.Distinct().Count() == 1) return false;
        for (var count = 9; count <= 10; count++)
        {
            var sum = 0;
            for (var i = 0; i < count; i++) sum += (cpf[i] - '0') * (count + 1 - i);
            var digit = sum * 10 % 11;
            if (digit == 10) digit = 0;
            if (cpf[count] - '0' != digit) return false;
        }
        return true;
    }
}
public sealed class ApiInputException : Exception;
public sealed class ApiConflictException : Exception;
public sealed class ApiNotFoundException : Exception;
public sealed class ApiUnauthorizedException : Exception;
public sealed class ApiForbiddenException : Exception;
public sealed class ApiRateLimitException : Exception;
/// <summary>Erro com status e código próprios no problem+json (ex.: invalid-current-password).</summary>
public sealed class ApiCodedException(int status, string code) : Exception
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
