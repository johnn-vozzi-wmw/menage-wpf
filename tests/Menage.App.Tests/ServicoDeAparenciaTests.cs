using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Messaging;
using Menage.App.Animacoes;
using Menage.App.Modulos.Personalizacao;
using Menage.App.Tema;
using Menage.Core.Armazenamento;
using Menage.Core.Personalizacao;

namespace Menage.App.Tests;

[Collection("Movimento")]
public sealed class ServicoDeAparenciaTests : IDisposable
{
    readonly string _pasta = Path.Combine(Path.GetTempPath(), "mga" + Guid.NewGuid().ToString("N")[..6]);
    readonly WeakReferenceMessenger _mensageiro = new();
    readonly AplicadorFalso _aplicador = new();
    readonly MedidasFalsas _medidas = new();
    readonly DetectorFalso _sistema = new();
    readonly ServicoDeTransicao _transicao = new();
    readonly ServicoDePreferencias _preferencias;
    readonly ServicoDeTema _tema;
    readonly ServicoDeAparencia _aparencia;

    public ServicoDeAparenciaTests()
    {
        var pasta = new PastaDeDados(_pasta);
        var destaque = new AjusteDeDestaque();
        _preferencias = new ServicoDePreferencias(new Armazenamento<PreferenciasDeAparencia>(pasta, "aparencia", padrao: () => new PreferenciasDeAparencia()));
        var catalogo = new CatalogoDeTemas();
        _tema = new ServicoDeTema(catalogo,
            new Armazenamento<PreferenciasDeTema>(pasta, "tema", padrao: () => new PreferenciasDeTema("escuro")), _aplicador, _mensageiro, destaque);
        _aparencia = new ServicoDeAparencia(_preferencias, _tema, catalogo, destaque, _transicao, _sistema, _medidas, _mensageiro);
    }

    public void Dispose()
    {
        if (Directory.Exists(_pasta)) Directory.Delete(_pasta, true);
    }

    void Iniciar()
    {
        _tema.Iniciar();
        _aparencia.Iniciar();
    }

    [Fact]
    public void Sem_escolha_de_modo_o_tema_salvo_vale_como_sempre()
    {
        _tema.Iniciar();
        _tema.Trocar("ameixa-claro");
        _aparencia.Iniciar();

        _tema.Atual.Id.Should().Be("ameixa-claro");
        _preferencias.Valor.Modo.Should().BeNull();
    }

    [Fact]
    public void Modo_escuro_leva_um_tema_claro_para_o_par_escuro_ao_iniciar()
    {
        _tema.Iniciar();
        _tema.Trocar("grafite-claro");
        _preferencias.Alterar(p => p with { Modo = ModoDeTema.Escuro });

        _aparencia.Iniciar();

        _tema.Atual.Id.Should().Be("grafite-escuro");
    }

    [Fact]
    public void Mudar_o_modo_troca_o_lado_ao_vivo_e_mantem_a_familia()
    {
        Iniciar();
        _tema.Trocar("azul-escuro");

        _aparencia.Alterar(p => p with { Modo = ModoDeTema.Claro });
        _tema.Atual.Id.Should().Be("azul-claro");
        _aplicador.Ultima.Should().Be(("azul-claro", true));

        _aparencia.Alterar(p => p with { Modo = ModoDeTema.Escuro });
        _tema.Atual.Id.Should().Be("azul-escuro");
    }

    [Fact]
    public void Modo_sistema_segue_o_windows_agora_e_quando_ele_muda()
    {
        _sistema.Escuro = false;
        Iniciar();
        _aparencia.Alterar(p => p with { Modo = ModoDeTema.Sistema });
        _tema.Atual.Escuro.Should().BeFalse();

        _sistema.Escuro = true;
        _sistema.Disparar();
        _tema.Atual.Escuro.Should().BeTrue();

        _sistema.Escuro = false;
        _sistema.Disparar();
        _tema.Atual.Escuro.Should().BeFalse();
    }

    [Fact]
    public void Modo_claro_ignora_o_windows()
    {
        _sistema.Escuro = true;
        Iniciar();
        _aparencia.Alterar(p => p with { Modo = ModoDeTema.Claro });

        _sistema.Disparar();
        _tema.Atual.Escuro.Should().BeFalse();
    }

    [Fact]
    public void Ctrl_T_com_modo_escolhido_passa_a_ser_o_novo_modo()
    {
        Iniciar();
        _aparencia.Alterar(p => p with { Modo = ModoDeTema.Claro });

        _tema.AlternarPar();

        _tema.Atual.Escuro.Should().BeTrue();
        _preferencias.Valor.Modo.Should().Be(ModoDeTema.Escuro);
    }

