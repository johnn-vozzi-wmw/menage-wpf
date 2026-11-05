using Menage.Core.Beta;
using Menage.Core.BetaAutomatica;
using Menage.TestKit;

namespace Menage.Tests.BetaAutomatica;

sealed class JiraFalso : IClienteJira, IFabricaDeJira
{
    public List<IssueJira> Issues { get; } = [];

    public List<string> Consultas { get; } = [];

    public bool TemToken { get; set; } = true;

    public string NomeDaVariavel => "JIRA_TOKEN";

    public string Endereco => "http://jira.falso";

    public Task<string> UsuarioAsync(CancellationToken cancelar = default) => Task.FromResult("eu");

    public Task<IReadOnlyList<IssueJira>> BuscarCompletasAsync(string jql, int maximo, CancellationToken cancelar = default)
    {
        Consultas.Add(jql);
        IReadOnlyList<IssueJira> r = jql.StartsWith("key in", StringComparison.Ordinal) ? [] : [.. Issues];
        return Task.FromResult(r);
    }

    public IClienteJira Criar(ConfigBetaAuto config) => this;
}

/// <summary>Tudo em %TEMP% com caminho curto: repositório descartável (projeto de trabalho), pasta de dados, JDK falso e Maven falso.</summary>
sealed class CenarioDaBeta : IDisposable
{
    public const string Projeto = "lavenderecore";

    public RepositorioDescartavel Repo { get; } = RepositorioDescartavel.Criar();

    public MavenFalso Maven { get; }

    public string Raiz { get; } = Path.Combine(Path.GetTempPath(), "ba" + Guid.NewGuid().ToString("N")[..6]);

    public JiraFalso Jira { get; } = new();

    public ServicoDeBetaAutomatica Servico { get; }

    public ConsoleDaBeta Console { get; }

    public CenarioDaBeta(int codigoDoMaven = 0, PerfilDeUso perfil = PerfilDeUso.Desenvolvedor)
    {
        Directory.CreateDirectory(Raiz);
        Maven = MavenFalso.Criar(codigoDoMaven, "BUILD SUCCESS");
        var jdk = Directory.CreateDirectory(Path.Combine(Raiz, "jdk", "bin")).Parent!.FullName;
        File.WriteAllText(Path.Combine(jdk, "bin", "java.exe"), "");

        // a branch da atividade tem pom.xml com versão -SNAPSHOT (para a tag de complemento)
        Repo.Rodar("checkout", "feature/exemplo");
        Repo.Commitar("pom.xml", "<project><version>9.060-SNAPSHOT</version></project>", "pom");
        Repo.Rodar("push", "origin", "feature/exemplo");
        Repo.Rodar("checkout", "main");

        var dados = new PastaDeDados(Path.Combine(Raiz, "dados"));
        var git = GitDeTeste.Criar();
        var consultas = new ConsultasGit(git);
        var projetos = new ProjetosEmMemoria([new Projeto(Projeto, Repo.Caminho)]);
        var gerador = new RepositorioDoGerador(new Armazenamento<ConfiguracaoDoGerador>(dados, "gerador-beta", padrao: () => Gerador(jdk)));
        var perfilAtual = new PerfilFixo(perfil);
        Console = new ConsoleDaBeta(dados);
        var armazenamento = new Armazenamento<ConfigBetaAuto>(dados, "beta-automatica", padrao: () => new ConfigBetaAuto { PastaTrabalho = Path.Combine(Raiz, "clones") });
        Servico = new ServicoDeBetaAutomatica(
            armazenamento, Jira, new VerificadorDeBranches(git, consultas), projetos, gerador, perfilAtual, Console, dados,
            new PreparadorDeClone(git, Console), new PreparadorDeGeracao(gerador, projetos, consultas, perfilAtual, dados), new ProcessoRunner());
        Servico.Alterar(c => c.PastaTrabalho = Path.Combine(Raiz, "clones"));
    }

    ConfiguracaoDoGerador Gerador(string jdk) => new()
    {
        MavenBin = Maven.Pasta,
        Java8Home = jdk,
        Jdks = new() { ["17"] = jdk, ["8"] = jdk },
        Projetos = [new ProjetoDoGerador { Nome = Projeto, Grupo = "Web", Jdk = "17", Goals = "clean {modo}", Pastas = [Projeto] }],
    };

