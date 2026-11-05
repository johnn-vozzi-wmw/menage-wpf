using Menage.Core.Personalizacao;

namespace Menage.Tests.PersonalizacaoHistorico;

public sealed class PersonalizacaoTests : IDisposable
{
    readonly string _pasta = Path.Combine(Path.GetTempPath(), "mgp" + Guid.NewGuid().ToString("N")[..6]);

    public void Dispose()
    {
        if (Directory.Exists(_pasta)) Directory.Delete(_pasta, true);
    }

    ServicoDePreferencias Criar() =>
        new(new Armazenamento<PreferenciasDeAparencia>(new PastaDeDados(_pasta), PreferenciasDeAparencia.Nome, padrao: () => new PreferenciasDeAparencia()));

    [Theory]
    [InlineData("ctrl+shift+t", "Ctrl+Shift+T")]
    [InlineData("F5", "F5")]
    [InlineData("alt+ctrl+,", "Ctrl+Alt+,")]
    [InlineData("Control+esc", "Ctrl+Esc")]
    public void Gesto_valido_vira_texto_canonico(string entrada, string esperado) =>
        Gesto.Ler(entrada).Valor.ToString().Should().Be(esperado);

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+")]
    [InlineData("T")]
    [InlineData("Shift+Space")]
    [InlineData("Ctrl+A+B")]
    [InlineData("Ctrl+F13")]
    public void Gesto_invalido_e_recusado(string entrada) =>
        Gesto.Ler(entrada).EhOk.Should().BeFalse();

    [Fact]
    public void Tecla_de_funcao_vale_sozinha_mas_letra_nao()
    {
        Gesto.Ler("F7").EhOk.Should().BeTrue();
        Gesto.Ler("K").EhOk.Should().BeFalse();
    }

    static MapaDeAtalhos Mapa(Dictionary<string, string>? trocas = null) =>
        new([("tema.alternar", "Ctrl+T"), ("paleta.abrir", "ctrl+k"), ("janela.atualizar", "F5"), ("janela.saida", null)], trocas);

    [Fact]
    public void Padrao_do_catalogo_vale_ate_o_usuario_trocar()
    {
        var mapa = Mapa();
        mapa.Efetivo("tema.alternar").Should().Be("Ctrl+T");
        mapa.Efetivo("paleta.abrir").Should().Be("Ctrl+K");
        mapa.Efetivo("janela.saida").Should().BeNull();
        mapa.ComandoDe(Gesto.Ler("F5").Valor).Should().Be("janela.atualizar");
    }

    [Fact]
    public void Troca_libera_o_gesto_antigo_e_ocupa_o_novo()
    {
        var mapa = Mapa();
        mapa.Definir("tema.alternar", "Ctrl+Shift+T").EhOk.Should().BeTrue();

        mapa.Efetivo("tema.alternar").Should().Be("Ctrl+Shift+T");
        mapa.ComandoDe(Gesto.Ler("Ctrl+T").Valor).Should().BeNull();
        mapa.Trocas.Should().ContainKey("tema.alternar");
    }

    [Fact]
    public void Gesto_em_uso_e_recusado_e_nada_muda()
    {
        var mapa = Mapa();
        var r = mapa.Definir("janela.saida", "ctrl+k");

        r.EhOk.Should().BeFalse();
        r.Erro.Causa.Should().Contain("paleta.abrir");
        mapa.Efetivo("janela.saida").Should().BeNull();
        mapa.Trocas.Should().BeEmpty();
    }

    [Fact]
    public void Repetir_o_proprio_gesto_nao_e_conflito() =>
        Mapa().Definir("tema.alternar", "Ctrl+T").EhOk.Should().BeTrue();

    [Fact]
    public void Remover_deixa_sem_atalho_e_restaurar_traz_o_padrao()
    {
        var mapa = Mapa();
        mapa.Definir("janela.atualizar", "");
        mapa.Efetivo("janela.atualizar").Should().BeNull();
        mapa.Personalizado("janela.atualizar").Should().BeTrue();

        mapa.Restaurar("janela.atualizar");
        mapa.Efetivo("janela.atualizar").Should().Be("F5");
        mapa.Definir("janela.atualizar", "F6").EhOk.Should().BeTrue();
        mapa.RestaurarTodos();
        mapa.Trocas.Should().BeEmpty();
    }

    [Fact]
    public void Gesto_liberado_por_um_comando_pode_ir_para_outro()
    {
        var mapa = Mapa();
        mapa.Definir("janela.atualizar", "").EhOk.Should().BeTrue();
        mapa.Definir("janela.saida", "F5").EhOk.Should().BeTrue();
        mapa.ComandoDe(Gesto.Ler("F5").Valor).Should().Be("janela.saida");
    }

    [Fact]
    public void Comando_desconhecido_e_recusado() =>
        Mapa().Definir("nao.existe", "Ctrl+9").EhOk.Should().BeFalse();

