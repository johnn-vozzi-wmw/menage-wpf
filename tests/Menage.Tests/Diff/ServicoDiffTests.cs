using System.Text;

namespace Menage.Tests.Diff;

public sealed class ServicoDiffTests : IDisposable
{
    readonly RepositorioDescartavel _repo = RepositorioDescartavel.Criar();
    readonly ServicoDiff _servico = new(GitDeTeste.Criar());

    public void Dispose() => _repo.Dispose();

    void Gravar(string arquivo, byte[] bytes) => File.WriteAllBytes(Path.Combine(_repo.Caminho, arquivo), bytes);

    void Gravar(string arquivo, string texto) => Gravar(arquivo, Encoding.UTF8.GetBytes(texto));

    async Task<ArquivoAlterado> Status(string caminho) =>
        (await _servico.ListarAsync(_repo.Caminho)).Valor.Single(a => a.Caminho == caminho);

    async Task<ModeloDiff> Diff(string caminho) => (await _servico.MontarAsync(_repo.Caminho, await Status(caminho))).Valor;

    [Fact]
    public async Task Status_real_vira_lista_com_renomeado_novo_excluido_e_acento()
    {
        _repo.Commitar("velho.txt", "um\ndois\ntres\nquatro\ncinco\n", "base");
        _repo.Commitar("apagar.txt", "x\ny\n", "base2");
        _repo.Rodar("mv", "velho.txt", "renomeado.txt");
        File.Delete(Path.Combine(_repo.Caminho, "apagar.txt"));
        Gravar("ação.txt", "novo\n");

        var lista = (await _servico.ListarAsync(_repo.Caminho)).Valor;

        lista.Should().Contain(a => a.Tipo == TipoAlteracao.Renomeado && a.Antigo == "velho.txt");
        lista.Should().Contain(a => a.Tipo == TipoAlteracao.Novo && a.Caminho == "ação.txt");
        lista.Should().Contain(a => a.Tipo == TipoAlteracao.Excluido && a.Caminho == "apagar.txt");
    }

    [Fact]
    public async Task Arquivo_novo_aparece_todo_a_direita_e_excluido_todo_a_esquerda()
    {
        _repo.Commitar("apagar.txt", "x\ny\n", "base");
        File.Delete(Path.Combine(_repo.Caminho, "apagar.txt"));
        Gravar("fora.txt", "novo 1\nnovo 2\n");

        var novo = await Diff("fora.txt");
        var excluido = await Diff("apagar.txt");

        novo.Novo.Should().BeTrue();
        novo.Dir.Should().HaveCount(2);
        excluido.Excluido.Should().BeTrue();
        excluido.Esq.Should().HaveCount(2);
    }

    [Fact]
    public async Task Renomeado_compara_com_o_nome_antigo_no_head()
    {
        _repo.Commitar("velho.txt", "um\ndois\ntres\nquatro\ncinco\n", "base");
        _repo.Rodar("mv", "velho.txt", "renomeado.txt");
        Gravar("renomeado.txt", "um\ndois\nTRES\nquatro\ncinco\n");
        _repo.Rodar("add", "--", "renomeado.txt");

        var m = await Diff("renomeado.txt");

        m.Segmentos.Count(s => !s.Igual).Should().Be(1);
        m.Esq.Should().HaveCount(5);
        m.Dir.Should().HaveCount(5);
    }

    [Fact]
    public async Task Binario_nao_vira_texto()
    {
        _repo.Commitar("base.txt", "x\n", "base");
        Gravar("img.bin", [1, 2, 0, 3]);
        _repo.Rodar("add", "--", "img.bin");
        _repo.Rodar("commit", "-m", "bin");
        Gravar("img.bin", [1, 2, 0, 4]);

        (await Diff("img.bin")).Motivo.Should().Be(SemTexto.Binario);
    }

    [Fact]
    public async Task Com_autocrlf_o_head_vem_como_um_checkout_gravaria_e_nao_acusa_fim_de_linha()
    {
        _repo.Rodar("config", "core.autocrlf", "true");
        Gravar("a.txt", "um\r\ndois\r\n");
        _repo.Rodar("add", "--", "a.txt");
        _repo.Rodar("commit", "-m", "base");
        Gravar("a.txt", "um\r\nDOIS\r\n");

        var m = await Diff("a.txt");

        m.SoFimDeLinha.Should().Be(0);
        m.Segmentos.Count(s => !s.Igual).Should().Be(1);
    }

    [Fact]
    public async Task Arquivo_em_1252_e_lido_com_acento()
    {
        Gravar("a.txt", Codificacoes.Windows1252.GetBytes("ação\n"));
        _repo.Rodar("add", "--", "a.txt");
        _repo.Rodar("commit", "-m", "base");
        Gravar("a.txt", Codificacoes.Windows1252.GetBytes("ação nova\n"));

        var m = await Diff("a.txt");

        m.Esq[0].Should().Be("ação");
        m.Direita!.NomeCodificacao.Should().Be("Windows-1252");
    }
}
