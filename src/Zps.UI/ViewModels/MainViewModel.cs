using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zps.UI.Services;
using Zps.UI.Views;

namespace Zps.UI.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppServices _services;

    [ObservableProperty]
    private bool _temaOscuro;

    [ObservableProperty]
    private string _estadoConexion;

    /// <summary>Índice de pestaña activa del TabControl (0 = Generador Principal, 1 = Histórico).</summary>
    [ObservableProperty]
    private int _pestanaSeleccionada;

    public GeneradorPrincipalViewModel Generador { get; }

    public HistoricoViewModel Historico { get; }

    public MainViewModel(AppServices services)
    {
        _services = services;
        Generador = new GeneradorPrincipalViewModel(services);
        Historico = new HistoricoViewModel(services, Generador);

        // "Reimprimir Selección" en Histórico ya no imprime directo: carga los registros en
        // el Generador Principal y pide cambiar a esa pestaña para que el operador continúe ahí.
        Historico.SolicitoCambiarAGeneradorPrincipal += () => PestanaSeleccionada = 0;

        _estadoConexion = services.NeonDisponible
            ? "🟢 Conectado a Neon"
            : $"🟠 Modo offline (Neon no disponible: {services.ErrorConfiguracionNeon})";
    }

    public async Task InicializarAsync()
    {
        await Generador.CargarDatosInicialesAsync();
    }

    [RelayCommand]
    private void AlternarTema()
    {
        TemaOscuro = !TemaOscuro;
        ThemeManager.Aplicar(TemaOscuro);
    }

    /// <summary>
    /// Botón de engrane en la barra superior: pide la contraseña maestra de administración
    /// antes de abrir la administración de plantillas ZPL (plantillas_zpl), para que dar de
    /// alta o modificar el ZPL de un cliente en vivo no quede al alcance de cualquier
    /// operador de piso.
    /// </summary>
    [RelayCommand]
    private void AbrirAdministracionPlantillas()
    {
        var dialogo = new ConfirmacionPasswordWindow(
            titulo: "Acceso de administración",
            mensaje: "Esta sección permite crear, editar y eliminar las plantillas ZPL que usa el Generador Principal en producción. Ingresa la contraseña de administración para continuar.",
            textoBotonConfirmar: "Ingresar")
        {
            Owner = Application.Current.MainWindow
        };

        if (dialogo.ShowDialog() != true)
        {
            return;
        }

        var ventana = new AdminPlantillasWindow(new AdminPlantillasViewModel(_services))
        {
            Owner = Application.Current.MainWindow
        };
        ventana.Show();
    }
}
