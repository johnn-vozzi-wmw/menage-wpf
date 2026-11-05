using Menage.Core.Estado;

namespace Menage.Tests.JanelaPrincipal;

public sealed class SituacaoEApresentacaoTests : IDisposable
{
    readonly RepositorioDescartavel _repo = RepositorioDescartavel.Criar();

    public void Dispose() => _repo.Dispose();

    static EstadoDoProjeto Estado(Func<EstadoDoProjeto, EstadoDoProjeto>? ajuste = null) =>
        (ajuste ?? (e => e))(new EstadoDoProjeto(new Projeto("p", @"C:x")) { Branch = "feature/x", TemUpstream = true });

    [Theory]
    [InlineData("develop", TipoDeBranch.Principal)]
    [InlineData("main", TipoDeBranch.Principal)]
    [InlineData("release-1.2", TipoDeBranch.Principal)]
    [InlineData("feature/xyz", TipoDeBranch.Atividade)]
    public void Branch_principal_fica_apagada_e_a_de_atividade_em_negrito(string branch, TipoDeBranch esperado) =>
        ApresentacaoDoEstado.TipoDaBranch(Estado(e => e with { Branch = branch })).Should().Be(esperado);

    [Fact]
    public void Detached_e_falha_tem_tipo_proprio()
    {
        ApresentacaoDoEstado.TipoDaBranch(Estado(e => e with { Branch = "", Detached = true, Head = "abc123" })).Should().Be(TipoDeBranch.Detached);
        ApresentacaoDoEstado.TextoDaBranch(Estado(e => e with { Branch = "", Detached = true, Head = "abc123" })).Should().Be("detached (abc123)");
        ApresentacaoDoEstado.TipoDaBranch(Estado(e => e with { Falha = "pasta não encontrada" })).Should().Be(TipoDeBranch.Desconhecida);
    }

    [Fact]
    public void Situacao_limpa_alterada_e_com_operacao()
    {
        ApresentacaoDoEstado.Situacao(Estado()).Select(e => e.Texto).Should().Equal("limpo");
        ApresentacaoDoEstado.TemAlteracoes(Estado()).Should().BeFalse();

        var sujo = Estado(e => e with { Alteracoes = 2, NaoVersionados = 1 });
        ApresentacaoDoEstado.Situacao(sujo).Select(e => e.Texto).Should().Equal("2 alterados", "1 não versionados");
        ApresentacaoDoEstado.TemAlteracoes(sujo).Should().BeTrue();
        ApresentacaoDoEstado.Arquivos(sujo).Should().Be(3);

        var naoVersionados = Estado(e => e with { NaoVersionados = 4 });
        ApresentacaoDoEstado.TemAlteracoes(naoVersionados).Should().BeFalse("não versionados não contam, como no TortoiseGit");

        var rebase = Estado(e => e with { Operacao = "rebase em andamento" });
        ApresentacaoDoEstado.TemAlteracoes(rebase).Should().BeTrue();
        ApresentacaoDoEstado.Situacao(rebase)[0].Tom.Should().Be(TomDaEtiqueta.Vermelho);
    }

    [Fact]
    public void Origin_mostra_adiante_atras_em_dia_e_sem_origin()
    {
        ApresentacaoDoEstado.Origin(Estado(e => e with { Ahead = 2, Behind = 3 })).Select(e => e.Texto).Should().Equal("↑2", "↓3");
        ApresentacaoDoEstado.Origin(Estado()).Select(e => e.Texto).Should().Equal("em dia");
        ApresentacaoDoEstado.Origin(Estado(e => e with { TemUpstream = false })).Select(e => e.Texto).Should().Equal("sem origin");
    }

    [Fact]
    public async Task Leitor_de_situacao_traz_estado_e_versao_do_pom_de_repositorio_real()
    {
        File.WriteAllText(Path.Combine(_repo.Caminho, "pom.xml"), "<project><artifactId>a</artifactId><version>3.4.5</version></project>");
        File.WriteAllText(Path.Combine(_repo.Caminho, "README.md"), "mudou\n");
        var git = GitDeTeste.Criar();
        var leitor = new LeitorDeSituacao(new LeitorDeEstado(git));

        var situacao = (await leitor.LerTodosAsync([_repo.ComoProjeto()])).Single();

        situacao.Versao.Should().Be("3.4.5");
        situacao.Estado.Branch.Should().Be("main");
        situacao.Estado.Alteracoes.Should().Be(1);
        situacao.Estado.NaoVersionados.Should().Be(1);
    }

    [Fact]
    public void Versao_vem_do_parent_quando_o_projeto_nao_declara()
    {
        File.WriteAllText(Path.Combine(_repo.Caminho, "pom.xml"),
            "<project><parent><artifactId>p</artifactId><version>9.9</version></parent><artifactId>a</artifactId></project>");
        LeitorDeSituacao.VersaoDoPom(_repo.Caminho).Should().Be("9.9");
        LeitorDeSituacao.VersaoDoPom(Path.Combine(_repo.Raiz, "nao-existe")).Should().BeEmpty();
    }

    [Fact]
    public async Task Catalogo_de_pasta_agrupa_por_subpasta_de_familia()
    {
        var raiz = Path.Combine(Path.GetTempPath(), "mg" + Guid.NewGuid().ToString("N")[..6]);
        try
        {
            var git = GitDeTeste.Criar();
            foreach (var relativo in new[] { "solto", @"web\site", @"framework\fw1", @"pda\lavenderepda" })
            {
                var pasta = Path.Combine(raiz, relativo);
                Directory.CreateDirectory(pasta);
                (await git.ExecutarBrutoAsync(Git.Em(pasta).Comando("init"))).Sucesso.Should().BeTrue();
            }

            var config = new ConfiguracaoDeProjetos(new PastaDeDados(Path.Combine(raiz, "dados")));
            config.Salvar(CatalogoDePasta.Criar(raiz)).EhOk.Should().BeTrue();

            var projetos = config.Projetos.ToDictionary(p => p.Nome, p => p.Grupos);
            projetos.Should().HaveCount(4);
            projetos["solto"].Should().BeEmpty();
            projetos["site"].Should().Equal(Familias.Web);
            projetos["fw1"].Should().Equal(Familias.Framework);
            projetos["lavenderepda"].Should().Equal(Familias.App);
        }
        finally
        {
            foreach (var arquivo in Directory.EnumerateFiles(raiz, "*", SearchOption.AllDirectories)) File.SetAttributes(arquivo, FileAttributes.Normal);
            Directory.Delete(raiz, recursive: true);
        }
    }
}
