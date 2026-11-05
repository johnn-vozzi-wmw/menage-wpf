using Menage.Core.Http;
using Menage.Core.Nexus;

namespace Menage.Tests.Nexus;

/// <summary>HTTP de mentira: responde pela URL completa (com query) e grava as credenciais recebidas.</summary>
public sealed class ClienteHttpFalso(Func<Uri, RespostaHttp> resposta) : IClienteHttp
{
    public List<(Uri Url, CredencialHttp? Credencial)> Pedidos { get; } = [];

    public Func<Uri, byte[]?> Arquivos { get; set; } = _ => null;

    public Task<Resultado<RespostaHttp>> ObterTextoAsync(Uri url, CredencialHttp? credencial = null, CancellationToken cancelar = default)
    {
        Pedidos.Add((url, credencial));
        return Task.FromResult(Resultado.Ok(resposta(url)));
    }

    public Task<Resultado<int>> BaixarAsync(Uri url, string destino, CredencialHttp? credencial = null, Action<long>? progresso = null, CancellationToken cancelar = default)
    {
        Pedidos.Add((url, credencial));
        var bytes = Arquivos(url);
        if (bytes is null) return Task.FromResult(Resultado.Ok(404));
        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
        File.WriteAllBytes(destino, bytes);
        progresso?.Invoke(bytes.Length);
        return Task.FromResult(Resultado.Ok(200));
    }
}

/// <summary>Protege invertendo o texto (só para teste; o app usa DPAPI).</summary>
public sealed class ProtetorReversivel : IProtetorDeSegredo
{
    public string Proteger(string texto) => "P:" + new string(texto.Reverse().ToArray());

    public string Revelar(string protegido) => new(protegido[2..].Reverse().ToArray());
}

public static class JsonNexus
{
    public static string Componente(string nome, string versao, string baseUrl, params (string Caminho, long Tamanho)[] arquivos) =>
        $$"""{"repository":"releases","group":"br.com.wmw","name":"{{nome}}","version":"{{versao}}","format":"maven2","assets":[{{string.Join(",", arquivos.Select(a =>
            $$"""{"path":"{{a.Caminho}}","downloadUrl":"{{baseUrl}}repository/releases/{{a.Caminho}}","fileSize":{{a.Tamanho}},"lastModified":"2026-11-01T10:00:00.000+00:00"}"""))}}]}""";

    public static string Pagina(string? token, params string[] componentes) =>
        $$"""{"items":[{{string.Join(",", componentes)}}],"continuationToken":{{(token is null ? "null" : $"\"{token}\"")}}}""";
}

public sealed class NexusTests : IDisposable
{
    const string Base = "http://nexus.teste/";
    readonly string _pasta = Path.Combine(Path.GetTempPath(), "nx" + Guid.NewGuid().ToString("N")[..6]);

    static ConfiguracaoDoNexus Cfg(string url = Base) => new() { Url = url, Usuario = "u", SenhaProtegida = "x" };

    public void Dispose()
    {
        if (Directory.Exists(_pasta)) Directory.Delete(_pasta, true);
    }

    [Fact]
    public void Versoes_sao_comparadas_pelos_numeros()
    {
        var ordem = new[] { "9.060-13", "9.060-3", "9.060-5" }.OrderBy(v => v, VersaoNatural.Comparador);
        ordem.Should().Equal("9.060-3", "9.060-5", "9.060-13");
    }

    [Fact]
    public void Nome_da_pasta_lista_ate_cinco_versoes_e_resume_acima_disso()
    {
        BaixadorNexus.NomeDaPasta(["9.060-5", "9.030"]).Should().Be("beta_Nexus_9030_9060-5");
        BaixadorNexus.NomeDaPasta(["1.0", "2.0", "3.0", "4.0", "5.0", "6.0"]).Should().Be("beta_Nexus_10_a_60_6versoes");
    }

    [Fact]
    public void Separa_names_por_virgula_sem_repetir()
    {
        SessaoDeBuscaNexus.SepararNames("wmwvendas, wmwdados;WMWVENDAS\nfoo").Should().Equal("wmwvendas", "wmwdados", "foo");
    }

