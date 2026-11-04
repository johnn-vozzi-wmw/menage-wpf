using System.Text.Json.Nodes;

namespace Menage.Tests.Base;

public sealed class ArmazenamentoTests : IDisposable
{
    public sealed record Config(string Nome, int Valor);

    readonly string _pasta = Path.Combine(Path.GetTempPath(), "ar" + Guid.NewGuid().ToString("N")[..6]);

    Armazenamento<Config> Criar(int versao = 1, Func<Config>? padrao = null, Func<int, JsonNode, JsonNode>? migrar = null) =>
        new(new PastaDeDados(_pasta), "config", versao, padrao, migrar);

    public void Dispose()
    {
        if (Directory.Exists(_pasta)) Directory.Delete(_pasta, recursive: true);
    }

    [Fact]
    public void Grava_e_le_de_volta_criando_a_pasta()
    {
        var a = Criar();

        a.Salvar(new Config("x", 5)).EhOk.Should().BeTrue();

        Criar().Carregar().Valor.Should().Be(new Config("x", 5));
        File.ReadAllText(a.Arquivo).Should().Contain("\"versao\": 1");
    }

    [Fact]
    public void Sem_arquivo_devolve_o_padrao_ou_erro_claro()
    {
        Criar(padrao: () => new Config("padrao", 0)).Carregar().Valor.Nome.Should().Be("padrao");
        Criar().Carregar().EhOk.Should().BeFalse();
    }

    [Fact]
    public void Escrita_atomica_nao_deixa_temporario_e_nao_corrompe_o_anterior()
    {
        var a = Criar();
        a.Salvar(new Config("um", 1));
        a.Salvar(new Config("dois", 2));

        Directory.GetFiles(_pasta).Should().ContainSingle().Which.Should().EndWith("config.json");
        a.Carregar().Valor.Nome.Should().Be("dois");
    }

    [Fact]
    public void Json_corrompido_gera_erro_e_o_arquivo_fica_intacto()
    {
        Directory.CreateDirectory(_pasta);
        var a = Criar();
        File.WriteAllText(a.Arquivo, "{ isto não é json");

        var r = a.Carregar();

        r.EhOk.Should().BeFalse();
        r.Erro.AcaoSugerida.Should().Contain(a.Arquivo);
        File.ReadAllText(a.Arquivo).Should().Be("{ isto não é json");
    }

    [Fact]
    public void Migra_passo_a_passo_de_versoes_antigas()
    {
        Criar(versao: 1).Salvar(new Config("a", 1));
        var passos = new List<int>();

        var v3 = Criar(versao: 3, migrar: (v, dados) =>
        {
            passos.Add(v);
            dados["valor"] = dados["valor"]!.GetValue<int>() * 10;
            return dados;
        });

        v3.Carregar().Valor.Should().Be(new Config("a", 100));
        passos.Should().Equal(1, 2);
    }

    [Fact]
    public void Arquivo_de_versao_mais_nova_nao_e_lido_nem_sobrescrito()
    {
        var novo = Criar(versao: 2);
        novo.Salvar(new Config("futuro", 9));
        var antigo = Criar(versao: 1);

        antigo.Carregar().EhOk.Should().BeFalse();
        antigo.Salvar(new Config("velho", 1)).EhOk.Should().BeFalse();
        novo.Carregar().Valor.Nome.Should().Be("futuro");
    }

    [Fact]
    public void Pasta_padrao_fica_em_LocalAppData_Menage()
    {
        PastaDeDados.Padrao().Caminho.Should().EndWith(Path.Combine("AppData", "Local", "Menage"));
    }
}
