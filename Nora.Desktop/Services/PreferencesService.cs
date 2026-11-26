using System.IO;
using System.Text.Json;
using Nora.Desktop.Models;

namespace Nora.Desktop.Services;

public sealed class PreferencesService
{
    private readonly string applicationId;
    public string FilePath { get; }
    public string? Error { get; private set; }
    public PreferencesService(string applicationId, string? storageRoot = null)
    {
        if (applicationId is not ("obs" or "municipal")) throw new ArgumentException("Aplicativo desconhecido.", nameof(applicationId));
        this.applicationId = applicationId;
        FilePath = Path.Combine(storageRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NORA"), applicationId, "preferences.json");
    }
    public DesktopPreferences Load()
    {
        Error = null;
        try
        {
            if (!File.Exists(FilePath)) return new();
            var preferences = JsonSerializer.Deserialize<DesktopPreferences>(File.ReadAllText(FilePath)) ?? throw new JsonException("Arquivo vazio.");
            if (!Enum.IsDefined(preferences.Appearance)) preferences.Appearance = Appearance.System;
            var allowedPages = applicationId == "municipal" ? new[] { "home", "settings", "units", "specialties", "network", "indicators", "slots", "capacity", "alerts", "reports", "communication", "users" } : new[] { "home", "settings", "patients", "agenda", "slots", "indicators", "alerts", "unit-settings", "family", "announcements" };
            // A antiga Fila virou o painel automático da Vaga Relâmpago.
            if (preferences.LastPage == "queue" && applicationId != "municipal") preferences.LastPage = "slots";
            if (!allowedPages.Contains(preferences.LastPage)) preferences.LastPage = "home";
            // Modo cards/gráficos: só o que o programa entende (um valor desconhecido ou uma chave vazia volta ao padrão, cards).
            preferences.ViewModes = (preferences.ViewModes ?? new Dictionary<string, string>()).Where(mode => !string.IsNullOrWhiteSpace(mode.Key) && mode.Value == "charts").ToDictionary(mode => mode.Key, mode => mode.Value, StringComparer.Ordinal);
            return preferences;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Error = "Não foi possível carregar as preferências. Os padrões foram aplicados; o arquivo original foi preservado.";
            return new();
        }
    }
    public bool Save(DesktopPreferences preferences)
    {
        Error = null;
        var temporaryPath = FilePath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(preferences, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, FilePath, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = "A alteração vale nesta sessão, mas não foi possível salvar as preferências neste computador.";
            return false;
        }
    }
}
