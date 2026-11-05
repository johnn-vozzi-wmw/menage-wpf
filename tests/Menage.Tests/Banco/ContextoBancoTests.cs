using System.Text;
using Menage.Core.Banco;

namespace Menage.Tests.Banco;

/// <summary>context.xml sempre em pasta descartável de %TEMP% (caminho curto). Nenhum banco real é acessado.</summary>
public sealed class ContextoBancoTests : IDisposable
{
    const string Original = """
        <?xml version="1.0" encoding="UTF-8"?>
        <Context>
          <Resource name="jdbc/OUTRO" url="x"/>
          <Resource name="jdbc/LAVENDEREWEBDS" url="jdbc:oracle:thin:@10.0.0.9:1521/wmw?v$session.program=web_wmw" username="wmw" password="ENC(abc)" />
          <Resource name="jdbc/LAVENDEREPDADS" url="jdbc:oracle:thin:@10.0.0.9:1521/wmw?v$session.program=app_pda" username="pda" password="ENC(def)" />
          <Resource name="jdbc/LAVENDEREINTDS" url="jdbc:oracle:thin:@10.0.0.9:1521/wmw?v$session.program=int_wmw" username="wmw" password="ENC(abc)" />
          <!-- comentario que fica -->
        </Context>
        """;

    readonly string _pasta = Path.Combine(Path.GetTempPath(), "cb" + Guid.NewGuid().ToString("N")[..6]);

    public ContextoBancoTests() => Directory.CreateDirectory(_pasta);

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    static PerfilDeBanco Oracle(string nome = "Local", string host = "localhost") => new()
    {
        Nome = nome, Tipo = TipoDeBanco.Oracle, Host = host, Porta = "1521",
        Web = new ConexaoDeBanco("web", "xe", "ENC(w)"), App = new ConexaoDeBanco("pda", "xe", "ENC(p)"),
    };

    static PerfilDeBanco Postgres(string nome = "Pg") => new()
    {
        Nome = nome, Tipo = TipoDeBanco.Postgres, Host = "pg.local", Porta = "5432",
        Web = new ConexaoDeBanco("u", "vendas", "s1"), App = new ConexaoDeBanco("u", "vendas_pda", "s1"), IntIgualWeb = true,
    };

    string Arquivo(string conteudo, bool bom = false, string nl = "\n")
    {
        var caminho = Path.Combine(_pasta, "context.xml");
        File.WriteAllText(caminho, conteudo.Replace("\r\n", "\n").Replace("\n", nl), new UTF8Encoding(bom));
        return caminho;
    }

    [Fact]
    public void Aplicar_comenta_os_ativos_e_grava_so_o_perfil_escolhido_sem_mexer_no_resto()
    {
        var novo = ContextoXml.Aplicar(Original, Oracle()).Valor;

        novo.Should().Contain("<Resource name=\"jdbc/OUTRO\" url=\"x\"/>").And.Contain("<!-- comentario que fica -->");
        novo.Should().Contain("<!-- <Resource name=\"jdbc/LAVENDEREWEBDS\" url=\"jdbc:oracle:thin:@10.0.0.9");
        ContextoXml.LerAtivo(novo)!.Host.Should().Be("localhost");
        novo.Should().Contain(ContextoXml.MarcaInicio).And.Contain(ContextoXml.MarcaFim);
    }

    [Fact]
    public void Aplicar_e_idempotente_e_troca_de_perfil_so_alterna_qual_esta_ativo()
    {
        var a = Oracle("A");
        var b = Postgres("B");
        var todos = new[] { a, b };

        var emA = ContextoXml.Aplicar(Original, a, todos).Valor;
        ContextoXml.Aplicar(emA, a, todos).Valor.Should().Be(emA);

        var emB = ContextoXml.Aplicar(emA, b, todos).Valor;
        ContextoXml.LerAtivo(emB)!.Tipo.Should().Be(TipoDeBanco.Postgres);
        emB.Split(ContextoXml.MarcaInicio).Length.Should().Be(2, "a região gerada existe uma vez só");
        var bancos = ContextoXml.LerTodos(emB);
        bancos.Should().Contain(x => x.Ativo && x.Perfil.Tipo == TipoDeBanco.Postgres);
        bancos.Should().Contain(x => !x.Ativo && x.Perfil.Tipo == TipoDeBanco.Oracle && x.Perfil.Host == "localhost");
    }

