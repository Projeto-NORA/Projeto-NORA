using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Nora.Desktop.Views;

namespace Nora.Desktop.Checks;

internal static partial class Program
{
    private static void CheckEnterNavigation()
    {
        Check("Enter percorre os campos do formulário e aciona somente sua ação", () =>
        {
            var first = new TextBox();
            var disabled = new TextBox { IsEnabled = false };
            var readOnly = new TextBox { IsReadOnly = true };
            var next = new PasswordBox();
            var submit = new Button { Content = "Salvar" };
            var unrelated = new Button { Content = "Excluir" };
            var clicks = 0;
            var wrongClicks = 0;
            submit.Click += (_, _) => clicks++;
            unrelated.Click += (_, _) => wrongClicks++;
            foreach (var item in new FrameworkElement[] { first, disabled, readOnly, next, submit }) EnterNavigation.SetGroup(item, "form");
            EnterNavigation.SetGroup(unrelated, "other");
            var panel = new StackPanel();
            foreach (var item in new UIElement[] { first, disabled, readOnly, next, submit, unrelated }) panel.Children.Add(item);
            var window = new Window { Content = panel, Width = 420, Height = 300, ShowInTaskbar = false };
            try
            {
                window.Show(); window.Activate();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Require(first.Focus(), "Não foi possível focar o primeiro campo.");
                PressEnter(first);
                Require(next.IsKeyboardFocusWithin && clicks == 0, "Enter não avançou para a senha ou acionou o botão antes da hora.");
                PressEnter(next);
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Require(clicks == 1 && wrongClicks == 0, "Enter não acionou somente a ação do formulário.");
                submit.IsEnabled = false;
                PressEnter(next);
                Require(clicks == 1 && wrongClicks == 0, "Enter acionou uma ação desabilitada.");
                submit.IsEnabled = true;
                PressEnter(readOnly);
                Require(clicks == 1 && wrongClicks == 0, "Enter em campo somente de leitura acionou uma ação.");
            }
            finally { window.Close(); }
        });
        Check("Enter preserva texto multilinha e separa formulários repetidos", () =>
        {
            var panel = new StackPanel();
            var firstGroup = new StackPanel();
            var secondGroup = new StackPanel();
            EnterNavigation.SetScope(firstGroup, true);
            EnterNavigation.SetScope(secondGroup, true);
            var first = new TextBox();
            var multiline = new TextBox { AcceptsReturn = true };
            var firstButton = new Button();
            var second = new TextBox();
            var secondButton = new Button();
            var firstClicks = 0;
            var secondClicks = 0;
            firstButton.Click += (_, _) => firstClicks++;
            secondButton.Click += (_, _) => secondClicks++;
            foreach (var item in new FrameworkElement[] { first, multiline, firstButton, second, secondButton }) EnterNavigation.SetGroup(item, "repeated");
            firstGroup.Children.Add(first); firstGroup.Children.Add(multiline); firstGroup.Children.Add(firstButton);
            secondGroup.Children.Add(second); secondGroup.Children.Add(secondButton);
            panel.Children.Add(firstGroup); panel.Children.Add(secondGroup);
            var window = new Window { Content = panel, Width = 420, Height = 300, ShowInTaskbar = false };
            try
            {
                window.Show(); window.Activate();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Require(first.Focus(), "Não foi possível focar o campo.");
                PressEnter(first);
                Require(multiline.IsKeyboardFocusWithin, "Enter não chegou ao campo multilinha.");
                var handled = PressEnter(multiline);
                Require(!handled && firstClicks == 0 && secondClicks == 0, "Enter no texto multilinha disparou uma ação.");
                second.Focus(); PressEnter(second);
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Require(firstClicks == 0 && secondClicks == 1, "Enter acionou o formulário repetido errado.");
            }
            finally { window.Close(); }
        });
        Check("Enter avança no login real dos dois aplicativos", () =>
        {
            var view = new DesktopAccessView();
            var window = new Window { Content = view, Width = 600, Height = 500, ShowInTaskbar = false };
            try
            {
                window.Show(); window.Activate();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var email = Descendants<TextBox>(view).Single(item => AutomationProperties.GetName(item) == "E-mail");
                var password = Descendants<PasswordBox>(view).Single();
                Require(email.Focus(), "Não foi possível focar o e-mail do login.");
                PressEnter(email);
                Require(password.IsKeyboardFocusWithin, "Enter no e-mail não avançou para a senha no login real.");
            }
            finally { window.Close(); }
        });
        Check("Enter percorre o cadastro real de conta e aciona Criar conta", () =>
        {
            var view = new StaffCreateView();
            ((StackPanel)((Border)view.Content).Child).IsEnabled = true;
            var window = new Window { Content = view, Width = 800, Height = 700, ShowInTaskbar = false };
            try
            {
                window.Show(); window.Activate(); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var name = Descendants<TextBox>(view).Single(item => AutomationProperties.GetName(item) == "Nome da nova conta");
                var email = Descendants<TextBox>(view).Single(item => AutomationProperties.GetName(item) == "E-mail da nova conta");
                var role = Descendants<ComboBox>(view).Single(item => AutomationProperties.GetName(item) == "Perfil da nova conta");
                var password = Descendants<PasswordBox>(view).Single(item => AutomationProperties.GetName(item) == "Senha provisória da nova conta");
                var confirm = Descendants<PasswordBox>(view).Single(item => AutomationProperties.GetName(item) == "Confirmação da senha provisória da nova conta");
                var button = Descendants<Button>(view).Single(item => Equals(item.Content, "Criar conta"));
                var clicks = 0; button.Click += (_, _) => clicks++;
                Require(name.Focus(), "Não foi possível focar o nome.");
                PressEnter(name); Require(email.IsKeyboardFocusWithin, "Nome não avançou para e-mail.");
                PressEnter(email); Require(role.IsKeyboardFocusWithin, "E-mail não avançou para perfil.");
                PressEnter(role); Require(password.IsKeyboardFocusWithin, "Perfil não avançou para senha.");
                PressEnter(password); Require(confirm.IsKeyboardFocusWithin, "Senha não avançou para confirmação.");
                PressEnter(confirm); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Require(clicks == 1, "Enter na confirmação não acionou Criar conta uma vez.");
            }
            finally { window.Close(); }
        });
        Check("Enter respeita o seletor aberto e avança quando fechado", () =>
        {
            var selector = new ComboBox();
            selector.Items.Add("Um"); selector.Items.Add("Dois");
            var next = new TextBox();
            EnterNavigation.SetGroup(selector, "select"); EnterNavigation.SetGroup(next, "select");
            var panel = new StackPanel(); panel.Children.Add(selector); panel.Children.Add(next);
            var window = new Window { Content = panel, Width = 420, Height = 300, ShowInTaskbar = false };
            try
            {
                window.Show(); window.Activate(); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                selector.Focus(); selector.IsDropDownOpen = true;
                PressEnter(selector);
                Require(!next.IsKeyboardFocusWithin, "Enter com a lista aberta avançou antes da seleção.");
                selector.IsDropDownOpen = false; selector.Focus(); PressEnter(selector);
                Require(next.IsKeyboardFocusWithin, "Enter não avançou após fechar a lista.");
            }
            finally { window.Close(); }
        });
        Check("Enter alcança a lista de horários antes de agendar", () =>
        {
            var professional = new ComboBox();
            var slots = new ListBox(); slots.Items.Add("09:00");
            var book = new Button();
            var clicks = 0; book.Click += (_, _) => clicks++;
            foreach (var item in new FrameworkElement[] { professional, slots, book }) EnterNavigation.SetGroup(item, "booking");
            var panel = new StackPanel(); panel.Children.Add(professional); panel.Children.Add(slots); panel.Children.Add(book);
            var window = new Window { Content = panel, Width = 420, Height = 300, ShowInTaskbar = false };
            try
            {
                window.Show(); window.Activate(); window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                professional.Focus(); PressEnter(professional);
                Require(slots.IsKeyboardFocusWithin && clicks == 0, "Enter pulou a lista de horários.");
                slots.SelectedIndex = 0; PressEnter(slots);
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Require(clicks == 1, "Enter no horário escolhido não acionou Agendar.");
            }
            finally { window.Close(); }
        });
    }

    private static bool PressEnter(UIElement element)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(element)!, Environment.TickCount, Key.Enter)
        { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        element.RaiseEvent(args);
        return args.Handled;
    }
}
