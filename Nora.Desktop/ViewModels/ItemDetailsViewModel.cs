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

    public ItemDetailsViewModel(ItemDetails details, IClipboardService? clipboard = null)
    {
        Details = details;
        this.clipboard = clipboard ?? new WindowsClipboardService();
        CopyCommand = new RelayCommand(parameter => { if (parameter is DetailField field) Copy(field); }, parameter => parameter is DetailField field && field.CanCopy && Details.Fields.Contains(field) && !string.IsNullOrWhiteSpace(field.Value));
    }

    private void Copy(DetailField field)
    {
        if (!CopyCommand.CanExecute(field)) return;
        try { clipboard.SetText(field.Value); CopyStatus = $"{field.Label} copiado."; }
        catch (ExternalException) { CopyStatus = "Não foi possível copiar agora. Tente novamente."; }
    }
}
