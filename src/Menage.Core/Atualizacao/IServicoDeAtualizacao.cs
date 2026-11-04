using Velopack;

namespace Menage.Core.Atualizacao;

/// <summary>Versão nova encontrada; o <see cref="Detalhe"/> só serve ao serviço que a criou.</summary>
public sealed record AtualizacaoDisponivel(string Versao, UpdateInfo Detalhe);

public interface IServicoDeAtualizacao
{
    /// <summary>False quando rodando fora do instalador (ex.: F5 no Visual Studio): não há o que atualizar.</summary>
    bool PodeAtualizar { get; }

    Task<AtualizacaoDisponivel?> VerificarAsync(CancellationToken ct = default);

    Task BaixarAsync(AtualizacaoDisponivel nova, IProgress<int> progresso, CancellationToken ct = default);

    /// <summary>Fecha o app, aplica a versão baixada e reabre.</summary>
    void AplicarEReiniciar(AtualizacaoDisponivel nova);

    string? VersaoIgnorada { get; }

    void Ignorar(AtualizacaoDisponivel nova);
}
