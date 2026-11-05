using System.Text;
using Menage.Core.LinhaDoTempo;

namespace Menage.Tests.PersonalizacaoHistorico;

public sealed class HistoricoDeReleasesTests : IDisposable
{
    readonly string _logs = Path.Combine(Path.GetTempPath(), "mgh" + Guid.NewGuid().ToString("N")[..6]);

    public void Dispose()
    {
        if (Directory.Exists(_logs)) Directory.Delete(_logs, true);
    }

    string Log(string geracao, string arquivo, string projeto, string pasta, string branch, string? rodape)
    {
        var dir = Directory.CreateDirectory(Path.Combine(_logs, geracao)).FullName;
        var linhas = new List<string>
        {
            new('=', 100), $"Projeto  : {projeto}", $"Pasta    : {pasta}", $"Branch   : {branch}",
            "Comando  : mvn -B clean install", "Inicio   : 04/11/2026 10:00:00", new('=', 100),
            "[INFO] Scanning for projects...", "[INFO] BUILD",
        };
        if (rodape is not null) linhas.AddRange([new string('=', 100), rodape, new string('=', 100)]);
        var caminho = Path.Combine(dir, arquivo);
        File.WriteAllLines(caminho, linhas, Encoding.Latin1);
        return caminho;
    }

    const string G1 = "2026-11-04_10-00-00_install";

    [Fact]
    public void Geracao_vira_um_evento_com_data_modo_branch_e_ambiente()
    {
        Log(G1, "01-lavenderecore.log", "lavenderecore", @"C:\wmw\dev\dev1\lavenderecore", "develop", "RESULTADO: SUCESSO - lavenderecore em 02:31");
        Log(G1, "02-lavenderedados.log", "lavenderedados", @"C:\wmw\dev\dev1\lavenderedados", "develop", "RESULTADO: SUCESSO - lavenderedados em 01:09");

        var e = LeitorDeBetas.Ler(_logs, [@"C:\wmw\dev\dev1"]).Should().ContainSingle().Subject;

        e.Data.Should().Be(new DateTime(2026, 11, 4, 10, 0, 0));
        e.Tipo.Should().Be(TipoDeEvento.Beta);
        e.Ambiente.Should().Be("dev1");
        e.Estado.Should().Be(EstadoDoEvento.Ok);
        e.Titulo.Should().Contain("Beta install").And.Contain("develop").And.Contain("2 projetos");
        e.Detalhe.Should().Contain("lavenderecore - ok").And.Contain("3min 40s");
        e.Projetos.Should().Equal("lavenderecore", "lavenderedados");
    }

    [Fact]
    public void Um_projeto_com_erro_marca_o_evento_inteiro_como_erro()
    {
        Log(G1, "01-a.log", "a", @"C:\b\x\a", "develop", "RESULTADO: SUCESSO - a em 00:10");
        Log(G1, "02-b.log", "b", @"C:\b\x\b", "develop", "RESULTADO: ERRO (exit 1) - b em 00:05 [falhou]");

        LeitorDeBetas.Ler(_logs, []).Single().Estado.Should().Be(EstadoDoEvento.Erro);
    }

    [Fact]
    public void Log_sem_rodape_e_geracao_sem_resultado()
    {
        Log(G1, "01-a.log", "a", @"C:\b\x\a", "develop", null);

        var e = LeitorDeBetas.Ler(_logs, []).Single();
        e.Estado.Should().Be(EstadoDoEvento.SemResultado);
        e.Detalhe.Should().Contain("a - sem resultado");
    }

    [Fact]
    public void Interrompido_conta_como_erro()
    {
        Log(G1, "01-a.log", "a", @"C:\b\x\a", "develop", "RESULTADO: INTERROMPIDO - a em 00:03");
        LeitorDeBetas.Ler(_logs, []).Single().Estado.Should().Be(EstadoDoEvento.Erro);
    }

    [Fact]
    public void Repeticao_do_surefire_substitui_o_primeiro_log()
    {
        Log(G1, "01-lavenderepda.log", "lavenderepda", @"C:\b\x\lavenderepda", "develop", "RESULTADO: ERRO (exit 1) - lavenderepda em 00:40");
        Log(G1, "01-lavenderepda-surefire.log", "lavenderepda  (repetição: correção do surefire)", @"C:\b\x\lavenderepda", "develop",
            "RESULTADO: SUCESSO - lavenderepda em 00:55");

        var e = LeitorDeBetas.Ler(_logs, []).Single();
        e.Estado.Should().Be(EstadoDoEvento.Ok);
        e.Projetos.Should().Equal("lavenderepda");
    }

    [Fact]
    public void Pastas_que_nao_sao_geracoes_e_logs_estranhos_sao_ignorados()
    {
        Directory.CreateDirectory(Path.Combine(_logs, "qualquer-coisa"));
        Log("2026-11-04_10-00-00", "01-a.log", "a", @"C:\b\x\a", "develop", null); // sem modo no nome
        Log(G1, "anotacoes.log", "a", @"C:\b\x\a", "develop", null);
        File.WriteAllText(Path.Combine(_logs, G1, "01-a.cmd"), "@echo off");

        LeitorDeBetas.Ler(_logs, []).Should().BeEmpty();
    }

