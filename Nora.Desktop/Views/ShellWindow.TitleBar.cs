using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using System.Windows.Threading;
using Nora.Desktop.Services;

namespace Nora.Desktop.Views;

/// <summary>Barra de título própria (WindowChrome), recolher e reabrir a barra lateral e o ajuste da janela maximizada.</summary>
public partial class ShellWindow
{
    private const double TitleBarHeight = 35;
    private const double ResizeBorder = 6;
    private const double SidebarMinWidth = 208;
    private const int WmGetMinMaxInfo = 0x0024;
    private const int WmSize = 0x0005;
    private const int WmWindowPosChanged = 0x0047;
    private const int WmEnterSizeMove = 0x0231;
    private const int WmExitSizeMove = 0x0232;
    private int sidebarMotion;
    private BindingBase? sidebarWidthBinding;
    private bool shapeScheduled;

    private void InitializeTitleBar()
    {
        StateChanged += (_, _) => { UpdateWindowButtons(); RefreshWindowShape(); };
        UpdateWindowButtons();
        UpdateChrome();
        ApplySidebarState(animate: false);
    }

    // ---- Botões ----
    private void OnTitleSearch(object sender, RoutedEventArgs args) => OpenScreenSearch();
    private void OnMinimizeWindow(object sender, RoutedEventArgs args) => WindowState = WindowState.Minimized;
    private void OnMaximizeRestoreWindow(object sender, RoutedEventArgs args) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    /// <summary>Fechar passa pelo mesmo caminho do Alt + F4: a confirmação de fechamento continua valendo.</summary>
    private void OnCloseWindow(object sender, RoutedEventArgs args) => Close();

    private void UpdateWindowButtons()
    {
        var maximized = WindowState == WindowState.Maximized;
        MaximizeIcon.Visibility = maximized ? Visibility.Collapsed : Visibility.Visible;
        RestoreIcon.Visibility = maximized ? Visibility.Visible : Visibility.Collapsed;
        MaximizeButton.ToolTip = maximized ? "Restaurar" : "Maximizar";
        AutomationProperties.SetName(MaximizeButton, maximized ? "Restaurar janela" : "Maximizar janela");
    }

    /// <summary>Em tela cheia a barra de título some: a faixa de arrasto e as bordas de redimensionar saem junto, senão engoliriam os cliques do topo.</summary>
    private void UpdateChrome()
    {
        var chrome = WindowChrome.GetWindowChrome(this);
        var fullScreen = ViewModel.FullScreen;
        chrome.CaptionHeight = fullScreen ? 0 : TitleBarHeight;
        chrome.ResizeBorderThickness = new Thickness(fullScreen ? 0 : ResizeBorder);
    }

    // ---- Barra lateral recolhível ----
    /// <summary>
    /// Aplica o estado aberto/recolhido da barra lateral. Com animação, a coluna encolhe ou cresce em 200 ms enquanto o conteúdo da barra
    /// mantém a largura final (a barra revela ou recorta os itens, sem espremê-los). Durante o movimento a coluna não está ligada ao
    /// ViewModel: um valor intermediário nunca vira "largura escolhida" nem é gravado nas preferências.
    /// </summary>
    private void ApplySidebarState(bool animate)
    {
        var open = ViewModel.SidebarExpanded;
        // O menu da conta está ancorado na barra: não pode ficar flutuando depois que ela some.
        if (!open && AccountButton.ContextMenu is { IsOpen: true } accountMenu) accountMenu.IsOpen = false;
        var motion = ++sidebarMotion;
        var from = SidebarColumn.ActualWidth;
        var target = open ? ViewModel.SidebarWidth : 0;
        sidebarWidthBinding ??= BindingOperations.GetBindingBase(SidebarColumn, ColumnDefinition.WidthProperty);
        void Settle()
        {
            if (motion != sidebarMotion) return;
            SidebarColumn.BeginAnimation(ColumnDefinition.WidthProperty, null);
            if (sidebarWidthBinding is not null && !BindingOperations.IsDataBound(SidebarColumn, ColumnDefinition.WidthProperty)) BindingOperations.SetBinding(SidebarColumn, ColumnDefinition.WidthProperty, sidebarWidthBinding);
            SidebarColumn.MinWidth = open ? SidebarMinWidth : 0;
            SidebarContent.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            Sidebar.IsHitTestVisible = true;
        }
        if (!animate || !IsLoaded || !ViewModel.Animations || Math.Abs(from - target) < 1) { Settle(); return; }
        // Reabrindo, o conteúdo aparece já na largura final; recolhendo, ele só some depois que a coluna fechou.
        SidebarContent.Visibility = Visibility.Visible;
        Sidebar.IsHitTestVisible = false;
        SidebarColumn.MinWidth = 0;
        BindingOperations.ClearBinding(SidebarColumn, ColumnDefinition.WidthProperty);
        SidebarColumn.Width = new GridLength(from, GridUnitType.Pixel);
        var animation = new GridLengthAnimation { From = from, To = target, Duration = TimeSpan.FromMilliseconds(200), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        animation.Completed += (_, _) => Settle();
        SidebarColumn.BeginAnimation(ColumnDefinition.WidthProperty, animation);
    }

    // ---- Janela: maximizar sem vazar do monitor e sem cobrir a barra de tarefas ----
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (PresentationSource.FromVisual(this) is HwndSource source) source.AddHook(WindowProc);
        ApplyWindowCorners();
        RefreshWindowShape();
    }

