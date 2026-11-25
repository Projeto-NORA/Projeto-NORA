using System.Globalization;
using System.Text;
using Nora.Desktop.Models;

namespace Nora.Desktop.Services;

/// <summary>Exporta somente o resumo agregado recebido, sem identificar pacientes.</summary>
public static class MunicipalCsv
{
    public static string Create(MunicipalSummary summary, DateTimeOffset receivedAt, int lightningPeriodDays = 0, string? unitName = null)
    {
        var cohort = lightningPeriodDays == 0 ? null : summary.LightningCohorts?.FirstOrDefault(c => c.Days == lightningPeriodDays);
        if (lightningPeriodDays != 0 && cohort == null) throw new InvalidOperationException("Período indisponível no resumo carregado.");
        if (cohort != null) summary = summary with { OpenSlots = cohort.OpenSlots, AcceptedOffers = cohort.AcceptedOffers, RegisteredSlots = cohort.RegisteredSlots, CreatedOffers = cohort.CreatedOffers, DeclinedOffers = cohort.DeclinedOffers, ExpiredOffers = cohort.ExpiredOffers, PendingOffers = cohort.PendingOffers, CancelledOffers = cohort.CancelledOffers };
        var csv = new StringBuilder("Indicador;Valor;Escopo\r\n");
        Add("Recebido em", receivedAt.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture), "Horário local da consulta");
        Add("Unidade", unitName ?? "Toda a rede", "Escopo do resumo");
        Add("Período das vagas", lightningPeriodDays == 0 ? "Todo o histórico" : $"Últimos {lightningPeriodDays} dias", "Vagas por data de criação, estados na consulta");
        Add("Pacientes cadastrados", summary.Patients.ToString(CultureInfo.InvariantCulture), "Pessoas com cadastro ativo em alguma UBS (CPF contado uma vez), total atual");
        Add("Pacientes desativados", Number(summary.InactivePatients), "Pessoas que só têm cadastro desativado, total atual");
        Add("UBS sem base de dados", Number(summary.UnitsWithoutDatabase), "UBS fora dos totais por não terem a base criada");
        Add("Vagas abertas ou ofertadas", summary.OpenSlots.ToString(CultureInfo.InvariantCulture), "Estado atual das vagas no período selecionado");
        Add("Vagas registradas", Number(summary.RegisteredSlots), "Vagas do período selecionado");
        Add("Ofertas criadas", Number(summary.CreatedOffers), "Ofertas das vagas do período, não mede envio de notificações");
        Add("Ofertas aceitas", summary.AcceptedOffers.ToString(CultureInfo.InvariantCulture), "Ofertas das vagas do período, não confirma atendimento");
        Add("Ofertas recusadas", Number(summary.DeclinedOffers), "Ofertas das vagas do período");
        Add("Ofertas expiradas", Number(summary.ExpiredOffers), "Ofertas das vagas do período");
        Add("Ofertas pendentes", Number(summary.PendingOffers), "Estado atual das ofertas das vagas do período");
        Add("Ofertas canceladas", Number(summary.CancelledOffers), "Ofertas das vagas do período");
        return csv.ToString();
        void Add(string name, string value, string scope) => csv.Append(name).Append(';').Append(value).Append(';').Append(scope).Append("\r\n");
    }
    private static string Number(long? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "Indisponível";
}