    [Fact]
    public void Oracle_usa_um_service_name_e_o_int_espelha_o_web()
    {
        var p = Oracle() with { App = new ConexaoDeBanco("pda", "outro", "ENC(p)") };

        ContextoXml.GerarResource(p, ChavesDeConexao.App).Should().Contain("/xe?").And.NotContain("outro");
        ContextoXml.GerarResource(p, ChavesDeConexao.Integracao).Should().Contain("username=\"web\"");
    }

    [Theory]
    [InlineData("", "localhost", "1521", "u", "Informe o nome")]
    [InlineData("X", "host com espaço", "1521", "u", "Host/IP inválido")]
    [InlineData("X", "localhost", "70000", "u", "Porta inválida")]
    [InlineData("X", "localhost", "1521", "", "Usuário da conexão WEB")]
    [InlineData("X", "localhost", "1521", "a--b", "Usuário da conexão WEB")]
    public void Perfil_invalido_e_recusado_antes_de_gravar(string nome, string host, string porta, string usuario, string trecho)
    {
        var p = Oracle(nome, host) with { Porta = porta, Web = new ConexaoDeBanco(usuario, "xe", "s") };

        ContextoXml.Validar(p).Should().Contain(trecho);
        ContextoXml.Aplicar(Original, p).EhOk.Should().BeFalse();
    }

    [Fact]
    public void Arquivo_sem_context_ou_xml_quebrado_nao_e_gravado()
    {
        ContextoXml.Aplicar("<Raiz/>", Oracle()).Erro.Causa.Should().Contain("</Context>");
        ContextoXml.Aplicar("<Context><Resource name=\"jdbc/LAVENDEREWEBDS\" url=\"a--b\" /></Context>", Oracle()).Erro.Causa.Should().Contain("--");
    }

    [Fact]
    public void LerAtivo_aceita_postgres_sem_porta_e_sqlserver()
    {
        const string xml = """
            <Context>
              <Resource name="jdbc/LAVENDEREWEBDS" url="jdbc:postgresql://srv/vendas?ApplicationName=web" username="u" password="p"/>
              <Resource name="jdbc/LAVENDEREPDADS" url="jdbc:sqlserver://sql:1433;integratedSecurity=false;databaseName=pda;encrypt=true" username="u" password="p"/>
            </Context>
            """;

        ContextoXml.LerAtivo(xml.Replace("jdbc:sqlserver://sql:1433;integratedSecurity=false;databaseName=pda;encrypt=true", "jdbc:postgresql://srv/pda"))!
            .Should().Match<PerfilDeBanco>(p => p.Porta == "5432" && p.Web.Banco == "vendas" && p.App.Banco == "pda");

        var sql = ContextoXml.LerAtivo(
            "<Context><Resource name=\"jdbc/LAVENDEREWEBDS\" url=\"jdbc:sqlserver://sql;integratedSecurity=false;databaseName=pda;encrypt=true\" username=\"u\" password=\"p\"/></Context>")!;
        (sql.Tipo, sql.Porta, sql.Web.Banco).Should().Be((TipoDeBanco.SqlServer, "1433", "pda"));
    }

    [Fact]
    public void LerTodos_separa_bancos_comentados_pelo_servidor_e_pela_ordem()
    {
        const string xml = """
            <Context>
              <Resource name="jdbc/LAVENDEREWEBDS" url="jdbc:oracle:thin:@a:1521/x" username="u" password="p"/>
              <Resource name="jdbc/LAVENDEREPDADS" url="jdbc:oracle:thin:@a:1521/x" username="u" password="p"/>
              <!-- <Resource name="jdbc/LAVENDEREWEBDS" url="jdbc:postgresql://b:5432/v" username="u" password="p"/>
                   <Resource name="jdbc/LAVENDEREPDADS" url="jdbc:postgresql://b:5432/v" username="u" password="p"/> -->
              <!-- <Resource name="jdbc/LAVENDEREWEBDS" url="jdbc:oracle:thin:@c:1521/y" username="u" password="p"/> -->
            </Context>
            """;

        var bancos = ContextoXml.LerTodos(xml);

        bancos.Select(b => (b.Perfil.Host, b.Ativo)).Should().Equal(("a", true), ("b", false), ("c", false));
    }

