using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Nora.Desktop.Models;
using Nora.Desktop.Services;

namespace Nora.Desktop.Views;

/// <summary>
/// Um gráfico com título e, à direita do título, o alternador Linha | Pizza. A linha vem de <see cref="Lines"/> (o <see cref="LineChartView"/>) e a pizza,
/// em rosca com legenda de valor e percentual, de <see cref="Slices"/>. Quando só um dos dois existe, o alternador some e o gráfico que existe é o mostrado;
/// sem nenhum, aparece a nota explicando o motivo. Usado na grade de gráficos das telas e no pop-up aberto pelo botão de gráfico do card.
/// </summary>
public partial class ChartPanel : UserControl
{
    private static readonly string[] KindNames = ["Linha", "Pizza"];
    private bool refreshing;

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(ChartPanel), new PropertyMetadata("", (owner, _) => ((ChartPanel)owner).Refresh()));
    public static readonly DependencyProperty LinesProperty = DependencyProperty.Register(nameof(Lines), typeof(IReadOnlyList<ChartLine>), typeof(ChartPanel), new PropertyMetadata(null, (owner, _) => ((ChartPanel)owner).Refresh()));
    public static readonly DependencyProperty SlicesProperty = DependencyProperty.Register(nameof(Slices), typeof(IReadOnlyList<ChartPoint>), typeof(ChartPanel), new PropertyMetadata(null, (owner, _) => ((ChartPanel)owner).Refresh()));
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(ChartKind), typeof(ChartPanel), new FrameworkPropertyMetadata(ChartKind.Line, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (owner, _) => ((ChartPanel)owner).Refresh()));
    public static readonly DependencyProperty LineNoteProperty = DependencyProperty.Register(nameof(LineNote), typeof(string), typeof(ChartPanel), new PropertyMetadata("", (owner, _) => ((ChartPanel)owner).Refresh()));
    public static readonly DependencyProperty PieNoteProperty = DependencyProperty.Register(nameof(PieNote), typeof(string), typeof(ChartPanel), new PropertyMetadata("", (owner, _) => ((ChartPanel)owner).Refresh()));

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value ?? ""); }
    /// <summary>As linhas do gráfico de linha (uma ou mais, cada uma com o nome, a chave de cor e os pontos).</summary>
    public IReadOnlyList<ChartLine>? Lines { get => (IReadOnlyList<ChartLine>?)GetValue(LinesProperty); set => SetValue(LinesProperty, value); }
    /// <summary>As fatias da pizza (cada uma com a sua chave de cor ou, sem ela, a paleta do tema).</summary>
    public IReadOnlyList<ChartPoint>? Slices { get => (IReadOnlyList<ChartPoint>?)GetValue(SlicesProperty); set => SetValue(SlicesProperty, value); }
    /// <summary>O gráfico mostrado agora. Se só um tipo tem dado, vale esse, ainda que outro tenha sido pedido.</summary>
    public ChartKind Kind { get => (ChartKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    /// <summary>Nota sobre a origem dos dados da linha; sem linha, vira a explicação no lugar do gráfico.</summary>
    public string LineNote { get => (string)GetValue(LineNoteProperty); set => SetValue(LineNoteProperty, value ?? ""); }
    /// <summary>Nota sobre a origem dos dados da pizza.</summary>
    public string PieNote { get => (string)GetValue(PieNoteProperty); set => SetValue(PieNoteProperty, value ?? ""); }

    /// <summary>O alternador Linha | Pizza (visível só quando há os dois tipos de dado).</summary>
    public SegmentedToggle Toggle => KindToggle;
    public bool HasLine => Lines?.Any(line => line.Points.Count > 0) == true;
    public bool HasPie => Slices?.Any(slice => slice.Value > 0) == true;

    public ChartPanel()
    {
        InitializeComponent();
        KindToggle.ItemsSource = KindNames;
        KindToggle.SelectionChanged += OnToggleChanged;
        Refresh();
    }

    private void OnToggleChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!refreshing && KindToggle.SelectedIndex >= 0) Kind = KindToggle.SelectedIndex == 1 ? ChartKind.Pie : ChartKind.Line;
    }

    private void Refresh()
    {
        if (refreshing || LineView is null) return;
        refreshing = true;
        try
        {
            var line = HasLine; var pie = HasPie;
            var shown = Kind == ChartKind.Pie ? (pie || !line ? ChartKind.Pie : ChartKind.Line) : (line || !pie ? ChartKind.Line : ChartKind.Pie);
            if (shown != Kind) SetCurrentValue(KindProperty, shown);
            var empty = !line && !pie;
            TitleText.Text = Title;
            TitleText.ToolTip = Title;
            AutomationProperties.SetName(this, Title);
            KindToggle.Visibility = line && pie ? Visibility.Visible : Visibility.Collapsed;
            KindToggle.SelectedIndex = shown == ChartKind.Pie ? 1 : 0;
            var wasPie = PiePanel.Visibility == Visibility.Visible;
            LineView.Visibility = !empty && shown == ChartKind.Line ? Visibility.Visible : Visibility.Collapsed;
            PiePanel.Visibility = !empty && shown == ChartKind.Pie ? Visibility.Visible : Visibility.Collapsed;
            LineView.Lines = Lines;
            Pie.Slices = pie ? Slices : null;
            BuildLegend();
            var note = empty ? (LineNote.Length > 0 ? LineNote : PieNote) : shown == ChartKind.Pie ? PieNote : LineNote;
            EmptyText.Text = empty ? note : "";
            EmptyText.Visibility = empty && note.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            NoteText.Text = empty ? "" : note;
            NoteText.Visibility = NoteText.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (IsLoaded && !empty && wasPie != (shown == ChartKind.Pie))
                (shown == ChartKind.Pie ? (UIElement)PiePanel : LineView).BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        }
        finally { refreshing = false; }
    }

    /// <summary>Uma linha por fatia: bolinha na cor da fatia, nome, valor e percentual do total.</summary>
    private void BuildLegend()
    {
        PieLegend.Children.Clear();
        var slices = Slices?.Select((slice, index) => (slice, index)).Where(item => item.slice.Value > 0).ToList() ?? [];
        var total = slices.Sum(item => item.slice.Value);
        foreach (var (slice, index) in slices)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 48 });
            var dot = new Ellipse { Width = 12, Height = 12, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            dot.SetResourceReference(Shape.FillProperty, OutcomePie.BrushKeyFor(slice, index));
            row.Children.Add(dot);
            row.Children.Add(Text(slice.Label, 1, false, TextAlignment.Left, 0, true));
            row.Children.Add(Text(slice.ValueDisplay, 2, true, TextAlignment.Right, 12, false));
            row.Children.Add(Text((slice.Value / total).ToString("P0", DisplayFormat.Culture), 3, false, TextAlignment.Right, 0, false));
            PieLegend.Children.Add(row);
        }
    }

    private static TextBlock Text(string text, int column, bool bold, TextAlignment alignment, double leftMargin, bool trim)
    {
        var block = new TextBlock { Text = text, TextAlignment = alignment, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(leftMargin, 0, 0, 0), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = trim ? TextTrimming.CharacterEllipsis : TextTrimming.None, TextWrapping = TextWrapping.NoWrap };
        block.SetResourceReference(TextBlock.ForegroundProperty, !bold && column == 3 ? "SecondaryBrush" : "TextBrush");
        Grid.SetColumn(block, column);
        return block;
    }
}
