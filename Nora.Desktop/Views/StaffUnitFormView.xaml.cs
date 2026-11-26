using System.Windows.Automation;
using System.Windows.Controls;
using Nora.Desktop.Models;

namespace Nora.Desktop.Views;

/// <summary>Escolha da UBS no pop-up de conceder ou retirar acesso (<see cref="StaffAccessView"/>).</summary>
public partial class StaffUnitFormView : UserControl
{
    public StaffUnitFormView(string account, string hint, string label, IReadOnlyList<HealthUnit> options)
    {
        InitializeComponent();
        AccountText.Text = account;
        HintText.Text = hint;
        LabelText.Text = label;
        AutomationProperties.SetName(UnitPicker, label);
        UnitPicker.ItemsSource = options;
        if (options.Count == 1) UnitPicker.SelectedIndex = 0;
    }

    /// <summary>A UBS escolhida (nulo enquanto a pessoa não escolheu).</summary>
    public HealthUnit? SelectedUnit => UnitPicker.SelectedItem as HealthUnit;
}