    private IntPtr WindowProc(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (message)
        {
            case WmGetMinMaxInfo when FitMaximizedWindow(window, lParam):
                handled = true;
                break;
            case WmSize or WmWindowPosChanged:
                // O WindowChrome refaz a região da janela depois deste ponto: a limpeza espera a mensagem terminar.
                ScheduleWindowShape();
                break;
            case WmEnterSizeMove or WmExitSizeMove:
                // O Windows descarta a preferência de cantos arredondados no começo e no fim do arrasto.
                ApplyWindowCorners();
                ScheduleWindowShape();
                break;
        }
        return IntPtr.Zero;
    }

    /// <summary>
    /// Maximizada, a janela ocupa exatamente a área de trabalho do monitor (sem a barra de tarefas): sem esta resposta o Windows a estenderia
    /// além das bordas, escondendo a parte direita da barra de título. Também informa o tamanho mínimo, já na escala da tela.
    /// </summary>
    private bool FitMaximizedWindow(IntPtr window, IntPtr minMaxInfo)
    {
        if (!MonitorArea.TryGet(window, out var monitor, out var work)) return false;
        var info = Marshal.PtrToStructure<MinMaxInfo>(minMaxInfo);
        // A posição é relativa ao canto do monitor; o tamanho é o da área de trabalho.
        info.MaxPosition = new NativePoint { X = work.Left - monitor.Left, Y = work.Top - monitor.Top };
        info.MaxSize = new NativePoint { X = work.Width, Y = work.Height };
        var scale = VisualTreeHelper.GetDpi(this);
        info.MinTrackSize = new NativePoint { X = (int)Math.Ceiling(MinWidth * scale.DpiScaleX), Y = (int)Math.Ceiling(MinHeight * scale.DpiScaleY) };
        Marshal.StructureToPtr(info, minMaxInfo, false);
        return true;
    }

    private void ScheduleWindowShape()
    {
        if (shapeScheduled) return;
        shapeScheduled = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() => { shapeScheduled = false; RefreshWindowShape(); }));
    }

    /// <summary>
    /// Tira a região retangular que o WindowChrome aplica à janela restaurada: com ela o Windows 11 não arredonda os cantos. A região só
    /// é removida se existir (a limpeza não pode provocar novas mensagens de tamanho em laço) e nunca com a janela maximizada: ali a região
    /// do WindowChrome coincide com a área de trabalho e removê-la faz o Windows refazer a maximização com a moldura para fora do monitor.
    /// </summary>
    private void RefreshWindowShape()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || WindowState != WindowState.Normal) return;
        if (GetWindowRgnBox(handle, out _) != 0) SetWindowRgn(handle, IntPtr.Zero, true);
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRegionRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo { public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
    [DllImport("user32.dll")] private static extern int GetWindowRgnBox(IntPtr window, out NativeRegionRect box);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr window, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
}
