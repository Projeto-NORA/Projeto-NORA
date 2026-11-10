using System.Net;
using System.Net.Mail;

namespace Nora.Api.Services;

public interface IEmailSender
{
    bool IsConfigured { get; }
    Task SendAsync(string address, string subject, string body, CancellationToken ct);
}

public sealed class SmtpEmailSender(IConfiguration configuration) : IEmailSender
{
    private string? Host => configuration["Nora:Smtp:Host"];
    private string? Username => configuration["Nora:Smtp:Username"];
    private string? Password => configuration["Nora:Smtp:Password"];
    private string? FromAddress => configuration["Nora:Smtp:FromAddress"];
    private string FromName => configuration["Nora:Smtp:FromName"] ?? "NORA Saúde";
    private int Port => int.TryParse(configuration["Nora:Smtp:Port"], out var port) ? port : 587;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) &&
        !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password) &&
        !string.IsNullOrWhiteSpace(FromAddress) && Port is > 0 and <= 65535;

    public async Task SendAsync(string address, string subject, string body, CancellationToken ct)
    {
        if (!IsConfigured) throw new EmailDeliveryUnavailableException();
        using var message = new MailMessage(new MailAddress(FromAddress!, FromName), new MailAddress(address))
        {
            Subject = subject,
            Body = body,
            IsBodyHtml = false,
        };
        using var client = new SmtpClient(Host!, Port)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(Username, Password),
            DeliveryMethod = SmtpDeliveryMethod.Network,
        };
        await client.SendMailAsync(message, ct);
    }
}

public sealed class EmailDeliveryUnavailableException : Exception;
