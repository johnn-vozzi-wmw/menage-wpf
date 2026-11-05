using System.Globalization;
using Menage.Core.Melhorias;
using Menage.Core.Operacoes.Checkout;
using Menage.TestKit;

namespace Menage.Tests.Melhorias;

sealed class BancoFalso : IContextoDeBanco
{
    public List<PerfilDeBancoRef> Lista { get; } = [];

    public string? Ativo { get; set; }

    public List<string> Aplicados { get; } = [];

    public IReadOnlyList<PerfilDeBancoRef> Perfis => Lista;

    public PerfilDeBancoRef? Achar(string id) => Lista.FirstOrDefault(p => p.Id == id);

    public PerfilDeBancoRef? AtivoNoContexto() => Ativo is null ? null : Achar(Ativo);

    public Task<Resultado<Nada>> AplicarAsync(string perfilId, CancellationToken cancelar = default)
    {
        Aplicados.Add(perfilId);
        Ativo = perfilId;
        return Task.FromResult(Resultado.Ok());
    }
}

public sealed class FiltroDeMelhoriasTests
{
    static Melhoria M(string id, string titulo, string status = Melhoria.Andamento, string tags = "", string uso = "", params string[] projetos) => new()
    {
        Id = id, Titulo = titulo, Status = status, Tags = Melhoria.InterpretarTags(tags), UltimoUso = uso, Branches = [.. projetos.Select(p => new BranchDaMelhoria { Projeto = p, Branch = id })],
    };

    static readonly BancoFalso Banco = new() { Lista = { new("p1", "Producao Lebon", "oracle"), new("p2", "Local", "postgres") } };

    static List<Melhoria> Buscar(string q, params Melhoria[] todas) => FiltroDeMelhorias.Parse(q).Aplicar(todas, "VENDAS-1", Banco);

    [Fact]
    public void Tags_sao_normalizadas_sem_acento_espaco_vira_hifen()
    {
        Melhoria.InterpretarTags("Urgente, Cliente Lebon; ÁREA  x, urgente").Should().Equal("urgente", "cliente-lebon", "area-x");
    }

    [Fact]
    public void Operadores_combinam_com_E_e_negam()
    {
        var a = M("VENDAS-1", "Pedido", tags: "urgente", projetos: "lavendereweb");
        var b = M("VENDAS-2", "Cliente", Melhoria.Pausada, "urgente,lebon", projetos: "lavenderepda");
        var c = M("VENDAS-3", "Relatorio", Melhoria.Concluida);

        Buscar("tag:urgente", a, b, c).Should().Equal(a, b);
        Buscar("tag:urgente -tag:lebon", a, b, c).Should().Equal(a);
        Buscar("tag:x,lebon status:pausada", a, b, c).Should().Equal(b);
        Buscar("projeto:web", a, b, c).Should().Equal(a);
        Buscar("sem:tag", a, b, c).Should().Equal(c);
        Buscar("ativa:sim", a, b, c).Should().Equal(a);
        Buscar("\"relatorio\" status:concluidas", a, b, c).Should().Equal(c);
        Buscar("-pedido", a, b, c).Should().Equal(b, c);
    }

