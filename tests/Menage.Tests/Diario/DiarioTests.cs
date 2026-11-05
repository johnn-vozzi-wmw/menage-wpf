using System.Net;
using System.Text;
using Menage.Core.Armazenamento;
using Menage.Core.Diario;
using Menage.Core.Melhorias;
using Menage.TestKit;

namespace Menage.Tests.Diario;

public sealed class VariaveisFalsas(params (string Nome, string Valor)[] valores) : IVariaveisDeAmbiente
{
    readonly Dictionary<string, string> _v = valores.ToDictionary(x => x.Nome, x => x.Valor);

    public string Ler(string nome) => _v.GetValueOrDefault(nome, "");
}

/// <summary>Servidor HTTP de mentira: nada sai da máquina, nada chega ao Jira real.</summary>
public sealed class ServidorFalso(Func<HttpRequestMessage, string, (HttpStatusCode, string)> responder) : HttpMessageHandler
{
    public List<(HttpMethod Metodo, string Url, string Corpo, string? Autorizacao)> Chamadas { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var corpo = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Chamadas.Add((request.Method, request.RequestUri!.ToString(), corpo, request.Headers.Authorization?.ToString()));
        var (status, texto) = responder(request, corpo);
        return new HttpResponseMessage(status) { Content = new StringContent(texto, Encoding.UTF8, "application/json") };
    }
}

public sealed class ReconhecimentoETempoTests
{
    [Theory]
    [InlineData("VENDAS-123 corrige pedido", "VENDAS", "VENDAS-123")]
    [InlineData("fix(vendas-9): ajuste", "VENDAS", "VENDAS-9")]
    [InlineData("feature/VENDAS-245741-teste", "VENDAS", "VENDAS-245741")]
    [InlineData("WMW-5 outro projeto", "VENDAS", "")]
    [InlineData("WMW-5 outro projeto", "", "WMW-5")]
    [InlineData("XVENDAS-1", "VENDAS", "")]
    public void Issue_vem_da_mensagem_ou_da_branch_pelas_chaves_configuradas(string texto, string chaves, string esperado) =>
        ReconhecedorDeIssue.IssueDe(texto, chaves).Should().Be(esperado);

    static CommitDoDia Commit(string issue, int linhas, int arquivos, int hora, string assunto = "ajuste")
    {
        var c = new CommitDoDia { Projeto = "p", Hash = $"h{hora}{linhas}", Assunto = assunto, Quando = DateTime.Today.AddHours(hora), Issue = issue, Linhas = linhas };
        for (var i = 0; i < arquivos; i++) c.Arquivos.Add($"src/A{i}.java");
        return c;
    }

    [Fact]
    public void Jornada_proporcional_fecha_exatamente_8h48_e_respeita_o_minimo()
    {
        var cfg = new ConfiguracaoDoDiario();
        var r = DistribuidorDeJornada.Distribuir(DateTime.Today, [Commit("VENDAS-1", 300, 5, 9), Commit("VENDAS-2", 2, 1, 10), Commit("VENDAS-3", 40, 2, 11)], cfg);

        r.Modo.Should().Be("proporcional");
        r.Total.Should().Be(528);
        r.Blocos.Should().OnlyContain(b => b.Minutos > 0 && b.Minutos <= 240);
        r.Blocos.Where(b => b.Issue == "VENDAS-2").Sum(b => b.Minutos).Should().BeGreaterThanOrEqualTo(15);
        r.Blocos.First().Issue.Should().Be("VENDAS-1", "a issue de maior atividade vem primeiro");
    }