    public IssueJira Atividade(string chave, string comentario, DateTime passagem, string status = "Waiting for Testing")
    {
        var i = new IssueJira { Chave = chave, Titulo = "Titulo " + chave, Tipo = "Story", Status = status, Responsavel = "eu" };
        i.Comentarios.Add(new ComentarioJira("1", "eu", comentario, passagem));
        i.Historico.Add(new MudancaJira(passagem, "status", "", "", "In Code Review", status, "x"));
        Jira.Issues.Add(i);
        return i;
    }

    public void Dispose()
    {
        Servico.Dispose();
        Maven.Dispose();
        Repo.Dispose();
        if (!Directory.Exists(Raiz)) return;
        foreach (var f in Directory.EnumerateFiles(Raiz, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(Raiz, recursive: true);
    }
}

public sealed class CicloDaBetaAutomaticaTests
{
    [Fact]
    public async Task Primeira_consulta_grava_a_referencia_e_traz_o_que_ja_estava_sem_alerta()
    {
        using var c = new CenarioDaBeta();
        var alertas = new List<ItemBeta>();
        c.Servico.ItemPronto += alertas.Add;
        c.Atividade("VENDAS-1", "BRANCH: feature/exemplo\nPROJETOS ALTERADOS: lavenderecore", DateTime.Now.AddDays(-1));

        var novas = await c.Servico.CicloAsync(false);

        novas.Should().Be(1);
        alertas.Should().BeEmpty("já estava no status antes do monitor");
        c.Servico.Config.Baseline.Should().NotBeEmpty();
        var item = c.Servico.Achar("VENDAS-1")!;
        item.Estado.Should().Be(EstadoDoItem.Pronta);
        item.Branch.Should().Be("feature/exemplo");
        item.Projetos.Single(p => p.Nome == CenarioDaBeta.Projeto).Situacao.Should().Be(SituacaoDaBranch.Local);
    }

    [Fact]
    public async Task Atividade_nova_depois_da_referencia_alerta_uma_vez_e_nao_repete()
    {
        using var c = new CenarioDaBeta();
        var alertas = new List<ItemBeta>();
        c.Servico.ItemPronto += alertas.Add;
        await c.Servico.CicloAsync(false);

        c.Atividade("VENDAS-2", "BRANCH: feature/exemplo", DateTime.Now.AddMinutes(2));
        (await c.Servico.CicloAsync(false)).Should().Be(1);
        (await c.Servico.CicloAsync(false)).Should().Be(0, "sem nova passagem a atividade já foi tratada");

        alertas.Should().ContainSingle().Which.Chave.Should().Be("VENDAS-2");
    }

    [Fact]
    public async Task Branch_inexistente_fica_precisa_de_atencao()
    {
        using var c = new CenarioDaBeta();
        c.Atividade("VENDAS-3", "BRANCH: nao/existe", DateTime.Now.AddDays(-1));

        await c.Servico.CicloAsync(false);

        var item = c.Servico.Achar("VENDAS-3")!;
        item.Estado.Should().Be(EstadoDoItem.Problema);
        item.Erro.Should().Contain("não existe em nenhum");
    }

    [Fact]
    public async Task Sem_comentario_vale_o_nome_da_atividade_e_sem_a_branch_precisa_de_atencao()
    {
        using var c = new CenarioDaBeta();
        var i = c.Atividade("VENDAS-4", "so um oi", DateTime.Now.AddDays(-1));
        i.Comentarios.Clear();
        i.EhSubtask = true;
        i.Tipo = "Coding";

        await c.Servico.CicloAsync(false);

        var item = c.Servico.Achar("VENDAS-4")!;
        item.Branch.Should().Be("VENDAS-4");
        item.Estado.Should().Be(EstadoDoItem.Problema);
    }

    [Fact]
    public async Task Reentrada_reabre_uma_ignorada_como_nova()
    {
        using var c = new CenarioDaBeta();
        var issue = c.Atividade("VENDAS-5", "BRANCH: feature/exemplo", DateTime.Now.AddDays(-1));
        await c.Servico.CicloAsync(false);
        c.Servico.Ignorar("VENDAS-5");
        c.Servico.Achar("VENDAS-5")!.Estado.Should().Be(EstadoDoItem.Ignorada);

        await c.Servico.CicloAsync(false);
        c.Servico.Achar("VENDAS-5")!.Estado.Should().Be(EstadoDoItem.Ignorada, "sem nova passagem continua ignorada");

        issue.Historico.Add(new MudancaJira(DateTime.Now.AddMinutes(5), "status", "", "", "In Testing", "Waiting for Testing", "x"));
        (await c.Servico.CicloAsync(false)).Should().Be(1);

        c.Servico.Achar("VENDAS-5")!.Estado.Should().Be(EstadoDoItem.Pronta);
    }

    [Fact]
    public async Task Ignorar_e_reverter_voltam_ao_estado_de_antes_e_remover_traz_de_volta_na_proxima()
    {
        using var c = new CenarioDaBeta();
        c.Atividade("VENDAS-6", "BRANCH: feature/exemplo", DateTime.Now.AddDays(-1));
        await c.Servico.CicloAsync(false);

        c.Servico.Ignorar("VENDAS-6");
        c.Servico.Ignoradas().Should().ContainSingle();
        await c.Servico.ReverterIgnoradaAsync("VENDAS-6");
        c.Servico.Achar("VENDAS-6")!.Estado.Should().Be(EstadoDoItem.Pronta);

        c.Servico.Remover("VENDAS-6").Should().Be(1);
        c.Servico.Achar("VENDAS-6").Should().BeNull();
        (await c.Servico.CicloAsync(false)).Should().Be(1, "a passagem removida volta na próxima consulta");
    }

    [Fact]
    public async Task Atividade_que_foi_para_o_status_final_e_finalizada_sozinha_e_nao_volta_na_limpeza()
    {
        using var c = new CenarioDaBeta();
        c.Atividade("VENDAS-7", "BRANCH: feature/exemplo", DateTime.Now.AddDays(-1));
        await c.Servico.CicloAsync(false);
        c.Jira.Issues.Clear();
        c.Jira.Issues.Add(new IssueJira { Chave = "VENDAS-7", Status = "Waiting for Product Increment" });

        // a busca de "fora da lista" usa key in (...): o falso devolve vazio, então simulamos pelo método público
        var motivo = c.Servico.MotivoDeFinalizacao(c.Jira.Issues[0]);

        motivo.Should().Contain("Waiting for Product Increment");
        c.Servico.Finalizar("VENDAS-7").Should().BeTrue();
        c.Servico.Limpar().Should().Be(0, "as finalizadas ficam");
        c.Servico.Finalizadas().Should().ContainSingle();
        await c.Servico.ReabrirFinalizadaAsync("VENDAS-7");
        c.Servico.Achar("VENDAS-7")!.Estado.Should().Be(EstadoDoItem.Pronta);
    }

    [Fact]
    public async Task Sem_token_o_ciclo_avisa_e_nao_consulta()
    {
        using var c = new CenarioDaBeta();
        c.Jira.TemToken = false;

        (await c.Servico.CicloAsync(false)).Should().Be(0);

        c.Servico.Config.UltimoErro.Should().Contain("JIRA_TOKEN");
        c.Jira.Consultas.Should().BeEmpty();
    }

    [Fact]
    public async Task Tester_usa_a_consulta_do_tester()
    {
        using var c = new CenarioDaBeta(perfil: PerfilDeUso.Tester);
        c.Atividade("VENDAS-8", "BRANCH: feature/exemplo", DateTime.Now.AddDays(-1));

        await c.Servico.CicloAsync(false);

        c.Jira.Consultas.Should().ContainSingle().Which.Should().Contain("assignee = currentUser()");
        c.Servico.Achar("VENDAS-8").Should().NotBeNull();
    }

    [Fact]
    public async Task Diagnostico_so_le_e_nao_altera_nada()
    {
        using var c = new CenarioDaBeta();
        c.Atividade("VENDAS-9", "BRANCH: feature/exemplo", DateTime.Now.AddDays(-1));

        var texto = await c.Servico.DiagnosticarAsync();

        texto.Should().Contain("VENDAS-9").And.Contain("DECISÃO: NOVA").And.Contain("[ok] lavenderecore");
        c.Servico.Itens().Should().BeEmpty();
        c.Servico.Config.Baseline.Should().BeEmpty();
    }

    [Fact]
    public async Task Complemento_web_entra_pela_tag_da_versao_do_pom_da_branch()
    {
        using var c = new CenarioDaBeta();
        c.Atividade("VENDAS-10", "BRANCH: feature/exemplo\nPROJETOS ALTERADOS: lavenderecore", DateTime.Now.AddDays(-1));
        await c.Servico.CicloAsync(false);
        var item = c.Servico.Achar("VENDAS-10")!;

        // lavenderedados e lavendereweb não existem neste computador de teste: nenhum complemento é criado
        await c.Servico.CompletarWebAsync(item);

        item.Projetos.Where(p => p.Complemento).Should().BeEmpty();
        VerificadorDeBranches.VersaoDoPom("<project><parent><version>1</version></parent><version>9.060-SNAPSHOT</version></project>").Should().Be("9.060-SNAPSHOT");
        VerificadorDeBranches.VersaoDoPom("<project><parent><version>1.2</version></parent></project>").Should().Be("1.2");
    }
}

public sealed class GeracaoDaBetaAutomaticaTests
{
    static async Task<ItemBeta> Aguardar(ServicoDeBetaAutomatica servico, string chave)
    {
        var fim = DateTime.Now.AddSeconds(90);
        while (DateTime.Now < fim)
        {
            var item = servico.Achar(chave)!;
            if (item.Estado is EstadoDoItem.Concluida or EstadoDoItem.Falhou) return item;
            await Task.Delay(200);
        }

        throw new TimeoutException("a geração não terminou");
    }

    [Fact]
    public async Task Gera_em_clone_isolado_com_maven_falso_install_e_nao_toca_no_projeto_de_trabalho()
    {
        using var c = new CenarioDaBeta();
        c.Atividade("VENDAS-20", "BRANCH: feature/exemplo\nPROJETOS ALTERADOS: lavenderecore", DateTime.Now.AddDays(-1));
        await c.Servico.CicloAsync(false);
        var antes = c.Repo.BranchAtual;
        var concluidas = new List<(ItemBeta, bool)>();
        c.Servico.GeracaoConcluida += (i, ok) => concluidas.Add((i, ok));

        c.Servico.Gerar("VENDAS-20");
        var item = await Aguardar(c.Servico, "VENDAS-20");

        item.Estado.Should().Be(EstadoDoItem.Concluida, item.Erro);
        item.Projetos.Single(p => p.Nome == CenarioDaBeta.Projeto).Resultado.Should().Be("ok");
        var clone = Path.Combine(c.Servico.Config.PastaTrabalho, CenarioDaBeta.Projeto);
        File.Exists(Path.Combine(clone, "pom.xml")).Should().BeTrue("o clone isolado tem a branch da atividade");
        c.Repo.BranchAtual.Should().Be(antes, "o projeto de trabalho não é tocado");
        c.Maven.Chamadas.Should().ContainSingle().Which.Should().Contain("install").And.NotContain("deploy");
        Directory.Exists(item.PastaLogs).Should().BeTrue();
        concluidas.Should().ContainSingle().Which.Item2.Should().BeTrue();
        c.Console.Registros.Should().Contain(r => r.Texto.Contains("clone criado"));
    }

    [Fact]
    public async Task Falha_do_maven_marca_o_projeto_e_a_atividade_como_falhou()
    {
        using var c = new CenarioDaBeta(codigoDoMaven: 1);
        c.Atividade("VENDAS-21", "BRANCH: feature/exemplo", DateTime.Now.AddDays(-1));
        await c.Servico.CicloAsync(false);

        c.Servico.Gerar("VENDAS-21");
        var item = await Aguardar(c.Servico, "VENDAS-21");

        item.Estado.Should().Be(EstadoDoItem.Falhou);
        item.Erro.Should().Contain(CenarioDaBeta.Projeto);
        item.Projetos.Single(p => p.Nome == CenarioDaBeta.Projeto).Resultado.Should().Be("falhou");
    }

    [Fact]
    public async Task So_gera_atividade_pronta_e_presas_voltam_para_pronta_ao_abrir()
    {
        using var c = new CenarioDaBeta();
        c.Atividade("VENDAS-22", "BRANCH: nao/existe", DateTime.Now.AddDays(-1));
        await c.Servico.CicloAsync(false);

        c.Servico.Gerar("VENDAS-22");
        c.Servico.Achar("VENDAS-22")!.Estado.Should().Be(EstadoDoItem.Problema, "só Prontas entram na fila");

        c.Servico.Achar("VENDAS-22")!.Estado = EstadoDoItem.Gerando;
        c.Servico.DestravarPresas().Should().Be(1);
        c.Servico.Achar("VENDAS-22")!.Estado.Should().Be(EstadoDoItem.Pronta);
    }
}
