using Menage.Core.Estado;
using Menage.Core.Operacoes.Commit;
using Menage.Core.Operacoes.Decoradores;
using Menage.Core.Operacoes.Push;
using Menage.Core.Protecao;
using Menage.Tests.NucleoGit;

namespace Menage.Tests.OperacoesLote;

public sealed class CommitEPushTests : NucleoGitBase
{
    CommitEmLote Commit() => new(Projetos, Dialogos, Progresso, GitExec, new LeitorDeEstado(GitExec), Envio(RegrasDeProtecao.Padrao));

    PushEmLote Push(IRegrasDeProtecao? regras = null) =>
        new(Projetos, Dialogos, Progresso, GitExec, new LeitorDeEstado(GitExec), regras ?? RegrasDeProtecao.Padrao, Envio(regras ?? RegrasDeProtecao.Padrao));

    EnvioParaOrigin Envio(IRegrasDeProtecao regras) => new(GitExec, regras);

    string UltimaMensagem => Repo.Rodar("log", "-1", "--format=%B");

    [Fact]
    public async Task Commit_so_leva_versionados_por_padrao_e_novos_com_a_opcao()
    {
        Repo.Rodar("checkout", "-b", "feat");
        Escrever("README.md", "alterado\n");
        Escrever("novo.txt", "novo\n");

        var item = Unico(await Commit().ExecutarAsync(new OpcoesDeCommit("feat: mexe no readme\n\ncorpo")));

        item.Situacao.Should().Be(Situacao.Ok);
        UltimaMensagem.Should().Contain("corpo");
        Repo.Rodar("status", "--porcelain").Should().Be("?? novo.txt");

        var segundo = Unico(await Commit().ExecutarAsync(new OpcoesDeCommit("feat: novo", IncluirNovos: true)));
        segundo.Situacao.Should().Be(Situacao.Ok);
        Repo.Rodar("status", "--porcelain").Should().BeEmpty();
    }

    [Fact]
    public async Task Commit_com_push_publica_a_branch_nova()
    {
        Repo.Rodar("checkout", "-b", "feat/nova");
        Escrever("README.md", "x\n");

        var item = Unico(await Commit().ExecutarAsync(new OpcoesDeCommit("fix: x", Push: true)));

        item.Situacao.Should().Be(Situacao.Ok, item.Mensagem);
        item.Dados!.Enviado.Should().BeTrue();
        Repo.Rodar("ls-remote", "--heads", "origin", "feat/nova").Should().NotBeEmpty();
    }

    [Fact]
    public async Task Commit_ignora_detached_e_operacao_em_andamento()
    {
        Repo.Rodar("checkout", "--detach");
        Escrever("README.md", "x\n");

        var item = Unico(await Commit().ExecutarAsync(new OpcoesDeCommit("fix: x")));

        item.Situacao.Should().Be(Situacao.Aviso);
        item.Mensagem.Should().Contain("detached");
    }

    [Fact]
    public async Task Hook_que_recusa_vira_erro_do_projeto()
    {
        Repo.Rodar("checkout", "-b", "feat");
        var hook = Path.Combine(Repo.Caminho, ".git", "hooks", "pre-commit");
        File.WriteAllText(hook, "#!/bin/sh\necho \"proibido pelo hook\" >&2\nexit 1\n");
        Escrever("README.md", "x\n");

        var item = Unico(await Commit().ExecutarAsync(new OpcoesDeCommit("fix: x")));

        item.Situacao.Should().Be(Situacao.Erro);
        item.Mensagem.Should().Contain("commit falhou");
    }

    [Fact]
    public async Task Convencao_de_commit_ligada_recusa_mensagem_fora_do_padrao()
    {
        var protecao = new ProtecaoDeBranches(new MemoriaDeProtecao(new ConfigDeProtecao { ValidarCommit = true }));
        var executor = new ExecutorComConvencaoDeCommit(new ExecutorDeOperacoes(), protecao);
        Escrever("README.md", "x\n");

        var resultado = await executor.ExecutarAsync(Commit(), new OpcoesDeCommit("mensagem solta"));

        resultado.EhOk.Should().BeFalse();
        Repo.Rodar("status", "--porcelain").Should().Contain("README.md");
    }

    [Fact]
    public void Ticket_e_sugerido_a_partir_da_branch()
    {
        SugestaoDeTicket.DaBranch("feature/vendas-123-tela").Should().Be("VENDAS-123: ");
        SugestaoDeTicket.DaBranch("main").Should().BeNull();
    }

    [Fact]
    public async Task Push_envia_commits_novos_e_publica_branch_nova()
    {
        Repo.Rodar("checkout", "-b", "feat/p");
        Repo.Commitar("p.txt", "p\n", "feat: p");

        var item = Unico(await Push().ExecutarAsync(new OpcoesDePush()));

        item.Situacao.Should().Be(Situacao.Ok, item.Mensagem);
        item.Dados!.Publicada.Should().BeTrue();
        Repo.Rodar("ls-remote", "--heads", "origin", "feat/p").Should().NotBeEmpty();
    }

    [Fact]
    public async Task Push_nao_envia_quando_o_origin_tem_commits_novos()
    {
        Repo.AvancarOrigin();
        Repo.Commitar("local.txt", "l\n", "local");
        var antes = Repo.Rodar("ls-remote", "origin", "main");

        var item = Unico(await Push(new RegrasDeProtecao([])).ExecutarAsync(new OpcoesDePush()));

        item.Situacao.Should().Be(Situacao.Erro);
        item.Mensagem.Should().Contain("origin tem 1 commit");
        Repo.Rodar("ls-remote", "origin", "main").Should().Be(antes);
    }

    [Fact]
    public async Task Push_respeita_a_protecao_de_branches_e_nao_envia()
    {
        Repo.Commitar("local.txt", "l\n", "local na main");
        var antes = Repo.Rodar("ls-remote", "origin", "main");

        var item = Unico(await Push().ExecutarAsync(new OpcoesDePush()));

        item.Situacao.Should().Be(Situacao.Aviso);
        item.Mensagem.Should().Contain("protegida");
        Repo.Rodar("ls-remote", "origin", "main").Should().Be(antes);
    }

    [Fact]
    public async Task Push_forcado_so_roda_depois_da_confirmacao()
    {
        Repo.Rodar("checkout", "-b", "feat/f");
        Repo.Commitar("f.txt", "f\n", "feat: f");
        Repo.Rodar("push", "-u", "origin", "feat/f");
        Repo.Rodar("commit", "--amend", "-m", "feat: f reescrito");
        Dialogos.RespostaDaConfirmacao = false;

        var negado = await Push().ExecutarAsync(new OpcoesDePush(Forcar: true));
        negado.EhOk.Should().BeFalse();
        Dialogos.Previas.Should().ContainSingle(p => p.Titulo == "Push forçado");

        Dialogos.RespostaDaConfirmacao = true;
        var item = Unico(await Push().ExecutarAsync(new OpcoesDePush(Forcar: true)));
        item.Situacao.Should().Be(Situacao.Ok, item.Mensagem);
    }
}

sealed class MemoriaDeProtecao(ConfigDeProtecao config) : IArmazenamento<ConfigDeProtecao>
{
    public string Arquivo => "memoria";

    public Resultado<ConfigDeProtecao> Carregar() => Resultado.Ok(config);

    public Resultado<Nada> Salvar(ConfigDeProtecao dados) => Resultado.Ok();
}
