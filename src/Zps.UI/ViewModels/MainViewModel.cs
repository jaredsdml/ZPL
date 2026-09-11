using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Zps.UI.Services;

namespace Zps.UI.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppServices _services;

    [ObservableProperty]
    private bool _temaOscuro;

    [ObservableProperty]
    private string _estadoConexion;

    public GeneradorPrincipalViewModel Generador { get; }

    public HistoricoViewModel Historico { get; }

    public MainViewModel(AppServices services)
    {
        _services = services;
        Generador = new GeneradorPrincipalViewModel(services);
        Historico = new HistoricoViewModel(services, Generador);

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
}
