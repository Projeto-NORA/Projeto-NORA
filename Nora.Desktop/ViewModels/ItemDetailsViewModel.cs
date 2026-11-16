using System.Runtime.InteropServices;
using System.Windows.Input;
using Nora.Desktop.Generics;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.ViewModels;

public sealed class ItemDetailsViewModel : ObservableObject
{
    private readonly IClipboardService clipboard;
    private string copyStatus = "";
    public ItemDetails Details { get; }
    public string CopyStatus { get => copyStatus; private set { copyStatus = value; Notify(); } }
    public ICommand CopyCommand { get; }

    private bool healthTab;
    /// <summary>Aba "Exames e vacinas" da ficha de um paciente da UBS (nula nas outras fichas).</summary>
    public HealthRecordsViewModel? Health { get; }
    public bool HasHealth => Health is not null;
    public bool ShowDataTab => !healthTab;
    public bool ShowHealthTab => healthTab;
    public ICommand SelectDataCommand { get; }
    public ICommand SelectHealthCommand { get; }

    public ItemDetailsViewModel(ItemDetails details, IClipboardService? clipboard = null, HealthRecordsViewModel? health = null)
    {
        Details = details; Health = health;
        SelectDataCommand = new RelayCommand(_ => SelectTab(false));
        SelectHealthCommand = new RelayCommand(_ => SelectTab(true), _ => HasHealth);
        this.clipboard = clipboard ?? new WindowsClipboardService();
        CopyCommand = new RelayCommand(parameter => { if (parameter is DetailField field) Copy(field); }, parameter => parameter is DetailField field && field.CanCopy && Details.Fields.Contains(field) && !string.IsNullOrWhiteSpace(field.Value));
    }

    /// <summary>A Jornada é lida na primeira vez que a aba abre (uma requisição); depois, só no Atualizar, no "Mostrar anulados" e a cada ação.</summary>
    private void SelectTab(bool health)
    {
        if (health && Health is null) return;
        healthTab = health; Notify(nameof(ShowDataTab)); Notify(nameof(ShowHealthTab));
        if (health) _ = Health!.EnsureLoadedAsync();
    }

    private void Copy(DetailField field)
    {
        if (!CopyCommand.CanExecute(field)) return;
        try { clipboard.SetText(field.Value); CopyStatus = $"{field.Label} copiado."; }
        catch (ExternalException) { CopyStatus = "Não foi possível copiar agora. Tente novamente."; }
    }
}
