using System.Net;

namespace Menage.Tests.Kit;

public class TestKitTests
{
    [Fact]
    public void Repositorio_descartavel_tem_origin_local_branches_e_some_ao_descartar()
    {
        string raiz;
        using (var repo = RepositorioDescartavel.Criar())
        {
            raiz = repo.Raiz;
            repo.Raiz.Length.Should().BeLessThan(Path.GetTempPath().Length + 12, "caminho curto");
            repo.BranchAtual.Should().Be("main");
            repo.Rodar("remote", "get-url", "origin").Should().Be(repo.Origin);
            repo.Rodar("branch", "-r").Should().Contain("origin/develop").And.Contain("origin/feature/exemplo");
            repo.Rodar("status", "--porcelain").Should().BeEmpty();
            repo.Rodar("rev-list", "--count", "main..origin/main").Should().Be("0");
        }

        Directory.Exists(raiz).Should().BeFalse();
    }

    [Fact]
    public void Repositorios_de_testes_diferentes_nao_se_misturam()
    {
        using var a = RepositorioDescartavel.Criar();
        using var b = RepositorioDescartavel.Criar();

        a.Raiz.Should().NotBe(b.Raiz);
        a.Commitar("so-no-a.txt", "x", "so no a");
        b.Rodar("log", "--format=%s").Should().NotContain("so no a");
    }

    [Fact]
    public async Task Maven_falso_registra_chamadas_e_devolve_o_codigo_pedido()
    {
        using var maven = MavenFalso.Criar(codigoDeSaida: 2, saida: "BUILD FAILURE", erro: "deu ruim");

        var saida = await new ProcessoRunner().ExecutarAsync(new ComandoProcesso("cmd.exe", ["/c", maven.Caminho, "clean", "install", "-DskipTests"]));

        saida.CodigoDeSaida.Should().Be(2);
        saida.Texto.Should().Contain("BUILD FAILURE");
        saida.TextoErro.Should().Contain("deu ruim");
        maven.Chamadas.Should().Equal("clean install -DskipTests");
    }

    [Fact]
    public async Task Maven_falso_pode_demorar_para_testar_timeout()
    {
        using var maven = MavenFalso.Criar(atrasoEmSegundos: 20);

        var saida = await new ProcessoRunner().ExecutarAsync(
            new ComandoProcesso("cmd.exe", ["/c", maven.Caminho], null, null, TimeSpan.FromMilliseconds(500)));

        saida.Expirou.Should().BeTrue();
    }

    [Fact]
    public void Maven_falso_recusa_caracteres_que_quebrariam_o_script()
    {
        Action criar = () => MavenFalso.Criar(saida: "a & b");

        criar.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Nexus_falso_serve_publica_e_registra_requisicoes()
    {
        using var nexus = NexusFalso.Iniciar();
        using var http = new HttpClient { BaseAddress = new Uri(nexus.Url) };
        nexus.Publicar("repo/a/1.0/a-1.0.jar", "conteudo");

        (await http.GetStringAsync("repo/a/1.0/a-1.0.jar")).Should().Be("conteudo");
        (await http.GetAsync("repo/nao/existe")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await http.PutAsync("repo/novo.jar", new ByteArrayContent([1, 2, 3]))).StatusCode.Should().Be(HttpStatusCode.Created);

        nexus.Obter("/repo/novo.jar").Should().Equal(1, 2, 3);
        nexus.Requisicoes.Select(r => r.Metodo).Should().Equal("GET", "GET", "PUT");
    }

    [Fact]
    public async Task Nexus_falso_simula_falha_so_nas_vezes_pedidas_e_so_no_caminho_filtrado()
    {
        using var nexus = NexusFalso.Iniciar();
        using var http = new HttpClient { BaseAddress = new Uri(nexus.Url) };
        nexus.Publicar("a", "ok");
        nexus.Publicar("b", "ok");
        nexus.SimularFalha(503, vezes: 2, caminhoContem: "a");

        (await http.GetAsync("b")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await http.GetAsync("a")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await http.GetAsync("a")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await http.GetAsync("a")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Nexus_falso_simula_conexao_cortada()
    {
        using var nexus = NexusFalso.Iniciar();
        using var http = new HttpClient { BaseAddress = new Uri(nexus.Url) };
        nexus.SimularConexaoCortada();

        await http.Invoking(h => h.GetAsync("x")).Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task Nexus_falso_simula_lentidao_e_permite_timeout_no_cliente()
    {
        using var nexus = NexusFalso.Iniciar();
        using var http = new HttpClient { BaseAddress = new Uri(nexus.Url), Timeout = TimeSpan.FromMilliseconds(300) };
        nexus.SimularLentidao(TimeSpan.FromSeconds(3));

        await http.Invoking(h => h.GetAsync("x")).Should().ThrowAsync<TaskCanceledException>();
    }
}
