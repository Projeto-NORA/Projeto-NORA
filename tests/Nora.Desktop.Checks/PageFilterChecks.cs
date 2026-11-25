using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Nora.Desktop.Models;
using Nora.Desktop.ViewModels;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

internal static partial class Program
{
    private static void RunPageFilterChecks(string storageRoot)
    {
        Check("Filtros: indicador acompanha abertura e fechamento nos dois perfis", () =>
        {
            using var fixture = new ApiFixture();
            fixture.AddUnit("UBS dos filtros");
            foreach (var municipal in new[] { true, false })
            {
                var window = municipal ? OpenMunicipal(fixture, storageRoot, "filter-chevron-municipal") : OpenOperational(fixture, storageRoot, "filter-chevron-obs");
                try
                {
                    LoginObs(window);
                    Show(window, municipal ? "units" : "patients");
                    var button = (Button)window.FindName("FiltersButton");
                    var popup = (Popup)window.FindName("FiltersPopup");
                    var arrow = Descendants<System.Windows.Shapes.Path>(button).Last();
                    window.UpdateLayout();
                    Require(((RotateTransform)arrow.RenderTransform).Angle == 0, "Seta fechada não aponta para baixo.");
                    InvokeButton(button); window.UpdateLayout();
                    Require(popup.IsOpen && ((RotateTransform)arrow.RenderTransform).Angle == 180, "Seta não acompanhou a abertura.");
                    InvokeButton(button); window.UpdateLayout();
                    Require(!popup.IsOpen && ((RotateTransform)arrow.RenderTransform).Angle == 0, "Seta não acompanhou o fechamento.");
                }
                finally { CloseWindow(window); }
            }
        });
        Check("Filtros: a busca local pesquisa o catálogo com acentos e preserva os dados", () =>
        {
            using var fixture = new ApiFixture();
            fixture.AddUnit("UBS do catálogo");
            var window = OpenMunicipal(fixture, storageRoot, "page-filter-catalog");
            try
            {
                var vm = LoginObs(window);
                var content = Show(window, "specialties");
                vm.SpecialtyCatalog.Clear();
                vm.SpecialtyCatalog.Add(new(Guid.NewGuid(), "Clínica geral", true));
                vm.SpecialtyCatalog.Add(new(Guid.NewGuid(), "Pediatria", true));
                var list = GridNamed(content, "Lista de especialidades");
                var popupBody = OpenPageFilters(window);
                var search = Descendants<TextBox>(popupBody).Single(box => AutomationProperties.GetLabeledBy(box) is TextBlock { Text: "Buscar na lista" });
                search.Text = "clinica";
                Require(list.Items.Cast<Specialty>().Single().Name == "Clínica geral" && vm.SpecialtyCatalog.Count == 2, "O catálogo não foi filtrado pela grafia sem acento.");
                search.Text = "ausente";
                popupBody.UpdateLayout();
                Require(list.Items.Count == 0 && HasText(popupBody, "Nenhum resultado nesta lista."), $"A busca sem resultado deveria mostrar o estado vazio. Linhas: {list.Items.Count}. Textos: {string.Join(" | ", VisibleTexts(popupBody))}.");
                InvokeButton(FindActionButton(popupBody, "Limpar filtros"));
                Require(list.Items.Count == 2 && search.Text.Length == 0, "Limpar filtros não restaurou o catálogo.");
            }
            finally { CloseWindow(window); }
        });
    }

    private static FrameworkElement OpenPageFilters(ShellWindow window)
    {
        var popup = (Popup)window.FindName("FiltersPopup");
        if (!popup.IsOpen) InvokeButton((Button)window.FindName("FiltersButton"));
        var panel = (FrameworkElement)popup.Child;
        panel.UpdateLayout();
        return panel;
    }

    private static T PageField<T>(FrameworkElement content, Func<T, bool> match) where T : FrameworkElement
    {
        var inline = Descendants<T>(content).SingleOrDefault(match);
        if (inline is not null) return inline;
        if (Window.GetWindow(content) is ShellWindow window && window.FindName("FiltersButton") is Button { IsVisible: true })
            return Descendants<T>(OpenPageFilters(window)).Single(match);
        throw new InvalidOperationException($"Controle {typeof(T).Name} ausente.");
    }
}