    [Fact]
    public async Task Busca_um_name_por_vez_poe_curinga_na_versao_e_ignora_checksums()
    {
        var http = new ClienteHttpFalso(url =>
        {
            var q = url.Query;
            var nome = q.Contains("name=a", StringComparison.Ordinal) ? "a" : "b";
            return new RespostaHttp(200, JsonNexus.Pagina(null, JsonNexus.Componente(nome, "9.060-3", Base, ($"{nome}.war", 10), ($"{nome}.war.sha1", 1))));
        });
        var sessao = new SessaoDeBuscaNexus(new ClienteNexus(http, Cfg(), "s"));
        var r = await sessao.IniciarAsync(new ConsultaNexus("a,b", "", "9.060"));
        r.EhOk.Should().BeTrue();
        sessao.Componentes.Select(c => c.Nome).Should().Equal("a", "b");
        sessao.Componentes[0].Arquivos.Should().ContainSingle();
        http.Pedidos.Should().HaveCount(2);
        http.Pedidos.Should().OnlyContain(p => p.Url.Query.Contains("version=9.060%2A", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Pagina_ate_cinco_vezes_e_oferece_carregar_mais()
    {
        var n = 0;
        var http = new ClienteHttpFalso(_ => new RespostaHttp(200, JsonNexus.Pagina("t", JsonNexus.Componente("a", $"1.{n++}", Base, ("a.jar", 1)))));
        var sessao = new SessaoDeBuscaNexus(new ClienteNexus(http, Cfg(), "s"));
        (await sessao.IniciarAsync(new ConsultaNexus("a"))).Valor.Should().Be(5);
        sessao.TemMais.Should().BeTrue();
        (await sessao.CarregarMaisAsync()).Valor.Should().Be(5);
        sessao.Componentes.Should().HaveCount(10);
    }

    [Fact]
    public async Task Sem_filtro_nao_busca()
    {
        var http = new ClienteHttpFalso(_ => new RespostaHttp(200, "{}"));
        var r = await new SessaoDeBuscaNexus(new ClienteNexus(http, Cfg(), "s")).IniciarAsync(new ConsultaNexus());
        r.EhOk.Should().BeFalse();
        http.Pedidos.Should().BeEmpty();
    }

    [Theory]
    [InlineData(401, "recusados")]
    [InlineData(403, "permissão")]
    [InlineData(404, "não encontrado")]
    public async Task Traduz_status_do_servidor(int status, string trecho)
    {
        var http = new ClienteHttpFalso(_ => new RespostaHttp(status, ""));
        var r = await new ClienteNexus(http, Cfg(), "s").RepositoriosAsync();
        r.EhOk.Should().BeFalse();
        r.Erro.Causa.Should().Contain(trecho);
    }

    [Fact]
    public async Task Lista_repositorios_sem_grupos()
    {
        var http = new ClienteHttpFalso(_ => new RespostaHttp(200, """[{"name":"z","type":"hosted"},{"name":"g","type":"group"},{"name":"a","type":"proxy"}]"""));
        (await new ClienteNexus(http, Cfg(), "s").RepositoriosAsync()).Valor.Should().Equal("a", "z");
    }

    [Fact]
    public async Task Baixa_de_verdade_do_servidor_falso_com_progresso_e_subpasta()
    {
        using var nexus = TestKit.NexusFalso.Iniciar();
        var conteudo = new byte[300_000];
        new Random(1).NextBytes(conteudo);
        nexus.Publicar("repository/releases/br/wmw/a/1.0/a-1.0.war", conteudo);
        var componente = new ComponenteNexus("releases", "br.wmw", "a", "1.0", "maven2", null,
            [new ArquivoNexus("br/wmw/a/1.0/a-1.0.war", $"{nexus.Url}repository/releases/br/wmw/a/1.0/a-1.0.war", "war", conteudo.Length),
             new ArquivoNexus("br/wmw/a/1.0/a-1.0.pom", $"{nexus.Url}repository/releases/br/wmw/a/1.0/a-1.0.pom", "pom", 5)]);
        using var http = new ClienteHttp();
        var cliente = new ClienteNexus(http, Cfg(nexus.Url), "s");
        long ultimo = 0;
        var andamento = new Progress<AndamentoDoDownload>(a => ultimo = Math.Max(ultimo, a.Feito));

        var r = await BaixadorNexus.BaixarAsync(cliente, new PedidoDeDownload([componente], new HashSet<string> { "war" }, _pasta, ManterEstrutura: false, Subpasta: true), andamento);

        r.EhOk.Should().BeTrue();
        r.Valor.Ok.Should().BeTrue();
        r.Valor.Pasta.Should().EndWith("beta_Nexus_10");
        File.ReadAllBytes(Path.Combine(r.Valor.Pasta, "a-1.0.war")).Should().Equal(conteudo);
        Directory.GetFiles(r.Valor.Pasta, "*.parte").Should().BeEmpty();
        File.Exists(Path.Combine(r.Valor.Pasta, "a-1.0.pom")).Should().BeFalse();
        await Task.Delay(100);
        ultimo.Should().Be(conteudo.Length);
    }

    [Fact]
    public async Task Falha_de_um_arquivo_nao_para_os_outros()
    {
        using var nexus = TestKit.NexusFalso.Iniciar();
        nexus.Publicar("ok.jar", "x");
        using var http = new ClienteHttp();
        var cliente = new ClienteNexus(http, Cfg(nexus.Url), "s");
        var componente = new ComponenteNexus("r", "g", "n", "1", "maven2", null,
            [new ArquivoNexus("sumiu.jar", $"{nexus.Url}sumiu.jar", "jar", 1), new ArquivoNexus("ok.jar", $"{nexus.Url}ok.jar", "jar", 1)]);

        var r = await BaixadorNexus.BaixarAsync(cliente, new PedidoDeDownload([componente], new HashSet<string> { "jar" }, _pasta, false, false), null);

        r.Valor.Baixados.Should().Be(1);
        r.Valor.Falhas.Should().ContainSingle().Which.Should().Contain("sumiu.jar").And.Contain("404");
    }

    [Fact]
    public async Task Caminho_que_foge_da_pasta_e_recusado_e_credencial_nao_vai_para_outro_servidor()
    {
        var http = new ClienteHttpFalso(_ => new RespostaHttp(200, "")) { Arquivos = _ => [1] };
        var cliente = new ClienteNexus(http, Cfg(), "s");
        var fuga = await cliente.BaixarAsync(new ArquivoNexus("../fora.jar", Base + "x", "jar", 1), _pasta, manterEstrutura: true, null);
        fuga.EhOk.Should().BeFalse();

        var r = await cliente.BaixarAsync(new ArquivoNexus("a.jar", "http://outro.servidor/a.jar", "jar", 1), _pasta, false, null);
        r.EhOk.Should().BeTrue();
        http.Pedidos.Single().Credencial.Should().BeNull();
    }

    [Fact]
    public void Senha_fica_protegida_no_arquivo_e_nunca_em_claro()
    {
        var pasta = new PastaDeDados(_pasta);
        var repo = new RepositorioDoNexus(new Armazenamento<ConfiguracaoDoNexus>(pasta, "nexus", padrao: ConfiguracaoDoNexus.Padrao), new ProtetorReversivel(), new ClienteHttpFalso(_ => new RespostaHttp(200, "")));
        repo.SalvarComSenha(new ConfiguracaoDoNexus { Url = "http://n/", Usuario = "u" }, "segredo123").EhOk.Should().BeTrue();
        File.ReadAllText(Path.Combine(_pasta, "nexus.json")).Should().NotContain("segredo123");
        repo.Configuracao.Completa.Should().BeTrue();
        repo.SalvarComSenha(repo.Configuracao with { Destino = "C:\\x" }, "").EhOk.Should().BeTrue();
        repo.Configuracao.SenhaProtegida.Should().Be("P:321oderges");
        repo.Cliente().EhOk.Should().BeTrue();
    }
}
