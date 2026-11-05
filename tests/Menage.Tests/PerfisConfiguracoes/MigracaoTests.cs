using System.Security.Cryptography;
using System.Text;
using Menage.Core.Configuracoes;
using Menage.Core.Protecao;

namespace Menage.Tests.PerfisConfiguracoes;

public sealed class MigracaoTests : IDisposable
{
    readonly string _tudo = Path.Combine(Path.GetTempPath(), "mg" + Guid.NewGuid().ToString("N")[..6]);
    readonly string _antiga;
    readonly string _dados;
    readonly string _raiz;

    public MigracaoTests()
    {
        _antiga = Path.Combine(_tudo, "antiga");
        _dados = Path.Combine(_tudo, "dados");
        _raiz = Path.Combine(_tudo, "raiz");
        Directory.CreateDirectory(_antiga);
        Directory.CreateDirectory(_raiz);
        Escrever("perfil.json", """{"perfil":"tester","perfis":{"dev":{"nome":"Desenvolvedor","permitidas":["*","-gerador-deploy"]},"tester":{"permitidas":["fetch","status","voar"]}}}""");
        Escrever("preferencias.json", """{"fetchAutomatico":false,"fetchMinutos":30,"fetchTags":true,"saidaRecolhida":false,"ferramentaDiff":"winmerge"}""");
        Escrever("protecao.json", """{"bloquearPush":false,"bloquearExclusao":true,"branches":["main","hotfix*"],"validarCommit":true,"tipos":["feat","fix"],"aceitarTicket":false}""");
        Escrever("config-geral.cfg", _raiz + "\r\n");
        Escrever("config-ssh.cfg", "NENHUMA\r\n");
        Escrever("merge.json", """{"excluirScript":true,"padrao":"scripts/x_%NOME_BRANCH%*","destinos":["develop","release*"]}""");
        Escrever("config-update.cfg", "verificar=0\r\nignorar=3.1.0\r\n");
        Escrever("diario.json", """{"jiraUrl":"https://jira.exemplo.com/","projetosJira":"VENDAS,SUP","varTokenJira":"MEU_TOKEN","outraCoisa":1}""");
        Escrever("config-tema.cfg", "esmeralda-escuro\r\n");
        Escrever("config-beta.cfg", "1\r\n");
        if (OperatingSystem.IsWindows())
        {
            var senha = Convert.ToBase64String(ProtectedData.Protect("senha-antiga"u8.ToArray(), null, DataProtectionScope.CurrentUser));
            Escrever("config-nexus.json", "{\"url\":\"https://nexus\",\"usuario\":\"ana\",\"senha\":\"" + senha + "\"}");
        }
    }

    void Escrever(string nome, string texto) => File.WriteAllText(Path.Combine(_antiga, nome), texto, new UTF8Encoding(false));

    public void Dispose()
    {
        if (Directory.Exists(_tudo)) Directory.Delete(_tudo, recursive: true);
    }

    sealed class ProtetorFalso : IProtetorDeSegredos
    {
        public string Proteger(string texto) => "[" + texto + "]";

        public string Revelar(string protegido) => protegido[1..^1];
    }

    sealed record Montagem(
        MigradorDoGitManager Migrador, ServicoDePerfis Perfis, ServicoDeConfiguracoes Config, ProtecaoDeBranches Protecao,
        ConfiguracaoDeProjetos Projetos, CofreDeCredenciais Cofre);

    Montagem Montar()
    {
        var pasta = new PastaDeDados(_dados);
        var perfis = new ServicoDePerfis(pasta);
        var config = new ServicoDeConfiguracoes(pasta);
        var protecao = new ProtecaoDeBranches(pasta);
        var projetos = new ConfiguracaoDeProjetos(pasta);
        var cofre = new CofreDeCredenciais(pasta, new ProtetorFalso());
        return new Montagem(new MigradorDoGitManager(perfis, config, protecao, projetos, cofre), perfis, config, protecao, projetos, cofre);
    }