    [Fact]
    public void Blocos_em_sequencia_com_uma_hora_de_almoco_depois_das_primeiras_4h()
    {
        var r = DistribuidorDeJornada.Distribuir(DateTime.Today, [Commit("VENDAS-1", 100, 3, 9)], new ConfiguracaoDoDiario());

        r.Blocos.Select(b => (b.Inicio.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture), b.Minutos)).ToList().Should().Equal(("09:00", 240), ("14:00", 240), ("18:00", 48));
        r.Blocos[1].Comentario.Should().Contain("continuação");
    }

    [Fact]
    public void Sem_commits_usa_blocos_padrao_e_commit_sem_issue_vira_aviso()
    {
        var vazio = DistribuidorDeJornada.Distribuir(DateTime.Today, [], new ConfiguracaoDoDiario(), "VENDAS-9");
        vazio.Modo.Should().Be("blocos");
        vazio.Total.Should().Be(528);
        vazio.Motivo.Should().Contain("Nenhum commit");

        var sem = DistribuidorDeJornada.Distribuir(DateTime.Today, [Commit("", 10, 1, 9), Commit("VENDAS-1", 10, 1, 10)], new ConfiguracaoDoDiario());
        sem.Avisos.Should().ContainSingle(a => a.Contains("sem id de issue"));
        sem.Blocos.Should().OnlyContain(b => b.Issue == "VENDAS-1");
    }

    [Fact]
    public void Um_worklog_por_commit_divide_a_jornada_na_ordem_dos_commits()
    {
        var cfg = new ConfiguracaoDoDiario { WorklogPorCommit = true };
        var commits = new[] { Commit("VENDAS-1", 200, 4, 11, "segundo"), Commit("VENDAS-1", 20, 1, 9, "primeiro"), Commit("VENDAS-2", 60, 2, 10, "meio") };

        var r = DistribuidorDeJornada.Distribuir(DateTime.Today, commits, cfg);

        r.Modo.Should().Be("por-commit");
        r.Total.Should().Be(528);
        r.Blocos.Select(b => b.Hash).Should().StartWith(commits[1].Hash);
        r.Blocos.Select(b => b.Inicio).Should().BeInAscendingOrder();
        r.Blocos[0].Comentario.Should().Contain("Primeiro").And.NotContain("Segundo");
    }

    [Fact]
    public void Resumo_por_regras_limpa_a_mensagem_e_so_usa_fatos_dos_commits()
    {
        var g = new GrupoDeIssue { Issue = "VENDAS-1", Commits = [Commit("VENDAS-1", 10, 1, 9, "VENDAS-1: fix - corrige desconto")] };
        g.Commits[0].Simbolos = ["calculaDesconto"];

        var resumo = ResumoDoDiario.PorRegras(g);

        resumo.Should().Contain("Fix - corrige desconto").And.Contain("calculaDesconto").And.Contain("A0.java").And.Contain("Projetos: p.");
        resumo.Should().NotContain("VENDAS-1");
    }
}

public sealed class AnaliseDeDiffTests
{
    [Theory]
    [InlineData("conf/context.xml", true)]
    [InlineData("src/SenhaService.java", true)]
    [InlineData("src/PedidoService.java", false)]
    public void Arquivos_sensiveis_tem_o_conteudo_omitido(string arquivo, bool sensivel) => AnaliseDeDiff.Sensivel(arquivo).Should().Be(sensivel);

    [Fact]
    public void Mascara_senha_token_bearer_e_chaves_mas_preserva_nomes_de_metodo()
    {
        AnaliseDeDiff.Mascarar("password=abc123 e token: xyz").Should().Be("password=*** e token: ***");
        AnaliseDeDiff.Mascarar("Authorization: Bearer abcdefgh12345").Should().NotContain("abcdefgh12345");
        AnaliseDeDiff.Mascarar("AKIAIOSFODNN7EXAMPLE1234567890ABCDEF12").Should().Be("***");
        AnaliseDeDiff.Mascarar("calculaDescontoPorFaixaDeQuantidadeDoPedido").Should().Be("calculaDescontoPorFaixaDeQuantidadeDoPedido");
    }