    [Fact]
    public void Aplicar_no_arquivo_preserva_bom_e_quebra_de_linha_e_deixa_copia()
    {
        var caminho = Arquivo(Original, bom: true, nl: "\r\n");

        ArquivoDeContexto.Aplicar(caminho, Oracle(), []).EhOk.Should().BeTrue();

        var bytes = File.ReadAllBytes(caminho);
        bytes[..3].Should().Equal(0xEF, 0xBB, 0xBF);
        var texto = Encoding.UTF8.GetString(bytes);
        texto.Replace("\r\n", "").Should().NotContain("\n");
        File.ReadAllText(caminho + ArquivoDeContexto.SufixoDaCopia).Should().Contain("ENC(abc)").And.NotContain(ContextoXml.MarcaInicio);
    }

    [Fact]
    public void Perfil_invalido_nao_cria_copia_nem_altera_o_arquivo()
    {
        var caminho = Arquivo(Original);

        ArquivoDeContexto.Aplicar(caminho, Oracle("") , []).EhOk.Should().BeFalse();

        File.ReadAllText(caminho).Should().Be(Original.Replace("\r\n", "\n"));
        File.Exists(caminho + ArquivoDeContexto.SufixoDaCopia).Should().BeFalse();
    }

    RepositorioDeBanco Repositorio() => new(new Armazenamento<ConfiguracaoDoBanco>(new PastaDeDados(_pasta), ConfiguracaoDoBanco.NomeDoArquivo, padrao: ConfiguracaoDoBanco.Padrao));

    [Fact]
    public void Repositorio_valida_nome_repetido_duplica_exclui_e_persiste()
    {
        var repo = Repositorio();
        repo.Salvar(Oracle("Local")).EhOk.Should().BeTrue();
        repo.Salvar(Oracle("local") with { Id = "outro" }).Erro.Causa.Should().Contain("Já existe");

        var copia = repo.Duplicar(repo.Perfis[0].Id).Valor;
        copia.Nome.Should().Be("Local (cópia)");
        repo.Duplicar(repo.Perfis[0].Id).Valor.Nome.Should().Be("Local (cópia 2)");

        Repositorio().Perfis.Should().HaveCount(3);
        repo.Excluir(copia.Id);
        repo.Perfis.Should().HaveCount(2);
    }

    [Fact]
    public void Importar_cria_perfis_com_nome_sugerido_e_reconhece_os_que_ja_existem()
    {
        var repo = Repositorio();
        var lidos = ContextoXml.LerTodos(Original).Select(b => b.Perfil).ToList();

        var criados = repo.Importar(lidos).Valor;

        criados.Should().ContainSingle().Which.Nome.Should().Be("Oracle wmw");
        repo.Equivalente(lidos[0]).Should().NotBeNull();
        repo.Importar(lidos).Valor.Single().Nome.Should().Be("Oracle wmw 2");
    }

    [Fact]
    public async Task Servico_aplica_o_perfil_escolhido_e_o_estado_vem_do_proprio_arquivo()
    {
        var caminho = Arquivo(Original);
        var repo = Repositorio();
        repo.DefinirCaminho(caminho);
        repo.Salvar(Oracle("Local"));
        repo.Salvar(Postgres("Pg"));
        var servico = new ServicoDeBancoContexto(repo);
        servico.Estado.Should().Be("Ativo: outra configuração");

        servico.Escolher("Pg");
        (await servico.AplicarAsync()).EhOk.Should().BeTrue();

        servico.PerfilAtual.Should().Be("Pg");
        servico.Estado.Should().Be("Ativo: Pg");
        servico.Perfis.Should().Equal("Local", "Pg");
    }

    [Fact]
    public async Task Servico_sem_arquivo_ou_sem_perfil_devolve_erro_claro()
    {
        var repo = Repositorio();
        repo.DefinirCaminho(Path.Combine(_pasta, "nao-existe.xml"));
        var servico = new ServicoDeBancoContexto(repo);

        servico.Estado.Should().Be("context.xml não encontrado");
        (await servico.AplicarAsync()).Erro.Causa.Should().Contain("Nenhum perfil");
    }
}
