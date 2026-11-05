using Menage.Core.Operacoes.CommitPom;
using Menage.Core.Operacoes.DesfazerCommit;
using Menage.Core.Operacoes.Decoradores;
using Menage.Core.Operacoes.Excluir;
using Menage.Core.Operacoes.Limpeza;
using Menage.Core.Operacoes.Push;
using Menage.Core.Estado;
using Menage.Tests.NucleoGit;

namespace Menage.Tests.OperacoesLote;

public sealed class LimpezaEDesfazerTests : NucleoGitBase
{
    LimpezaEmLote Limpeza() =>
        new(Projetos, Dialogos, Progresso, GitExec, Consultas, new ExclusorDeRefs(GitExec, Consultas, RegrasDeProtecao.Padrao), RegrasDeProtecao.Padrao);

    DesfazerCommitEmLote Desfazer() => new(Projetos, Dialogos, Progresso, GitExec, Consultas);

    CommitPomEmLote Pom() => new(Projetos, Dialogos, Progresso, GitExec, Consultas, new EnvioParaOrigin(GitExec, RegrasDeProtecao.Padrao));

    string[] Branches() => Repo.Rodar("branch", "--format=%(refname:short)").Split('\n');

    void MesclarNaMain(string branch)
    {
        Repo.Rodar("checkout", "-b", branch);
        Repo.Commitar(branch.Replace('/', '-') + ".txt", "x\n", "trabalho de " + branch);
        Repo.Rodar("checkout", "main");
        Repo.Rodar("merge", "--no-ff", "-m", "merge " + branch, branch);
        Repo.Rodar("push", "origin", "main");
    }

    [Fact]
    public async Task Limpeza_lista_so_as_mescladas_e_ignora_protegidas_e_nao_mescladas()
    {
        MesclarNaMain("feat/done");

        var previa = await Limpeza().ListarAsync([Projeto], new OpcoesDeLimpeza());

        var item = previa.Itens.Should().ContainSingle().Subject;
        item.Alvo.Nome.Should().Be("feat/done");
        item.MarcarPadrao.Should().BeTrue();
        item.Alvo.DispensaBackup.Should().BeTrue();
    }

    [Fact]
    public async Task Limpeza_exclui_so_as_marcadas_e_mescladas_dispensam_backup()
    {
        MesclarNaMain("feat/done");
        MesclarNaMain("feat/outra");
        var opcoes = new OpcoesDeLimpeza();
        var previa = await Limpeza().ListarAsync([Projeto], opcoes);
        var marcada = previa.Itens.Single(i => i.Alvo.Nome == "feat/done").Chave;

        var item = Unico(await Limpeza().ExecutarAsync(opcoes with { Selecionadas = [marcada] }));

        item.Situacao.Should().Be(Situacao.Ok, item.Mensagem);
        Branches().Should().NotContain("feat/done").And.Contain("feat/outra");
        Branches().Should().NotContain(b => b.StartsWith("backup/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Limpeza_sem_selecao_e_recusada()
    {
        var resultado = await Limpeza().ExecutarAsync(new OpcoesDeLimpeza(Selecionadas: []));

        resultado.EhOk.Should().BeFalse();
    }

    [Fact]
    public async Task Desfazer_volta_um_commit_mantendo_as_alteracoes_na_staging()
    {
        Repo.Rodar("checkout", "-b", "feat/d");
        Repo.Commitar("d.txt", "d\n", "feat: d");

        var item = Unico(await Desfazer().ExecutarAsync(new OpcoesDeDesfazer()));

        item.Situacao.Should().Be(Situacao.Ok, item.Mensagem);
        Repo.Rodar("status", "--porcelain").Should().Contain("A  d.txt");
    }

    [Fact]
    public async Task Desfazer_nao_mexe_em_commit_ja_enviado_sem_permissao()
    {
        Repo.Rodar("checkout", "-b", "feat/e");
        Repo.Commitar("e.txt", "e\n", "feat: e");
        Repo.Rodar("push", "-u", "origin", "feat/e");
        var antes = Repo.Rodar("rev-parse", "HEAD");

        var negado = Unico(await Desfazer().ExecutarAsync(new OpcoesDeDesfazer()));
        negado.Situacao.Should().Be(Situacao.Aviso);
        Repo.Rodar("rev-parse", "HEAD").Should().Be(antes);

        var permitido = Unico(await Desfazer().ExecutarAsync(new OpcoesDeDesfazer(EnviadosPermitidos: new HashSet<string> { Projeto.Caminho })));
        permitido.Mensagem.Should().Contain("force");
        Repo.Rodar("rev-parse", "HEAD").Should().NotBe(antes);
    }

    [Fact]
    public async Task Desfazer_ignora_se_o_head_mudou_depois_da_previa()
    {
        Repo.Rodar("checkout", "-b", "feat/f");
        Repo.Commitar("f.txt", "f\n", "feat: f");
        var antes = Repo.Rodar("rev-parse", "HEAD");

        var item = Unico(await Desfazer().ExecutarAsync(new OpcoesDeDesfazer(Esperados: new Dictionary<string, string> { [Projeto.Caminho] = "0000000" })));

        item.Situacao.Should().Be(Situacao.Erro);
        Repo.Rodar("rev-parse", "HEAD").Should().Be(antes);
    }

    [Fact]
    public async Task Commit_pom_leva_so_o_pom_e_deixa_o_resto()
    {
        Repo.Rodar("checkout", "-b", "feat/pom");
        Repo.Commitar("pom.xml", "<project/>\n", "pom inicial");
        Escrever("pom.xml", "<project>novo</project>\n");
        Escrever("README.md", "mexido\n");

        var item = Unico(await Pom().ExecutarAsync(new OpcoesDeCommitPom()));

        item.Situacao.Should().Be(Situacao.Ok, item.Mensagem);
        Assunto().Should().Be("pom.xml");
        Repo.Rodar("status", "--porcelain").Should().Be(" M README.md");
    }

    [Fact]
    public async Task Commit_pom_sem_alteracao_no_pom_avisa()
    {
        Escrever("README.md", "mexido\n");

        var item = Unico(await Pom().ExecutarAsync(new OpcoesDeCommitPom()));

        item.Situacao.Should().Be(Situacao.Aviso);
    }
}
