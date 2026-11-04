using Velopack;
using Velopack.Sources;

namespace Menage.Core.Atualizacao;

public sealed class ServicoDeAtualizacaoVelopack : IServicoDeAtualizacao
{
    private const string PrefixoGithub = "github:";
    private readonly UpdateManager? _gerente;
    private readonly string _caminhoOpcoes;
    private OpcoesDeAtualizacao _opcoes;

    public ServicoDeAtualizacaoVelopack(OpcoesDeAtualizacao opcoes, string caminhoOpcoes)
    {
        _opcoes = opcoes;
        _caminhoOpcoes = caminhoOpcoes;
        if (!string.IsNullOrWhiteSpace(opcoes.Origem))
            _gerente = new UpdateManager(CriarOrigem(opcoes.Origem));
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

    private static IUpdateSource CriarOrigem(string origem)
    {
        if (!origem.StartsWith(PrefixoGithub, StringComparison.OrdinalIgnoreCase))
            return origem.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? new SimpleWebSource(origem)
                : new SimpleFileSource(new DirectoryInfo(origem));

        var token = Environment.GetEnvironmentVariable(OpcoesDeAtualizacao.VariavelToken);
        return new GithubSource($"https://github.com/{origem[PrefixoGithub.Length..]}", token, false);
    }
}
