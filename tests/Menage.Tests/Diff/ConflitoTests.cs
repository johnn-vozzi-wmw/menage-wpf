using System.Text;

namespace Menage.Tests.Diff;

public sealed class ConflitoTests : IDisposable
{
    readonly RepositorioDescartavel _repo = RepositorioDescartavel.Criar();
    readonly ServicoConflito _servico = new(GitDeTeste.Criar());

    public void Dispose() => _repo.Dispose();

    void Gravar(string arquivo, byte[] bytes) => File.WriteAllBytes(Path.Combine(_repo.Caminho, arquivo), bytes);

    /// <summary>Deixa p.txt divergente: a branch "outra" e a main mudam a mesma linha.</summary>
    void Divergir(string arquivo = "p.txt", string fim = "\n")
    {
        Gravar(arquivo, Encoding.UTF8.GetBytes($"a{fim}b{fim}c{fim}"));
        _repo.Rodar("add", "--", arquivo);
        _repo.Rodar("commit", "-m", "base");
        _repo.Rodar("checkout", "-b", "outra");
        Gravar(arquivo, Encoding.UTF8.GetBytes($"a{fim}B OUTRA{fim}c{fim}"));
        _repo.Rodar("commit", "-am", "outra");
        _repo.Rodar("checkout", "main");
        Gravar(arquivo, Encoding.UTF8.GetBytes($"a{fim}B MAIN{fim}c{fim}"));
        _repo.Rodar("commit", "-am", "main");
    }