    const string Patch = """
        diff --git a/src/PedidoService.java b/src/PedidoService.java
        index 111..222 100644
        --- a/src/PedidoService.java
        +++ b/src/PedidoService.java
        @@ -10,3 +10,6 @@ public class PedidoService {
         contexto
        +    public BigDecimal calculaDesconto(int qtd) {
        +        String senha = "segredo123";
        +    }
        -    velho();
        diff --git a/conf/context.xml b/conf/context.xml
        --- a/conf/context.xml
        +++ b/conf/context.xml
        @@ -1 +1 @@
        -<Resource password="x"/>
        +<Resource password="y"/>
        diff --git a/db/V1.sql b/db/V1.sql
        @@ -0,0 +1 @@
        +create table PEDIDO_ITEM (id int);
        """;

    [Fact]
    public void Processar_detecta_simbolos_mascara_segredos_e_omite_arquivos_sensiveis()
    {
        var c = new CommitDoDia();

        AnaliseDeDiff.Processar(Patch, c);

        c.Simbolos.Should().Contain(["PedidoService", "calculaDesconto", "PEDIDO_ITEM"]);
        c.DiffTexto.Should().Contain("### src/PedidoService.java (+3/-1)").And.Contain("senha = \"***\"".Replace("\"***\"", "\"***\""));
        c.DiffTexto.Should().NotContain("segredo123").And.NotContain("password=\"y\"");
        c.DiffTexto.Should().Contain("### conf/context.xml (+1/-1)  [conteúdo omitido]");
    }

    [Fact]
    public void Resumo_que_cita_nome_inexistente_e_apontado_e_a_frase_removida()
    {
        const string baseTexto = "Mensagens: PedidoService calculaDesconto";
        var resumo = "O PedidoService passou a usar calculaDesconto. Também foi criado o FantasmaService.java para isso.";

        var ruins = AnaliseDeDiff.NaoSustentados(resumo, baseTexto);

        ruins.Should().Contain("FantasmaService.java");
        var (limpo, removidas) = AnaliseDeDiff.RemoverFrases(resumo, ruins);
        (limpo, removidas).Should().Be(("O PedidoService passou a usar calculaDesconto.", 1));
    }
}

/// <summary>Repositório descartável em %TEMP%; o Jira e a IA são servidores falsos.</summary>
public sealed class ServicoDoDiarioTests : IDisposable
{
    const string Autor = "teste@menage.local";

    readonly RepositorioDescartavel _repo = RepositorioDescartavel.Criar();
    readonly string _dados = Path.Combine(Path.GetTempPath(), "dd" + Guid.NewGuid().ToString("N")[..6]);

    public void Dispose()
    {
        _repo.Dispose();
        if (Directory.Exists(_dados)) Directory.Delete(_dados, recursive: true);
    }

    ServicoDoDiario Criar(ServidorFalso servidor, params (string, string)[] ambiente) => Criar(servidor, null, ambiente);

    ServicoDoDiario Criar(ServidorFalso servidor, RepositorioDeMelhorias? melhorias, params (string, string)[] ambiente)
    {
        var git = GitDeTeste.Criar();
        var repositorio = new RepositorioDoDiario(new Armazenamento<ConfiguracaoDoDiario>(new PastaDeDados(_dados), ConfiguracaoDoDiario.NomeDoArquivo, padrao: ConfiguracaoDoDiario.Padrao));
        repositorio.Salvar(repositorio.Configuracao with { Autor = Autor, JiraUrl = "https://jira.exemplo.local", Ia = new ConfiguracaoDaIa { Provedor = "groq" } });
        var clientes = new FabricaDeClientes(new HttpClient(servidor), new VariaveisFalsas(ambiente));
        return new ServicoDoDiario(new ProjetosEmMemoria([_repo.ComoProjeto("web")]), new ColetorDoDiario(git), new LeitorDeDiffs(git), repositorio, clientes, melhorias);
    }

