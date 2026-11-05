using Menage.Core.CherryPick;
using Menage.Tests.NucleoGit;

namespace Menage.Tests.CherryPick;

public sealed class FilaDeCherryPickTests : NucleoGitBase
{
    const string Script = "docs/modelo-er/scripts/script_lavendere_alteracoes_ATIV-1";

    readonly string _dados;
    ServicoDaFila _servico;

    public FilaDeCherryPickTests()
    {
        _dados = Path.Combine(Repo.Raiz, "dados");
        _servico = NovoServico();
    }

    ServicoDaFila NovoServico() => new(new Armazenamento<FilaDeCherryPick>(new PastaDeDados(_dados), CherryPickExtensions.ArquivoDaFila));

    RetiradorDeScript Retirador() =>
        new(GitExec, Consultas, new Armazenamento<ConfigDoScript>(new PastaDeDados(_dados), ConfigDoScript.Nome, padrao: () => new ConfigDoScript()));

    FilaDeCherryPickEmLote Fila() =>
        new(Projetos, Dialogos, Progresso, _servico, new PreparoDoProjeto(GitExec, Consultas), new AplicadorDaFila(GitExec, Consultas, Retirador()));

    ControleDaFila Controle() => new(_servico, GitExec, Consultas, Retirador());

    /// <summary>Cria a branch ativ a partir da develop com os commits dados (arquivo, conteúdo, mensagem) e volta para a main.</summary>
    List<ItemDaFila> Atividade(params (string Arquivo, string Conteudo, string Mensagem)[] commits)
    {
        Repo.Rodar("checkout", "develop");
        Repo.Rodar("checkout", "-b", "ativ");
        var itens = new List<ItemDaFila>();
        foreach (var (arquivo, conteudo, mensagem) in commits)
        {
            Repo.Commitar(arquivo, conteudo, mensagem);
            var hash = Repo.Rodar("rev-parse", "HEAD");
            itens.Add(new ItemDaFila { Projeto = Projeto.Nome, Caminho = Projeto.Caminho, Hash = hash, Curto = hash[..9], Assunto = mensagem });
        }

        Repo.Rodar("checkout", "main");
        return itens;
    }

    async Task<ResultadoDoProjeto<ResultadoDaFila>> Rodar(string destino = "develop", bool backup = true)
    {
        _servico.Configurar(destino, backup, atualizarDestino: true);
        return Unico(await Fila().ExecutarAsync(new OpcoesDaFila()));
    }

    [Fact]
    public async Task Aplica_em_sequencia_pula_o_que_ja_existe_no_destino_cria_backup_e_grava_a_fila()
    {
        var itens = Atividade(("a.txt", "a\n", "VENDAS-1 a"), ("b.txt", "b\n", "VENDAS-1 b"), ("c.txt", "c\n", "VENDAS-1 c"));
        Repo.Rodar("checkout", "develop");
        Repo.Rodar("cherry-pick", itens[0].Hash);
        Repo.Rodar("push", "origin", "develop");
        Repo.Rodar("checkout", "main");
        _servico.Adicionar(itens);

        var resultado = await Rodar();

        resultado.Situacao.Should().Be(Situacao.Ok, resultado.Mensagem);
        itens.Select(i => i.Estado).Should().Equal(EstadoDoItem.Pulado, EstadoDoItem.Aplicado, EstadoDoItem.Aplicado);
        Repo.Rodar("log", "-2", "--format=%B", "develop").Should().Contain($"cherry picked from commit {itens[1].Hash}").And.Contain(itens[2].Hash);
        Repo.Rodar("branch", "--list", "backup/cherry-develop-*").Should().NotBeEmpty();
        Repo.Rodar("ls-remote", "origin", "refs/heads/develop").Should().NotContain(Repo.Rodar("rev-parse", "develop"));
        var gravada = NovoServico().Fila;
        gravada.Estado.Should().Be(EstadoDaFila.Concluida);
        gravada.Itens.Select(i => i.Estado).Should().Equal(EstadoDoItem.Pulado, EstadoDoItem.Aplicado, EstadoDoItem.Aplicado);
    }