    [Fact]
    public void Busca_por_uso_e_por_banco()
    {
        var recente = M("VENDAS-1", "A", uso: DateTime.Now.AddDays(-2).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        recente.Perfis = ["p1"];
        var antiga = M("VENDAS-2", "B", uso: DateTime.Now.AddDays(-40).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        antiga.Perfis = ["p2"];
        var nunca = M("VENDAS-3", "C");

        Buscar("usada:7", recente, antiga, nunca).Should().Equal(recente);
        Buscar("usada:nunca", recente, antiga, nunca).Should().Equal(nunca);
        Buscar("banco:oracle", recente, antiga, nunca).Should().Equal(recente);
        Buscar("banco:lebon", recente, antiga, nunca).Should().Equal(recente);
        Buscar("banco:postgresql", recente, antiga, nunca).Should().Equal(antiga);
        Buscar("sem:banco", recente, antiga, nunca).Should().Equal(nunca);
    }

    [Fact]
    public void Alternar_e_trocar_valor_reescrevem_a_busca()
    {
        FiltroDeMelhorias.Alternar("texto", "tag", "a", false).Should().Be("texto tag:a");
        FiltroDeMelhorias.Alternar("texto tag:a", "tag", "a", false).Should().Be("texto");
        FiltroDeMelhorias.Alternar("status:pausada", "status", "concluida", exclusivo: true).Should().Be("status:concluida");
        FiltroDeMelhorias.TrocarValor("-tag:a,b x", "tag", "a", "z").Should().Be("-tag:z,b x");
    }

    [Fact]
    public void Id_no_commit_nao_confunde_numeros_maiores()
    {
        var p = Melhoria.PadraoDeCommit("VENDAS-24574");

        p.IsMatch("VENDAS-24574 ajuste").Should().BeTrue();
        p.IsMatch("vendas-24574: x").Should().BeTrue();
        p.IsMatch("VENDAS-245741 outro").Should().BeFalse();
        p.IsMatch("XVENDAS-24574").Should().BeFalse();
    }
}

public sealed class RepositorioDeMelhoriasTests : IDisposable
{
    readonly string _pasta = Path.Combine(Path.GetTempPath(), "mh" + Guid.NewGuid().ToString("N")[..6]);

    RepositorioDeMelhorias Criar() => new(new Armazenamento<ConfigMelhorias>(new PastaDeDados(_pasta), "melhorias", padrao: ConfigMelhorias.Padrao), new BancoFalso());

    public void Dispose()
    {
        if (Directory.Exists(_pasta)) Directory.Delete(_pasta, true);
    }

    [Fact]
    public void Grava_valida_persiste_e_nao_exclui_a_ativa()
    {
        var r = Criar();
        r.Gravar(new Melhoria { Id = "" }, true).EhOk.Should().BeFalse();
        r.Gravar(new Melhoria { Id = "VENDAS-1", Tags = ["Urgente X"], Branches = [new() { Projeto = "a", Branch = "VENDAS-1" }] }, true).EhOk.Should().BeTrue();
        r.Gravar(new Melhoria { Id = "VENDAS-1" }, true).EhOk.Should().BeFalse("id repetido");
        r.Gravar(new Melhoria { Id = "VENDAS-2", Branches = [new() { Projeto = "a", Branch = "" }] }, true).EhOk.Should().BeFalse("branch vazia");

        Criar().Achar("VENDAS-1")!.Tags.Should().Equal("urgente-x");
        r.Config.Ativa = "VENDAS-1";
        r.Excluir("VENDAS-1").EhOk.Should().BeFalse();
        r.Config.Ativa = "";
        r.Excluir("VENDAS-1").EhOk.Should().BeTrue();
    }

    [Fact]
    public void Renomear_tag_junta_duplicadas_leva_cor_e_filtros_salvos()
    {
        var r = Criar();
        r.Gravar(new Melhoria { Id = "A", Tags = ["x", "y"] }, true);
        r.Gravar(new Melhoria { Id = "B", Tags = ["x"] }, true);
        r.SalvarVisao("minha", "tag:x -tag:y");
        r.DefinirCorDaTag("x", "azul");

        r.RenomearTag("x", "y");

        r.Achar("A")!.Tags.Should().Equal("y");
        r.Achar("B")!.Tags.Should().Equal("y");
        r.Config.Filtros.Single().Consulta.Should().Be("tag:y -tag:y");
        r.Config.CoresTags.Should().ContainKey("y").WhoseValue.Should().Be("azul");
        r.Config.CoresTags.Should().NotContainKey("x");
        r.RemoverTag("y");
        r.Achar("A")!.Tags.Should().BeEmpty();
    }

    [Fact]
    public void Visoes_prontas_somem_quando_vazias_e_sqlite_exige_caminho()
    {
        var r = Criar();
        r.Gravar(new Melhoria { Id = "A" }, true);

        r.VisoesProntas().Select(v => v.Nome).Should().Contain(["Todas", "Em andamento", "Sem tags"]).And.NotContain("Pausadas");
        r.ConfigurarSqlite(true).EhOk.Should().BeFalse();
        r.ConfigurarSqlite(true, @"C:\x\wmwvendas.sqlite3").EhOk.Should().BeTrue();
    }
}

/// <summary>Dois repositórios descartáveis como projetos (lavenderepda e lavendereweb) e uma pasta de arquivos em %TEMP%.</summary>
sealed class CenarioDeMelhoria : IDisposable
{
    public RepositorioDescartavel Pda { get; } = RepositorioDescartavel.Criar();

    public RepositorioDescartavel Web { get; } = RepositorioDescartavel.Criar();

    public string Pasta { get; } = Path.Combine(Path.GetTempPath(), "me" + Guid.NewGuid().ToString("N")[..6]);

    public BancoFalso Banco { get; } = new();

    public RepositorioDeMelhorias Repositorio { get; }

    public AtivadorDeMelhorias Ativador { get; }

    public SqliteDeAtividade Sqlite { get; } = new();

    public CommitsDaMelhoria Commits { get; }

    public CenarioDeMelhoria()
    {
        Directory.CreateDirectory(Pasta);
        Pda.Commitar("pom.xml", "<project><version>9.060-SNAPSHOT</version></project>", "pom");
        var git = GitDeTeste.Criar();
        var consultas = new ConsultasGit(git);
        var projetos = new ProjetosEmMemoria([Pda.ComoProjeto("lavenderepda"), Web.ComoProjeto("lavendereweb")]);
        Banco.Lista.AddRange([new("p1", "Producao", "oracle"), new("p2", "Local", "postgres")]);
        Repositorio = new RepositorioDeMelhorias(new Armazenamento<ConfigMelhorias>(new PastaDeDados(Path.Combine(Pasta, "dados")), "melhorias", padrao: ConfigMelhorias.Padrao), Banco);
        Ativador = new AtivadorDeMelhorias(Repositorio, projetos, git, consultas, new AutoStash(git, consultas), Banco, Sqlite);
        Commits = new CommitsDaMelhoria(projetos, git);
    }

    public Melhoria Nova(string id = "VENDAS-100")
    {
        var m = new Melhoria
        {
            Id = id, Perfis = ["p1"],
            Branches = [new() { Projeto = "lavenderepda", Branch = id, Base = "develop" }, new() { Projeto = "lavendereweb", Branch = "feature/exemplo" }],
        };
        Repositorio.Gravar(m, true);
        return m;
    }

    public void Dispose()
    {
        Pda.Dispose();
        Web.Dispose();
        if (!Directory.Exists(Pasta)) return;
        foreach (var f in Directory.EnumerateFiles(Pasta, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(Pasta, true);
    }
}

public sealed class AtivacaoDeMelhoriaTests
{
    [Fact]
    public async Task Previa_mostra_o_que_sera_feito_sem_mexer_em_nada()
    {
        using var c = new CenarioDeMelhoria();
        var m = c.Nova();

        var previa = await c.Ativador.PreviaAsync(m);

        previa.Single(l => l.Projeto == "lavenderepda").Situacao.Should().Contain("será criada a partir de develop");
        previa.Single(l => l.Projeto == "lavendereweb").Situacao.Should().StartWith("trocar");
        c.Pda.BranchAtual.Should().Be("main");
    }

    [Fact]
    public async Task Ativar_cria_troca_confere_aplica_banco_e_guarda_pendencias_em_stash()
    {
        using var c = new CenarioDeMelhoria();
        var m = c.Nova();
        c.Banco.Ativo = "p2";
        File.WriteAllText(Path.Combine(c.Pda.Caminho, "novo.txt"), "pendente");

        var res = await c.Ativador.AtivarAsync(m, aplicarBanco: true);

        res.Erros.Should().Be(0, string.Join(" | ", res.Linhas.Select(l => l.Texto)));
        c.Pda.BranchAtual.Should().Be("VENDAS-100");
        c.Web.BranchAtual.Should().Be("feature/exemplo");
        File.Exists(Path.Combine(c.Pda.Caminho, "novo.txt")).Should().BeFalse("a pendência ficou no stash de main");
        c.Banco.Aplicados.Should().Equal("p1");
        c.Repositorio.Config.Ativa.Should().Be("VENDAS-100");
        c.Repositorio.Config.Anterior!.Branches.Select(b => b.Projeto + ":" + b.Branch).Should().BeEquivalentTo("lavenderepda:main", "lavendereweb:main");
        c.Repositorio.Config.Anterior.BancoId.Should().Be("p2");
        c.Repositorio.Achar("VENDAS-100")!.UltimoUso.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Desvincular_volta_branches_pendencias_e_banco_e_com_erro_mantem_o_estado()
    {
        using var c = new CenarioDeMelhoria();
        var m = c.Nova();
        c.Banco.Ativo = "p2";
        File.WriteAllText(Path.Combine(c.Pda.Caminho, "novo.txt"), "pendente");
        await c.Ativador.AtivarAsync(m, true);

        var previa = await c.Ativador.DesvincularPreviaAsync();
        previa.Should().Contain(l => l.Contains("lavenderepda") && l.Contains("main"));
        var res = await c.Ativador.DesvincularAsync(restaurarBanco: true);

        res.Erros.Should().Be(0, string.Join(" | ", res.Linhas.Select(l => l.Texto)));
        c.Pda.BranchAtual.Should().Be("main");
        c.Web.BranchAtual.Should().Be("main");
        File.Exists(Path.Combine(c.Pda.Caminho, "novo.txt")).Should().BeTrue("a pendência volta ao retornar à branch");
        c.Banco.Ativo.Should().Be("p2");
        c.Repositorio.Config.Ativa.Should().BeEmpty();
        c.Repositorio.Config.Anterior.Should().BeNull();
    }

    [Fact]
    public async Task Branch_inexistente_sem_base_vira_erro_e_a_ativacao_confere()
    {
        using var c = new CenarioDeMelhoria();
        var m = new Melhoria { Id = "VENDAS-7", Branches = [new() { Projeto = "lavendereweb", Branch = "nao/existe" }, new() { Projeto = "fantasma", Branch = "x" }] };
        c.Repositorio.Gravar(m, true);

        var res = await c.Ativador.AtivarAsync(m, false);

        res.Linhas.Where(l => l.Nivel == NivelDeLinha.Erro).Select(l => l.Texto).Should().Contain(t => t.Contains("não existe")).And.Contain(t => t.Contains("não encontrado"));
        c.Web.BranchAtual.Should().Be("main");
    }

    [Fact]
    public async Task Criar_branches_adota_as_existentes_e_publica_as_novas()
    {
        using var c = new CenarioDeMelhoria();
        var m = c.Nova();

        var res = await c.Ativador.CriarBranchesAsync(m, publicar: true);

        res.Erros.Should().Be(0, string.Join(" | ", res.Linhas.Select(l => l.Texto)));
        res.Linhas.Select(l => l.Texto).Should().Contain(t => t.Contains("já existe - adotada")).And.Contain(t => t.Contains("criada") && t.Contains("publicada"));
        c.Pda.Rodar("ls-remote", "--heads", "origin", "VENDAS-100").Should().Contain("VENDAS-100");
    }

    [Fact]
    public async Task Version_conf_acompanha_o_pom_e_desvincular_restaura()
    {
        using var c = new CenarioDeMelhoria();
        var arquivo = Path.Combine(c.Pasta, "version.conf");
        File.WriteAllText(arquivo, "8.000\r\n");
        c.Repositorio.ConfigurarVersaoConf(true, arquivo);
        var m = c.Nova();
        c.Pda.Rodar("checkout", "-b", "VENDAS-100", "develop");
        c.Pda.Commitar("pom.xml", "<project><version>9.060-SNAPSHOT</version></project>", "pom");
        c.Pda.Rodar("checkout", "main");

        await c.Ativador.AtivarAsync(m, false);
        File.ReadAllText(arquivo).Should().Be("9.060-SNAPSHOT\r\n");

        await c.Ativador.DesvincularAsync(false);
        File.ReadAllText(arquivo).Should().Be("8.000\r\n");
    }

    [Fact]
    public async Task Commits_da_melhoria_citam_o_id_sem_confundir_numeros_maiores()
    {
        using var c = new CenarioDeMelhoria();
        var m = c.Nova();
        c.Pda.Commitar("a.txt", "1", "VENDAS-100 corrige pedido");
        c.Pda.Commitar("b.txt", "2", "VENDAS-1005 outra coisa");

        var commits = await c.Commits.ListarAsync(m);

        commits.Should().ContainSingle().Which.Assunto.Should().Contain("corrige pedido");
    }
}

public sealed class SqliteDeAtividadeTests : IDisposable
{
    readonly string _pasta = Path.Combine(Path.GetTempPath(), "sq" + Guid.NewGuid().ToString("N")[..6]);
    readonly SqliteDeAtividade _sqlite = new();

    public SqliteDeAtividadeTests() => Directory.CreateDirectory(_pasta);

    public void Dispose() => Directory.Delete(_pasta, true);

    string Arq => Path.Combine(_pasta, "wmwvendas.sqlite3");

    ConfigSqlite Config(string dono = "") => new() { Ativo = true, Caminho = Arq, Dono = dono };

    [Theory]
    [InlineData("feature/vendas-25174-xyz", "VENDAS-25174")]
    [InlineData("VENDAS_9", "VENDAS-9")]
    [InlineData("develop", "")]
    [InlineData("", "")]
    public void Id_da_branch(string branch, string esperado) => SqliteDeAtividade.IdDaBranch(branch).Should().Be(esperado);

    [Fact]
    public async Task Troca_guarda_o_corrente_com_o_id_do_dono_e_puxa_o_da_atividade_nova()
    {
        File.WriteAllText(Arq, "A");
        File.WriteAllText(Path.Combine(_pasta, "wmwvendas-VENDAS-2.sqlite3"), "B");
        var res = new ResumoDeMelhoria("t");
        var cfg = Config("VENDAS-1");

        (await _sqlite.TrocarAsync(cfg, "VENDAS-2", "", res)).Should().BeTrue();

        File.ReadAllText(Arq).Should().Be("B");
        File.ReadAllText(Path.Combine(_pasta, "wmwvendas-VENDAS-1.sqlite3")).Should().Be("A");
        cfg.Dono.Should().Be("VENDAS-2");
    }

    [Fact]
    public async Task Nunca_sobrescreve_destino_existente()
    {
        File.WriteAllText(Arq, "A");
        File.WriteAllText(Path.Combine(_pasta, "wmwvendas-VENDAS-1.sqlite3"), "JA EXISTE");
        var res = new ResumoDeMelhoria("t");

        (await _sqlite.TrocarAsync(Config("VENDAS-1"), "VENDAS-2", "", res)).Should().BeFalse();

        File.ReadAllText(Arq).Should().Be("A");
        File.ReadAllText(Path.Combine(_pasta, "wmwvendas-VENDAS-1.sqlite3")).Should().Be("JA EXISTE");
        res.Linhas.Should().Contain(l => l.Nivel == NivelDeLinha.Aviso);
    }

    [Fact]
    public async Task Sem_atividade_identificada_nada_muda_e_dono_desconhecido_usa_semid()
    {
        File.WriteAllText(Arq, "A");
        var res = new ResumoDeMelhoria("t");

        (await _sqlite.TrocarAsync(Config("VENDAS-1"), "", "", res)).Should().BeTrue();
        File.Exists(Arq).Should().BeTrue();

        (await _sqlite.TrocarAsync(Config(), "VENDAS-3", "", res)).Should().BeTrue();
        File.Exists(Arq).Should().BeFalse("a atividade nova começa sem banco");
        Directory.GetFiles(_pasta, "wmwvendas-SEMID-*.sqlite3").Should().ContainSingle();
    }

    [Fact]
    public async Task Arquivo_aberto_pergunta_para_tentar_de_novo_e_conclui_depois_de_liberar()
    {
        File.WriteAllText(Arq, "A");
        var aberto = new FileStream(Arq, FileMode.Open, FileAccess.Read, FileShare.None);
        var perguntas = 0;
        _sqlite.AoArquivoEmUso = _ =>
        {
            perguntas++;
            aberto.Dispose();
            return Task.FromResult(true);
        };

        (await _sqlite.TrocarAsync(Config("VENDAS-1"), "VENDAS-2", "", new ResumoDeMelhoria("t"))).Should().BeTrue();

        perguntas.Should().Be(1);
        File.Exists(Path.Combine(_pasta, "wmwvendas-VENDAS-1.sqlite3")).Should().BeTrue();
    }

    [Fact]
    public async Task Arquivo_aberto_e_cancelado_so_avisa_e_nada_e_renomeado()
    {
        File.WriteAllText(Arq, "A");
        using var aberto = new FileStream(Arq, FileMode.Open, FileAccess.Read, FileShare.None);
        _sqlite.AoArquivoEmUso = _ => Task.FromResult(false);
        var res = new ResumoDeMelhoria("t");

        (await _sqlite.TrocarAsync(Config("VENDAS-1"), "VENDAS-2", "", res)).Should().BeFalse();

        res.Linhas.Should().Contain(l => l.Nivel == NivelDeLinha.Aviso && l.Texto.Contains("aberto"));
    }

    [Fact]
    public async Task Adotar_renomeia_para_o_id_da_melhoria_sem_sobrescrever()
    {
        var origem = Path.Combine(_pasta, "backup.sqlite3");
        File.WriteAllText(origem, "X");

        var (ok, msg) = await _sqlite.AdotarAsync(Config(), origem, "VENDAS-55");

        ok.Should().BeTrue(msg);
        File.Exists(Path.Combine(_pasta, "wmwvendas-VENDAS-55.sqlite3")).Should().BeTrue();

        File.WriteAllText(origem, "Y");
        (await _sqlite.AdotarAsync(Config(), origem, "VENDAS-55")).Ok.Should().BeFalse("o destino já existe");
        File.ReadAllText(origem).Should().Be("Y");
        File.WriteAllText(Arq, "Z");
        (await _sqlite.AdotarAsync(Config(), Arq, "VENDAS-56")).Ok.Should().BeFalse("o em uso agora não é renomeado");
    }
}
