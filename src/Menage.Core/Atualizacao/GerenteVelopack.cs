using Velopack;
using Velopack.Sources;

namespace Menage.Core.Atualizacao;

/// <summary>Origem: pasta local/UNC, URL http(s) ou "github:dono/repo" (token só pela variável de ambiente).</summary>
public static class FabricaDeOrigem
{
    const string PrefixoGithub = "github:";

    public static IUpdateSource Criar(string origem)
    {
        if (!origem.StartsWith(PrefixoGithub, StringComparison.OrdinalIgnoreCase))
            return origem.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? new SimpleWebSource(origem)
                : new SimpleFileSource(new DirectoryInfo(origem));

        var token = Environment.GetEnvironmentVariable(OpcoesDeAtualizacao.VariavelToken);
        return new GithubSource($"https://github.com/{origem[PrefixoGithub.Length..]}", token, false);
    }
}

/// <summary>Implementação real sobre o <see cref="UpdateManager"/>. Voltar de versão = baixar o pacote completo mais antigo do feed.</summary>
public sealed class GerenteVelopack : IGerenteDeInstalacao
{
    readonly Expondo _gerente;
    readonly IUpdateSource _origem;
    readonly Dictionary<string, VelopackAsset> _completos = [];

    public GerenteVelopack(string origem, string? canal)
    {
        _origem = FabricaDeOrigem.Criar(origem);
        _gerente = new Expondo(_origem, OpcoesDoCanal(canal));
    }

    /// <summary>Canal estável é o padrão do pacote; "beta" é publicado com <c>vpk pack --channel beta</c>.</summary>
    public static UpdateOptions OpcoesDoCanal(string? canal) =>
        new() { ExplicitChannel = string.IsNullOrWhiteSpace(canal) ? null : canal, AllowVersionDowngrade = true };

    public bool Instalado => _gerente.IsInstalled;

    public string? VersaoAtual => _gerente.IsInstalled ? _gerente.CurrentVersion?.ToString() : null;

    public async Task<IReadOnlyList<LancamentoDaVersao>> ListarAsync(CancellationToken ct = default)
    {
        if (!Instalado) return [];
        var feed = await _origem.GetReleaseFeed(_gerente.Registro, _gerente.AppId!, _gerente.CanalEfetivo, null, null!).WaitAsync(ct);
        _completos.Clear();
        foreach (var a in feed.Assets.Where(a => a.Type == VelopackAssetType.Full)) _completos[a.Version.ToString()!] = a!;
        return _completos.Values.Select(a => new LancamentoDaVersao(a.Version.ToString(), a.NotesMarkdown, a.Size)).ToList();
    }

    public Task BaixarAsync(string versao, IProgress<int> progresso, CancellationToken ct = default) =>
        _gerente.DownloadUpdatesAsync(Informacao(versao), progresso.Report, ct);

    public void AplicarEReiniciar(string versao) => _gerente.ApplyUpdatesAndRestart(Informacao(versao).TargetFullRelease);

    UpdateInfo Informacao(string versao)
    {
        if (!_completos.TryGetValue(versao, out var alvo)) throw new InvalidOperationException($"A versão {versao} não está no feed carregado.");
        var recuo = alvo.Version < _gerente.CurrentVersion;
        return new UpdateInfo(alvo, recuo, null!, []);
    }
}

public sealed class FabricaDeGerenteVelopack : IFabricaDeGerente
{
    public IGerenteDeInstalacao? Criar(string? origem, string? canal) =>
        string.IsNullOrWhiteSpace(origem) ? null : new GerenteVelopack(origem, canal);
}

/// <summary>Abre os membros protegidos do <see cref="UpdateManager"/> que a leitura do feed precisa.</summary>
sealed class Expondo(IUpdateSource origem, UpdateOptions opcoes) : UpdateManager(origem, opcoes)
{
    public Velopack.Logging.IVelopackLogger Registro => Log;

    public string CanalEfetivo => Channel ?? DefaultChannel;
}
