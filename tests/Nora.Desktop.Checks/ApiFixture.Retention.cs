using System.Net;
using System.Net.Http;
using System.Text.Json;
using Nora.Desktop.Models;

namespace Nora.Desktop.Checks;

internal static partial class Program
{
    /// <summary>Exclusão programada da fixture: política (prazo automático), cadastros desativados e os agendamentos gravados, como o servidor guarda (docs/api.md).</summary>
    private sealed partial class ApiFixture
    {
        public int? RetentionDays { get; set; }
        public List<RetentionItem> RetentionItems { get; } = [];
        /// <summary>Cada gravação recebida: o prazo da política (<c>Item</c> nulo) ou o agendamento de um cadastro (<c>Days</c> nulo cancela).</summary>
        public List<(Guid? Item, int? Days)> RetentionWrites { get; } = [];
        public int RetentionReads { get; private set; }
        public bool FailRetentionReads { get; set; }

        private async Task<HttpResponseMessage?> HandleRetentionAsync(HttpRequestMessage request, string path, CancellationToken cancellationToken)
        {
            var at = path.IndexOf("/retention", StringComparison.Ordinal);
            if (at < 0) return null;
            var tail = path[(at + "/retention".Length)..].Trim('/');
            if (request.Method == HttpMethod.Get && tail.Length == 0)
            {
                RetentionReads++;
                return FailRetentionReads ? new(HttpStatusCode.InternalServerError) : Reply(new RetentionOverview(new(RetentionDays), RetentionItems.ToArray()));
            }
            if (request.Method != HttpMethod.Put) return null;
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            int? days = body.RootElement.TryGetProperty("days", out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;
            if (tail.Length == 0)
            {
                RetentionDays = days; RetentionWrites.Add((null, days));
                return Reply(new RetentionPolicy(days));
            }
            var index = Guid.TryParse(tail, out var id) ? RetentionItems.FindIndex(item => item.Id == id) : -1;
            if (index < 0) return Problem(HttpStatusCode.NotFound, "retention-item-unavailable");
            var current = RetentionItems[index];
            RetentionItems[index] = days is { } amount ? current with { ScheduledAt = DateTimeOffset.UtcNow.AddDays(amount), Cancelled = false } : current with { ScheduledAt = null, Cancelled = current.ScheduledAt is not null };
            RetentionWrites.Add((id, days));
            return new(HttpStatusCode.NoContent);
        }
    }
}
