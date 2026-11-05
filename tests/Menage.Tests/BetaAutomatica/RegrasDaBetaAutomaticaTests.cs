using Menage.Core.BetaAutomatica;

namespace Menage.Tests.BetaAutomatica;

public sealed class LeitorDeComentarioTests
{
    static readonly string[] Conhecidos = ["framework-util", "framework-core", "framework-tc", "lavenderepda", "lavenderecore", "lavenderedados", "lavendereweb"];

    [Fact]
    public void Le_branch_e_projetos_alterados_com_negrito_do_Jira()
    {
        var c = LeitorDeComentario.Ler("*BRANCH:* `VENDAS-44220`\n*PROJETOS ALTERADOS:* core, wmwvendas e fm-tc");

        c.Branch.Should().Be("VENDAS-44220");
        c.Projetos.Should().Equal("core", "wmwvendas", "fm-tc");
        c.Forte.Should().BeTrue();
    }

    [Fact]
    public void Id_na_linha_de_projetos_vira_a_branch_e_nao_projeto()
    {
        var c = LeitorDeComentario.Ler("PROJETOS ALTERADOS: VENDAS-123");

        c.Branch.Should().Be("VENDAS-123");
        c.Projetos.Should().BeEmpty();
    }

    [Fact]
    public void Link_de_pull_request_aponta_o_projeto_alterado()
    {
        var c = LeitorDeComentario.Ler("revisar https://github.com/wmwsystems/framework-tc/pull/3/changes");

        c.Projetos.Should().Equal("framework-tc");
        c.Forte.Should().BeTrue();
    }

    [Fact]
    public void Id_solto_e_so_um_palpite_fraco()
    {
        var c = LeitorDeComentario.Ler("ajustei o VENDAS-9 hoje");

        c.Branch.Should().Be("VENDAS-9");
        c.Forte.Should().BeFalse();
    }

    [Fact]
    public void Siglas_e_curingas_sao_expandidos()
    {
        var projetos = LeitorDeComentario.Expandir(["core", "wmwdados", "FRAMEWORK-XXXX"], Conhecidos);

        projetos.Select(p => p.Nome).Should().Contain(["lavenderecore", "lavenderedados", "framework-util", "framework-core", "framework-tc"]);
        projetos.Single(p => p.Nome == "lavenderecore").Explicito.Should().BeTrue();
        projetos.Single(p => p.Nome == "framework-util").Explicito.Should().BeFalse();
        projetos.Select(p => p.Nome).Should().NotContain("lavendereweb");
    }

    [Fact]
    public void Nome_desconhecido_aparece_para_o_usuario_ver()
    {
        var projetos = LeitorDeComentario.Expandir(["projeto-novo"], Conhecidos);

        projetos.Should().ContainSingle().Which.Explicito.Should().BeTrue();
    }
}

public sealed class OrdemDeProjetosTests
{
    [Fact]
    public void Segue_a_ordem_de_dependencia_e_manda_desconhecidos_para_o_fim()
    {
        var ordem = OrdemDeProjetos.Ordenar(["lavendereweb", "x-novo", "lavenderecore", "framework-util", "lavenderedados"], null, out var sem);

        ordem.Should().Equal("framework-util", "lavenderecore", "lavenderedados", "lavendereweb", "x-novo");
        sem.Should().Equal("x-novo");
    }

    [Fact]
    public void Ordem_escolhida_e_incorporada_depois_do_que_veio_antes()
    {
        var nova = OrdemDeProjetos.Incorporar(null, ["lavenderecore", "x-novo", "lavendereweb"]);

        nova.IndexOf("x-novo").Should().Be(nova.IndexOf("lavenderecore") + 1);
    }
}

public sealed class EscopoBetaTests
{
    static readonly ConfigBetaAuto Config = new();

    static IssueJira Issue(string chave = "VENDAS-1", string responsavel = "eu", bool subtask = false, string tipo = "Story")
        => new() { Chave = chave, Status = Config.StatusAlvo, Responsavel = responsavel, EhSubtask = subtask, Tipo = tipo };

    static MudancaJira Status(string de, string para, DateTime quando) => new(quando, "status", "", "", de, para, "x");

    [Fact]
    public void Dev_subtask_de_coding_minha_entra()
    {
        var i = Issue(subtask: true, tipo: "Coding");

        EscopoBeta.Avaliar(i, "eu", Config).Entra.Should().BeTrue();
        EscopoBeta.Avaliar(i, "outra", Config).Entra.Should().BeFalse();
    }

    [Fact]
    public void Dev_comentario_meu_forte_entra_e_de_outro_nao()
    {
        var i = Issue(responsavel: "outra");
        i.Comentarios.Add(new ComentarioJira("1", "eu", "BRANCH: VENDAS-5", DateTime.Now));

        var escopo = EscopoBeta.Avaliar(i, "eu", Config);

        escopo.Entra.Should().BeTrue();
        escopo.Comentario!.Branch.Should().Be("VENDAS-5");
        EscopoBeta.Avaliar(i, "terceiro", Config).Entra.Should().BeFalse();
    }