    void RodarEsperandoFalha(params string[] args)
    {
        var acao = () => _repo.Rodar(args);
        acao.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task Cherry_pick_nomeia_commit_e_branch()
    {
        Divergir();
        RodarEsperandoFalha("cherry-pick", "outra");

        var lados = await _servico.LerLadosAsync(_repo.Caminho);
        var lista = (await _servico.ListarAsync(_repo.Caminho)).Valor;

        lados.Operacao.Should().Be("cherry-pick");
        lados.DetalheEntrando.Should().Contain("outra");
        lados.DetalheAtual.Should().StartWith("main");
        lista.Should().ContainSingle().Which.CodigoStatus.Should().Be("UU");
    }

    [Fact]
    public async Task Merge_nomeia_o_que_entra()
    {
        Divergir();
        RodarEsperandoFalha("merge", "outra");

        var lados = await _servico.LerLadosAsync(_repo.Caminho);

        lados.Operacao.Should().Be("merge");
        lados.UsarEntrando.Should().Be("Usar do que entra");
    }

    [Fact]
    public async Task Rebase_inverte_os_lados()
    {
        Divergir();
        _repo.Rodar("checkout", "outra");
        RodarEsperandoFalha("rebase", "main");

        var lados = await _servico.LerLadosAsync(_repo.Caminho);

        lados.Operacao.Should().Be("rebase");
        lados.TituloAtual.Should().Be("Nova base");
        lados.DetalheEntrando.Should().Contain("outra");
    }

    [Fact]
    public async Task Revert_nomeia_a_reversao()
    {
        _repo.Commitar("p.txt", "a\nb\nc\n", "base");
        _repo.Commitar("p.txt", "a\nB1\nc\n", "muda");
        _repo.Commitar("p.txt", "a\nB2\nc\n", "muda de novo");
        RodarEsperandoFalha("revert", "--no-edit", "HEAD~1");

        var lados = await _servico.LerLadosAsync(_repo.Caminho);

        lados.Operacao.Should().Be("revert");
        lados.TituloEntrando.Should().Be("Reversão");
    }

    [Fact]
    public async Task Stash_nomeia_a_alteracao_entrando()
    {
        _repo.Commitar("p.txt", "a\nb\nc\n", "base");
        Gravar("p.txt", Encoding.UTF8.GetBytes("a\nSTASH\nc\n"));
        _repo.Rodar("stash", "push");
        _repo.Commitar("p.txt", "a\nMAIN\nc\n", "main");
        RodarEsperandoFalha("stash", "pop");

        (await _servico.LerLadosAsync(_repo.Caminho)).Operacao.Should().Be("stash");
    }

    [Fact]
    public async Task Carrega_a_juncao_com_uma_linha_de_aviso_por_conflito()
    {
        Divergir();
        RodarEsperandoFalha("merge", "outra");

        var c = (await _servico.CarregarAsync(_repo.Caminho, "p.txt")).Valor;

        c.Atual.Should().Equal("a", "B MAIN", "c");
        c.Entrando.Should().Equal("a", "B OUTRA", "c");
        c.Juncao.Conflitos.Should().ContainSingle();
        c.Juncao.Conflitos[0].Base.Should().Equal("b");
        c.Juncao.Resultado.Should().HaveCount(3);
        JuncaoConflito.Pendentes(c.Juncao.Resultado).Should().ContainKey(1).WhoseValue.Should().Be(1);
    }

    [Fact]
    public void Texto_com_marcador_comum_do_git_nao_e_confundido_com_conflito()
    {
        var abre = new string('<', JuncaoConflito.TamanhoDoMarcador) + " atual";
        var fecha = new string('>', JuncaoConflito.TamanhoDoMarcador) + " entrando";

        var j = JuncaoConflito.Ler(["<<<<<<< HEAD", abre, "x", new string('=', JuncaoConflito.TamanhoDoMarcador), "y", fecha, "fim"]);

        j.Conflitos.Should().ContainSingle();
        j.Resultado[0].Should().Be("<<<<<<< HEAD");
        j.Conflitos[0].Linhas(EscolhaConflito.AtualDepoisEntrando).Should().Equal("x", "y");
        j.Conflitos[0].Linhas(EscolhaConflito.EntrandoDepoisAtual).Should().Equal("y", "x");
    }

    [Fact]
    public async Task Nao_salva_com_conflito_pendente()
    {
        Divergir();
        RodarEsperandoFalha("merge", "outra");
        var c = (await _servico.CarregarAsync(_repo.Caminho, "p.txt")).Valor;

        var r = await _servico.GravarAsync(_repo.Caminho, c, c.Juncao.Resultado);

        r.EhOk.Should().BeFalse();
        r.Erro.Causa.Should().Contain("1 conflito");
    }

    [Fact]
    public async Task Salva_com_o_fim_de_linha_do_lado_atual_e_marca_resolvido()
    {
        Divergir(fim: "\r\n");
        RodarEsperandoFalha("merge", "outra");
        var c = (await _servico.CarregarAsync(_repo.Caminho, "p.txt")).Valor;

        var r = await _servico.GravarAsync(_repo.Caminho, c, ["a", "ESCOLHIDO", "c"]);

        r.EhOk.Should().BeTrue();
        File.ReadAllBytes(Path.Combine(_repo.Caminho, "p.txt")).Should().Equal(Encoding.UTF8.GetBytes("a\r\nESCOLHIDO\r\nc\r\n"));
        (await _servico.ListarAsync(_repo.Caminho)).Valor.Should().BeEmpty();
    }

    [Fact]
    public async Task Binario_em_conflito_fica_com_o_lado_escolhido()
    {
        Gravar("b.bin", [1, 0, 1]);
        _repo.Rodar("add", "--", "b.bin");
        _repo.Rodar("commit", "-m", "base");
        _repo.Rodar("checkout", "-b", "outra");
        Gravar("b.bin", [2, 0, 2]);
        _repo.Rodar("commit", "-am", "outra");
        _repo.Rodar("checkout", "main");
        Gravar("b.bin", [3, 0, 3]);
        _repo.Rodar("commit", "-am", "main");
        RodarEsperandoFalha("merge", "outra");
        var arquivo = (await _servico.ListarAsync(_repo.Caminho)).Valor.Single();

        (await _servico.ClassificarAsync(_repo.Caminho, arquivo)).Should().Be(FormaDeResolver.EscolherLado);
        (await _servico.ResolverAsync(_repo.Caminho, arquivo, LadoEscolhido.Entrando)).EhOk.Should().BeTrue();

        File.ReadAllBytes(Path.Combine(_repo.Caminho, "b.bin")).Should().Equal(2, 0, 2);
        (await _servico.ListarAsync(_repo.Caminho)).Valor.Should().BeEmpty();
    }

    [Theory]
    [InlineData(LadoEscolhido.Excluir, false)]
    [InlineData(LadoEscolhido.Manter, true)]
    public async Task Excluido_de_um_lado_pergunta_se_fica(LadoEscolhido lado, bool existeDepois)
    {
        _repo.Commitar("p.txt", "a\n", "base");
        _repo.Rodar("checkout", "-b", "outra");
        _repo.Rodar("rm", "p.txt");
        _repo.Rodar("commit", "-m", "remove");
        _repo.Rodar("checkout", "main");
        _repo.Commitar("p.txt", "a mudou\n", "muda");
        RodarEsperandoFalha("merge", "outra");
        var arquivo = (await _servico.ListarAsync(_repo.Caminho)).Valor.Single();

        (await _servico.ClassificarAsync(_repo.Caminho, arquivo)).Should().Be(FormaDeResolver.ManterOuExcluir);
        (await _servico.ResolverAsync(_repo.Caminho, arquivo, lado)).EhOk.Should().BeTrue();

        File.Exists(Path.Combine(_repo.Caminho, "p.txt")).Should().Be(existeDepois);
        (await _servico.ListarAsync(_repo.Caminho)).Valor.Should().BeEmpty();
    }

    [Fact]
    public void Analise_acha_o_bloco_de_cada_lado_e_as_ancoras_chegam_ao_fim()
    {
        string[] atual = ["a", "b1", "b2", "c", "d"];
        string[] entrando = ["a", "x", "c", "d", "e"];
        var marca = JuncaoConflito.TextoDaMarca(1, 1);
        string[] resultado = ["a", marca, "c", "d", "e"];

        var analise = AnaliseConflito.Analisar(atual, entrando, resultado);

        var p = analise.Pendentes.Single();
        p.LinhaResultado.Should().Be(1);
        (p.AtualIni, p.AtualLen).Should().Be((1, 2));
        (p.EntrandoIni, p.EntrandoLen).Should().Be((1, 1));
        analise.LinhasQueVieram.Should().Equal(4);
        analise.Ancoras[^1].Should().Equal(5, 5, 5);
        analise.Ancoras.Zip(analise.Ancoras.Skip(1)).Should().OnlyContain(q => q.First.Zip(q.Second).All(v => v.First <= v.Second));
    }
}
