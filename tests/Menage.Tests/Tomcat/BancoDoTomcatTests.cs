using System.Net;
using System.Net.Sockets;
using Menage.Core.Tomcat;

namespace Menage.Tests.Tomcat;

public sealed class BancoDoTomcatTests : IDisposable
{
    const string Bat = "@echo off\r\nrem banco\r\nset \"db_driver=postgres\"\r\nset \"db_host=10.0.0.5\"\r\nset \"db_port=5432\"\r\nset \"db_user=wmw\"\r\nset \"db_pass=ENC(abc)\"\r\nset \"db=wmw\"\r\nset \"wmapa=1\"\r\n";

    readonly string _pasta = Path.Combine(Path.GetTempPath(), "bt" + Guid.NewGuid().ToString("N")[..6]);

    public BancoDoTomcatTests()
    {
        Directory.CreateDirectory(Path.Combine(_pasta, "bin"));
        File.WriteAllText(BancoDoTomcat.Arquivo(_pasta), Bat);
    }

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    [Fact]
    public void Le_o_padrao_e_os_grupos_herdam_dele()
    {
        var b = BancoDoTomcat.Ler(_pasta).Valor;
        Assert.Equal("10.0.0.5", b.Obter("host", ""));
        Assert.Equal("", b.Obter("host", "web"));
        Assert.Equal("10.0.0.5", b.Efetivo("host", "web"));
        Assert.Empty(b.Validar());
    }

    [Fact]
    public void Sem_arquivo_ou_formato_errado_devolve_erro_claro()
    {
        File.Delete(BancoDoTomcat.Arquivo(_pasta));
        Assert.Contains("Não achei", BancoDoTomcat.Ler(_pasta).Erro.Causa);
        File.WriteAllText(BancoDoTomcat.Arquivo(_pasta), "@echo off\r\n");
        Assert.Contains("formato esperado", BancoDoTomcat.Ler(_pasta).Erro.Causa);
    }

    [Fact]
    public void Grava_so_as_linhas_db_insere_a_que_nao_existe_e_guarda_a_copia_uma_vez()
    {
        var b = BancoDoTomcat.Ler(_pasta).Valor;
        b.Definir("host", "", "10.0.0.9");
        b.Definir("user", "int", "integra");
        Assert.True(b.Gravar().EhOk);

        var texto = File.ReadAllText(BancoDoTomcat.Arquivo(_pasta));
        Assert.Contains("set \"db_host=10.0.0.9\"\r\n", texto);
        Assert.Contains("set \"db_user=wmw\"\r\nset \"db_user_int=integra\"\r\n", texto);
        Assert.Contains("set \"wmapa=1\"", texto);
        Assert.Contains("@echo off\r\nrem banco", texto);

        var bak = BancoDoTomcat.Arquivo(_pasta) + ".menage.bak";
        Assert.Equal(Bat, File.ReadAllText(bak));
        b.Definir("host", "", "10.0.0.10");
        b.Gravar();
        Assert.Equal(Bat, File.ReadAllText(bak)); // a cópia é a original, de antes do Menage
    }

    [Theory]
    [InlineData("driver", "mysql", "driver")]
    [InlineData("port", "99999", "porta")]
    [InlineData("pass", "a\"b", "aspas")]
    [InlineData("user", "100%", "%")]
    public void Valor_invalido_nao_grava(string campo, string valor, string trecho)
    {
        var b = BancoDoTomcat.Ler(_pasta).Valor;
        b.Definir(campo, "web", valor);
        var r = b.Gravar();
        Assert.False(r.EhOk);
        Assert.Contains(trecho, r.Erro.ToString());
        Assert.Equal(Bat, File.ReadAllText(BancoDoTomcat.Arquivo(_pasta)));
    }

    [Fact]
    public void Tudo_em_branco_vale_mas_so_o_padrao_faltando_coisa_e_erro()
    {
        var b = BancoDoTomcat.Ler(_pasta).Valor;
        b.Definir("db", "", "");
        Assert.Contains("falta db", string.Join(" ", b.Validar()));
        foreach (var c in BancoDoTomcat.Campos) b.Definir(c, "", "");
        Assert.Empty(b.Validar());
    }

    [Fact]
    public void Oracle_com_service_name_diferente_de_wmw_avisa_sem_bloquear()
    {
        var b = BancoDoTomcat.Ler(_pasta).Valor;
        b.Definir("driver", "", "oracle");
        b.Definir("db", "", "outro");
        Assert.Empty(b.Validar());
        Assert.Equal(3, b.Avisos().Count);
    }

    [Fact]
    public async Task Alcance_diz_quem_responde_e_quem_nao()
    {
        using var servidor = new TcpListener(IPAddress.Loopback, 0);
        servidor.Start();
        var porta = ((IPEndPoint)servidor.LocalEndpoint).Port;
        var b = BancoDoTomcat.Ler(_pasta).Valor;
        b.Definir("host", "", "127.0.0.1");
        b.Definir("port", "", porta.ToString(System.Globalization.CultureInfo.InvariantCulture));
        b.Definir("port", "int", "1");
        var linhas = await b.TestarAlcanceAsync(TimeSpan.FromSeconds(2));
        Assert.Contains($"WEB: 127.0.0.1:{porta} responde.", linhas);
        Assert.Contains(linhas, l => l.StartsWith("INT: 127.0.0.1:1 NÃO responde", StringComparison.Ordinal));
    }
}
