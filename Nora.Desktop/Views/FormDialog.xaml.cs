using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Nora.Desktop.Services;

namespace Nora.Desktop.Views;

/// <summary>
/// Pedido do pop-up de cadastro. <paramref name="Content"/> é o formulário (as telas usam o <see cref="FormGrid"/>) e deve trazer o próprio DataContext. <paramref name="Save"/> é
/// a função assíncrona do botão primário: devolve verdadeiro quando salvou (o pop-up fecha) ou falso quando houve validação ou erro da API (o pop-up continua aberto, com
/// <paramref name="Error"/> lido na hora e mostrado dentro dele). <paramref name="Secondary"/> é um botão opcional à esquerda do primário que roda <paramref name="SecondaryAction"/>
/// sem fechar o pop-up.
/// </summary>
public sealed record FormDialogRequest(string Title, FrameworkElement Content, Func<Task<bool>> Save, string Primary = "Salvar", string? Secondary = null, Func<Task>? SecondaryAction = null, Func<string?>? Error = null, string BusyText = "Salvando…", double Width = 760);

/// <summary>Pop-up de cadastro da marca criado pelo <see cref="DialogService.ShowForm"/>; não é aberto diretamente pelas telas.</summary>
public partial class FormDialog : Window
{
    private const string DefaultError = "Não foi possível salvar. Confira os campos e tente de novo.";
    private bool busy;

    public FormDialogRequest Request { get; }
    /// <summary>O primário devolveu verdadeiro (o pop-up fechou salvando). Fechar pelo X ou pelo Esc deixa falso.</summary>
    public bool Saved { get; private set; }
    /// <summary>A função do botão primário ou do secundário está rodando: os botões ficam desabilitados e o pop-up não fecha.</summary>
    public bool IsBusy => busy;
    /// <summary>A mensagem de erro mostrada dentro do pop-up (vazia quando não há).</summary>
    public string ErrorMessage => ErrorText.Text;

    public FormDialog(FormDialogRequest request)
    {
        Request = request;
        InitializeComponent();
        Title = request.Title;
        Width = request.Width;
        TitleText.Text = request.Title;
        Body.Content = request.Content;
        PrimaryButton.Content = request.Primary;
        PrimaryButton.ToolTip = request.Primary;
        BusyText.Text = request.BusyText;
        if (request.Secondary is not null) { SecondaryButton.Content = request.Secondary; SecondaryButton.ToolTip = request.Secondary; SecondaryButton.Visibility = Visibility.Visible; }
        System.Windows.Automation.AutomationProperties.SetName(this, request.Title);
        SourceInitialized += (_, _) => MaxHeight = Math.Max(320, Math.Min(SystemParameters.WorkArea.Height, Owner is { IsLoaded: true } owner ? owner.ActualHeight : double.PositiveInfinity) - 40);
        Loaded += (_, _) =>
        {
            Frame.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            Zoom.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
            Zoom.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease });
            LinkEnterToPrimary();
            FocusFirstField();
        };
        Closing += (_, args) => { if (busy) args.Cancel = true; };
        // O formulário volta a ficar livre: a tela pode montar outro (ou reaproveitar este) na próxima vez.
        Closed += (_, _) => Body.Content = null;
    }

    /// <summary>Enter no último campo aciona o primário: o botão entra no mesmo grupo do <see cref="EnterNavigation"/> dos campos do formulário.</summary>
    private void LinkEnterToPrimary()
    {
        var group = Fields(Body).Select(EnterNavigation.GetGroup).FirstOrDefault(name => !string.IsNullOrEmpty(name));
        if (group is not null) EnterNavigation.SetGroup(PrimaryButton, group);
    }

    /// <summary>O foco abre no primeiro campo do formulário (ou no botão primário, se não houver campo).</summary>
    private void FocusFirstField()
    {
        if (!Body.MoveFocus(new TraversalRequest(FocusNavigationDirection.First))) PrimaryButton.Focus();
    }

    private static IEnumerable<FrameworkElement> Fields(DependencyObject node)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is FrameworkElement element && !string.IsNullOrEmpty(EnterNavigation.GetGroup(element))) yield return element;
            foreach (var inner in Fields(child)) yield return inner;
        }
    }

    /// <summary>Roda a função do botão primário (o mesmo que o clique): verdadeiro fecha o pop-up; falso o deixa aberto com a mensagem de erro.</summary>
    public async Task SubmitAsync()
    {
        if (busy) return;
        ShowError(null);
        SetBusy(true);
        bool saved;
        try { saved = await Request.Save(); }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            System.Diagnostics.Trace.TraceError("NORA: o cadastro falhou: {0}", failure);
            ShowError("Não foi possível concluir agora. Tente novamente em instantes.");
            SetBusy(false);
            return;
        }
        SetBusy(false);
        if (!saved) { ShowError(Request.Error?.Invoke() is { Length: > 0 } message ? message : DefaultError); return; }
        Saved = true;
        Close();
    }

    /// <summary>Roda a ação do botão secundário (ex.: "Sugerir pela localização"); o pop-up continua aberto.</summary>
    public async Task RunSecondaryAsync()
    {
        if (busy || Request.SecondaryAction is not { } action) return;
        ShowError(null);
        SetBusy(true);
        try { await action(); }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            System.Diagnostics.Trace.TraceError("NORA: a ação do pop-up falhou: {0}", failure);
            ShowError("Não foi possível concluir agora. Tente novamente em instantes.");
        }
        finally { SetBusy(false); }
    }

    /// <summary>Mostra (ou, com nulo, esconde) a mensagem de erro dentro do pop-up. A tela pode chamar durante o salvamento.</summary>
    public void ShowError(string? message)
    {
        ErrorText.Text = message ?? "";
        ErrorBox.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SetBusy(bool value)
    {
        busy = value;
        Progress.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        BusyText.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        PrimaryButton.IsEnabled = SecondaryButton.IsEnabled = CloseButton.IsEnabled = !value;
    }

    private async void OnPrimary(object sender, RoutedEventArgs e) => await SubmitAsync();
    private async void OnSecondary(object sender, RoutedEventArgs e) => await RunSecondaryAsync();
    private void OnClose(object sender, RoutedEventArgs e) => Close();
    private void OnDrag(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed && e.OriginalSource is not Button) DragMove(); }
}