    [Fact]
    public void Cor_de_destaque_vale_ao_vivo_em_qualquer_tema_e_a_do_tema_volta_ao_limpar()
    {
        Iniciar();
        var original = _tema.Atual[PapelDeCor.Destaque];

        _aparencia.Alterar(p => p with { CorDeDestaque = "#7C3AED" });
        _tema.Atual[PapelDeCor.Destaque].Should().Be(Color.FromRgb(0x7C, 0x3A, 0xED));
        _tema.Atual[PapelDeCor.DestaqueHover].Should().NotBe(_tema.Atual[PapelDeCor.Destaque]);
        _tema.Atual.Id.Should().Be("escuro");

        _tema.Trocar("esmeralda-claro");
        _tema.Atual[PapelDeCor.Destaque].Should().Be(Color.FromRgb(0x7C, 0x3A, 0xED), "o destaque do usuário vale para todos os temas");

        _aparencia.Alterar(p => p with { CorDeDestaque = null });
        _tema.Trocar("escuro");
        _tema.Atual[PapelDeCor.Destaque].Should().Be(original);
    }

    [Fact]
    public void Cor_invalida_gravada_nao_derruba_e_fica_com_a_do_tema()
    {
        Iniciar();
        var original = _tema.Atual[PapelDeCor.Destaque];
        _aparencia.Alterar(p => p with { CorDeDestaque = "xyz" });
        _tema.Atual[PapelDeCor.Destaque].Should().Be(original);
    }

    [Fact]
    public void Destaque_salvo_vale_na_proxima_abertura_sem_animar()
    {
        Iniciar();
        _aparencia.Alterar(p => p with { CorDeDestaque = "#D6336C" });

        var outro = new ServicoDeAparenciaTests();
        try
        {
            Directory.CreateDirectory(outro.PastaDeDados);
            File.Copy(Path.Combine(_pasta, "aparencia.json"), Path.Combine(outro.PastaDeDados, "aparencia.json"));
            outro.Iniciar();
            outro._tema.Atual[PapelDeCor.Destaque].Should().Be(Color.FromRgb(0xD6, 0x33, 0x6C));
            outro._aplicador.Ultima.Animar.Should().BeFalse();
        }
        finally
        {
            outro.Dispose();
        }
    }

    string PastaDeDados => _pasta;

    [Fact]
    public void Reduzir_movimento_chega_ao_servico_de_transicao()
    {
        Iniciar();
        _transicao.ReduzirMovimento.Should().BeFalse();

        _aparencia.Alterar(p => p with { ReduzirMovimento = true });
        _transicao.ReduzirMovimento.Should().BeTrue();

        _aparencia.Alterar(p => p with { ReduzirMovimento = false });
        _transicao.ReduzirMovimento.Should().BeFalse();
    }

    [Fact]
    public void Fonte_e_densidade_vao_para_os_recursos_na_hora()
    {
        Iniciar();
        _medidas.Chamadas.Should().HaveCount(1, "a aparência salva é aplicada na abertura");

        _aparencia.Alterar(p => p with { Fonte = TamanhoDeFonte.Grande, Densidade = Densidade.Compacta });

        _medidas.Ultima.Fonte.Should().Be(TamanhoDeFonte.Grande);
        _medidas.Ultima.Densidade.Should().Be(Densidade.Compacta);
    }

    [Fact]
    public void Quem_escuta_recebe_aviso_de_cada_mudanca()
    {
        Iniciar();
        PreferenciasDeAparencia? recebido = null;
        _mensageiro.Register<AparenciaMudou>(this, (_, m) => recebido = m.Preferencias);

        _aparencia.Alterar(p => p with { Barra = ModeloDaBarra.B });

        recebido!.Barra.Should().Be(ModeloDaBarra.B);
    }

    [Fact]
    public void Reaplicar_nao_grava_o_tema()
    {
        Iniciar();
        _aparencia.Alterar(p => p with { CorDeDestaque = "#00A878" });
        File.Exists(Path.Combine(_pasta, "tema.json")).Should().BeFalse();
    }

    sealed class AplicadorFalso : IAplicadorDeTema
    {
        public (string Id, bool Animar) Ultima { get; private set; }

        public void Aplicar(DefinicaoDeTema tema, bool animar) => Ultima = (tema.Id, animar);
    }

    sealed class MedidasFalsas : IAplicadorDeMedidas
    {
        public List<PreferenciasDeAparencia> Chamadas { get; } = [];

        public PreferenciasDeAparencia Ultima => Chamadas[^1];

        public void Aplicar(PreferenciasDeAparencia preferencias) => Chamadas.Add(preferencias);
    }

    sealed class DetectorFalso : IDetectorDoSistema
    {
        public bool Escuro { get; set; } = true;

        public event Action? Mudou;

        public void Disparar() => Mudou?.Invoke();
    }
}
