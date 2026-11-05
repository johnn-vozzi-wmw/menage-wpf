using Menage.Core.Http;
using Menage.Core.PullRequests;
using Menage.Tests.Nexus;

namespace Menage.Tests.PullRequests;

public sealed class TokenFalso(string? token) : IProvedorDeTokenGitHub
{
    public Task<string?> ObterAsync(CancellationToken cancelar = default) => Task.FromResult(token);
}

public sealed class GravadorDeLinhas : IProgress<LinhaDoPr>
{
    public List<LinhaDoPr> Linhas { get; } = [];

    public void Report(LinhaDoPr value) => Linhas.Add(value);
}

/// <summary>Repositório descartável com um PR (refs/pull/1/head no origin). Nada de GitHub: a API é um HTTP de mentira.</summary>
public sealed class PrsTests : IDisposable
{
    readonly RepositorioDescartavel _repo = RepositorioDescartavel.Criar();
    readonly string _temp = Path.Combine(Path.GetTempPath(), "pr" + Guid.NewGuid().ToString("N")[..6]);

    public PrsTests()
    {
        Directory.CreateDirectory(_temp);
        _repo.CriarBranch("feature/pr-um");
        var sha = _repo.Rodar("rev-parse", "HEAD");
        _repo.Rodar("--git-dir=" + _repo.Origin, "update-ref", "refs/pull/1/head", sha);
        _repo.Rodar("checkout", "main");
    }

