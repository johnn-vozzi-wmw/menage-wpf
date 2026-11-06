using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Menage.App.Controles;

namespace Menage.App.Modulos.BetaAutomatica;

/// <summary>
/// Janela "Betas habilitadas": abas Betas, Configurar, JQL e Diagnóstico (uma tela por aba, com transição) e o console ancorado embaixo.
/// Os comandos e atalhos abrem esta janela já na aba certa (<see cref="PedidoDeAba"/>).
/// </summary>
public partial class JanelaBetaAutomatica : JanelaBase, IJanelaComParametro
{
    public const string Id = "beta-auto.painel";

    readonly BetaAutomaticaViewModel _vm;
    readonly UserControl[] _telas;

    public JanelaBetaAutomatica(ContextoDeJanela contexto, BetaAutomaticaViewModel vm) : base(contexto)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();
        TelaDeBetas = new AbaBetas { DataContext = vm };
        _telas =
        [
            TelaDeBetas, new AbaConfigurar { DataContext = vm.Configurar }, new AbaJql { DataContext = vm.Jql }, new AbaDiagnostico { DataContext = vm.Diagnostico },
        ];
        vm.PropertyChanged += AoMudarVm;
        Closed += (_, _) => vm.PropertyChanged -= AoMudarVm;
        InputBindings.Add(new KeyBinding(new RelayCommand(Dock.FocarBusca), Key.F, ModifierKeys.Control));
        Hospede.Indice = vm.Posicao;
        Hospede.Destino = _telas[vm.Posicao];
        Loaded += (_, _) => _vm.Atualizar();
    }

    /// <summary>A aba Betas (a galeria abre as gavetas para capturar).</summary>
    public AbaBetas TelaDeBetas { get; }

    /// <summary>Aceita um <see cref="PedidoDeAba"/>, uma aba ou a chave de uma atividade (alerta de "pronta para gerar").</summary>
    public void Receber(object? parametro)
    {
        switch (parametro)
        {
            case PedidoDeAba pedido: _vm.Abrir(pedido); break;
            case AbaDaBeta aba: _vm.Abrir(new PedidoDeAba(aba)); break;
            case string chave when chave.Length > 0: _vm.Abrir(new PedidoDeAba(AbaDaBeta.Betas, chave)); break;
        }
    }

    void AoMudarVm(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BetaAutomaticaViewModel.AbaAtual)) return;
        Hospede.Indice = _vm.Posicao;
        Hospede.Destino = _telas[_vm.Posicao];
    }
}