    void Commitar()
    {
        _repo.Commitar("a.txt", "um\n", "VENDAS-10 primeira correção");
        _repo.Rodar("checkout", "-b", "feature/VENDAS-77-ajuste");
        _repo.Commitar("b.txt", "dois\n", "ajuste sem id na mensagem");
        _repo.Rodar("checkout", "main");
        _repo.Commitar("c.txt", "tres\n", "sem issue nenhuma");
    }

    static ServidorFalso JiraQueAceita(Func<string>? aoLancar = null) => new((req, _) => req.RequestUri!.AbsolutePath switch
    {
        "/rest/api/2/myself" => (HttpStatusCode.OK, """{"name":"eu"}"""),
        var p when p.EndsWith("/worklog", StringComparison.Ordinal) && req.Method == HttpMethod.Post => (HttpStatusCode.OK, $$"""{"id":"{{aoLancar?.Invoke() ?? "9001"}}"}"""),
        var p when p.EndsWith("/worklog", StringComparison.Ordinal) => (HttpStatusCode.OK, """{"worklogs":[]}"""),
        _ => (HttpStatusCode.OK, """{"key":"VENDAS-10","fields":{"summary":"Corrigir pedido","status":{"name":"Em andamento"},"issuetype":{"name":"Bug"}}}"""),
    });

    [Fact]
    public async Task Coleta_os_commits_do_dia_de_todas_as_branches_e_acha_a_issue_na_mensagem_ou_na_branch()
    {
        Commitar();
        var servico = Criar(JiraQueAceita());

        var r = (await servico.CarregarDiaAsync(DateTime.Today)).Valor;

        r.Commits.Should().Contain(c => c.Assunto.StartsWith("VENDAS-10") && c.Issue == "VENDAS-10" && c.Arquivos.Contains("a.txt") && c.Linhas == 1);
        r.Commits.Should().Contain(c => c.Assunto == "ajuste sem id na mensagem" && c.Issue == "VENDAS-77" && c.Branch == "feature/VENDAS-77-ajuste");
        r.Commits.Should().Contain(c => c.Issue == "" && c.Assunto == "sem issue nenhuma");
        r.Total.Should().Be(528);
        r.Avisos.Should().Contain(a => a.Contains("sem id de issue"));
    }

    [Fact]
    public async Task Commit_sem_id_na_mensagem_nem_na_branch_usa_a_melhoria_cadastrada_para_a_branch()
    {
        _repo.Rodar("checkout", "-b", "ajuste-livre");
        _repo.Commitar("d.txt", "x\n", "ajuste sem id nenhum");
        var melhorias = new RepositorioDeMelhorias(new Armazenamento<ConfigMelhorias>(new PastaDeDados(_dados + "m"), "melhorias", padrao: ConfigMelhorias.Padrao), new SemContextoDeBanco());
        melhorias.Gravar(new Melhoria { Id = "VENDAS-500", Branches = [new() { Projeto = "web", Branch = "ajuste-livre" }] }, true);

        var sem = (await Criar(JiraQueAceita()).CarregarDiaAsync(DateTime.Today)).Valor;
        var com = (await Criar(JiraQueAceita(), melhorias).CarregarDiaAsync(DateTime.Today)).Valor;

        sem.Commits.Should().Contain(c => c.Assunto == "ajuste sem id nenhum" && c.Issue == "");
        com.Commits.Should().Contain(c => c.Assunto == "ajuste sem id nenhum" && c.Issue == "VENDAS-500");
    }

    [Fact]
    public async Task Dia_sem_commits_seus_nao_coleta_nada_de_outro_autor()
    {
        Commitar();
        var servico = Criar(JiraQueAceita());

        var r = (await servico.CarregarDiaAsync(DateTime.Today.AddDays(-3))).Valor;

        r.Commits.Should().BeEmpty();
        r.Modo.Should().Be("blocos");
    }