    [Fact]
    public async Task Conflito_pausa_a_fila_deixa_o_git_em_andamento_e_retomar_confere_antes_de_continuar()
    {
        Repo.Rodar("checkout", "develop");
        Repo.Commitar("x.txt", "base\n", "base x");
        Repo.Rodar("push", "origin", "develop");
        Repo.Rodar("checkout", "-b", "ativ");
        Repo.Commitar("x.txt", "ativ\n", "VENDAS-2 muda x");
        var conflitante = Repo.Rodar("rev-parse", "HEAD");
        Repo.Commitar("y.txt", "y\n", "VENDAS-2 y");
        var seguinte = Repo.Rodar("rev-parse", "HEAD");
        Repo.Rodar("checkout", "develop");
        Repo.Commitar("x.txt", "develop\n", "develop muda x");
        Repo.Rodar("push", "origin", "develop");
        Repo.Rodar("checkout", "main");
        var itens = new[] { conflitante, seguinte }.Select(h => new ItemDaFila { Projeto = Projeto.Nome, Caminho = Projeto.Caminho, Hash = h, Curto = h[..9], Assunto = h }).ToList();
        _servico.Adicionar(itens);

        var resultado = await Rodar();

        resultado.Situacao.Should().Be(Situacao.Erro);
        _servico.Fila.Estado.Should().Be(EstadoDaFila.Pausada);
        itens[0].Estado.Should().Be(EstadoDoItem.Conflito);
        itens[0].Arquivos.Should().Equal("x.txt");
        itens[1].Estado.Should().Be(EstadoDoItem.Pendente);
        (await Consultas.RefVerificaAsync(Repo.Caminho, "CHERRY_PICK_HEAD")).Should().BeTrue();

        (await Controle().RetomarAsync()).Resultado.Should().Be(Retomada.AindaConflito);

        Escrever("x.txt", "<<<<<<< HEAD\nx\n=======\ny\n>>>>>>> ativ\n");
        Repo.Rodar("add", "x.txt");
        (await Controle().RetomarAsync()).Resultado.Should().Be(Retomada.Marcadores);

        Escrever("x.txt", "resolvido\n");
        Repo.Rodar("add", "x.txt");
        (await Controle().RetomarAsync()).Resultado.Should().Be(Retomada.Pronto);
        itens[0].Estado.Should().Be(EstadoDoItem.Aplicado);

        (await Rodar()).Situacao.Should().Be(Situacao.Ok);
        itens[1].Estado.Should().Be(EstadoDoItem.Aplicado);
        _servico.Fila.Estado.Should().Be(EstadoDaFila.Concluida);
        File.ReadAllText(Arquivo("x.txt")).Should().Be("resolvido\n");
    }

    [Fact]
    public async Task Abortar_encerra_o_cherry_pick_e_devolve_cada_projeto_ao_head_de_antes()
    {
        Repo.Rodar("checkout", "develop");
        Repo.Commitar("x.txt", "base\n", "base x");
        Repo.Rodar("push", "origin", "develop");
        var antes = Repo.Rodar("rev-parse", "develop");
        Repo.Rodar("checkout", "-b", "ativ");
        Repo.Commitar("ok.txt", "ok\n", "VENDAS-3 ok");
        var bom = Repo.Rodar("rev-parse", "HEAD");
        Repo.Commitar("x.txt", "ativ\n", "VENDAS-3 muda x");
        var ruim = Repo.Rodar("rev-parse", "HEAD");
        Repo.Rodar("checkout", "develop");
        Repo.Commitar("x.txt", "develop\n", "develop muda x");
        Repo.Rodar("push", "origin", "develop");
        antes = Repo.Rodar("rev-parse", "develop");
        Repo.Rodar("checkout", "main");
        var itens = new[] { bom, ruim }.Select(h => new ItemDaFila { Projeto = Projeto.Nome, Caminho = Projeto.Caminho, Hash = h, Curto = h[..9], Assunto = h }).ToList();
        _servico.Adicionar(itens);
        await Rodar();
        itens.Select(i => i.Estado).Should().Equal(EstadoDoItem.Aplicado, EstadoDoItem.Conflito);

        var relatorio = await Controle().AbortarAsync(restaurar: true);

        relatorio.Should().ContainSingle().Which.Should().Contain("voltou ao HEAD inicial");
        Repo.Rodar("rev-parse", "develop").Should().Be(antes);
        (await Consultas.RefVerificaAsync(Repo.Caminho, "CHERRY_PICK_HEAD")).Should().BeFalse();
        itens.Select(i => i.Estado).Should().Equal(EstadoDoItem.Desfeito, EstadoDoItem.Pendente);
        _servico.Fila.Estado.Should().Be(EstadoDaFila.Abortada);
    }

