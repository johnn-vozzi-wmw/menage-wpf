using Menage.Core.Operacoes.Pull;
using Menage.Core.Operacoes.Status;
using Menage.Core.Operacoes.Sync;

namespace Menage.Tests.NucleoGit;

public sealed class FetchPullSyncTests : NucleoGitBase
{
    FetchEmLote Fetch() => new(Projetos, Dialogos, Progresso, GitExec);

    PullEmLote Pull() => new(Projetos, Dialogos, Progresso, GitExec, Consultas);

    SyncEmLote Sync() => new(Projetos, Dialogos, Progresso, GitExec, Consultas, Guarda);

    [Fact]
    public async Task Fetch_sem_a_opcao_nao_baixa_tag_solta_e_com_ela_baixa_mantendo_a_tag_local()
    {
        var solto = Repo.Rodar("commit-tree", "HEAD^{tree}", "-m", "solto");
        Repo.Rodar("tag", "so-no-origin", solto);
        Repo.Rodar("push", "origin", "so-no-origin");
        Repo.Rodar("tag", "-d", "so-no-origin");
        Repo.Rodar("gc", "--prune=now", "--quiet"); // o commit solto só pode existir no origin
        Repo.Rodar("tag", "so-local");

        await Fetch().ExecutarAsync(new OpcoesDeFetch());
        Repo.Rodar("tag", "-l", "so-no-origin").Should().BeEmpty();

        var item = Unico(await Fetch().ExecutarAsync(new OpcoesDeFetch(Tags: true)));

        item.Situacao.Should().Be(Situacao.Ok);
        Repo.Rodar("tag", "-l", "so-no-origin").Should().Be("so-no-origin");
        Repo.Rodar("tag", "-l", "so-local").Should().Be("so-local");
    }

    [Fact]
    public async Task Pull_traz_o_commit_do_origin()
    {
        Repo.AvancarOrigin();

        var item = Unico(await Pull().ExecutarAsync(new OpcoesDePull()));

        item.Situacao.Should().Be(Situacao.Ok);
        item.Mensagem.Should().Be("pull concluído em main");
        Assunto().Should().Be("Commit remoto");
    }

    [Fact]
    public async Task Status_limpo_e_ok_e_com_alteracoes_e_aviso()
    {
        var status = new StatusEmLote(Projetos, Dialogos, Progresso, GitExec);
        Unico(await status.ExecutarAsync(new OpcoesDeStatus())).Mensagem.Should().Be("sem alterações");

        Escrever("README.md", "x\n");

        var item = Unico(await status.ExecutarAsync(new OpcoesDeStatus()));
        item.Situacao.Should().Be(Situacao.Aviso);
        item.Mensagem.Should().Be("1 arquivo(s) alterado(s)");
    }

    [Fact]
    public async Task Parar_apos_o_atual_deixa_os_restantes_nao_executados()
    {
        var outro = new Projeto("outro", Repo.Raiz);
        Projetos.Adicionar(outro);
        Projetos.Marcar(outro.Caminho, true);
        using var parar = new CancellationTokenSource();
        await parar.CancelAsync();

        var r = await Fetch().ExecutarAsync(new OpcoesDeFetch(), new SinaisDoLote(default, parar.Token));

        r.Valor.Interrompido.Should().BeTrue();
        r.Valor.Itens.Should().BeEmpty();
        r.Valor.NaoExecutados.Should().Equal("projeto-teste", "outro");
    }

    [Fact]
    public async Task Sync_traz_a_novidade_e_preserva_as_alteracoes_locais()
    {
        Repo.AvancarOrigin();
        Escrever("README.md", "alterado local\n");
        Escrever("rascunho.txt", "r\n");

        var item = Unico(await Sync().ExecutarAsync(new OpcoesDeSync()));

        item.Situacao.Should().Be(Situacao.Ok);
        item.Mensagem.Should().Contain("main atualizada").And.Contain("preservadas");
        File.Exists(Arquivo("remoto.txt")).Should().BeTrue();
        File.ReadAllText(Arquivo("README.md")).Should().StartWith("alterado local");
        File.Exists(Arquivo("rascunho.txt")).Should().BeTrue();
        Repo.Rodar("stash", "list").Should().BeEmpty();
    }

    [Fact]
    public async Task Sync_sem_alteracoes_nao_menciona_preservacao()
    {
        var item = Unico(await Sync().ExecutarAsync(new OpcoesDeSync()));

        item.Mensagem.Should().Be("main atualizada");
    }

    [Fact]
    public async Task Sync_em_detached_avisa_e_nao_mexe()
    {
        Repo.Rodar("checkout", "--detach");

        Unico(await Sync().ExecutarAsync(new OpcoesDeSync())).Situacao.Should().Be(Situacao.Aviso);
    }

    [Fact]
    public async Task Fetch_silencioso_baixa_sem_falhas()
    {
        Repo.AvancarOrigin();

        var falhas = await new FetchSilencioso(GitExec).ExecutarAsync([Projeto]);

        falhas.Should().Be(0);
        Repo.Rodar("rev-list", "--count", "main..origin/main").Should().Be("1");
    }

    [Fact]
    public async Task Fetch_silencioso_conta_a_falha_de_um_remoto_invalido_e_ignora_pasta_sem_git()
    {
        Repo.Rodar("remote", "set-url", "origin", Path.Combine(Repo.Raiz, "nao-existe.git"));
        var vazia = new Projeto("vazia", Path.Combine(Repo.Raiz, "vazia"));

        (await new FetchSilencioso(GitExec).ExecutarAsync([Projeto, vazia])).Should().Be(1);
    }
}