    [Fact]
    public async Task Abrir_o_dia_nunca_chama_jira_nem_ia()
    {
        Commitar();
        var servidor = JiraQueAceita();

        await Criar(servidor, ("JIRA_TOKEN", "t"), ("GROQ_API_KEY", "k")).CarregarDiaAsync(DateTime.Today);

        servidor.Chamadas.Should().BeEmpty();
    }

    [Fact]
    public async Task Lanca_no_jira_com_bearer_registra_e_reabrir_o_dia_nao_duplica()
    {
        Commitar();
        var servidor = JiraQueAceita();
        var servico = Criar(servidor, ("JIRA_TOKEN", "segredo-do-token"));
        var r = (await servico.CarregarDiaAsync(DateTime.Today)).Valor;
        var total = ServicoDoDiario.Pendentes(r).Count;

        var lancado = (await servico.LancarAsync(r)).Valor;

        lancado.Should().Match<ResumoDoLancamento>(x => x.Lancados == total && x.Total == total);
        var posts = servidor.Chamadas.Where(c => c.Metodo == HttpMethod.Post).ToList();
        posts.Should().HaveCount(total);
        posts.Should().OnlyContain(c => c.Autorizacao == "Bearer segredo-do-token" && c.Corpo.Contains("timeSpentSeconds"));
        File.ReadAllText(Path.Combine(_dados, "diario.json")).Should().NotContain("segredo-do-token");

        var reaberto = (await servico.CarregarDiaAsync(DateTime.Today)).Valor;
        reaberto.Blocos.Should().OnlyContain(b => b.Lancado && !b.Lancar && b.WorklogId == "9001");
        (await servico.LancarAsync(reaberto)).Erro.Causa.Should().Be("Nada para lançar");
        servidor.Chamadas.Count(c => c.Metodo == HttpMethod.Post).Should().Be(total);
    }

    [Fact]
    public async Task Sem_token_nada_e_enviado_e_o_erro_diz_qual_variavel_definir()
    {
        Commitar();
        var servidor = JiraQueAceita();
        var servico = Criar(servidor);
        var r = (await servico.CarregarDiaAsync(DateTime.Today)).Valor;

        var resultado = await servico.LancarAsync(r);

        resultado.Erro.ToString().Should().Contain("JIRA_TOKEN");
        servidor.Chamadas.Should().BeEmpty();
    }

    [Fact]
    public async Task Validacao_barra_minutos_zerados_e_comentario_vazio_antes_de_qualquer_envio()
    {
        Commitar();
        var servidor = JiraQueAceita();
        var servico = Criar(servidor, ("JIRA_TOKEN", "t"));
        var r = (await servico.CarregarDiaAsync(DateTime.Today)).Valor;
        r.Blocos[0].Minutos = 0;

        (await servico.LancarAsync(r)).Erro.Causa.Should().Contain("0 minutos");
        r.Blocos[0].Minutos = 30;
        r.Blocos[0].Comentario = " ";
        (await servico.LancarAsync(r)).Erro.Causa.Should().Contain("sem comentário");
        servidor.Chamadas.Should().BeEmpty();
    }

    [Fact]
    public async Task Falha_do_jira_para_no_primeiro_erro_e_o_que_ja_foi_enviado_fica_registrado()
    {
        Commitar();
        var n = 0;
        var servidor = new ServidorFalso((req, _) => req.Method == HttpMethod.Post && ++n == 2
            ? (HttpStatusCode.Forbidden, """{"errorMessages":["sem permissão"]}""")
            : (HttpStatusCode.OK, """{"id":"77"}"""));
        var servico = Criar(servidor, ("JIRA_TOKEN", "t"));
        var r = (await servico.CarregarDiaAsync(DateTime.Today)).Valor;

        var resultado = await servico.LancarAsync(r);

        resultado.EhOk.Should().BeFalse();
        resultado.Erro.Explicacao.Should().Contain("sem permissão").And.Contain("1 de");
        r.Blocos.Count(b => b.Lancado).Should().Be(1);
        servico.Configuracao.Lancamentos.Should().ContainSingle();
    }

