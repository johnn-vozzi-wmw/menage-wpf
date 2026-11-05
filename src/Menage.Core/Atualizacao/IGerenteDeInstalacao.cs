namespace Menage.Core.Atualizacao;

/// <summary>Uma versão publicada no feed da origem.</summary>
public sealed record LancamentoDaVersao(string Versao, string? Notas, long Tamanho);

/// <summary>
/// O que o Menage precisa do instalador (Velopack), escondido atrás de uma interface para os testes usarem um feed falso.
/// Só mexe no Menage instalado; nunca em outro aplicativo.
/// </summary>
public interface IGerenteDeInstalacao
{
    /// <summary>False fora do instalador (F5 no Visual Studio, zip portátil): nada a atualizar.</summary>
    bool Instalado { get; }

    string? VersaoAtual { get; }

    /// <summary>Versões completas do feed (do canal configurado), em qualquer ordem.</summary>
    Task<IReadOnlyList<LancamentoDaVersao>> ListarAsync(CancellationToken ct = default);

    /// <summary>Baixa o pacote completo da versão (serve para subir e para voltar de versão).</summary>
    Task BaixarAsync(string versao, IProgress<int> progresso, CancellationToken ct = default);

    /// <summary>Fecha o app, aplica a versão baixada e reabre.</summary>
    void AplicarEReiniciar(string versao);
}

public interface IFabricaDeGerente
{
    /// <summary>Nulo se não há origem configurada.</summary>
    IGerenteDeInstalacao? Criar(string? origem, string? canal);
}
