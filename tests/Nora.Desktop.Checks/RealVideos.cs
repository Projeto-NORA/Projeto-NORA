using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Nora.Desktop.Models;
using Nora.Desktop.Services;
using Nora.Desktop.ViewModels;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

/// <summary>
/// Vídeos de demonstração gravados quadro a quadro (1920x1080, 30 quadros por segundo) a partir dos programas reais conectados à API de demonstração,
/// com um cursor desenhado indo até cada botão antes do clique. Os quadros viram MP4 pelo ffmpeg.
/// Uso: <c>--videos-reais --saida &lt;pasta&gt; [--video 1|2|3|4]</c>, com as mesmas variáveis NORA_CAPTURA_* das capturas.
/// </summary>
internal static partial class Program
{
    private const double VideoWidth = 1920, VideoHeight = 1080;
    private const int VideoFps = 30;

    private static int RunRealVideos(string[] args)
    {
        var output = Path.GetFullPath(ArgumentValue(args, "--saida") ?? "videos");
        var only = ArgumentValue(args, "--video");
        Directory.CreateDirectory(output);
        var url = Environment.GetEnvironmentVariable("NORA_CAPTURA_URL") ?? throw new InvalidOperationException("Defina NORA_CAPTURA_URL.");
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(application.Dispatcher));
        using var themes = new ThemeService(application);
        themes.Apply(Appearance.Light);
        ShellWindow.AutoOpenProfileChoice = false;
        var videos = new (string Id, string Nome, DesktopProfile Perfil, Func<VideoRecorder, Task> Roteiro)[]
        {
            ("1", "1-painel-municipal-cards-e-graficos", DesktopProfile.Municipal, VideoMunicipalOverview),
            ("2", "2-painel-municipal-cadastro-e-listagem-de-ubs", DesktopProfile.Municipal, VideoMunicipalUnits),
            ("3", "3-obs-cadastro-e-listagem-de-pacientes", DesktopProfile.Operational, VideoObsPatients),
            ("4", "4-obs-visao-geral-cards-e-graficos", DesktopProfile.Operational, VideoObsOverview),
        };
        var failures = 0;
        foreach (var video in videos.Where(item => only is null || item.Id == only))
        {
            var storage = Path.Combine(Path.GetTempPath(), "nora-video-" + Guid.NewGuid().ToString("N"));
            var frames = Path.Combine(storage, "quadros");
            Directory.CreateDirectory(frames);
            try
            {
                var municipal = video.Perfil.IsMunicipal;
                var email = Environment.GetEnvironmentVariable(municipal ? "NORA_CAPTURA_MUN_EMAIL" : "NORA_CAPTURA_OBS_EMAIL")!;
                var password = Environment.GetEnvironmentVariable(municipal ? "NORA_CAPTURA_MUN_SENHA" : "NORA_CAPTURA_OBS_SENHA")!;
                var window = OpenRealWindow(application, video.Perfil, Path.Combine(storage, "preferencias"));
                try
                {
                    LoginVideo(window, url, email, password, municipal);
                    var recorder = new VideoRecorder(window, frames);
                    var task = video.Roteiro(recorder);
                    RealPumpUntil(() => task.IsCompleted, 900, video.Nome);
                    task.GetAwaiter().GetResult();
                    var mp4 = Path.Combine(output, video.Nome + ".mp4");
                    Encode(frames, mp4);
                    Console.WriteLine($"VIDEO {mp4} ({recorder.Count} quadros, {recorder.Count / (double)VideoFps:0.0} s)");
                }
                finally { foreach (var other in application.Windows.OfType<Window>().ToList()) other.Close(); }
            }
            catch (Exception error) { failures++; Console.WriteLine($"ERRO vídeo {video.Nome}: {error}"); }
            finally { try { Directory.Delete(storage, true); } catch { } }
        }
        return failures == 0 ? 0 : 1;
    }

    private static void LoginVideo(ShellWindow window, string url, string email, string password, bool municipal)
    {
        var workspace = window.ViewModel.Workspace;
        var content = Layout(window, VideoWidth, VideoHeight);
        Input(content, "Endereço da API").Text = url;
        Input(content, "E-mail").Text = email;
        Descendants<PasswordBox>(content).Single(input => AutomationProperties.GetName(input) == "Senha de acesso").Password = password;
        InvokeButton(FindActionButton(content, "Entrar"));
        RealPumpUntil(() => !workspace.IsBusy, 60, "login");
        if (!workspace.IsAuthenticated) throw new InvalidOperationException("O login falhou.");
        if (!municipal) ChooseRealUnit(workspace);
    }

    private static void Encode(string frames, string mp4)
    {
        var ffmpeg = Environment.GetEnvironmentVariable("NORA_FFMPEG") ?? "ffmpeg";
        var start = new ProcessStartInfo(ffmpeg, $"-y -loglevel error -framerate {VideoFps} -i \"{Path.Combine(frames, "%05d.png")}\" -c:v libx264 -preset medium -crf 18 -pix_fmt yuv420p -movflags +faststart \"{mp4}\"")
        { UseShellExecute = false, RedirectStandardError = true };
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException("ffmpeg falhou: " + error);
    }

    // ---------- Roteiros ----------

    private static async Task VideoMunicipalOverview(VideoRecorder r)
    {
        await r.Go("home");
        await r.EnsureCards();
        await r.Hold(2.0);
        await r.ScrollPage(0.55, 3.0); await r.Hold(1.0); await r.ScrollPage(0, 2.0); await r.Hold(0.6);
        await r.Click(r.Find<ViewModeToggle>());
        await r.Settle(); await r.Hold(2.0);
        await r.ScrollPage(1.0, 6.0); await r.Hold(2.0);
    }

    private static async Task VideoObsOverview(VideoRecorder r)
    {
        await r.Go("home");
        await r.EnsureCards();
        await r.Hold(2.0);
        await r.ScrollPage(1.0, 4.0); await r.Hold(1.0); await r.ScrollPage(0, 2.5); await r.Hold(0.6);
        await r.Click(r.Find<ViewModeToggle>());
        await r.Settle(); await r.Hold(2.0);
        await r.ScrollPage(1.0, 5.0); await r.Hold(2.0);
    }

    private static async Task VideoMunicipalUnits(VideoRecorder r)
    {
        await r.Go("units:registry");
        await r.Hold(1.5);
        var list = r.OpenList();
        await r.SelectItem(list, 1); await r.Hold(2.0);
        await r.SelectItem(list, 3); await r.Hold(1.5);
        await r.Click(r.NewButton());
        await r.WaitDialog(); await r.Hold(1.0);
        var fields = r.DialogTextBoxes();
        // A região precisa casar com a cidade no serviço de localização (Photon) e o número com um único ponto.
        string[] values = ["UBS Vila Nove de Julho", "São José dos Campos", "Avenida Nove de Julho, 95"];
        for (var i = 0; i < Math.Min(fields.Count, values.Length); i++) await r.Type(fields[i], values[i]);
        await r.Hold(1.0);
        await r.Click(r.DialogPrimaryButton());
        await r.WaitRecording(40); await r.Hold(1.0);
        if (r.Dialog() is not null) { await r.Hold(2.0); await r.CloseDialog(); }
        await r.Settle(); await r.Hold(1.0);
        var after = r.OpenList();
        var index = after.Items.Cast<object>().ToList().FindIndex(item => DescribeItem(item).Contains("Nove de Julho"));
        if (index >= 0) { await r.ScrollIntoView(after, index); await r.SelectItem(after, index); }
        await r.Hold(3.0);
    }

    private static async Task VideoObsPatients(VideoRecorder r)
    {
        await r.Go("patients:list");
        await r.Hold(1.5);
        var list = r.OpenList();
        await r.ScrollList(list, 0.5, 2.5); await r.ScrollList(list, 0, 1.5);
        await r.SelectItem(list, 1); await r.Hold(1.5);
        await r.OpenDetails(list, 1); await r.Hold(3.0); await r.CloseDialog(); await r.Hold(0.6);
        await r.Go("patients:register"); await r.Hold(1.0);
        var name = "Renata Siqueira Lopes";
        var page = r.PageTextBoxes();
        string[] values = [name, NewCpf(), "renata.lopes@email.example", "(12) 99741-5832"];
        for (var i = 0; i < Math.Min(page.Count, values.Length); i++) await r.Type(page[i], values[i]);
        await r.Hold(1.0);
        await r.Click(r.PageButton("Cadastrar paciente"));
        await r.WaitRecording(20); await r.Hold(2.0);
        await r.Go("patients:list"); await r.Hold(0.8);
        var found = r.OpenList();
        var index = found.Items.Cast<object>().ToList().FindIndex(item => DescribeItem(item).Contains(name));
        if (index >= 0)
        {
            var scroll = Descendants<ScrollViewer>(found).FirstOrDefault();
            if (scroll is not null && scroll.ScrollableHeight > 1)
                await r.ScrollList(found, Math.Clamp((index - 6) / (double)Math.Max(1, found.Items.Count - 12), 0, 1), 2.5);
            await r.ScrollIntoView(found, index);
            await r.SelectItem(found, index); await r.Hold(1.0);
            await r.OpenDetails(found, index); await r.Hold(3.5); await r.CloseDialog();
        }
        await r.Hold(1.5);
    }

    private static string DescribeItem(object item) =>
        string.Join(" ", item.GetType().GetProperties().Where(p => p.PropertyType == typeof(string)).Select(p => p.GetValue(item) as string ?? ""));

    /// <summary>CPF fictício com dígitos verificadores válidos.</summary>
    private static string NewCpf()
    {
        var random = new Random();
        var digits = Enumerable.Range(0, 9).Select(_ => random.Next(10)).ToList();
        for (var round = 0; round < 2; round++)
        {
            var sum = digits.Select((d, i) => d * (digits.Count + 1 - i)).Sum();
            var check = sum * 10 % 11 % 10;
            digits.Add(check);
        }
        var s = string.Concat(digits);
        return $"{s[..3]}.{s[3..6]}.{s[6..9]}-{s[9..]}";
    }

    // ---------- Gravador ----------

    private sealed class VideoRecorder(ShellWindow window, string folder)
    {
        private readonly WorkspaceViewModel workspace = window.ViewModel.Workspace;
        private Point cursor = new(VideoWidth * 0.55, VideoHeight * 0.55);
        private double pulse;
        public int Count { get; private set; }

        private FrameworkElement Content() => Layout(window, VideoWidth, VideoHeight);

        public Window? Dialog() => Application.Current.Windows.OfType<Window>().FirstOrDefault(item => !ReferenceEquals(item, window) && item.IsVisible && item.IsLoaded && item.Content is FrameworkElement);

        private static async Task Yield(int ms = 0) { if (ms > 0) await Task.Delay(ms); else await Dispatcher.Yield(DispatcherPriority.Background); }

        public async Task Frame(int count = 1)
        {
            if (count <= 0) return;
            var main = Content();
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new VisualBrush(main) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, new Rect(0, 0, VideoWidth, VideoHeight));
                if (Dialog() is { } dialog && dialog.Content is FrameworkElement root)
                {
                    StopAnimations(root);
                    dialog.UpdateLayout();
                    var rect = DialogRect(dialog);
                    dc.DrawRectangle(new VisualBrush(root) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top }, null, rect);
                }
                DrawCursor(dc);
            }
            var bitmap = new RenderTargetBitmap((int)VideoWidth, (int)VideoHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            var first = Path.Combine(folder, $"{Count:00000}.png");
            using (var stream = File.Create(first)) encoder.Save(stream);
            Count++;
            for (var i = 1; i < count; i++) { File.Copy(first, Path.Combine(folder, $"{Count:00000}.png")); Count++; }
            await Yield();
        }

        private static void StopAnimations(FrameworkElement root)
        {
            if (root.RenderTransform is ScaleTransform zoom)
            {
                zoom.BeginAnimation(ScaleTransform.ScaleXProperty, null); zoom.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                zoom.ScaleX = 1; zoom.ScaleY = 1;
            }
            root.BeginAnimation(UIElement.OpacityProperty, null); root.Opacity = 1;
        }

        private static Rect DialogRect(Window dialog)
        {
            var w = dialog.ActualWidth; var h = dialog.ActualHeight;
            return new Rect(Math.Max(0, (VideoWidth - w) / 2), Math.Max(0, (VideoHeight - h) / 2), w, h);
        }

        private void DrawCursor(DrawingContext dc)
        {
            if (pulse > 0)
            {
                var radius = 10 + (1 - pulse) * 22;
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(110 * pulse), 30, 111, 209)), null, cursor, radius, radius);
            }
            var arrow = Geometry.Parse("M 0,0 L 0,22 L 6,16.5 L 10,26 L 14,24.3 L 10,15 L 17,15 Z");
            dc.PushTransform(new TranslateTransform(cursor.X, cursor.Y));
            dc.DrawGeometry(Brushes.Black, new Pen(Brushes.White, 1.6), arrow);
            dc.Pop();
        }

        public Task Hold(double seconds) => Frame((int)Math.Round(seconds * VideoFps));

        /// <summary>Espera a tela carregar sem gravar (corte limpo no vídeo).</summary>
        public async Task Settle(int seconds = 20)
        {
            var clock = Stopwatch.StartNew(); var stable = 0;
            while (stable < 3 && clock.Elapsed.TotalSeconds < seconds)
            {
                await Yield(150);
                var root = Dialog()?.Content as FrameworkElement ?? Content();
                stable = RealIsLoading(root, workspace) ? 0 : stable + 1;
            }
        }

        /// <summary>Espera gravando em tempo real (mostra os passos de uma operação que demora).</summary>
        public async Task WaitRecording(int seconds)
        {
            var clock = Stopwatch.StartNew(); var stable = 0;
            while (stable < 3 && clock.Elapsed.TotalSeconds < seconds)
            {
                await Frame(3); await Yield(100);
                var root = Dialog()?.Content as FrameworkElement ?? Content();
                stable = RealIsLoading(root, workspace) || (Dialog() is FormDialog form && form.IsBusy) ? 0 : stable + 1;
            }
        }

        public async Task Go(string route) { window.ViewModel.Navigate(route); await Settle(); }

        public T Find<T>() where T : FrameworkElement => Descendants<T>(Content()).First(item => item.IsVisible);

        public async Task EnsureCards()
        {
            var toggle = Descendants<ViewModeToggle>(Content()).FirstOrDefault(item => item.IsVisible);
            if (toggle is not null && toggle.Mode != ViewMode.Cards) { InvokeButton(toggle); await Settle(); }
            if (window.FindName("PageScroll") is ScrollViewer scroll) scroll.ScrollToTop();
        }

        public ListBox OpenList()
        {
            var styles = new[] { "OpenList", "OpenListLow" }.Select(key => Application.Current.TryFindResource(key) as Style).Where(style => style is not null).ToList();
            return Descendants<ListBox>(Content()).First(box => box.IsVisible && styles.Any(style => ReferenceEquals(box.Style, style) || ReferenceEquals(box.Style?.BasedOn, style)));
        }

        public Button NewButton()
        {
            var style = Application.Current.TryFindResource("NewItemButton") as Style;
            return Descendants<Button>(Content()).First(button => button.IsVisible && ReferenceEquals(button.Style, style));
        }

        public TextBox? SearchBox() => Descendants<TextBox>(Content()).FirstOrDefault(box => box.IsVisible && (AutomationProperties.GetName(box) ?? "").Contains("Pesquisar", StringComparison.OrdinalIgnoreCase));

        public List<TextBox> PageTextBoxes()
        {
            var content = Content();
            var header = window.FindName("PageHeader") as FrameworkElement;
            return Descendants<TextBox>(content).Where(box => box.IsVisible && box.IsEnabled && !box.IsReadOnly && (header is null || !IsInside(box, header))).ToList();
        }

        public List<TextBox> DialogTextBoxes() =>
            Dialog() is { Content: FrameworkElement root } ? Descendants<TextBox>(root).Where(box => box.IsVisible && box.IsEnabled && !box.IsReadOnly).ToList() : [];

        public Button DialogPrimaryButton()
        {
            var root = (FrameworkElement)Dialog()!.Content;
            var buttons = Descendants<Button>(root).Where(button => button.IsVisible && button.IsEnabled && ButtonText(button).Length > 0).ToList();
            // O primário fica embaixo à direita.
            return buttons.OrderByDescending(button => Position(button).Y + Position(button).X / 10000).First();
        }

        public Button PageButton(string text) => Descendants<Button>(Content()).First(button => button.IsVisible && ButtonText(button).Contains(text, StringComparison.OrdinalIgnoreCase));

        private static string ButtonText(Button button) =>
            button.Content as string ?? string.Concat(Descendants<TextBlock>(button).Select(text => text.Text));

        private static bool IsInside(DependencyObject child, DependencyObject parent)
        {
            for (var node = child; node is not null; node = VisualTreeHelper.GetParent(node)) if (ReferenceEquals(node, parent)) return true;
            return false;
        }

        /// <summary>Centro do elemento no quadro do vídeo (na janela principal ou dentro do pop-up centralizado).</summary>
        private Point Position(FrameworkElement element)
        {
            var center = new Point(element.ActualWidth / 2, element.ActualHeight / 2);
            if (Dialog() is { Content: FrameworkElement root } dialog && IsInside(element, root))
            {
                var p = element.TransformToAncestor(root).Transform(center);
                var rect = DialogRect(dialog);
                return new Point(rect.X + p.X, rect.Y + p.Y);
            }
            return element.TransformToAncestor(Content()).Transform(center);
        }

        public async Task MoveTo(FrameworkElement target, double seconds = 0.6)
        {
            var from = cursor; var to = Position(target);
            var frames = Math.Max(1, (int)(seconds * VideoFps));
            for (var i = 1; i <= frames; i++)
            {
                var t = i / (double)frames; t = t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
                cursor = new Point(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t);
                await Frame();
            }
        }

        private async Task Pulse()
        {
            for (var i = 6; i >= 1; i--) { pulse = i / 6.0; await Frame(); }
            pulse = 0;
        }

        public async Task Click(Button button)
        {
            await MoveTo(button);
            await Pulse();
            // Pode abrir um pop-up modal: o clique vai para a fila do Dispatcher e o roteiro continua dentro do laço do pop-up.
            _ = window.Dispatcher.BeginInvoke(() => InvokeButton(button));
            await Yield(150);
        }

        public async Task WaitDialog(int seconds = 10)
        {
            var clock = Stopwatch.StartNew();
            while (Dialog() is null && clock.Elapsed.TotalSeconds < seconds) await Yield(100);
            await Settle();
        }

        public async Task CloseDialog()
        {
            if (Dialog() is not { } dialog) return;
            var close = Descendants<Button>((FrameworkElement)dialog.Content).FirstOrDefault(button => button.IsVisible && (AutomationProperties.GetName(button) ?? "").Contains("Fechar", StringComparison.OrdinalIgnoreCase));
            if (close is not null) { await MoveTo(close, 0.5); await Pulse(); }
            dialog.Close();
            await Yield(200);
            await Frame(3);
        }

        public async Task SelectItem(ListBox list, int index)
        {
            if (index >= list.Items.Count) return;
            list.UpdateLayout();
            if (list.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement row) { await MoveTo(row, 0.6); await Pulse(); }
            list.SelectedItem = list.Items[index];
            await Yield(150);
            await Frame(6);
        }

        public async Task OpenDetails(ListBox list, int index)
        {
            if (index >= list.Items.Count) return;
            var item = list.Items[index];
            if (list.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement row) { await Pulse(); await Pulse(); _ = row; }
            _ = window.Dispatcher.BeginInvoke(() => _ = window.OpenItemDetailsAsync(item));
            await WaitDialog();
        }

        public async Task ScrollIntoView(ListBox list, int index) { list.ScrollIntoView(list.Items[index]); await Yield(200); await Frame(6); }

        public async Task Type(TextBox box, string text)
        {
            await MoveTo(box, 0.5); await Pulse();
            box.Focus();
            box.Text = "";
            foreach (var character in text) { box.Text += character; box.CaretIndex = box.Text.Length; await Frame(character == ' ' ? 2 : 1); }
            await Frame(8);
        }

        private async Task Animate(ScrollViewer scroll, double to, double seconds)
        {
            var from = scroll.VerticalOffset;
            var frames = Math.Max(1, (int)(seconds * VideoFps));
            for (var i = 1; i <= frames; i++)
            {
                var t = i / (double)frames; t = t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2;
                scroll.ScrollToVerticalOffset(from + (to - from) * t);
                Content();
                await Frame();
            }
        }

        /// <summary>Rola a página até a fração indicada (0 = topo, 1 = fim).</summary>
        public async Task ScrollPage(double fraction, double seconds)
        {
            Content();
            var scroll = window.FindName("PageScroll") as ScrollViewer;
            if (scroll is null || scroll.ScrollableHeight <= 1)
                scroll = Descendants<ScrollViewer>(Content()).Where(item => item.IsVisible && item.ScrollableHeight > 1).OrderByDescending(item => item.ActualHeight).FirstOrDefault();
            if (scroll is null) { await Hold(seconds); return; }
            await Animate(scroll, scroll.ScrollableHeight * fraction, seconds);
        }

        public async Task ScrollList(ListBox list, double fraction, double seconds)
        {
            var scroll = Descendants<ScrollViewer>(list).FirstOrDefault();
            if (scroll is null || scroll.ScrollableHeight <= 1) { await Hold(seconds); return; }
            await Animate(scroll, scroll.ScrollableHeight * fraction, seconds);
        }
    }
}