    [Fact]
    public void Pasta_de_logs_inexistente_nao_e_erro() =>
        LeitorDeBetas.Ler(Path.Combine(_logs, "nada"), []).Should().BeEmpty();

    [Fact]
    public void Respeita_o_maximo_e_vem_da_mais_recente_para_a_mais_antiga()
    {
        foreach (var dia in new[] { "01", "02", "03" })
            Log($"2026-11-{dia}_09-00-00_install", "01-a.log", "a", @"C:\b\x\a", "develop", "RESULTADO: SUCESSO - a em 00:01");

        LeitorDeBetas.Ler(_logs, [], maximo: 2).Select(e => e.Data.Day).Should().Equal(3, 2);
    }

    [Fact]
    public void Ambiente_sem_base_conhecida_e_a_pasta_acima_do_projeto_ou_do_grupo() =>
        (LeitorDeBetas.AmbienteDe(@"C:\wmw\dev\dev2\web\lavendereweb", []) + LeitorDeBetas.AmbienteDe(@"C:\wmw\dev\dev1\lavenderecore", [])).Should().Be("dev2dev1");

    [Fact]
    public void Ambiente_usa_a_base_mais_especifica_que_contem_o_projeto() =>
        LeitorDeBetas.AmbienteDe(@"D:\env\dev3\web\lavendereweb", [@"D:\env", @"D:\env\dev3"]).Should().Be("dev3");

    static EventoDeRelease Beta(string dia, string ambiente, string projeto) =>
        new(DateTime.Parse(dia, System.Globalization.CultureInfo.InvariantCulture), TipoDeEvento.Beta, $"Beta {projeto}", "detalhe", ambiente, EstadoDoEvento.Ok, [projeto]);

    static EventoDeRelease Tag(string dia, string tag) =>
        new(DateTime.Parse(dia, System.Globalization.CultureInfo.InvariantCulture), TipoDeEvento.Tag, $"Tag {tag}", "", "", EstadoDoEvento.Ok, ["lavendereweb"]);

    [Fact]
    public void Filtro_combina_ambiente_texto_e_tipo_em_ordem_decrescente()
    {
        var todos = new[] { Beta("2026-11-01", "dev1", "lavendereweb"), Beta("2026-11-03", "dev2", "lavenderecore"), Tag("2026-11-02", "v1.0"), Beta("2026-11-04", "dev1", "lavenderecore") };

        new FiltroDeReleases().Aplicar(todos).Select(e => e.Data.Day).Should().Equal(4, 3, 2, 1);
        new FiltroDeReleases(Ambiente: "dev1").Aplicar(todos).Should().HaveCount(3, "tag não pertence a ambiente e continua visível");
        new FiltroDeReleases(Texto: "core").Aplicar(todos).Should().HaveCount(2);
        new FiltroDeReleases(Texto: "core dev2").Aplicar(todos).Should().ContainSingle();
        new FiltroDeReleases(Tags: false).Aplicar(todos).Should().OnlyContain(e => !e.EhTag);
        new FiltroDeReleases(Betas: false).Aplicar(todos).Should().ContainSingle().Which.EhTag.Should().BeTrue();
    }

    [Fact]
    public void Lista_de_ambientes_e_unica_e_ordenada() =>
        HistoricoDeReleases.Ambientes([Beta("2026-11-01", "dev2", "a"), Beta("2026-11-02", "dev1", "a"), Beta("2026-11-03", "DEV1", "a"), Tag("2026-11-02", "v")])
            .Should().Equal("dev1", "dev2");

    [Fact]
    public async Task Tags_do_mesmo_nome_no_mesmo_horario_viram_um_evento_com_os_projetos()
    {
        using var a = RepositorioDescartavel.Criar();
        using var b = RepositorioDescartavel.Criar();
        a.Rodar("tag", "-a", "v9.9", "-m", "release nove");
        b.Rodar("tag", "v9.9");
        a.Rodar("tag", "so-no-a");

        var leitor = new LeitorDeTags(GitDeTeste.Criar());
        var eventos = await leitor.LerAsync([a.ComoProjeto("web"), b.ComoProjeto("dados")]);

        var v99 = eventos.Single(e => e.Titulo == "Tag v9.9");
        v99.Projetos.Should().BeEquivalentTo("web", "dados");
        v99.Detalhe.Should().Contain("web @").And.Contain("dados @");
        v99.EhTag.Should().BeTrue();
        eventos.Single(e => e.Titulo == "Tag so-no-a").Projetos.Should().Equal("web");
    }

    [Fact]
    public async Task Pasta_que_nao_e_repositorio_e_ignorada()
    {
        var leitor = new LeitorDeTags(GitDeTeste.Criar());
        (await leitor.LerAsync([new Projeto("x", _logs)])).Should().BeEmpty();
    }
}
