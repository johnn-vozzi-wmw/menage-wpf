using System.Security.Cryptography;
using System.Text;
using Menage.Core.Configuracoes;

namespace Menage.Tests.PerfisConfiguracoes;

public sealed class ConfiguracoesTests : IDisposable
{
    readonly string _pasta = Path.Combine(Path.GetTempPath(), "cf" + Guid.NewGuid().ToString("N")[..6]);

    public void Dispose()
    {
        if (Directory.Exists(_pasta)) Directory.Delete(_pasta, recursive: true);
    }

    sealed class ProtetorFalso : IProtetorDeSegredos
    {
        public string Proteger(string texto) => Convert.ToBase64String(Encoding.UTF8.GetBytes("##" + texto));

        public string Revelar(string protegido) =>
            Encoding.UTF8.GetString(Convert.FromBase64String(protegido)) is var t && t.StartsWith("##", StringComparison.Ordinal)
                ? t[2..]
                : throw new CryptographicException("outro usuário");
    }

    [Fact]
    public void Padroes_sao_os_do_Git_Manager_antigo_e_gravam_e_voltam()
    {
        var s = new ServicoDeConfiguracoes(new PastaDeDados(_pasta));
        s.Atual.Git.FetchMinutos.Should().Be(10);
        s.Atual.Jira.Url.Should().Be("https://jira2.wmw.com.br");
        s.Atual.Comportamento.ScriptDaAtividade.Excluir.Should().BeTrue();
        s.Atual.Comportamento.ScriptDaAtividade.Destinos.Should().Equal("develop");

        var nova = s.Atual with
        {
            Git = s.Atual.Git with { FetchMinutos = 25, FerramentaDeDiff = FerramentasDeDiff.WinMerge },
            Caminhos = new ConfigDeCaminhos { ChaveSsh = @"C:\k\id_wmw" },
        };
        s.Salvar(nova).EhOk.Should().BeTrue();

        var outro = new ServicoDeConfiguracoes(new PastaDeDados(_pasta));
        outro.Atual.Git.FetchMinutos.Should().Be(25);
        outro.Atual.Git.FerramentaDeDiff.Should().Be("winmerge");
        outro.Atual.Caminhos.ChaveSsh.Should().Be(@"C:\k\id_wmw");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(241)]
    public void Intervalo_de_fetch_fora_de_1_a_240_nao_grava(int minutos)
    {
        var s = new ServicoDeConfiguracoes(new PastaDeDados(_pasta));

        s.Salvar(s.Atual with { Git = new ConfigDeGit { FetchMinutos = minutos } }).Erro.Causa.Should().Contain("fetch");
        File.Exists(Path.Combine(_pasta, "configuracoes.json")).Should().BeFalse();
    }

    [Fact]
    public void Jira_com_endereco_invalido_e_script_sem_destino_nao_gravam()
    {
        var s = new ServicoDeConfiguracoes(new PastaDeDados(_pasta));
        var semDestino = new ConfigDeComportamento { ScriptDaAtividade = new ConfigDoScriptDaAtividade { Destinos = [] } };

        s.Salvar(s.Atual with { Jira = new ConfigDoJira { Url = "jira sem endereco" } }).EhOk.Should().BeFalse();
        s.Salvar(s.Atual with { Comportamento = semDestino }).EhOk.Should().BeFalse();
        s.Salvar(s.Atual with { Jira = new ConfigDoJira { Url = "" } }).EhOk.Should().BeTrue();
    }

    [Fact]
    public void Ferramenta_de_diff_desconhecida_vira_a_interna()
    {
        FerramentasDeDiff.Normalizar("kdiff").Should().Be("interno");
        FerramentasDeDiff.Normalizar(null).Should().Be("interno");
        FerramentasDeDiff.Normalizar(" WinMerge ").Should().Be("winmerge");
    }

    [Fact]
    public void Cofre_guarda_o_segredo_protegido_e_nunca_em_texto_claro()
    {
        var cofre = new CofreDeCredenciais(new PastaDeDados(_pasta), new ProtetorFalso());

        cofre.Guardar(ServicosDeCredencial.Jira, "ana", "token-super-secreto").EhOk.Should().BeTrue();

        File.ReadAllText(Path.Combine(_pasta, "credenciais.json")).Should().NotContain("token-super-secreto").And.Contain("ana");
        cofre.Tem("jira").Should().BeTrue();
        cofre.Usuario("jira").Should().Be("ana");
        cofre.Segredo("jira").Valor.Should().Be("token-super-secreto");
    }

    [Fact]
    public void Cofre_trocar_usuario_mantem_o_segredo_e_remover_apaga()
    {
        var cofre = new CofreDeCredenciais(new PastaDeDados(_pasta), new ProtetorFalso());
        cofre.Guardar("nexus", "ana", "senha").EhOk.Should().BeTrue();

        cofre.TrocarUsuario("nexus", "bia").EhOk.Should().BeTrue();
        cofre.Usuario("nexus").Should().Be("bia");
        cofre.Segredo("nexus").Valor.Should().Be("senha");

        cofre.Remover("nexus");
        cofre.Tem("nexus").Should().BeFalse();
        cofre.Segredo("nexus").EhOk.Should().BeFalse();
        cofre.Guardar("nexus", "x", " ").EhOk.Should().BeFalse();
    }

    [Fact]
    public void Segredo_de_outro_usuario_vira_erro_claro_sem_excecao()
    {
        var arquivo = new Armazenamento<ArquivoDeCredenciais>(new PastaDeDados(_pasta), "credenciais");
        arquivo.Salvar(new ArquivoDeCredenciais { Itens = { ["jira"] = new CredencialGuardada("ana", Convert.ToBase64String("lixo"u8.ToArray())) } });
        var cofre = new CofreDeCredenciais(new PastaDeDados(_pasta), new ProtetorFalso());

        cofre.Segredo("jira").Erro.Explicacao.Should().Contain("outro usuário");
    }

    [Fact]
    public void Dpapi_do_Windows_vai_e_volta_e_o_texto_protegido_nao_contem_o_segredo()
    {
        if (!OperatingSystem.IsWindows()) return;
        var protetor = new ProtetorDpapi();

        var protegido = protetor.Proteger("minha-senha-123");

        protegido.Should().NotContain("minha-senha-123");
        Encoding.UTF8.GetString(Convert.FromBase64String(protegido)).Should().NotContain("minha-senha-123");
        protetor.Revelar(protegido).Should().Be("minha-senha-123");
    }
}
