using System.Globalization;

namespace Nora.Api.Services;

/// <summary>
/// Computadores do OBS (P-014), de <c>Nora:ObsDevices:*</c>. <c>Enabled</c> (padrão falso, decisão de 24/11/2026) liga o recurso: desligado, o login do OBS vale sozinho, nenhum computador é
/// registrado nem exigido e a sessão de desktop pode ser mantida por 30 dias. Ligado, <c>RequireForAttendants</c> (padrão verdadeiro) exige computador ativado para a atendente entrar; com falso
/// (implantação gradual) a ativação e o "manter conectado" funcionam, mas a atendente também entra sem computador. <c>KeepSignedInDays</c> (padrão 30; 1 a 90) é a validade da sessão com
/// "manter conectado", sem renovação: vale para o OBS do contrato antigo (só com computador) e, desde 26/11/2026, para o paciente (sem a opção, a sessão do paciente vale 24 h). O desktop segue fixo em 24 h e 30 dias. Valor inválido impede a inicialização.
/// </summary>
public sealed class ObsDeviceOptions
{
    public bool Enabled { get; }
    public bool RequireForAttendants { get; }
    public int KeepSignedInDays { get; }

    public ObsDeviceOptions(IConfiguration configuration)
    {
        Enabled = configuration.GetValue("Nora:ObsDevices:Enabled", false);
        RequireForAttendants = Enabled && configuration.GetValue("Nora:ObsDevices:RequireForAttendants", true);
        var value = configuration["Nora:ObsDevices:KeepSignedInDays"];
        if (string.IsNullOrWhiteSpace(value)) KeepSignedInDays = 30;
        else if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var days) && days is >= 1 and <= 90) KeepSignedInDays = days;
        else throw new InvalidOperationException("Nora:ObsDevices:KeepSignedInDays inválido: informe um inteiro de 1 a 90.");
    }
}
