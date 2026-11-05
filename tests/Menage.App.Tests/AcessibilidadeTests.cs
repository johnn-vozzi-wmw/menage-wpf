using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Menage.App.Animacoes;
using Menage.App.Controles;
using Menage.App.Tema;

namespace Menage.App.Tests;

[Collection("Movimento")]
public sealed class AcessibilidadeTests
{
    [Fact]
    public void Pilula_compacta_tem_nome_para_leitor_de_tela()
    {
        WpfDeTeste.Executar(() =>
        {
            var pilula = new Pilula { Texto = "Fetch", Compacta = true };
            AutomationProperties.GetName(pilula).Should().Be("Fetch");
        });
    }

    [Fact]
    public void Campo_de_busca_tem_nome()
    {
        WpfDeTeste.Executar(() => AutomationProperties.GetName(new CampoDeBusca()).Should().Be("Buscar"));
    }

    [Fact]
    public void Enter_e_espaco_repetem_o_clique_do_mouse_e_outras_teclas_nao()
    {
        WpfDeTeste.Executar(() =>
        {
            var cartao = new Border();
            var cliques = 0;
            cartao.MouseLeftButtonUp += (_, _) => cliques++;
            AcessoPorTeclado.SetAtivar(cartao, true);

            cartao.Focusable.Should().BeTrue();
            foreach (var tecla in new[] { Key.Enter, Key.Space, Key.A })
                cartao.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, new FakeSource(), 0, tecla) { RoutedEvent = Keyboard.KeyDownEvent, Source = cartao });

            cliques.Should().Be(2);
        });
    }

    [Fact]
    public void Reduzir_movimento_zera_a_duracao_das_animacoes_de_deslocamento()
    {
        WpfDeTeste.Executar(() =>
        {
            var servico = new ServicoDeTransicao();
            try
            {
                servico.ReduzirMovimento = true;
                Animacao.Reduzir.Should().BeTrue();
                Animacao.Movimento(Animacao.Media).TimeSpan.Should().Be(TimeSpan.Zero);

                servico.ReduzirMovimento = false;
                if (SystemParameters.ClientAreaAnimation) Animacao.Movimento(Animacao.Media).Should().Be(Animacao.Media);
            }
            finally
            {
                servico.ReduzirMovimento = false;
            }
        });
    }

    sealed class FakeSource : PresentationSource
    {
        protected override System.Windows.Media.CompositionTarget GetCompositionTargetCore() => throw new NotSupportedException();

        public override System.Windows.Media.Visual RootVisual { get; set; } = new System.Windows.Media.DrawingVisual();

        public override bool IsDisposed => false;
    }
}

public sealed class SeloDoPerfilTests : IDisposable
{
    readonly string _pasta = Path.Combine(Path.GetTempPath(), "mg-selo-" + Guid.NewGuid().ToString("N")[..6]);

    public void Dispose()
    {
        if (Directory.Exists(_pasta)) Directory.Delete(_pasta, recursive: true);
    }

    [Fact]
    public void Legenda_so_cita_o_perfil_quando_nao_e_o_geral()
    {
        var perfis = new Menage.Core.Perfis.ServicoDePerfis(new Menage.Core.Armazenamento.PastaDeDados(_pasta));
        Menage.App.Modulos.JanelaPrincipal.BarraLateralViewModel.LegendaDaMarca(perfis).Should().Be("Projetos WMW");

        perfis.Ativar("tester").EhOk.Should().BeTrue();

        Menage.App.Modulos.JanelaPrincipal.BarraLateralViewModel.LegendaDaMarca(perfis).Should().Be("Projetos WMW · Tester");
    }
}
