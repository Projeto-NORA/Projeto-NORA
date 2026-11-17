using System.Globalization;

namespace Nora.Api.Services;

/// <summary>
/// Computadores do OBS (P-014), de <c>Nora:ObsDevices:*</c>. <c>RequireForAttendants</c> (padrão verdadeiro) exige computador ativado para a atendente entrar; com falso (implantação gradual) a
/// ativação e o "manter conectado" funcionam, mas a atendente também entra sem computador. <c>KeepSignedInDays</c> (padrão 30; 1 a 90) é a validade da sessão com "manter conectado", sem renovação.
/// Valor inválido impede a inicialização.
/// </summary>
public sealed class ObsDeviceOptions
{
    public bool RequireForAttendants { get; }
    public int KeepSignedInDays { get; }

    public ObsDeviceOptions(IConfiguration configuration)
    {
        RequireForAttendants = configuration.GetValue("Nora:ObsDevices:RequireForAttendants", true);
        var value = configuration["Nora:ObsDevices:KeepSignedInDays"];
        if (string.IsNullOrWhiteSpace(value)) KeepSignedInDays = 30;
        else if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var days) && days is >= 1 and <= 90) KeepSignedInDays = days;
        else throw new InvalidOperationException("Nora:ObsDevices:KeepSignedInDays inválido: informe um inteiro de 1 a 90.");
    }
}