    public void Dispose()
    {
        _repo.Dispose();
        if (!Directory.Exists(_temp)) return;
        foreach (var f in Directory.EnumerateFiles(_temp, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_temp, true);
    }

    ServicoDePullRequests Servico(string comando = "cmd.exe /c exit 0", ClienteHttpFalso? http = null, string? token = null, bool semApi = true)
    {
        var armazenamento = new Armazenamento<ConfiguracaoDosPrs>(new PastaDeDados(_temp), "pull-requests", padrao: ConfiguracaoDosPrs.Padrao);
        armazenamento.Salvar(new ConfiguracaoDosPrs { Fontes = _repo.Caminho, ComandoDeValidacao = comando });
        return new ServicoDePullRequests(GitDeTeste.Criar(), new ProcessoRunner(), http ?? new ClienteHttpFalso(_ => new RespostaHttp(500, "")), new TokenFalso(token), armazenamento)
        {
            SemApi = semApi,
            PastaTemporaria = _temp,
        };
    }

    [Fact]
    public async Task Lista_pelas_refs_do_origin_com_titulo_do_ultimo_commit()
    {
        var lista = await Servico().ListarAsync(_repo.Caminho);
        lista.EhOk.Should().BeTrue(lista.ToString());
        var pr = lista.Valor.Prs.Should().ContainSingle().Subject;
        (pr.Numero, pr.Titulo).Should().Be((1, "Commit de feature/pr-um"));
        lista.Valor.Fonte.Should().Contain("refs");
        _repo.Rodar("for-each-ref", "refs/gm-pr/").Should().BeEmpty();
    }

    [Fact]
    public async Task Lista_pela_api_com_token_bearer_quando_ha_credencial()
    {
        _repo.Rodar("remote", "set-url", "origin", "https://github.com/dono/repo.git");
        var http = new ClienteHttpFalso(_ => new RespostaHttp(200,
            """[{"number":7,"title":"Corrige","user":{"login":"ana"},"head":{"ref":"fix","sha":"abc"},"html_url":"https://github.com/dono/repo/pull/7","updated_at":"2026-11-01T10:00:00Z","draft":true}]"""));
        var lista = await Servico(http: http, token: "tok", semApi: false).ListarAsync(_repo.Caminho);
        var pr = lista.Valor.Prs.Single();
        (pr.Numero, pr.Autor, pr.Branch, pr.Rascunho).Should().Be((7, "ana", "fix", true));
        http.Pedidos.Single().Credencial!.Bearer.Should().BeTrue();
        http.Pedidos.Single().Url.AbsolutePath.Should().Be("/repos/dono/repo/pulls");
    }

    [Fact]
    public async Task Valida_ok_e_exige_revalidar_se_o_commit_mudar()
    {
        var servico = Servico();
        var pr = (await servico.ListarAsync(_repo.Caminho)).Valor.Prs[0];
        var validado = await servico.ValidarAsync(_repo.Caminho, pr, new GravadorDeLinhas());
        validado.Valido.Should().BeTrue(validado.Validacao);
        validado.ProntoParaMesclar.Should().BeTrue();
        (validado with { Sha = "outro" }).ProntoParaMesclar.Should().BeFalse();
        Directory.Exists(Path.Combine(_temp, "gpr1")).Should().BeFalse();
        _repo.Rodar("worktree", "list").Split('\n').Should().HaveCount(1);
    }

    [Fact]
    public async Task Valida_reprovando_quando_nao_compila()
    {
        var servico = Servico("cmd.exe /c exit 1");
        var pr = (await servico.ListarAsync(_repo.Caminho)).Valor.Prs[0];
        var r = await servico.ValidarAsync(_repo.Caminho, pr, new GravadorDeLinhas());
        r.Valido.Should().BeFalse();
        r.Validacao.Should().StartWith("não compila");
    }

    [Fact]
    public async Task Valida_reprovando_por_conflito_com_a_main()
    {
        _repo.Rodar("checkout", "feature/pr-um");
        _repo.Commitar("README.md", "do PR\n", "PR mexe no README");
        _repo.Rodar("push", "origin", "feature/pr-um");
        _repo.Rodar("--git-dir=" + _repo.Origin, "update-ref", "refs/pull/1/head", _repo.Rodar("rev-parse", "HEAD"));
        _repo.Rodar("checkout", "main");
        _repo.Commitar("README.md", "da main\n", "main mexe no README");
        _repo.Rodar("push", "origin", "main");

        var servico = Servico();
        var pr = (await servico.ListarAsync(_repo.Caminho)).Valor.Prs[0];
        var r = await servico.ValidarAsync(_repo.Caminho, pr, new GravadorDeLinhas());
        r.Valido.Should().BeFalse();
        r.Validacao.Should().Contain("conflito").And.Contain("README.md");
    }

    [Fact]
    public async Task Mescla_na_main_local_sem_enviar_e_recusa_nao_validado()
    {
        var servico = Servico();
        var pr = (await servico.ListarAsync(_repo.Caminho)).Valor.Prs[0];
        (await servico.MesclarAsync(_repo.Caminho, [pr], false, new GravadorDeLinhas())).EhOk.Should().BeFalse();

        var validado = await servico.ValidarAsync(_repo.Caminho, pr, new GravadorDeLinhas());
        var origemAntes = _repo.Rodar("--git-dir=" + _repo.Origin, "rev-parse", "main");
        var r = await servico.MesclarAsync(_repo.Caminho, [validado], false, new GravadorDeLinhas());

        r.EhOk.Should().BeTrue(r.ToString());
        r.Valor.Enviado.Should().BeFalse();
        _repo.Rodar("log", "-1", "--format=%s").Should().StartWith("Merge pull request #1");
        _repo.Rodar("--git-dir=" + _repo.Origin, "rev-parse", "main").Should().Be(origemAntes);
        _repo.BranchAtual.Should().Be("main");
    }

    [Fact]
    public async Task Mescla_recusa_pasta_com_alteracao_sem_commit()
    {
        var servico = Servico();
        var pr = (await servico.ListarAsync(_repo.Caminho)).Valor.Prs[0];
        var validado = await servico.ValidarAsync(_repo.Caminho, pr, new GravadorDeLinhas());
        File.WriteAllText(Path.Combine(_repo.Caminho, "README.md"), "sujo\n");
        var r = await servico.MesclarAsync(_repo.Caminho, [validado], false, new GravadorDeLinhas());
        r.EhOk.Should().BeFalse();
        r.Erro.Causa.Should().Contain("sem commit");
    }

    [Theory]
    [InlineData("johnn.bryan@wmw.com.br", true)]
    [InlineData("outro@wmw.com.br", false)]
    public async Task So_o_dono_pode_usar(string email, bool esperado) =>
        (await Servico().PermitidoAsync(email)).Should().Be(esperado);

    [Theory]
    [InlineData("https://github.com/dono/repo.git", "dono/repo")]
    [InlineData("git@github.com:dono/repo", "dono/repo")]
    [InlineData("C:\\local\\repo", null)]
    public void Reconhece_o_repositorio_do_github(string url, string? esperado) =>
        RepositorioGitHub.DeUrl(url).Should().Be(esperado);
}
