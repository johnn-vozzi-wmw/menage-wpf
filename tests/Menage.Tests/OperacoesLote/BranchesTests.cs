using Menage.Core.Operacoes.ComDevelop;
using Menage.Core.Operacoes.Decoradores;
using Menage.Core.Operacoes.Excluir;
using Menage.Core.Operacoes.Renomear;
using Menage.Tests.NucleoGit;

namespace Menage.Tests.OperacoesLote;

public sealed class BranchesTests : NucleoGitBase
{
    ExcluirEmLote Excluir(IRegrasDeProtecao? regras = null) =>
        new(Projetos, Dialogos, Progresso, new ExclusorDeRefs(GitExec, Consultas, regras ?? RegrasDeProtecao.Padrao), Consultas);

    RenomearBranchEmLote Renomear(IRegrasDeProtecao? regras = null) =>
        new(Projetos, Dialogos, Progresso, GitExec, Consultas, regras ?? RegrasDeProtecao.Padrao);

    ComDevelopEmLote ComDevelop() => new(Projetos, Dialogos, Progresso, GitExec, Consultas, Guarda, Resolvedor);

    static OpcoesDeExclusao Opcoes(string nome, bool remoto = false, bool backup = true, bool extra = false) =>
        new([nome], Local: true, Remoto: remoto, Backup: backup, ConfirmouExtra: extra);

    string[] Branches() => Repo.Rodar("branch", "--format=%(refname:short)").Split('\n');

    [Fact]
    public async Task Excluir_local_faz_backup_antes_de_apagar()
    {
        var sha = Repo.Rodar("rev-parse", "feature/exemplo");

        var item = Unico(await Excluir().ExecutarAsync(Opcoes("feature/exemplo")));

        item.Situacao.Should().Be(Situacao.Ok, item.Mensagem);
        Branches().Should().NotContain("feature/exemplo");
        var backup = Branches().Single(b => b.StartsWith("backup/feature-exemplo-", StringComparison.Ordinal));
        Repo.Rodar("rev-parse", backup).Should().Be(sha);
    }

    [Fact]
    public async Task Excluir_remoto_apaga_no_origin_e_envia_o_backup()
    {
        var item = Unico(await Excluir(new RegrasDeProtecao([])).ExecutarAsync(Opcoes("feature/exemplo", remoto: true)));

        item.Situacao.Should().Be(Situacao.Ok, item.Mensagem);
        Repo.Rodar("ls-remote", "--heads", "origin", "feature/exemplo").Should().BeEmpty();
        Repo.Rodar("ls-remote", "--heads", "origin", "backup/*").Should().NotBeEmpty();
    }

    [Fact]
    public async Task Branch_protegida_e_bloqueada_e_continua_la()
    {
        var item = Unico(await Excluir().ExecutarAsync(Opcoes("develop", extra: true)));

        item.Situacao.Should().Be(Situacao.Aviso);
        item.Mensagem.Should().Contain("BLOQUEADA");
        Branches().Should().Contain("develop");
    }

    [Fact]
    public async Task Branch_atual_nao_e_excluida_localmente()
    {
        Repo.Rodar("checkout", "feature/exemplo");

        var item = Unico(await Excluir(new RegrasDeProtecao([])).ExecutarAsync(Opcoes("feature/exemplo")));

        item.Mensagem.Should().Contain("branch atual");
        Branches().Should().Contain("feature/exemplo");
    }

    [Fact]
    public async Task Branch_sensivel_ou_sem_backup_exige_confirmacao_extra()
    {
        var sensivel = await Excluir().ExecutarAsync(Opcoes("develop"));
        var semBackup = await Excluir().ExecutarAsync(Opcoes("feature/exemplo", backup: false));

        sensivel.EhOk.Should().BeFalse();
        semBackup.EhOk.Should().BeFalse();
        Branches().Should().Contain(["develop", "feature/exemplo"]);
    }

    [Fact]
    public async Task Tag_e_excluida_quando_o_nome_nao_e_branch()
    {
        Repo.Rodar("tag", "v1.0");

        var item = Unico(await Excluir().ExecutarAsync(Opcoes("v1.0")));

        item.Situacao.Should().Be(Situacao.Ok, item.Mensagem);
        Repo.Rodar("tag", "--list", "v1.0").Should().BeEmpty();
        Branches().Should().Contain(b => b.StartsWith("backup/tag-v1.0-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Renomear_publica_o_nome_novo_e_apaga_o_antigo_no_origin()
    {
        Repo.Rodar("checkout", "feature/exemplo");

        var item = Unico(await Renomear().ExecutarAsync(new OpcoesDeRenomear("feature/novo", PublicarNoOrigin: true)));

        item.Situacao.Should().Be(Situacao.Ok, item.Mensagem);
        Repo.BranchAtual.Should().Be("feature/novo");
        Repo.Rodar("ls-remote", "--heads", "origin", "feature/novo").Should().NotBeEmpty();
        Repo.Rodar("ls-remote", "--heads", "origin", "feature/exemplo").Should().BeEmpty();
    }

    [Fact]
    public async Task Renomear_para_nome_que_ja_existe_e_erro()
    {
        Repo.Rodar("checkout", "feature/exemplo");

        var item = Unico(await Renomear().ExecutarAsync(new OpcoesDeRenomear("develop")));

        item.Situacao.Should().Be(Situacao.Erro);
        Repo.BranchAtual.Should().Be("feature/exemplo");
    }

    [Fact]
    public async Task Com_develop_traz_a_develop_atualizada_do_origin_para_a_branch_atual()
    {
        Repo.Rodar("checkout", "develop");
        Repo.Commitar("dev.txt", "d\n", "develop novo");
        Repo.Rodar("push", "origin", "develop");
        Repo.Rodar("checkout", "feature/exemplo");

        var item = Unico(await ComDevelop().ExecutarAsync(new OpcoesDeComDevelop()));

        item.Situacao.Should().Be(Situacao.Ok, item.Mensagem);
        File.Exists(Arquivo("dev.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task Com_develop_com_conflito_e_sem_decisao_aborta_e_deixa_o_repositorio_limpo()
    {
        Repo.Rodar("checkout", "develop");
        Repo.Commitar("c.txt", "da develop\n", "dev c");
        Repo.Rodar("push", "origin", "develop");
        Repo.Rodar("checkout", "feature/exemplo");
        Repo.Commitar("c.txt", "da feature\n", "feat c");

        var item = Unico(await ComDevelop().ExecutarAsync(new OpcoesDeComDevelop()));

        item.Situacao.Should().Be(Situacao.Erro);
        Repo.Rodar("status", "--porcelain").Should().BeEmpty();
        File.ReadAllText(Arquivo("c.txt")).Should().Be("da feature\n");
    }
}