    [Fact]
    public void Preferencias_voltam_na_proxima_abertura_com_enums_legiveis()
    {
        Criar().Alterar(p => p with
        {
            Modo = ModoDeTema.Sistema, CorDeDestaque = "#7C3AED", Densidade = Densidade.Compacta,
            Fonte = TamanhoDeFonte.Grande, Barra = ModeloDaBarra.B, ReduzirMovimento = true,
            Atalhos = new Dictionary<string, string> { ["tema.alternar"] = "Ctrl+Shift+T" },
        }).EhOk.Should().BeTrue();

        File.ReadAllText(Path.Combine(_pasta, "aparencia.json")).Should().Contain("Sistema").And.Contain("Compacta").And.Contain("Grande");

        var lido = Criar().Valor;
        lido.Modo.Should().Be(ModoDeTema.Sistema);
        lido.CorDeDestaque.Should().Be("#7C3AED");
        lido.Densidade.Should().Be(Densidade.Compacta);
        lido.Fonte.Should().Be(TamanhoDeFonte.Grande);
        lido.Barra.Should().Be(ModeloDaBarra.B);
        lido.ReduzirMovimento.Should().BeTrue();
        lido.Atalhos["tema.alternar"].Should().Be("Ctrl+Shift+T");
    }

    [Fact]
    public void Sem_arquivo_vale_o_padrao_e_o_modo_fica_sem_escolha()
    {
        var p = Criar().Valor;
        p.Modo.Should().BeNull();
        p.CorDeDestaque.Should().BeNull();
        p.Densidade.Should().Be(Densidade.Confortavel);
        p.Fonte.Should().Be(TamanhoDeFonte.Normal);
        p.Barra.Should().Be(ModeloDaBarra.A);
        p.ReduzirMovimento.Should().BeFalse();
    }

    [Fact]
    public void Alterar_avisa_quem_escuta_e_nao_avisa_sem_mudanca()
    {
        var servico = Criar();
        var avisos = 0;
        servico.Alterado += _ => avisos++;

        servico.Alterar(p => p with { Densidade = Densidade.Compacta });
        servico.Alterar(p => p with { Densidade = Densidade.Compacta });

        avisos.Should().Be(1);
    }

    [Fact]
    public void Arquivo_corrompido_cai_no_padrao_sem_derrubar()
    {
        Directory.CreateDirectory(_pasta);
        File.WriteAllText(Path.Combine(_pasta, "aparencia.json"), "{ isto nao e json");
        Criar().Valor.Densidade.Should().Be(Densidade.Confortavel);
    }

    [Theory]
    [InlineData(TamanhoDeFonte.Pequeno, 14, 12.5)]
    [InlineData(TamanhoDeFonte.Normal, 14, 14)]
    [InlineData(TamanhoDeFonte.Grande, 14, 16)]
    [InlineData(TamanhoDeFonte.Grande, 12, 14)]
    public void Fonte_escala_em_meio_ponto(TamanhoDeFonte tamanho, double basePx, double esperado) =>
        MedidasDeAparencia.Fonte(basePx, tamanho).Should().Be(esperado);

    [Fact]
    public void Densidade_compacta_reduz_controle_margem_e_linha()
    {
        MedidasDeAparencia.AlturaDoControle(Densidade.Compacta).Should().BeLessThan(MedidasDeAparencia.AlturaDoControle(Densidade.Confortavel));
        MedidasDeAparencia.MargemDoCartao(Densidade.Compacta).Should().BeLessThan(MedidasDeAparencia.MargemDoCartao(Densidade.Confortavel));
        MedidasDeAparencia.AlturaDaLinha(Densidade.Compacta, TamanhoDeFonte.Grande)
            .Should().BeGreaterThan(MedidasDeAparencia.AlturaDaLinha(Densidade.Compacta, TamanhoDeFonte.Normal));
    }

    [Theory]
    [InlineData("#7c3aed", "#7C3AED")]
    [InlineData("7c3aed", "#7C3AED")]
    [InlineData("#abc", "#AABBCC")]
    [InlineData("  #FF0000 ", "#FF0000")]
    [InlineData("#12345", null)]
    [InlineData("vermelho", null)]
    [InlineData(null, null)]
    public void Cor_de_destaque_e_normalizada_ou_recusada(string? entrada, string? esperado) =>
        MedidasDeAparencia.NormalizarCor(entrada).Should().Be(esperado);

    [Fact]
    public void Hover_clareia_cor_escura_e_escurece_cor_clara()
    {
        MedidasDeAparencia.Hover((0, 60, 160)).B.Should().BeGreaterThan(160);
        MedidasDeAparencia.Hover((250, 220, 120)).R.Should().BeLessThan(250);
    }

    [Theory]
    [InlineData(ModoDeTema.Claro, true, true, false)]
    [InlineData(ModoDeTema.Escuro, false, false, true)]
    [InlineData(ModoDeTema.Sistema, true, false, true)]
    [InlineData(ModoDeTema.Sistema, false, true, false)]
    [InlineData(null, true, false, false)]
    [InlineData(null, false, true, true)]
    public void Modo_decide_o_lado_do_par_de_temas(ModoDeTema? modo, bool sistemaEscuro, bool temaSalvoEscuro, bool esperado) =>
        MedidasDeAparencia.UsaEscuro(modo, sistemaEscuro, temaSalvoEscuro).Should().Be(esperado);
}
