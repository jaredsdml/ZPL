using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Zps.Data.Models;
using Zps.UI.ViewModels;

namespace Zps.UI.Views;

public partial class HistoricoView : UserControl
{
    public HistoricoView()
    {
        InitializeComponent();
    }

    private void DataGridHistorico_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is HistoricoViewModel vm)
        {
            vm.ActualizarSeleccion(DataGridHistorico.SelectedItems.Cast<HistoricoImpresionRecord>().ToList());
        }
    }

    /// <summary>Doble clic sobre una fila: selecciona solo esa fila y dispara "Editar y Reimprimir".</summary>
    private void DataGridHistorico_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not HistoricoViewModel vm || ObtenerRegistroBajoElMouse(e.OriginalSource) is not { } registro)
        {
            return;
        }

        DataGridHistorico.SelectedItem = registro;
        vm.ActualizarSeleccion(new[] { registro });

        if (vm.EditarYReimprimirCommand.CanExecute(null))
        {
            vm.EditarYReimprimirCommand.Execute(null);
        }
    }

    /// <summary>
    /// Clic derecho: WPF no cambia la selección por sí solo al abrir un menú contextual, así
    /// que se selecciona explícitamente la fila bajo el mouse antes de mostrar el menú, para
    /// que "Editar y Reimprimir" actúe siempre sobre la fila que el operador señaló.
    /// </summary>
    private void DataGridHistorico_OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not HistoricoViewModel vm || ObtenerRegistroBajoElMouse(e.OriginalSource) is not { } registro)
        {
            return;
        }

        DataGridHistorico.SelectedItem = registro;
        vm.ActualizarSeleccion(new[] { registro });
    }

    private static HistoricoImpresionRecord? ObtenerRegistroBajoElMouse(object originalSource)
    {
        var elemento = originalSource as DependencyObject;
        while (elemento is not null and not DataGridRow)
        {
            elemento = VisualTreeHelper.GetParent(elemento);
        }

        return (elemento as DataGridRow)?.Item as HistoricoImpresionRecord;
    }
}
