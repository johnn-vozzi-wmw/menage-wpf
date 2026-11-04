using Menage.Core.ModoBeta;
using Menage.Core.Operacoes.Checkout;

namespace Menage.Tests.NucleoGit;

/// <summary>Um principal (este repositório) e um framework (outro), com o modo beta ligado.</summary>
public sealed class ModoBetaTests : NucleoGitBase
{
    readonly RepositorioDescartavel _framework = RepositorioDescartavel.Criar();

    public ModoBetaTests()
    {
        Projetos.Adicionar(_framework.ComoProjeto("framework-core") with { Familia = Familias.Framework });
        Projetos.Marcar(_framework.Caminho, true);

        Repo.Commitar("pom.xml", "<project>\n  <properties>\n    <framework.version>1.0-SNAPSHOT</framework.version>\n  </properties>\n</project>\n", "pom");
        Repo.Rodar("push", "origin", "main");
        Repo.CriarBranch("feat");
        Repo.Rodar("checkout", "main");

        _framework.Commitar("pom.xml", "<project>\n  <artifactId>framework-core</artifactId>\n  <version>1.0</version>\n</project>\n", "tag 1.0");
        _framework.Rodar("tag", "1.0");
        _framework.Rodar("push", "origin", "main", "refs/tags/1.0");
    }

    [Fact]
    public async Task Framework_vai_para_a_tag_da_versao_e_o_pom_ganha_snapshot_sem_commit()
    {
        var r = await Checkout(beta: true).ExecutarAsync(new OpcoesDeCheckout("feat"));

        Repo.BranchAtual.Should().Be("feat");
        (await Consultas.BranchAtualAsync(_framework.Caminho)).Should().BeEmpty("a tag deixa o framework em detached HEAD");
        _framework.Rodar("log", "-1", "--format=%s").Should().Be("tag 1.0");
        File.ReadAllText(Path.Combine(_framework.Caminho, "pom.xml")).Should().Contain("<version>1.0-SNAPSHOT</version>");
        _framework.Rodar("status", "--short").Should().Contain("M pom.xml");
        r.Valor.Itens.Should().Contain(i => i.Resultado.Mensagem.StartsWith("[beta]", StringComparison.Ordinal) && i.Projeto.Nome == "framework-core");
        r.Valor.Observacoes.Should().Contain(o => o.Texto.Contains("feat_b"));
    }

    [Fact]
    public async Task Com_a_branch_b_no_origin_o_framework_vai_para_ela()
    {
        _framework.CriarBranch("feat_b");
        _framework.Rodar("checkout", "main");
        _framework.Rodar("branch", "-D", "feat_b");

        await Checkout(beta: true).ExecutarAsync(new OpcoesDeCheckout("feat"));

        _framework.BranchAtual.Should().Be("feat_b");
    }

    [Fact]
    public async Task Develop_nao_aciona_o_beta_e_os_frameworks_vao_juntos()
    {
        await Checkout(beta: true).ExecutarAsync(new OpcoesDeCheckout("develop"));

        Repo.BranchAtual.Should().Be("develop");
        _framework.BranchAtual.Should().Be("develop");
    }

    [Fact]
    public async Task Sem_projeto_principal_o_checkout_e_normal()
    {
        var r = await Checkout(beta: true).ExecutarAsync(new OpcoesDeCheckout("develop"));

        r.Valor.Observacoes.Should().BeEmpty();
        var soFramework = Projetos.Marcados.Count(p => p.EhFramework);
        soFramework.Should().Be(1);
    }

    [Theory]
    [InlineData("develop", true)]
    [InlineData("Develop", true)]
    [InlineData("release-9.1", true)]
    [InlineData("release/2026", true)]
    [InlineData("feat", false)]
    [InlineData("9.050", false)]
    [InlineData("main", false)]
    public void Beta_so_nao_age_em_develop_e_release(string destino, bool naoAge) =>
        AjustadorDeFrameworks.NaoAgeEm(destino).Should().Be(naoAge);

    [Fact]
    public void Le_framework_version_e_dependencias_com_propriedade_resolvida()
    {
        Directory.CreateDirectory(Path.Combine(Repo.Raiz, "pomtest"));
        var pasta = Path.Combine(Repo.Raiz, "pomtest");
        File.WriteAllText(Path.Combine(pasta, "pom.xml"),
            "<project><properties><framework.version>5.050-3</framework.version></properties><dependencies>" +
            "<dependency><artifactId>framework-web</artifactId><version>${framework.version}</version></dependency>" +
            "<dependency><artifactId>framework-integrator-tc</artifactId><version>5.050</version><exclusions><exclusion><artifactId>framework-x</artifactId></exclusion></exclusions></dependency>" +
            "<!-- <dependency><artifactId>framework-comentado</artifactId><version>1</version></dependency> -->" +
            "</dependencies></project>");

        LeitorDePom.LerFrameworkVersion(pasta).Should().Be("5.050-3");
        var deps = LeitorDePom.LerDependenciasFramework(pasta);
        deps.Should().HaveCount(2);
        deps["framework-web"].Should().Be("5.050-3");
        deps["framework-integrator-tc"].Should().Be("5.050");
    }

    [Fact]
    public void Pom_le_versao_do_projeto_e_do_parent()
    {
        var info = LeitorDePom.Interpretar("<project>\n<parent><artifactId>pai</artifactId><version>7</version></parent>\n<artifactId>filho</artifactId>\n<version>8</version>\n</project>");

        info.Versao.Should().Be("8");
        info.Artefato.Should().Be("filho");
        info.PaiVersao.Should().Be("7");
        info.PaiArtefato.Should().Be("pai");
    }

    protected override void Liberar() => _framework.Dispose();

}