    Dictionary<string, string> Fotografia() =>
        Directory.GetFiles(_antiga).ToDictionary(f => Path.GetFileName(f), f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))) + File.GetLastWriteTimeUtc(f).Ticks);

    [Fact]
    public void Analisar_lista_cada_arquivo_encontrado_com_resumo_e_nao_altera_nada()
    {
        var m = Montar();
        var antes = Fotografia();

        var achados = m.Migrador.Analisar(_antiga);

        achados.Should().HaveCount(11);
        achados.Where(a => a.Item != ItemDeMigracao.Nexus || OperatingSystem.IsWindows()).Should().OnlyContain(a => a.Encontrado);
        achados.First(a => a.Item == ItemDeMigracao.Preferencias).Resumo.Should().Contain("desligado").And.Contain("WinMerge");
        achados.First(a => a.Item == ItemDeMigracao.ChaveSsh).Resumo.Should().Contain("Sem chave");
        File.Exists(Path.Combine(_dados, "perfis.json")).Should().BeFalse();
        Fotografia().Should().Equal(antes);
    }

    [Fact]
    public void Pasta_sem_arquivos_marca_tudo_como_nao_encontrado_e_aplicar_nao_faz_nada()
    {
        var vazia = Path.Combine(_tudo, "vazia");
        Directory.CreateDirectory(vazia);
        var m = Montar();

        m.Migrador.Analisar(vazia).Should().OnlyContain(a => !a.Encontrado);
        var r = m.Migrador.Aplicar(vazia, Enum.GetValues<ItemDeMigracao>());

        r.Aplicados.Should().BeEmpty();
        r.Falhas.Should().BeEmpty();
        File.Exists(Path.Combine(_dados, "perfis.json")).Should().BeFalse();
        File.Exists(Path.Combine(_dados, "configuracoes.json")).Should().BeFalse();
        File.Exists(Path.Combine(_dados, "protecao.json")).Should().BeFalse();
    }

    [Fact]
    public void Aplicar_traz_tudo_para_o_Menage_e_deixa_a_pasta_antiga_intacta()
    {
        var m = Montar();
        var antes = Fotografia();
        var arquivosAntes = Directory.GetFiles(_antiga).Length;

        var r = m.Migrador.Aplicar(_antiga, Enum.GetValues<ItemDeMigracao>());

        r.Falhas.Should().BeEmpty();
        m.Perfis.Ativo.Id.Should().Be("tester");
        m.Perfis.Obter("tester")!.Permitidas.Should().Equal("fetch", "status");
        m.Perfis.Obter("dev")!.Permite("gerador-deploy").Should().BeFalse();
        m.Projetos.Ligado.Should().BeTrue();
        m.Config.Atual.Git.FetchMinutos.Should().Be(30);
        m.Config.Atual.Git.FetchComTags.Should().BeTrue();
        m.Config.Atual.Git.FerramentaDeDiff.Should().Be("winmerge");
        m.Config.Atual.Caminhos.ChaveSsh.Should().BeEmpty();
        m.Config.Atual.Comportamento.VerificarAtualizacaoAoAbrir.Should().BeFalse();
        m.Config.Atual.Comportamento.ScriptDaAtividade.Destinos.Should().Equal("develop", "release*");
        m.Config.Atual.Comportamento.ScriptDaAtividade.Padrao.Should().Be("scripts/x_%NOME_BRANCH%*");
        m.Config.Atual.Jira.Should().Be(new ConfigDoJira { Url = "https://jira.exemplo.com", Projetos = "VENDAS,SUP", VariavelDoToken = "MEU_TOKEN" });
        m.Protecao.Atual.BloquearPush.Should().BeFalse();
        m.Protecao.Atual.Branches.Should().Equal("main", "hotfix*");
        m.Protecao.Atual.Tipos.Should().Equal("feat", "fix");
        m.Protecao.Atual.ValidarCommit.Should().BeTrue();
        m.Projetos.Catalogo.Raiz.Should().Be(_raiz);
        r.TemaId.Should().Be("esmeralda-escuro");
        r.FetchAutomatico.Should().BeFalse();
        r.SaidaRecolhida.Should().BeFalse();
        if (OperatingSystem.IsWindows())
        {
            m.Cofre.Usuario("nexus").Should().Be("ana");
            m.Cofre.Segredo("nexus").Valor.Should().Be("senha-antiga");
        }

        Fotografia().Should().Equal(antes);
        Directory.GetFiles(_antiga).Should().HaveCount(arquivosAntes);
    }

    [Fact]
    public void So_os_itens_marcados_sao_importados()
    {
        var m = Montar();

        var r = m.Migrador.Aplicar(_antiga, [ItemDeMigracao.Protecao]);

        r.Aplicados.Should().ContainSingle().Which.Should().Contain("Proteção");
        m.Protecao.Atual.Branches.Should().Equal("main", "hotfix*");
        m.Perfis.Ativo.Id.Should().Be("completo");
        m.Config.Atual.Git.FetchMinutos.Should().Be(10);
    }

    [Fact]
    public void Arquivo_quebrado_ou_de_formato_errado_vira_nao_encontrado_sem_excecao()
    {
        Escrever("protecao.json", "{ isto não é json");
        Escrever("merge.json", "[1,2,3]");
        var m = Montar();

        var achados = m.Migrador.Analisar(_antiga);

        achados.First(a => a.Item == ItemDeMigracao.Protecao).Encontrado.Should().BeFalse();
        achados.First(a => a.Item == ItemDeMigracao.Protecao).Resumo.Should().Contain("não deu para entender");
        achados.First(a => a.Item == ItemDeMigracao.ScriptDaAtividade).Encontrado.Should().BeFalse();
    }

    [Fact]
    public void Localizar_acha_so_pastas_com_arquivos_do_Git_Manager()
    {
        Directory.CreateDirectory(Path.Combine(_tudo, "outra"));
        File.WriteAllText(Path.Combine(_tudo, "outra", "x.txt"), "x");

        LeitorDoGitManager.Localizar(_tudo).Should().Equal(_antiga);
    }
}
