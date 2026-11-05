using System.Windows;
using Menage.App.Controles;
using Menage.Core.Interacao;

namespace Menage.App.Modulos.BetaAutomatica;

/// <summary>Só liga os botões à tela; as regras estão no Core.</summary>
public partial class JanelaBetaAutomatica : JanelaBase
{
    public const string Id = "beta-auto.painel";

    readonly BetaAutomaticaViewModel _vm;
    readonly IServicoDeJanelas _janelas;

    public JanelaBetaAutomatica(ContextoDeJanela contexto, BetaAutomaticaViewModel vm, IServicoDeJanelas janelas) : base(contexto)
    {
        (_vm, _janelas) = (vm, janelas);
        DataContext = vm;
        InitializeComponent();
        Loaded += (_, _) => _vm.Atualizar();
    }

    void AoAlternarMonitor(object sender, RoutedEventArgs e) => _vm.AlternarMonitor();

    void AoVerificarAgora(object sender, RoutedEventArgs e) => _vm.VerificarAgora();

    async void AoLimpar(object sender, RoutedEventArgs e) => await _vm.LimparListaAsync();

    void AoCancelarTodas(object sender, RoutedEventArgs e) => _vm.CancelarTodas();

    async void AoConfigurar(object sender, RoutedEventArgs e)
    {
        if (await _janelas.AbrirDialogoAsync(JanelaDeConfigBeta.Id, _vm.Servico) is true) _vm.Atualizar();
    }

    async void AoJql(object sender, RoutedEventArgs e) => await _janelas.AbrirDialogoAsync(JanelaDeJql.Id, _vm.Servico);

    void AoDiagnostico(object sender, RoutedEventArgs e) => _janelas.Abrir(JanelaDeDiagnostico.Id);

    void AoConsole(object sender, RoutedEventArgs e) => _janelas.Abrir(JanelaConsoleBeta.Id);
}
