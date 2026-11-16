using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Nora.Api.DAOs;
using Nora.Api.Models;

namespace Nora.Api.Services;

public sealed class StaffAccountService(StaffAccountDao dao, IPasswordHasher<Account> passwords)
{
    public Task<StaffAccount> CreateAsync(Guid actorId, CreateStaffRequest request, CancellationToken ct)
    {
        var fullName = request.FullName?.Trim() ?? "";
        var cpf = AccountService.NormalizeCpf(request.Cpf);
        var email = request.Email?.Trim().ToLowerInvariant() ?? "";
        if (request.Role is not ("attendant" or "administrator") || fullName.Length is < 2 or > 160 || fullName.Any(char.IsControl) || !AccountService.ValidCpf(cpf) ||
            email.Length > 254 || !MailAddress.TryCreate(email, out var address) || address.Address != email || request.Password is null || request.Password.Length is < 8 or > 256)
            throw new ApiInputException();
        var account = new Account(new(Guid.Empty, fullName, request.Role, "obs"), cpf, email, "");
        return dao.CreateAsync(actorId, request.Role, new(fullName, cpf, email), passwords.HashPassword(account, request.Password), ct);
    }

    public Task SetActiveAsync(Guid actorId, Guid id, SetStaffActiveRequest request, CancellationToken ct) =>
        id == Guid.Empty || !request.Confirmed ? throw new ApiInputException() : dao.SetActiveAsync(actorId, id, request.IsActive, ct);

    public Task ResetPasswordAsync(Guid actorId, Guid id, ResetStaffPasswordRequest request, CancellationToken ct)
    {
        if (id == Guid.Empty || !request.Confirmed || request.Password is null || request.Password.Length is < 8 or > 256) throw new ApiInputException();
        var account = new Account(new(id, "", "attendant", "obs"), "", "", "");
        return dao.ResetPasswordAsync(actorId, id, passwords.HashPassword(account, request.Password), ct);
    }
}
