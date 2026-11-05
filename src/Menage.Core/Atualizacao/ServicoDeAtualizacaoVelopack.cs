using Velopack;

namespace Menage.Core.Atualizacao;

public sealed class ServicoDeAtualizacaoVelopack : IServicoDeAtualizacao
{
    private readonly UpdateManager? _gerente;
    private readonly string _caminhoOpcoes;
    private OpcoesDeAtualizacao _opcoes;

    public ServicoDeAtualizacaoVelopack(OpcoesDeAtualizacao opcoes, string caminhoOpcoes)
    {
        _opcoes = opcoes;
        _caminhoOpcoes = caminhoOpcoes;
        if (!string.IsNullOrWhiteSpace(opcoes.Origem))
            _gerente = new UpdateManager(FabricaDeOrigem.Criar(opcoes.Origem), GerenteVelopack.OpcoesDoCanal(opcoes.Canal));
    }

    public bool PodeAtualizar => _gerente is { IsInstalled: true };

    public string? VersaoIgnorada => _opcoes.VersaoIgnorada;

    public async Task<AtualizacaoDisponivel?> VerificarAsync(CancellationToken ct = default)
    {
        if (!PodeAtualizar) return null;
        var info = await _gerente!.CheckForUpdatesAsync().WaitAsync(ct);
        return info is null ? null : new AtualizacaoDisponivel(info.TargetFullRelease.Version.ToString(), info);
    }

    public Task BaixarAsync(AtualizacaoDisponivel nova, IProgress<int> progresso, CancellationToken ct = default) =>
        _gerente!.DownloadUpdatesAsync(nova.Detalhe, progresso.Report, ct);

    public void AplicarEReiniciar(AtualizacaoDisponivel nova) =>
        _gerente!.ApplyUpdatesAndRestart(nova.Detalhe.TargetFullRelease);

    public void Ignorar(AtualizacaoDisponivel nova)
    {
        _opcoes = _opcoes with { VersaoIgnorada = nova.Versao };
        _opcoes.Salvar(_caminhoOpcoes);
    }
}
