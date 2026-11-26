using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

/// <summary>
/// Componentes de listagem (OpenList, ItemTitleText, StatusTag, SelectionBalloon, ListAction*, ListingBehavior, PageLayout.Fill, NewItemButton, NoticeItem),
/// o hover azul bebê, os tons dos cartões e as chaves novas dos temas. Montam uma listagem de teste fora da tela, sem depender das telas.
/// </summary>
internal static partial class Program
{
    private sealed record ListingRow(Guid Id, string Name, string Cpf = "", string Meta = "", string Status = "", Tone Tone = Tone.Neutral);

    private const string ListingSampleXaml = """
        <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
              xmlns:views="clr-namespace:Nora.Desktop.Views;assembly=Nora.Desktop" Width="760" Height="440" Background="{DynamicResource BackgroundBrush}"
              TextElement.Foreground="{DynamicResource TextBrush}" TextElement.FontSize="14" TextElement.FontFamily="Segoe UI">
          <Grid.RowDefinitions><RowDefinition Height="Auto" /><RowDefinition Height="*" /></Grid.RowDefinitions>
          <TextBox x:Name="SearchBox" Width="240" HorizontalAlignment="Left" Margin="0,0,0,8" />
          <Grid x:Name="ListCell" Grid.Row="1">
            <ListBox x:Name="Rows" Style="{DynamicResource OpenList}" views:ListingBehavior.Balloon="{Binding ElementName=Balloon}"
                     views:ListingBehavior.LabelPath="Name" views:ListingBehavior.KeyPath="Id" views:ListingBehavior.PluralName="pacientes" views:ListingBehavior.ComplementPath="Cpf">
              <ListBox.ItemTemplate>
                <DataTemplate>
                  <Grid>
                    <Grid.ColumnDefinitions><ColumnDefinition Width="*" /><ColumnDefinition Width="Auto" /></Grid.ColumnDefinitions>
                    <StackPanel>
                      <TextBlock Text="{Binding Name}" Style="{DynamicResource ItemTitleText}" />
                      <TextBlock Text="{Binding Meta}" Style="{DynamicResource ItemMetaText}" />
                    </StackPanel>
                    <views:StatusTag Grid.Column="1" Text="{Binding Status}" Tone="{Binding Tone}" Margin="12,0,0,0" />
                  </Grid>
                </DataTemplate>
              </ListBox.ItemTemplate>
            </ListBox>
            <views:SelectionBalloon x:Name="Balloon">
              <StackPanel Orientation="Horizontal">
                <Button x:Name="ViewButton" Style="{DynamicResource ListActionButton}" views:ListAction.Icon="{DynamicResource IconEyeGeometry}" views:SelectionBalloon.SingleItemOnly="True" Content="Ver ficha" />
                <Button x:Name="EditButton" Style="{DynamicResource ListActionEditButton}" views:SelectionBalloon.SingleItemOnly="True" Content="Editar ficha" />
                <Button x:Name="RemoveButton" Style="{DynamicResource ListActionDangerButton}" Content="Desativar" />
              </StackPanel>
            </views:SelectionBalloon>
          </Grid>
        </Grid>
        """;

    /// <summary>Uma listagem de teste (lista + balão + comportamento) montada em XAML numa janela fora da tela.</summary>
    private sealed class ListingSample : IDisposable
    {
        public Window Window { get; }
        public Grid Root { get; }
        public ListBox List { get; }
        public SelectionBalloon Balloon { get; }
        public TextBox SearchBox { get; }
        public Button RemoveButton { get; }
        public Button ViewButton { get; }
        public ObservableCollection<ListingRow> Source { get; } = [];
        public List<ListingRow> Opened { get; } = [];
        public List<TaskCompletionSource> Pending { get; } = [];
        public int BalloonVisibilityChanges { get; set; }
        public int Refreshes { get; set; }

        public ListingSample(int rows = 8, bool holdOpening = false)
        {
            Root = (Grid)XamlReader.Parse(ListingSampleXaml);
            List = (ListBox)Root.FindName("Rows");
            Balloon = (SelectionBalloon)Root.FindName("Balloon");
            SearchBox = (TextBox)Root.FindName("SearchBox");
            RemoveButton = (Button)Root.FindName("RemoveButton");
            ViewButton = (Button)Root.FindName("ViewButton");
            for (var index = 0; index < rows; index++)
                Source.Add(new ListingRow(Guid.Parse($"00000000-0000-0000-0000-{index + 1:D12}"), $"Paciente {index + 1}", $"CPF {index + 1}", $"{index + 1}0 anos | paciente{index + 1}@nora.test", index % 2 == 0 ? "Ativo" : "Desativado", index % 2 == 0 ? Tone.Green : Tone.Pink));
            ListingBehavior.SetOpenHandler(List, item =>
            {
                Opened.Add((ListingRow)item);
                if (!holdOpening) return Task.CompletedTask;
                var gate = new TaskCompletionSource();
                Pending.Add(gate);
                return gate.Task;
            });
            List.ItemsSource = Source;
            ListingBehavior.AddBalloonRefreshedHandler(List, (_, _) => Refreshes++);
            Balloon.IsVisibleChanged += (_, _) => BalloonVisibilityChanges++;
            Window = new Window { Content = Root, SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
            Window.Show();
            Settle();
        }

        public void Settle()
        {
            Window.UpdateLayout();
            Window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Window.UpdateLayout();
        }

        public ListBoxItem Row(int index) => (ListBoxItem)List.ItemContainerGenerator.ContainerFromIndex(index);
        public string[] SelectedNames() => ListingBehavior.GetSelectedItems<ListingRow>(List).Select(row => row.Name).ToArray();
        public void SelectOnly(params int[] indexes) { List.SelectedItems.Clear(); foreach (var index in indexes) List.SelectedItems.Add(Source[index]); Settle(); }
        public FrameworkElement Template(string name) => (FrameworkElement)Balloon.Template.FindName(name, Balloon);
        public Button CloseButton => (Button)Template("PART_Close");
        public void Dispose() { Window.Close(); }
    }

    private static MouseButtonEventArgs MouseArgs(RoutedEvent routedEvent, UIElement source, int clicks = 1)
    {
        var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = routedEvent, Source = source };
        typeof(MouseButtonEventArgs).GetProperty(nameof(MouseButtonEventArgs.ClickCount))!.GetSetMethod(true)!.Invoke(args, [clicks]);
        return args;
    }

