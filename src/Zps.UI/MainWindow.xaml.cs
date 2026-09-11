using System.Windows;
using System.Windows.Controls;
using Zps.UI.ViewModels;

namespace Zps.UI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void TabControl_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source is not TabControl || DataContext is not MainViewModel vm)
        {
            return;
        }

        if (TabHistorico.IsSelected)
        {
            vm.Historico.SincronizarCommand.Execute(null);
        }
    }
}
