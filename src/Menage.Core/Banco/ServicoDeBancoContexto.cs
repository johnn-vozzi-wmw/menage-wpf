using System.Text;

namespace Menage.Core.Banco;

/// <summary>Estado do context.xml em relação aos perfis cadastrados.</summary>
public sealed record SituacaoDoContexto(string Texto, string? PerfilAtivo, PerfilDeBanco? Lido);

/// <summary>Leitura e gravação do context.xml no disco: preserva BOM e quebra de linha e deixa uma cópia <c>.gm.bak</c>.</summary>
public static class ArquivoDeContexto
{
    public const string SufixoDaCopia = ".gm.bak";

    public static Resultado<string> Ler(string caminho)
    {
        if (!File.Exists(caminho))
            return Resultado.Falha<string>($"context.xml não encontrado: {caminho}", null, "Ajuste o caminho em Gerenciar.");
        try
        {
            return Resultado.Ok(LerTexto(File.ReadAllBytes(caminho), out _));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Resultado.Falha<string>($"Não foi possível ler {caminho}", ex.Message);
        }
    }

    static string LerTexto(byte[] bytes, out bool bom)
    {
        bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        return new UTF8Encoding(false).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
    }

    /// <summary>Qual perfil cadastrado está ativo no arquivo: nome, "" (outra configuração) ou nulo (nenhum banco ativo).</summary>
    public static Resultado<SituacaoDoContexto> Situacao(string caminho, IEnumerable<PerfilDeBanco> perfis) =>
        Ler(caminho).Mapear(texto =>
        {
            var lido = ContextoXml.LerAtivo(texto);
            if (lido is null) return new SituacaoDoContexto(texto, null, null);
            return new SituacaoDoContexto(texto, perfis.FirstOrDefault(p => p.Equivale(lido))?.Nome ?? "", lido);
        });

    /// <summary>Grava o perfil ativo no arquivo (a cópia de segurança só é feita se a validação passar).</summary>
    public static Resultado<Nada> Aplicar(string caminho, PerfilDeBanco perfil, IEnumerable<PerfilDeBanco> todos)
    {
        if (!File.Exists(caminho))
            return Resultado.Falha<Nada>($"context.xml não encontrado: {caminho}", null, "Ajuste o caminho em Gerenciar.");
        try
        {
            var texto = LerTexto(File.ReadAllBytes(caminho), out var bom);
            var novo = ContextoXml.Aplicar(texto, perfil, todos);
            if (!novo.EhOk) return Resultado.Falha<Nada>(novo.Erro);
            File.Copy(caminho, caminho + SufixoDaCopia, overwrite: true);
            File.WriteAllText(caminho, novo.Valor, new UTF8Encoding(bom));
            return Resultado.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Resultado.Falha<Nada>($"Não foi possível gravar {caminho}", ex.Message, "Feche o programa que usa o arquivo e tente de novo.");
        }
    }
}

/// <summary>
/// O módulo de banco de verdade: a janela principal escolhe e aplica perfis (nomes) por aqui e o estado é lido do próprio context.xml.
/// </summary>
public sealed class ServicoDeBancoContexto : IServicoDeBanco
{
    readonly RepositorioDeBanco _repositorio;

    public ServicoDeBancoContexto(RepositorioDeBanco repositorio)
    {
        _repositorio = repositorio;
        _repositorio.Alterado += AoAlterarCadastro;
        Estado = LerEstado();
    }

    public IReadOnlyList<string> Perfis => [.. _repositorio.Perfis.Select(p => p.Nome)];

    public string PerfilAtual => _repositorio.PorId(_repositorio.Configuracao.Selecionado)?.Nome ?? "";

    public string Estado { get; private set; }

    public event EventHandler? Alterado;

    public void Escolher(string perfil)
    {
        if (_repositorio.PorNome(perfil) is { } p) _repositorio.Escolher(p.Id);
    }

    public Task<Resultado<Nada>> AplicarAsync(CancellationToken cancelar = default)
    {
        if (_repositorio.PorId(_repositorio.Configuracao.Selecionado) is not { } perfil)
            return Task.FromResult(Resultado.Falha<Nada>("Nenhum perfil de banco escolhido", null, "Use Gerenciar para criar ou importar um perfil."));
        var r = ArquivoDeContexto.Aplicar(_repositorio.Caminho, perfil, _repositorio.Perfis);
        Atualizar();
        return Task.FromResult(r);
    }

    /// <summary>Relê o context.xml (ele pode ter sido alterado fora do programa).</summary>
    public void Atualizar()
    {
        Estado = LerEstado();
        Alterado?.Invoke(this, EventArgs.Empty);
    }

    void AoAlterarCadastro() => Atualizar();

    string LerEstado()
    {
        var s = ArquivoDeContexto.Situacao(_repositorio.Caminho, _repositorio.Perfis);
        if (!s.EhOk) return "context.xml não encontrado";
        return s.Valor.PerfilAtivo switch
        {
            null => "Nenhum banco ativo",
            "" => "Ativo: outra configuração",
            var nome => $"Ativo: {nome}",
        };
    }
}