    [Fact]
    public async Task Conferir_no_jira_traz_o_titulo_e_avisa_horas_ja_lancadas()
    {
        Commitar();
        var servidor = new ServidorFalso((req, _) => req.RequestUri!.AbsolutePath switch
        {
            "/rest/api/2/myself" => (HttpStatusCode.OK, """{"name":"eu"}"""),
            var p when p.EndsWith("/worklog", StringComparison.Ordinal) =>
                (HttpStatusCode.OK, $$"""{"worklogs":[{"id":"1","author":{"name":"eu"},"started":"{{DateTime.Today.AddHours(9):yyyy-MM-ddTHH:mm:ss.fff}}-0000","timeSpentSeconds":3600}]}"""),
            _ => (HttpStatusCode.OK, """{"key":"X","fields":{"summary":"Corrigir pedido","status":{"name":"Em andamento"}}}"""),
        });
        var servico = Criar(servidor, ("JIRA_TOKEN", "t"));
        var r = (await servico.CarregarDiaAsync(DateTime.Today)).Valor;

        (await servico.ConferirNoJiraAsync(r)).EhOk.Should().BeTrue();

        r.Blocos.Should().OnlyContain(b => b.TituloJira == "Corrigir pedido [Em andamento]");
        servidor.Chamadas.Should().OnlyContain(c => c.Metodo == HttpMethod.Get);
    }

    [Fact]
    public async Task Ia_so_roda_quando_pedida_uma_vez_por_worklog_e_refaz_se_citar_nome_inexistente()
    {
        Commitar();
        var respostas = new Queue<string>(["O FantasmaService.java passou a tratar o pedido.", "Foi ajustada a primeira correção do pedido."]);
        var servidor = new ServidorFalso((_, _) => (HttpStatusCode.OK, System.Text.Json.JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = respostas.Dequeue() } } } })));
        var servico = Criar(servidor, ("GROQ_API_KEY", "k"));
        var r = (await servico.CarregarDiaAsync(DateTime.Today)).Valor;
        servidor.Chamadas.Should().BeEmpty();

        var resumo = (await servico.ResumirComIaAsync(r, r.Blocos.First(b => b.Issue == "VENDAS-10"))).Valor;

        resumo.Texto.Should().Be("Foi ajustada a primeira correção do pedido.");
        resumo.Aviso.Should().Contain("foi refeita");
        servidor.Chamadas.Should().HaveCount(2).And.OnlyContain(c => c.Autorizacao == "Bearer k");
        servidor.Chamadas[0].Corpo.Should().NotContain("diff --git", "o código só vai se EnviarDiff estiver ligado");
    }

    [Fact]
    public async Task Ia_sem_chave_explica_qual_variavel_falta_e_nao_faz_chamada()
    {
        Commitar();
        var servidor = JiraQueAceita();
        var servico = Criar(servidor);
        var r = (await servico.CarregarDiaAsync(DateTime.Today)).Valor;

        var resultado = await servico.ResumirComIaAsync(r, r.Blocos[0]);

        resultado.Erro.Causa.Should().Contain("GROQ_API_KEY");
        servidor.Chamadas.Should().BeEmpty();
    }

    [Fact]
    public async Task Resumo_por_regras_le_o_diff_local_e_cita_o_metodo_criado()
    {
        _repo.Commitar("Servico.java", "public class Servico {\n    public int calculaTotal(int a) {\n        return a;\n    }\n}\n", "VENDAS-10 cria calculaTotal");
        var servico = Criar(JiraQueAceita());
        var r = (await servico.CarregarDiaAsync(DateTime.Today)).Valor;

        var texto = await servico.ResumoPorRegrasAsync(r, r.Blocos.First(b => b.Issue == "VENDAS-10"));

        texto.Should().Contain("calculaTotal").And.Contain("Servico.java");
    }
}
