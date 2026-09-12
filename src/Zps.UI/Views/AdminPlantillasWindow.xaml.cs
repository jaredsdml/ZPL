using System.Windows;
using Zps.UI.ViewModels;

namespace Zps.UI.Views;

/// <summary>
/// Ventana de administración de plantillas ZPL (plantillas_zpl), accesible desde el botón de
/// engrane en la barra superior, protegida con la contraseña maestra de administración.
/// </summary>
public partial class AdminPlantillasWindow : Window
{
    public AdminPlantillasWindow(AdminPlantillasViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