    [Fact]
    public void Dev_passagem_minha_de_desenvolvimento_para_revisao_entra_mesmo_com_novo_dono()
    {
        var i = Issue(responsavel: "outra");
        i.Historico.Add(Status(Config.StatusDev, Config.StatusRevisao, new DateTime(2026, 1, 2)));
        i.Historico.Add(new MudancaJira(new DateTime(2026, 1, 1), "assignee", "", "", "", "", "x") with { De = "eu", Para = "eu" });
        i.Historico.Add(new MudancaJira(new DateTime(2026, 1, 3), "assignee", "eu", "outra", "", "", "x"));

        EscopoBeta.Avaliar(i, "eu", Config).Entra.Should().BeTrue();
    }

    [Fact]
    public void Sem_comentario_a_branch_e_o_nome_da_atividade_ou_da_historia()
    {
        var sub = Issue("VENDAS-2", subtask: true, tipo: "Coding");
        sub.Pai = "VENDAS-1";

        EscopoBeta.Avaliar(sub, "eu", Config).Comentario!.Branch.Should().Be("VENDAS-1");
        EscopoBeta.Avaliar(sub, "eu", Config).Comentario!.PelaChave.Should().BeTrue();
    }

    [Fact]
    public void Tester_so_conta_quem_veio_de_code_review_e_respeita_o_escopo()
    {
        var i = Issue(responsavel: "outra");
        i.Historico.Add(Status(Config.StatusCodeReview, Config.StatusAlvo, DateTime.Now));
        i.Comentarios.Add(new ComentarioJira("1", "dev", "BRANCH: VENDAS-7", DateTime.Now));

        EscopoBeta.AvaliarTester(i, "eu", Config).Entra.Should().BeFalse("o escopo padrão é somente atribuídas a mim");
        var todas = new ConfigBetaAuto { TesterEscopo = "todas" };
        var escopo = EscopoBeta.AvaliarTester(i, "eu", todas);
        escopo.Entra.Should().BeTrue();
        escopo.Comentario!.Branch.Should().Be("VENDAS-7", "o tester lê o comentário de qualquer pessoa");

        var deOutroStatus = Issue();
        deOutroStatus.Historico.Add(Status("In Testing", Config.StatusAlvo, DateTime.Now));
        EscopoBeta.AvaliarTester(deOutroStatus, "eu", todas).Entra.Should().BeFalse();
    }
}

public sealed class GerenciadorDeJqlTests
{
    [Fact]
    public void Expande_os_marcadores()
    {
        var cfg = new ConfigBetaAuto { ChavesJira = "VENDAS, APP" };

        GerenciadorDeJql.Expandir("{projeto} AND status = \"{status}\"{escopo}", cfg, tester: true)
            .Should().Be("project in (VENDAS,APP) AND status = \"Waiting for Testing\" AND assignee = currentUser()");
        GerenciadorDeJql.Expandir("{projeto} AND status = \"{status}\"{escopo}", new ConfigBetaAuto { ChavesJira = "", TesterEscopo = "todas" }, tester: true)
            .Should().Be("status = \"Waiting for Testing\"");
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("status = \"A\" AND (x = 1", true)]
    [InlineData("status = \"A", true)]
    [InlineData("{foo} AND status = 1", true)]
    [InlineData("status = 1 ORDER BY updated AND x = 1", true)]
    public void Valida_erros(string jql, bool erro)
    {
        var r = GerenciadorDeJql.Avaliar(jql);

        r.Erro.Should().Be(erro);
        r.Mensagem.Should().NotBeNull();
    }

    [Fact]
    public void Avisa_sem_status_ou_sem_data_e_aceita_a_padrao()
    {
        GerenciadorDeJql.Avaliar("assignee = currentUser()").Mensagem.Should().Contain("status");
        GerenciadorDeJql.Avaliar("status = 1").Mensagem.Should().Contain("data");
        foreach (var padrao in GerenciadorDeJql.Padrao(false).Concat(GerenciadorDeJql.Padrao(true)))
            GerenciadorDeJql.Avaliar(padrao.Jql).Mensagem.Should().BeNull(padrao.Nome);
    }

    [Fact]
    public void Grava_so_o_que_difere_do_padrao()
    {
        var cfg = new ConfigBetaAuto();
        var efetivas = GerenciadorDeJql.Efetivas(cfg, false);
        GerenciadorDeJql.ParaGravar(cfg, efetivas, false).Should().BeEmpty("restaurar não deixa nada gravado");

        efetivas[0].Ativa = false;
        efetivas.Add(new JqlBeta { Nome = "minha", Jql = "status = 1 AND updated >= -1d" });
        var gravar = GerenciadorDeJql.ParaGravar(cfg, efetivas, false);
        gravar.Should().HaveCount(2);

        cfg.Jqls = gravar;
        GerenciadorDeJql.Efetivas(cfg, false).Should().HaveCount(3);
        GerenciadorDeJql.Consultas(cfg, false).Should().HaveCount(2, "a desligada não roda");
    }

    [Fact]
    public void Sem_nenhuma_ativa_usa_as_padrao()
    {
        var cfg = new ConfigBetaAuto();
        cfg.Jqls = [.. GerenciadorDeJql.Padrao(false).Select(p => new JqlBeta { Id = p.Id, Padrao = true, Ativa = false })];

        GerenciadorDeJql.Consultas(cfg, false).Should().HaveCount(2);
    }
}
