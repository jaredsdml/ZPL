using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Zps.UI.ViewModels;

namespace Zps.UI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // La versión se toma del ensamblado (Zps.UI.csproj -> <Version>), no de un texto
        // quemado, para que el título y el subtítulo siempre reflejen el build real.
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        var version = v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        Title = $"Zebra Print Station - ZPS v{version}";
        TxtVersion.Text = $"ZPS v{version}";
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