    /// <summary>O clique inteiro (botão para baixo e para cima) numa linha, pelos mesmos eventos que o Windows entrega.</summary>
    private static void ClickRow(ListingSample sample, int index, int clicks = 1)
    {
        var row = sample.Row(index);
        row.RaiseEvent(MouseArgs(Mouse.PreviewMouseDownEvent, row, clicks));
        row.RaiseEvent(MouseArgs(Mouse.MouseDownEvent, row, clicks));
        row.RaiseEvent(MouseArgs(Mouse.PreviewMouseUpEvent, row, clicks));
        row.RaiseEvent(MouseArgs(Mouse.MouseUpEvent, row, clicks));
    }

    private static void ClickOutside(UIElement target) => target.RaiseEvent(MouseArgs(Mouse.PreviewMouseDownEvent, target));

    private static bool PressKey(UIElement element, Key key)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(element)!, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        element.RaiseEvent(args);
        return args.Handled;
    }

    private static SolidColorBrush ThemeBrush(Application application, string key) => (SolidColorBrush)application.FindResource(key);

    private static readonly string[] ListingBrushKeys =
    [
        "ButtonHoverBrush", "HoverBrush",
        "ListItemBrush", "ListItemAlternateBrush", "ListItemHoverBrush", "ListItemSelectedBrush", "ListItemSelectedBorderBrush", "ListItemFocusBrush",
        "SelectionBalloonBrush", "SelectionBalloonBorderBrush",
        "ListActionBrush", "ListActionBorderBrush", "ListActionHoverBrush", "ListActionEditBrush", "ListActionEditHoverBrush", "ListActionDangerBrush", "ListActionDangerHoverBrush",
        "ToneBlueSurfaceBrush", "ToneBlueTextBrush", "ToneGreenSurfaceBrush", "ToneGreenTextBrush", "ToneYellowSurfaceBrush", "ToneYellowTextBrush",
        "TonePinkSurfaceBrush", "TonePinkTextBrush", "ToneVioletSurfaceBrush", "ToneVioletTextBrush", "ToneTealSurfaceBrush", "ToneTealTextBrush",
        "ToneNeutralSurfaceBrush", "ToneNeutralTextBrush",
        "StatusScheduledBrush", "StatusCalledBrush", "StatusCompletedBrush", "StatusMissedBrush", "StatusCancelledBrush"
    ];

    private static readonly string[] ListingStyleKeys =
    [
        "OpenList", "OpenListLow", "OpenListItem", "OpenListItemLow", "ItemTitleText", "ItemMetaText", "ListActionButton", "ListActionEditButton", "ListActionDangerButton",
        "SelectionBalloonCloseButton", "NewItemButton", "NoticeItem"
    ];

    private static readonly string[] ListingIconKeys =
    [
        "IconTrashGeometry", "IconEditGeometry", "IconRestoreGeometry", "IconEyeGeometry", "IconCheckGeometry", "IconCheckAllGeometry", "IconCloseGeometry",
        "IconMailGeometry", "IconPhoneGeometry", "IconBellGeometry", "IconWarningGeometry", "IconPlusGeometry"
    ];

    private static readonly string[] ToneNames = ["Blue", "Green", "Yellow", "Pink", "Violet", "Teal", "Neutral"];

    private static void RunListingComponentsChecks(Application application, ThemeService themes, string storageRoot, string[] args)
    {
        Check("Listagem aberta: chaves de tema novas existem nos dois temas e os estilos e ícones novos resolvem", () =>
        {
            foreach (var mode in new[] { Appearance.Light, Appearance.Dark })
            {
                themes.Apply(mode);
                foreach (var key in ListingBrushKeys) Require(application.TryFindResource(key) is SolidColorBrush, $"{mode}: a chave de cor {key} não resolve.");
                foreach (var key in ListingStyleKeys) Require(application.TryFindResource(key) is Style, $"{mode}: o estilo {key} não resolve.");
                foreach (var key in ListingIconKeys) Require(application.TryFindResource(key) is Geometry, $"{mode}: o ícone {key} não resolve.");
            }
            Require(application.TryFindResource(typeof(SelectionBalloon)) is Style && application.TryFindResource(typeof(StatusTag)) is Style, "SelectionBalloon e StatusTag não têm estilo implícito.");
            Require(application.TryFindResource(typeof(MetricCard)) is null && application.TryFindResource("MetricCardTemplate") is DataTemplate, "O modelo do cartão de indicador não resolve.");
            Require(application.TryFindResource("FloatingActions") is null, "O estilo sem uso FloatingActions deveria ter saído (o balão novo o substitui).");
            var light = new ResourceDictionary { Source = new Uri("/Nora.Desktop;component/Themes/LightTheme.xaml", UriKind.Relative) };
            var dark = new ResourceDictionary { Source = new Uri("/Nora.Desktop;component/Themes/DarkTheme.xaml", UriKind.Relative) };
            foreach (var key in ListingBrushKeys) Require(light.Contains(key) && dark.Contains(key), $"A chave {key} não está nos dois temas.");
        });

        Check("Hover azul bebê nos dois temas e contraste do texto sobre ele, sobre a listagem, o balão, os botões de ação e os tons", () =>
        {
            foreach (var mode in new[] { Appearance.Light, Appearance.Dark })
            {
                themes.Apply(mode);
                Color Of(string key) => ThemeBrush(application, key).Color;
                foreach (var key in new[] { "ButtonHoverBrush", "HoverBrush", "ListActionHoverBrush" })
                {
                    var color = Of(key);
                    Require(color.A == 255 && color.B > color.R && color.B >= color.G, $"{mode}: {key} ({color}) não é um azul.");
                }
                void Pair(string foreground, string background, double minimum = 4.5)
                {
                    var contrast = Contrast(Of(foreground), Of(background));
                    Require(contrast >= minimum, $"{mode}: {foreground} sobre {background} tem contraste {contrast:F2} (mínimo {minimum}).");
                }
                foreach (var background in new[] { "ButtonHoverBrush", "HoverBrush", "ListItemBrush", "ListItemAlternateBrush", "ListItemHoverBrush", "ListItemSelectedBrush", "SelectionBalloonBrush" })
                {
                    Pair("TextBrush", background);
                    if (background is not ("HoverBrush")) Pair("SecondaryBrush", background);
                }
                Pair("AccentTextBrush", "HoverBrush");
                Pair("BrandTextBrush", "ListActionEditBrush"); Pair("BrandTextBrush", "ListActionEditHoverBrush");
                Pair("BrandTextBrush", "ListActionDangerBrush"); Pair("BrandTextBrush", "ListActionDangerHoverBrush");
                Pair("TextBrush", "ListActionBrush"); Pair("TextBrush", "ListActionHoverBrush");
                foreach (var tone in ToneNames)
                {
                    Pair($"Tone{tone}TextBrush", $"Tone{tone}SurfaceBrush");
                    Pair("TextBrush", $"Tone{tone}SurfaceBrush");
                    Pair("SecondaryBrush", $"Tone{tone}SurfaceBrush");
                }
                Pair("TextBrush", "WarningSurfaceBrush");
            }
        });

        Check("Hover azul bebê: todo estilo de botão pinta o hover com ButtonHoverBrush ou HoverBrush nos dois temas (o primário mantém o degradê da marca e o estado próprio vence)", () =>
        {
            foreach (var mode in new[] { Appearance.Light, Appearance.Dark })
            {
                themes.Apply(mode);
                // (estilo, elemento do modelo que recebe o hover ("" = o próprio botão), chave do pincel esperado)
                var expected = new (object Key, string Part, string Brush)[]
                {
                    (typeof(Button), "", "ButtonHoverBrush"), ("IconRefreshButton", "Frame", "ButtonHoverBrush"), ("CircleIconButton", "Frame", "HoverBrush"), ("ChartButton", "Frame", "HoverBrush"),
                    ("NavItemButton", "Frame", "HoverBrush"), ("NavBackButton", "Frame", "HoverBrush"), ("TextActionButton", "Frame", "HoverBrush"), ("AvatarButton", "Ring", "HoverBrush"),
                    ("TitleBarToolButton", "Frame", "HoverBrush"), ("TitleBarWindowButton", "Frame", "HoverBrush"), ("PrimaryButton", "", "BrandGradientHoverBrush"), ("ObsPrimaryButton", "Frame", "BrandGradientHoverBrush"),
                    ("NewItemButton", "", "BrandGradientHoverBrush"), ("ListActionButton", "Frame", "ListActionHoverBrush"), ("ListActionEditButton", "Frame", "ListActionEditHoverBrush"), ("ListActionDangerButton", "Frame", "ListActionDangerHoverBrush")
                };
                var window = new Window { Width = 400, Height = 300, Left = -10000, Top = -10000, ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
                var panel = new StackPanel();
                window.Content = panel;
                try
                {
                    var buttons = expected.Select(item => new Button { Style = (Style)application.FindResource(item.Key), Content = "Botão" }).ToArray();
                    // Um estilo derivado com estado próprio (como o dia selecionado da agenda): o estado vence o hover, porque o gatilho dele vem depois.
                    var selected = new Button { Content = "Selecionado", Tag = "selecionado" };
                    var derived = new Style(typeof(Button), (Style)application.FindResource(typeof(Button)));
                    var selectedTrigger = new Trigger { Property = FrameworkElement.TagProperty, Value = "selecionado" };
                    selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, new DynamicResourceExtension("BrandSoftBrush")));
                    derived.Triggers.Add(selectedTrigger);
                    selected.Style = derived;
                    foreach (var button in buttons.Append(selected)) panel.Children.Add(button);
                    window.Show(); window.UpdateLayout();
                    var hover = typeof(UIElement).GetField("IsMouseOverPropertyKey", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as DependencyPropertyKey ?? throw new InvalidOperationException("Não achei o IsMouseOver do WPF.");
                    static IEnumerable<Color> Colors(Brush brush) => brush is GradientBrush gradient ? gradient.GradientStops.Select(stop => stop.Color) : [((SolidColorBrush)brush).Color];
                    for (var index = 0; index < expected.Length; index++)
                    {
                        var (key, part, brushKey) = expected[index];
                        var button = buttons[index];
                        button.SetValue(hover, true);
                        button.UpdateLayout();
                        var target = part.Length == 0 ? button.Background : ((Border)button.Template.FindName(part, button)).Background;
                        var want = (Brush)application.FindResource(brushKey);
                        Require(Colors(target).SequenceEqual(Colors(want)), $"{mode}: o hover de {(key is Type ? "Button" : key)} deveria usar {brushKey}.");
                        button.SetValue(hover, false);
                    }
                    selected.SetValue(hover, true);
                    selected.UpdateLayout();
                    Require(Colors(selected.Background).SequenceEqual(Colors((Brush)application.FindResource("BrandSoftBrush"))), $"{mode}: o estado próprio do estilo derivado deveria vencer o hover.");
                    var disabled = buttons[0];
                    disabled.IsEnabled = false; disabled.SetValue(hover, true); disabled.UpdateLayout();
                    Require(!Colors(disabled.Background).SequenceEqual(Colors((Brush)application.FindResource("ButtonHoverBrush"))), $"{mode}: botão desabilitado não deveria mostrar o hover.");
                }
                finally { window.Close(); }
            }
        });

        Check("OpenList: seleção Extended, rolagem por pixel, virtualização, sem borda nem fundo; o item tem canto 8, 12,8 e espaço de 4 (a variante baixa, 11,6 e 3)", () =>
        {
            themes.Apply(Appearance.Light);
            using var sample = new ListingSample();
            var list = sample.List;
            Require(list.SelectionMode == SelectionMode.Extended && VirtualizingPanel.GetIsVirtualizing(list) && VirtualizingPanel.GetScrollUnit(list) == ScrollUnit.Pixel, "A lista deveria ser Extended, virtualizada e rolar por pixel.");
            Require(list.BorderThickness == new Thickness(0) && (list.Background is null || list.Background is SolidColorBrush { Color.A: 0 }), "A lista aberta não pode ter borda nem fundo.");
            var row = sample.Row(0);
            Require(row.Padding == new Thickness(12, 8, 12, 8) && row.Margin == new Thickness(0, 0, 0, 4), $"Item fora das medidas: {row.Padding} / {row.Margin}.");
            Require(Descendants<Border>(row).Any(border => border.CornerRadius == new CornerRadius(8)), "O item não tem canto 8.");
            list.Style = (Style)application.FindResource("OpenListLow");
            sample.Settle();
            Require(sample.Row(0).Padding == new Thickness(11, 6, 11, 6) && sample.Row(0).Margin == new Thickness(0, 0, 0, 3), "A variante baixa deveria ter 11,6 e espaço de 3.");
            Require(Descendants<TextBlock>(sample.Row(0)).Any(text => text.Text == "Paciente 1" && text.FontSize == 14 && text.TextWrapping == TextWrapping.NoWrap), "O título do item não usa ItemTitleText.");
            Require(Descendants<TextBlock>(sample.Row(0)).Any(text => text.Text.StartsWith("10 anos") && text.FontSize == 12), "A linha de dados não usa ItemMetaText.");
        });

        Check("Listagem aberta: o balão fica oculto sem seleção; o clique seleciona, mostra o balão com o nome e o complemento e o balão flutua sem reservar espaço", () =>
        {
            themes.Apply(Appearance.Light);
            using var sample = new ListingSample();
            Require(sample.Balloon.Visibility != Visibility.Visible && !sample.Balloon.IsVisible, "O balão deveria começar oculto.");
            var listBefore = sample.List.ActualHeight;
            ClickRow(sample, 2);
            sample.Settle();
            Require(sample.SelectedNames().SequenceEqual(["Paciente 3"]), "O clique não selecionou a linha: " + string.Join(", ", sample.SelectedNames()));
            Require(sample.Balloon.IsVisible && sample.Balloon.Label == "Paciente 3" && sample.Balloon.Complement == "CPF 3", $"Balão: visível {sample.Balloon.IsVisible}, rótulo \"{sample.Balloon.Label}\", complemento \"{sample.Balloon.Complement}\".");
            Require(Math.Abs(sample.List.ActualHeight - listBefore) < 0.5 && sample.List.Padding == new Thickness(0), "O balão não pode reservar espaço na lista.");
            var cell = sample.Root.FindName("ListCell") as Grid;
            Require(ReferenceEquals(sample.Balloon.Parent, cell) && Grid.GetRow(sample.Balloon) == Grid.GetRow(sample.List) && Panel.GetZIndex(sample.Balloon) > Panel.GetZIndex(sample.List) && sample.Balloon.VerticalAlignment == VerticalAlignment.Bottom, "O balão deveria ser irmão da lista, na mesma célula, embaixo e na frente.");
            Require(sample.Balloon.Margin == new Thickness(0, 0, 0, 26) && sample.Balloon.HorizontalAlignment == HorizontalAlignment.Center, "Margem ou alinhamento do balão fora do padrão (26 da base, centralizado).");
            var frame = (Border)sample.Template("Balloon");
            Require(frame.CornerRadius == new CornerRadius(12) && frame.Padding == new Thickness(18, 10, 18, 10) && frame.Effect is System.Windows.Media.Effects.DropShadowEffect { BlurRadius: 20, ShadowDepth: 6, Opacity: > 0.21 and < 0.23 }, "O balão fora dos números da skill (canto 12, 18,10 e sombra 20/6/0,22).");
            var bottomGap = sample.List.ActualHeight - sample.Balloon.TranslatePoint(new Point(0, sample.Balloon.ActualHeight), sample.List).Y;
            Require(Math.Abs(bottomGap - 26) < 1.5, $"O balão deveria ficar a 26 px da base da lista (está a {bottomGap:F1}).");
            Require(sample.Refreshes >= 1, "O evento BalloonRefreshed não foi levantado.");
        });

        Check("Listagem aberta: Ctrl e Shift selecionam vários, o rótulo vira \"N pacientes selecionados\" e a ação de um item só some junto com o fio", () =>
        {
            themes.Apply(Appearance.Light);
            using var sample = new ListingSample();
            sample.SelectOnly(0);
            Require(sample.ViewButton.Visibility == Visibility.Visible && sample.Template("LeadingDivider").Visibility == Visibility.Visible, "Com um item, as ações de um item só deveriam aparecer.");
            // O Ctrl + clique e o Shift + clique são do WPF (SelectionMode Extended): aqui entram pelo mesmo caminho que eles, SelectedItems.
            sample.List.SelectedItems.Add(sample.Source[2]);
            sample.List.SelectedItems.Add(sample.Source[3]);
            sample.Settle();
            Require(sample.Balloon.Label == "3 pacientes selecionados" && sample.Balloon.Complement == "", $"Rótulo com vários: \"{sample.Balloon.Label}\".");
            Require(sample.ViewButton.Visibility == Visibility.Collapsed && sample.Row(0).IsSelected && sample.RemoveButton.Visibility == Visibility.Visible, "A ação de um item só deveria sumir e a de desativar ficar.");
            Require(sample.Balloon.HasActions && sample.Template("LeadingDivider").Visibility == Visibility.Visible, "Com o desativar visível o balão mantém o fio.");
            sample.RemoveButton.Visibility = Visibility.Collapsed;
            sample.Settle();
            Require(!sample.Balloon.HasActions && sample.Template("LeadingDivider").Visibility == Visibility.Collapsed && sample.Template("ActionsHost").Visibility == Visibility.Collapsed, $"Sem nenhuma ação, o fio e a área de ações deveriam sumir juntos (HasActions {sample.Balloon.HasActions}, fio {sample.Template("LeadingDivider").Visibility}, área {sample.Template("ActionsHost").Visibility}).");
            Require(sample.Template("TrailingDivider").Visibility == Visibility.Visible && sample.CloseButton.IsVisible, "O fio do X e o X ficam sempre.");
            sample.RemoveButton.Visibility = Visibility.Visible;
            sample.SelectOnly(1);
            Require(sample.Balloon.Label == "Paciente 2" && sample.ViewButton.Visibility == Visibility.Visible, "Voltar a um item deveria trazer de volta a ação de um item só.");
            ListingBehavior.SetFeminine(sample.List, true); ListingBehavior.SetPluralName(sample.List, "consultas");
            sample.SelectOnly(0, 1);
            Require(sample.Balloon.Label == "2 consultas selecionadas", $"Plural feminino: \"{sample.Balloon.Label}\".");
        });

        Check("Listagem aberta: Esc e o X do balão desfazem a seleção; Esc sem seleção não é engolido", () =>
        {
            themes.Apply(Appearance.Light);
            using var sample = new ListingSample();
            Require(!PressKey(sample.List, Key.Escape), "Esc sem seleção não deveria ser consumido.");
            sample.SelectOnly(1, 2);
            Require(sample.Balloon.IsVisible, "O balão deveria estar visível.");
            Require(PressKey(sample.List, Key.Escape), "Esc com seleção deveria ser tratado.");
            sample.Settle();
            Require(sample.List.SelectedItems.Count == 0 && !sample.Balloon.IsVisible, "Esc não desfez a seleção e escondeu o balão.");
            sample.SelectOnly(3);
            InvokeButton(sample.CloseButton);
            sample.Settle();
            Require(sample.List.SelectedItems.Count == 0 && !sample.Balloon.IsVisible, "O X não desfez a seleção.");
            Require(AutomationProperties.GetName(sample.CloseButton) == "Cancelar seleção", "O X precisa de nome acessível.");
            sample.SelectOnly(0);
            Require(PressKey(sample.Balloon, Key.Escape), "Esc com o foco no balão deveria desfazer a seleção.");
            sample.Settle();
            Require(sample.List.SelectedItems.Count == 0, "Esc no balão não desfez a seleção.");
            sample.SelectOnly(0, 2);
            ListingBehavior.ClearSelection(sample.List);
            sample.Settle();
            Require(sample.List.SelectedItems.Count == 0 && !sample.Balloon.IsVisible, "ListingBehavior.ClearSelection deveria desfazer a seleção e esconder o balão (é o que a tela chama depois de uma ação).");
        });

        Check("Listagem aberta: clicar no espaço passivo desfaz a seleção; clicar em linha, no balão, num botão ou num campo de texto a mantém", () =>
        {
            themes.Apply(Appearance.Light);
            using var sample = new ListingSample();
            sample.SelectOnly(1);
            foreach (var (target, name) in new (UIElement, string)[] { (sample.SearchBox, "campo de texto"), (sample.ViewButton, "botão do balão"), (sample.Balloon, "balão"), (sample.Row(4), "outra linha") })
            {
                ClickOutside(target);
                sample.Settle();
                Require(sample.List.SelectedItems.Count == 1, $"Um clique em {name} não deveria desfazer a seleção.");
            }
            ClickOutside(sample.Root);
            sample.Settle();
            Require(sample.List.SelectedItems.Count == 0 && !sample.Balloon.IsVisible, "Um clique no espaço passivo deveria desfazer a seleção.");
        });

        Check("Listagem aberta: o segundo clique numa linha já selecionada e o Enter abrem a ficha, uma por vez e na ordem da lista", () =>
        {
            themes.Apply(Appearance.Light);
            var before = ListingBehavior.DoubleClickInterval;
            ListingBehavior.DoubleClickInterval = () => TimeSpan.FromMilliseconds(5);
            try
            {
                using var sample = new ListingSample(holdOpening: true);
                // O primeiro clique numa linha ainda não selecionada só a seleciona.
                ClickRow(sample, 1);
                Thread.Sleep(30); sample.Settle();
                Require(sample.Opened.Count == 0 && sample.SelectedNames().SequenceEqual(["Paciente 2"]), "O primeiro clique só deveria selecionar.");
                // O segundo clique, sozinho, abre depois de esperar o intervalo do duplo clique.
                ClickRow(sample, 1);
                Thread.Sleep(30); sample.Settle();
                Require(sample.Opened.Select(row => row.Name).SequenceEqual(["Paciente 2"]), "O segundo clique deveria abrir a ficha: " + string.Join(", ", sample.Opened.Select(row => row.Name)));
                sample.Pending[0].SetResult(); sample.Settle();
                // O duplo clique de verdade (ClickCount 2) abre na hora.
                ClickRow(sample, 4); sample.Settle();
                Require(sample.Opened.Count == 1, "Um clique numa linha ainda não selecionada abriu a ficha.");
                ClickRow(sample, 4, clicks: 2); sample.Settle();
                Require(sample.Opened.Count == 2 && sample.Opened[1].Name == "Paciente 5", "O duplo clique deveria abrir a ficha na hora.");
                sample.Pending[1].SetResult(); sample.Settle();
                // Enter com vários: selecionados na ordem 4, 1, 3 (a do clique) abrem na ordem da lista, uma de cada vez.
                sample.SelectOnly(3, 0, 2);
                Require(PressKey(sample.List, Key.Enter), "Enter com seleção deveria ser tratado.");
                sample.Settle();
                Require(sample.Opened.Skip(2).Select(row => row.Name).SequenceEqual(["Paciente 1"]), "Só a primeira ficha deveria abrir enquanto ela está aberta: " + string.Join(", ", sample.Opened.Skip(2).Select(row => row.Name)));
                PressKey(sample.List, Key.Enter); sample.Settle();
                Require(sample.Opened.Count == 3, "Um segundo Enter com a fila em andamento abriu fichas a mais.");
                sample.Pending[2].SetResult(); sample.Settle();
                Require(sample.Opened.Skip(2).Select(row => row.Name).SequenceEqual(["Paciente 1", "Paciente 3"]), "A segunda ficha deveria abrir depois da primeira fechar: " + string.Join(", ", sample.Opened.Skip(2).Select(row => row.Name)));
                sample.Pending[3].SetResult(); sample.Settle();
                Require(sample.Opened.Skip(2).Select(row => row.Name).SequenceEqual(["Paciente 1", "Paciente 3", "Paciente 4"]), "A fila deveria seguir a ordem da lista (1, 3, 4): " + string.Join(", ", sample.Opened.Skip(2).Select(row => row.Name)));
                sample.Pending[4].SetResult(); sample.Settle();
                Require(sample.Opened.Count == 5 && sample.List.SelectedItems.Count == 3, "A seleção deveria continuar depois de abrir as fichas.");
                // O duplo clique numa linha de uma seleção múltipla devolve a seleção (o clique simples a reduziria a uma linha) e abre a fila inteira.
                using var multi = new ListingSample(holdOpening: true);
                multi.SelectOnly(0, 2, 3);
                ClickRow(multi, 2, clicks: 2); multi.Settle();
                Require(multi.Opened.Select(row => row.Name).SequenceEqual(["Paciente 1"]) && multi.Balloon.Label == "3 pacientes selecionados" && multi.SelectedNames().SequenceEqual(["Paciente 1", "Paciente 3", "Paciente 4"]), $"O duplo clique em uma seleção múltipla deveria manter a seleção e abrir a fila (abertas: {string.Join(", ", multi.Opened.Select(row => row.Name))}; seleção: {string.Join(", ", multi.SelectedNames())}).");
                multi.Pending[0].SetResult(); multi.Settle(); multi.Pending[1].SetResult(); multi.Settle(); multi.Pending[2].SetResult(); multi.Settle();
                Require(multi.Opened.Select(row => row.Name).SequenceEqual(["Paciente 1", "Paciente 3", "Paciente 4"]), "A fila da seleção múltipla deveria seguir a ordem da lista.");
            }
            finally { ListingBehavior.DoubleClickInterval = before; }
        });

        Check("Listagem aberta: trocar a fonte, recriar a coleção (ciclo de 20 s) ou filtrar mantém a seleção pelas chaves, sem o balão piscar nem sumir", () =>
        {
            themes.Apply(Appearance.Light);
            using var sample = new ListingSample();
            sample.SelectOnly(1, 3);
            var ids = ListingBehavior.GetSelectedItems<ListingRow>(sample.List).Select(row => row.Id).ToArray();
            sample.BalloonVisibilityChanges = 0;
            // Fonte trocada por objetos novos de mesmas chaves.
            sample.List.ItemsSource = new ObservableCollection<ListingRow>(sample.Source.Select(row => row with { Name = row.Name + " (novo)" }));
            sample.Settle();
            Require(ListingBehavior.GetSelectedItems<ListingRow>(sample.List).Select(row => row.Id).SequenceEqual(ids), "Trocar a fonte perdeu a seleção.");
            Require(sample.Balloon.IsVisible && sample.Balloon.Label == "2 pacientes selecionados" && sample.BalloonVisibilityChanges == 0, $"O balão piscou ao trocar a fonte (mudanças de visibilidade: {sample.BalloonVisibilityChanges}).");
            // O ciclo automático: a coleção da tela é esvaziada e preenchida de novo com objetos novos.
            sample.List.ItemsSource = sample.Source;
            sample.Settle();
            sample.BalloonVisibilityChanges = 0;
            var rebuilt = sample.Source.Select(row => row with { Meta = "atualizado" }).ToArray();
            sample.Source.Clear();
            foreach (var row in rebuilt) sample.Source.Add(row);
            sample.Settle();
            Require(ListingBehavior.GetSelectedItems<ListingRow>(sample.List).Select(row => row.Id).SequenceEqual(ids) && sample.List.SelectedItems.Count == 2, "O ciclo de recriação da coleção perdeu a seleção.");
            Require(sample.Balloon.IsVisible && sample.BalloonVisibilityChanges == 0, $"O balão piscou no ciclo automático (mudanças de visibilidade: {sample.BalloonVisibilityChanges}).");
            // Filtrar: o item que sai do filtro sai da seleção, e não volta quando o filtro é limpo.
            sample.List.Items.Filter = item => ((ListingRow)item).Id != ids[0];
            sample.Settle();
            Require(sample.List.SelectedItems.Count == 1 && sample.Balloon.IsVisible && sample.Balloon.Label == "Paciente 4", $"Filtrar deveria deixar só o Paciente 4 selecionado (rótulo \"{sample.Balloon.Label}\").");
            sample.List.Items.Filter = null;
            sample.Settle();
            Require(sample.SelectedNames().SequenceEqual(["Paciente 4"]), "O item que saiu do filtro não deveria voltar à seleção.");
            // Item removido da fonte sai da seleção; fonte vazia esconde o balão.
            sample.Source.Remove(sample.Source.Single(row => row.Id == ids[1]));
            sample.Settle();
            Require(sample.List.SelectedItems.Count == 0 && !sample.Balloon.IsVisible, "Sem o item selecionado na lista, o balão deveria sumir.");
            sample.SelectOnly(0);
            sample.Source.Clear();
            sample.Settle();
            Require(sample.List.SelectedItems.Count == 0 && !sample.Balloon.IsVisible, "Fonte vazia deveria esconder o balão.");
        });

        Check("Listagem aberta: o comportamento desliga ao sair da tela e liga uma vez só ao voltar (nenhuma assinatura repetida)", () =>
        {
            themes.Apply(Appearance.Light);
            using var sample = new ListingSample();
            sample.SelectOnly(0);
            var window = sample.Window;
            window.Content = null;
            sample.Settle();
            sample.Refreshes = 0;
            sample.List.SelectedItems.Add(sample.Source[1]);
            Require(sample.Refreshes == 0, "A lista fora da tela ainda atualiza o balão.");
            window.Content = sample.Root;
            sample.Settle();
            sample.Refreshes = 0;
            sample.List.SelectedItems.Add(sample.Source[2]);
            sample.Settle();
            Require(sample.Refreshes == 1, $"Depois de voltar à tela o balão deveria atualizar uma vez por mudança (foram {sample.Refreshes}).");
            Require(sample.Balloon.Label == "3 pacientes selecionados", "A seleção que já existia deveria voltar ao balão.");
        });

        Check("StatusTag e MetricCard: cada tom usa o fundo e o texto do tema nos dois temas, em inglês ou em português; sem tom o cartão fica Neutro como era", () =>
        {
            foreach (var mode in new[] { Appearance.Light, Appearance.Dark })
            {
                themes.Apply(mode);
                var window = new Window { Width = 960, Height = 640, Left = -10000, Top = -10000, ShowInTaskbar = false, ShowActivated = false, Opacity = 0, FontSize = 14 };
                window.SetResourceReference(Control.ForegroundProperty, "TextBrush");
                var panel = new StackPanel();
                window.Content = panel;
                try
                {
                    var tones = new[] { Tone.Blue, Tone.Green, Tone.Yellow, Tone.Pink, Tone.Violet, Tone.Teal, Tone.Neutral };
                    var tags = tones.Select(tone => new StatusTag { Text = tone.ToString(), Tone = tone }).ToArray();
                    var cards = tones.Select(tone => new ContentControl { Content = new MetricCard("Título", "42", "Dica", tone), ContentTemplate = (DataTemplate)application.FindResource("MetricCardTemplate") }).ToArray();
                    var tagRow = new WrapPanel { Margin = new Thickness(8) };
                    foreach (var tag in tags) { tag.Margin = new Thickness(0, 0, 8, 0); tagRow.Children.Add(tag); }
                    var cardGrid = new UniformGrid { Columns = 4 };
                    foreach (var card in cards) cardGrid.Children.Add(card);
                    panel.Children.Add(tagRow); panel.Children.Add(cardGrid);
                    window.Show(); window.UpdateLayout();
                    window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    foreach (var (tone, tag, card) in tones.Zip(tags, cards))
                    {
                        var name = tone.ToString();
                        var frame = Descendants<Border>(tag).First();
                        var label = Descendants<TextBlock>(tag).Single();
                        Require(((SolidColorBrush)frame.Background).Color == ThemeBrush(application, $"Tone{name}SurfaceBrush").Color && ((SolidColorBrush)label.Foreground).Color == ThemeBrush(application, $"Tone{name}TextBrush").Color, $"{mode}: a etiqueta {name} não usa os tons do tema.");
                        Require(frame.CornerRadius.TopLeft >= 8 && AutomationProperties.GetName(tag) == name, $"{mode}: a etiqueta {name} não é arredondada ou não tem nome acessível.");
                        var presenter = Descendants<ContentPresenter>(card).First();
                        var cardFrame = (Border)presenter.ContentTemplate.FindName("CardFrame", presenter);
                        var stripe = (Border)presenter.ContentTemplate.FindName("ToneStripe", presenter);
                        var value = (TextBlock)presenter.ContentTemplate.FindName("ValueText", presenter);
                        if (tone == Tone.Neutral)
                        {
                            Require(cardFrame.Background is LinearGradientBrush && stripe.Visibility == Visibility.Collapsed && ((SolidColorBrush)value.Foreground).Color == ThemeBrush(application, "TextBrush").Color, $"{mode}: o cartão Neutro deveria ficar como era (degradê do cartão, sem faixa, valor na cor do texto).");
                            continue;
                        }
                        Require(((SolidColorBrush)cardFrame.Background).Color == ThemeBrush(application, $"Tone{name}SurfaceBrush").Color, $"{mode}: o fundo do cartão {name} não é o do tom.");
                        Require(stripe.Visibility == Visibility.Visible && ((SolidColorBrush)stripe.Background).Color == ThemeBrush(application, $"Tone{name}TextBrush").Color && ((SolidColorBrush)value.Foreground).Color == ThemeBrush(application, $"Tone{name}TextBrush").Color, $"{mode}: a faixa e o valor do cartão {name} não usam a cor do tom.");
                    }
                    if (args.Contains("--visual-gallery"))
                    {
                        panel.SetResourceReference(Panel.BackgroundProperty, "BackgroundBrush");
                        window.UpdateLayout();
                        SavePreview(panel, System.IO.Path.Combine(PreviewRoot(args), $"cartoes-e-etiquetas-{(mode == Appearance.Light ? "claro" : "escuro")}.png"));
                    }
                    Require(new MetricCard("a", "b", "c").Tone == Tone.Neutral && new StatusTag().Tone == Tone.Neutral, "O tom padrão deveria ser Neutro.");
                    var portuguese = (StatusTag)XamlReader.Parse("""<views:StatusTag xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:views="clr-namespace:Nora.Desktop.Views;assembly=Nora.Desktop" Text="Concluída" Tone="Verde" />""");
                    var english = (StatusTag)XamlReader.Parse("""<views:StatusTag xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:views="clr-namespace:Nora.Desktop.Views;assembly=Nora.Desktop" Text="Concluída" Tone="Green" />""");
                    Require(portuguese.Tone == Tone.Green && english.Tone == Tone.Green, "O XAML deveria aceitar o tom em português e em inglês.");
                    var empty = new StatusTag();
                    panel.Children.Add(empty); window.UpdateLayout();
                    Require(!empty.IsVisible, "A etiqueta sem texto deveria sumir.");
                }
                finally { window.Close(); }
            }
        });

        Check("NewItemButton e NoticeItem: botão primário com o mais desenhado e nome acessível só com o texto; aviso amarelo com o texto", () =>
        {
            themes.Apply(Appearance.Light);
            var window = new Window { Width = 500, Height = 240, Left = -10000, Top = -10000, ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
            var button = new Button { Style = (Style)application.FindResource("NewItemButton"), Content = "Novo profissional" };
            var notice = new ContentControl { Style = (Style)application.FindResource("NoticeItem"), Content = "Horário ainda não informado" };
            var stack = new StackPanel { Children = { button, notice } };
            stack.SetResourceReference(Panel.BackgroundProperty, "BackgroundBrush");
            window.Content = stack;
            try
            {
                window.Show(); window.UpdateLayout();
                Require(Descendants<System.Windows.Shapes.Path>(button).Any(path => path.Data is not null) && Descendants<TextBlock>(button).Any(text => text.Text == "Novo profissional"), "O botão deveria ter o mais desenhado e o texto.");
                Require(UIElementAutomationPeer.CreatePeerForElement(button).GetName() == "Novo profissional" && button.ActualHeight >= 40 && ((SolidColorBrush)button.Foreground).Color == ThemeBrush(application, "BrandTextBrush").Color, "Nome acessível, altura ou cor do NewItemButton fora do padrão.");
                var frame = Descendants<Border>(notice).First();
                Require(((SolidColorBrush)frame.Background).Color == ThemeBrush(application, "WarningSurfaceBrush").Color && Descendants<TextBlock>(notice).Any(text => text.IsVisible && text.Text == "Horário ainda não informado"), "O aviso deveria ser amarelo e trazer o texto.");
                if (args.Contains("--visual-gallery")) SavePreview(stack, System.IO.Path.Combine(PreviewRoot(args), "botao-novo-e-aviso-claro.png"));
            }
            finally { window.Close(); }
        });

        Check("Botões de ação do balão: 38 px, canto 6, ícone desenhado e rótulo; o de perigo é a lixeira branca em fundo vermelho nos dois temas", () =>
        {
            foreach (var mode in new[] { Appearance.Light, Appearance.Dark })
            {
                themes.Apply(mode);
                using var sample = new ListingSample();
                sample.SelectOnly(0);
                var buttons = new[] { sample.ViewButton, (Button)sample.Root.FindName("EditButton"), sample.RemoveButton };
                foreach (var button in buttons)
                {
                    Require(Math.Abs(button.ActualHeight - 38) < 0.5 && button.ActualWidth >= 38, $"{mode}: o botão {button.Content} deveria ter 38 px de altura.");
                    var frame = Descendants<Border>(button).First();
                    Require(frame.CornerRadius == new CornerRadius(6) && Descendants<System.Windows.Shapes.Path>(button).Any(path => path.IsVisible && path.Data is not null) && Descendants<TextBlock>(button).Any(text => text.Text == (string)button.Content), $"{mode}: o botão {button.Content} sem canto 6, ícone ou rótulo.");
                }
                var danger = Descendants<Border>(sample.RemoveButton).First();
                Require(((SolidColorBrush)danger.Background).Color == ThemeBrush(application, "ListActionDangerBrush").Color, $"{mode}: o botão de perigo não é vermelho.");
                Require(Descendants<System.Windows.Shapes.Path>(sample.RemoveButton).First(path => path.Data is not null).Stroke is SolidColorBrush { Color: { R: 255, G: 255, B: 255 } }, $"{mode}: a lixeira deveria ser branca.");
                Require(ListAction.GetIcon(sample.RemoveButton)?.ToString() == ((Geometry)application.FindResource("IconTrashGeometry")).ToString(), $"{mode}: o botão de perigo não usa a lixeira.");
                var edit = Descendants<Border>(buttons[1]).First();
                Require(((SolidColorBrush)edit.Background).Color == ThemeBrush(application, "ListActionEditBrush").Color, $"{mode}: o botão de editar não é azul.");
                var neutral = Descendants<Border>(sample.ViewButton).First();
                Require(((SolidColorBrush)neutral.Background).Color == ThemeBrush(application, "ListActionBrush").Color && neutral.BorderThickness == new Thickness(1.4), $"{mode}: o botão neutro fora do padrão.");
                var frameBalloon = (Border)sample.Template("Balloon");
                Require(((SolidColorBrush)frameBalloon.Background).Color == ThemeBrush(application, "SelectionBalloonBrush").Color && ((SolidColorBrush)frameBalloon.BorderBrush).Color == ThemeBrush(application, "SelectionBalloonBorderBrush").Color, $"{mode}: o balão não usa as cores do tema.");
                if (args.Contains("--visual-gallery"))
                {
                    sample.SelectOnly(1);
                    SavePreview(sample.Root, System.IO.Path.Combine(PreviewRoot(args), $"listagem-aberta-{(mode == Appearance.Light ? "claro" : "escuro")}.png"));
                }
            }
        });

        Check("PageLayout.Fill: a tela (ou a subtela visível) recebe a altura da área de conteúdo e a rolagem externa desliga; sem Fill a página rola como antes", () =>
        {
            var window = OpenHidden(DesktopProfile.Operational, storageRoot, "page-fill");
            try
            {
                var scroll = (ScrollViewer)window.FindName("PageScroll");
                var watermark = (FrameworkElement)window.FindName("Watermark");
                var container = (Grid)window.FindName("PageContainer");
                Layout(window, 1280, 740);
                var pageRow = (RowDefinition)window.FindName("PageRow");
                Require(scroll.VerticalScrollBarVisibility == ScrollBarVisibility.Auto && watermark.IsVisible && pageRow.Height.IsAuto, "Sem Fill a página deveria rolar, mostrar a marca d'água e valer o que o conteúdo pede (linha Auto).");
                var page = new Grid { Background = Brushes.Transparent };
                SubPage.SetKey(page, "list");
                PageLayout.SetFill(page, true);
                container.Children.Add(page);
                window.ViewModel.Navigate("patients:list");
                var content = Layout(window, 1280, 740);
                Require(scroll.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled && !watermark.IsVisible && pageRow.Height.IsStar, "Com Fill visível a rolagem da página deveria desligar, a marca d'água sair e a linha da tela passar a *.");
                Require(Math.Abs(page.ActualHeight - (scroll.ViewportHeight - 48)) < 1.5 && page.ActualHeight > 300, $"A tela com Fill deveria ocupar a altura da área de conteúdo (tela {page.ActualHeight:F0}, área {scroll.ViewportHeight - 48:F0}).");
                // Uma listagem aberta dentro da tela com Fill: a lista tem a altura da tela, rola sozinha e a página não rola.
                var list = new ListBox { Style = (Style)application.FindResource("OpenList"), ItemsSource = Enumerable.Range(1, 80).Select(number => "Item " + number).ToArray() };
                page.Children.Add(list);
                Layout(window, 1280, 740);
                var inner = Descendants<ScrollViewer>(list).First();
                Require(Math.Abs(list.ActualHeight - page.ActualHeight) < 1.5 && inner.ScrollableHeight > 100 && scroll.ScrollableHeight < 1, $"A lista deveria ter a altura da tela e rolar sozinha (lista {list.ActualHeight:F0}, rolagem da lista {inner.ScrollableHeight:F0}, rolagem da página {scroll.ScrollableHeight:F0}).");
                page.Children.Remove(list);
                window.ViewModel.Navigate("patients:register");
                Layout(window, 1280, 740);
                Require(!page.IsVisible && scroll.VerticalScrollBarVisibility == ScrollBarVisibility.Auto && watermark.IsVisible && pageRow.Height.IsAuto, "Trocar de subtela (Key) deveria desligar o Fill e devolver a rolagem.");
                window.ViewModel.Navigate("patients:list");
                Layout(window, 1280, 740);
                Require(page.IsVisible && scroll.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled, "Voltar à subtela deveria ligar o Fill de novo.");
                container.Children.Remove(page);
                Layout(window, 1280, 740);
                Require(scroll.VerticalScrollBarVisibility == ScrollBarVisibility.Auto && watermark.IsVisible, "Tirar a tela da janela deveria devolver a rolagem.");
                container.Children.Add(page);
                PageLayout.SetFill(page, false);
                Layout(window, 1280, 740);
                Require(scroll.VerticalScrollBarVisibility == ScrollBarVisibility.Auto, "Desligar o Fill deveria devolver a rolagem.");
            }
            finally { CloseWindow(window); }
        });

        Check("OutcomePie usa as chaves de situação do tema (concluídas, faltas, canceladas) e acompanha a troca de tema", () =>
        {
            var pie = new OutcomePie { Width = 120, Height = 120 };
            var window = new Window { Content = pie, Width = 200, Height = 200, Left = -10000, Top = -10000, ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
            window.Show();
            try
            {
            foreach (var mode in new[] { Appearance.Light, Appearance.Dark })
            {
                themes.Apply(mode);
                application.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Require(((SolidColorBrush)pie.CompletedBrush!).Color == ThemeBrush(application, "StatusCompletedBrush").Color
                    && ((SolidColorBrush)pie.MissedBrush!).Color == ThemeBrush(application, "StatusMissedBrush").Color
                    && ((SolidColorBrush)pie.CancelledBrush!).Color == ThemeBrush(application, "StatusCancelledBrush").Color, $"{mode}: a pizza não usa as chaves de situação do tema.");
            }
            var yellow = ThemeBrush(application, "StatusCancelledBrush").Color;
            Require(yellow.R > 200 && yellow.G > 150 && yellow.B < 120, "As canceladas deveriam ser amarelas.");
            }
            finally { window.Close(); }
        });
        themes.Apply(Appearance.Light);
    }
}