    [Fact]
    public async Task Script_da_atividade_fica_fora_da_develop_e_o_commit_que_so_tinha_o_script_nao_e_criado()
    {
        var itens = Atividade((Script, "alter table a;\n", "VENDAS-1 so script"), ("codigo.txt", "x\n", "VENDAS-1 codigo"));
        Repo.Rodar("checkout", "ativ");
        File.WriteAllText(Arquivo(Script), "alter table a;\nalter table b;\n");
        Repo.Commitar("outro.txt", "o\n", "VENDAS-1 codigo e script");
        Repo.Rodar("add", Script);
        Repo.Rodar("commit", "--amend", "--no-edit");
        var misto = Repo.Rodar("rev-parse", "HEAD");
        Repo.Rodar("checkout", "main");
        itens.Add(new ItemDaFila { Projeto = Projeto.Nome, Caminho = Projeto.Caminho, Hash = misto, Curto = misto[..9], Assunto = "misto" });
        _servico.Adicionar(itens);

        await Rodar();

        itens.Select(i => i.Estado).Should().Equal(EstadoDoItem.Pulado, EstadoDoItem.Aplicado, EstadoDoItem.Aplicado);
        itens[0].Detalhe.Should().Contain("só tinha o script");
        Repo.Rodar("ls-tree", "-r", "--name-only", "develop").Should().NotContain("script_lavendere").And.Contain("codigo.txt").And.Contain("outro.txt");
        Repo.Rodar("log", "-1", "--format=%B", "develop").Should().Contain("VENDAS-1 codigo e script").And.Contain($"cherry picked from commit {misto}");
        Repo.Rodar("ls-tree", "-r", "--name-only", "ativ").Should().Contain("script_lavendere");
    }

    [Fact]
    public async Task Fora_da_lista_de_destinos_o_script_vai_junto()
    {
        var itens = Atividade((Script, "alter table a;\n", "VENDAS-1 script"));
        Repo.Rodar("checkout", "main");
        _servico.Adicionar(itens);

        await Rodar(destino: "feature/exemplo");

        itens[0].Estado.Should().Be(EstadoDoItem.Aplicado);
        Repo.Rodar("ls-tree", "-r", "--name-only", "feature/exemplo").Should().Contain("script_lavendere");
    }

    [Fact]
    public async Task Conflito_so_no_script_se_resolve_sozinho_e_o_script_continua_fora()
    {
        var itens = Atividade((Script, "v1\n", "VENDAS-1 script"), (Script, "v2\n", "VENDAS-1 script e codigo"));
        Repo.Rodar("checkout", "ativ");
        File.WriteAllText(Arquivo("codigo.txt"), "x\n");
        Repo.Rodar("add", "codigo.txt");
        Repo.Rodar("commit", "--amend", "--no-edit");
        itens[1].Hash = Repo.Rodar("rev-parse", "HEAD");
        Repo.Rodar("checkout", "main");
        itens.RemoveAt(0);
        _servico.Adicionar(itens);

        await Rodar();

        itens[0].Estado.Should().Be(EstadoDoItem.Aplicado, itens[0].Detalhe);
        (await Consultas.RefVerificaAsync(Repo.Caminho, "CHERRY_PICK_HEAD")).Should().BeFalse();
        Repo.Rodar("ls-tree", "-r", "--name-only", "develop").Should().NotContain("script_lavendere").And.Contain("codigo.txt");
    }

    [Fact]
    public void Reordenar_e_remover_so_valem_para_pendentes_e_a_lista_e_gravada()
    {
        var a = new ItemDaFila { Caminho = "c", Hash = "1" };
        var b = new ItemDaFila { Caminho = "c", Hash = "2" };
        var c = new ItemDaFila { Caminho = "c", Hash = "3", Estado = EstadoDoItem.Aplicado };
        _servico.Adicionar([a, b, c]).Should().Be(3);
        _servico.Adicionar([new ItemDaFila { Caminho = "c", Hash = "1" }]).Should().Be(0);

        _servico.Mover(b, -1).Should().BeTrue();
        _servico.Remover(c).Should().BeFalse();

        _servico.Fila.Itens.Select(i => i.Hash).Should().Equal("2", "1", "3");
        NovoServico().Fila.Itens.Select(i => i.Hash).Should().Equal("2", "1", "3");
        _servico = NovoServico();
        _servico.Remover(_servico.Fila.Itens[0]).Should().BeTrue();
        _servico.TemFilaPendente.Should().BeTrue();
    }

    [Theory]
    [InlineData("develop", "develop", true)]
    [InlineData("release/1", "release*", true)]
    [InlineData("feature/x", "develop", false)]
    public void Destino_aceita_curinga(string branch, string lista, bool vale) =>
        new ConfigDoScript { Destinos = [lista] }.DestinoVale(branch).Should().Be(vale);
}
