using System.IO;
using CommunityToolkit.Mvvm.Messaging;
using Menage.App.Tema;
using Menage.Core.Armazenamento;

namespace Menage.App.Tests;

public sealed class ServicoDeTemaTests : IDisposable
{
    readonly string _pasta = Path.Combine(Path.GetTempPath(), "mgt" + Guid.NewGuid().ToString("N")[..6]);
    readonly AplicadorFalso _aplicador = new();
    readonly WeakReferenceMessenger _mensageiro = new();

    public void Dispose()
    {
        if (Directory.Exists(_pasta)) Directory.Delete(_pasta, true);
    }

    [Fact]
    public void Sem_arquivo_abre_no_escuro_sem_animar()
    {
        var servico = Criar();
        servico.Iniciar();

        servico.Atual.Id.Should().Be("escuro");
        _aplicador.Chamadas.Should().ContainSingle().Which.Should().Be(("escuro", false));
    }

    [Fact]
    public void Tema_salvo_volta_na_proxima_abertura()
    {
        var primeiro = Criar();
        primeiro.Iniciar();
        primeiro.Trocar("ameixa-claro");

        var segundo = Criar();
        segundo.Iniciar();
        segundo.Atual.Id.Should().Be("ameixa-claro");
    }

    [Fact]
    public void Ctrl_T_vai_e_volta_no_par_do_tema_atual()
    {
        var servico = Criar();
        servico.Iniciar();
        servico.Trocar("grafite-escuro");

        servico.AlternarPar();
        servico.Atual.Id.Should().Be("grafite-claro");
        servico.AlternarPar();
        servico.Atual.Id.Should().Be("grafite-escuro");
    }

    [Fact]
    public void Id_desconhecido_nao_muda_nada_nem_grava()
    {
        var servico = Criar();
        servico.Iniciar();
        servico.Trocar("nao-existe");

        servico.Atual.Id.Should().Be("escuro");
        File.Exists(Path.Combine(_pasta, "tema.json")).Should().BeFalse();
    }

    [Fact]
    public void Trocar_avisa_quem_escuta()
    {
        DefinicaoDeTema? recebido = null;
        var ouvinte = new object();
        _mensageiro.Register<TemaMudou>(ouvinte, (_, m) => recebido = m.Tema);
        var servico = Criar();
        servico.Iniciar();

        servico.Trocar("azul-claro");
        recebido!.Id.Should().Be("azul-claro");
    }

    ServicoDeTema Criar() => new(
        new CatalogoDeTemas(),
        new Armazenamento<PreferenciasDeTema>(new PastaDeDados(_pasta), "tema", padrao: () => new PreferenciasDeTema("escuro")),
        _aplicador, _mensageiro);

    sealed class AplicadorFalso : IAplicadorDeTema
    {
        public List<(string Id, bool Animar)> Chamadas { get; } = [];

        public void Aplicar(DefinicaoDeTema tema, bool animar) => Chamadas.Add((tema.Id, animar));
    }
}
