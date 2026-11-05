using Menage.Core.Armazenamento;
using Menage.Core.Http;
using Menage.Core.Processos;
using Menage.Core.Repos;

namespace Menage.Core.PullRequests;

/// <summary>
/// Pull requests do repositório do Menage, para o dono: lista (API do GitHub com a credencial do Git Credential Manager, ou as refs
/// <c>refs/pull/*/head</c> do origin), valida cada PR numa cópia temporária (merge com a main + comando de compilação) e mescla os
/// validados na main LOCAL. Não sobe versão nem gera instalador (isso é o <c>publicar.ps1</c>) e só envia ao GitHub se pedido.
/// </summary>
public sealed class ServicoDePullRequests(IGit git, IProcessoRunner runner, IClienteHttp http, IProvedorDeTokenGitHub tokens, IArmazenamento<ConfiguracaoDosPrs> armazenamento)
{
    ConfiguracaoDosPrs? _configuracao;

    /// <summary>Testes: pula a API do GitHub e usa só o git.</summary>
    public bool SemApi { get; set; }

    /// <summary>Onde ficam as cópias temporárias da validação (padrão %TEMP%).</summary>
    public string? PastaTemporaria { get; set; }

    public ConfiguracaoDosPrs Configuracao => _configuracao ??= armazenamento.Carregar().ValorOu(ConfiguracaoDosPrs.Padrao());

    public Resultado<Nada> Salvar(ConfiguracaoDosPrs nova)
    {
        var r = armazenamento.Salvar(nova);
        if (r.EhOk) _configuracao = nova;
        return r;
    }

    /// <summary>Só o dono (e-mail global do git) vê a tela. Conveniência: o GitHub é quem autoriza o push.</summary>
    public async Task<bool> PermitidoAsync(string? emailSimulado = null, CancellationToken cancelar = default)
    {
        var email = emailSimulado;
        if (email is null)
        {
            var saida = await runner.ExecutarAsync(new ComandoProcesso("git", ["config", "--global", "user.email"], Timeout: TimeSpan.FromSeconds(10)), cancelar);
            email = saida.Sucesso ? saida.Texto.Trim() : "";
        }

        return email.StartsWith(Configuracao.PrefixoEmailDono, StringComparison.OrdinalIgnoreCase);
    }

    public static bool EhRepositorioGit(string? pasta) =>
        !string.IsNullOrWhiteSpace(pasta) && (Directory.Exists(Path.Combine(pasta, ".git")) || File.Exists(Path.Combine(pasta, ".git")));

    public async Task<Resultado<ListaDePrs>> ListarAsync(string fontes, IProgress<LinhaDoPr>? log = null, CancellationToken cancelar = default)
    {
        if (!EhRepositorioGit(fontes)) return Resultado.Falha<ListaDePrs>("A pasta das fontes não é um repositório git", fontes, "Escolha a pasta do repositório (a que tem .git).");
        var origem = await Git_(fontes, cancelar, "remote", "get-url", "origin");
        var repo = RepositorioGitHub.DeUrl(origem.EhOk ? origem.Valor.Texto.Trim() : null);
        if (!SemApi && repo is not null && await tokens.ObterAsync(cancelar) is { } token)
        {
            var api = await ListarPelaApiAsync(repo, token, cancelar);
            if (api.EhOk) return Resultado.Ok(new ListaDePrs(api.Valor, $"API do GitHub ({repo})"));
            log?.Report(new LinhaDoPr(TipoDeLinha.Aviso, $"API do GitHub indisponível ({api.Erro.Causa}); usando as refs de PR do git."));
        }

        var refs = await ListarPelasRefsAsync(fontes, cancelar);
        if (!refs.EhOk) return Resultado.Falha<ListaDePrs>(refs.Erro);
        var prs = repo is null ? refs.Valor : [.. refs.Valor.Select(p => p with { Url = $"https://github.com/{repo}/pull/{p.Numero}" })];
        return Resultado.Ok(new ListaDePrs(prs, "refs de PR do git (sem a API: título vem do último commit)"));
    }

    async Task<Resultado<IReadOnlyList<PullRequest>>> ListarPelaApiAsync(string repo, string token, CancellationToken cancelar)
    {
        var resposta = await http.ObterTextoAsync(new Uri($"https://api.github.com/repos/{repo}/pulls?state=open&per_page=100"), new CredencialHttp(null, token, Bearer: true), cancelar);
        if (!resposta.EhOk) return Resultado.Falha<IReadOnlyList<PullRequest>>(resposta.Erro);
        if (!resposta.Valor.Sucesso) return Resultado.Falha<IReadOnlyList<PullRequest>>($"HTTP {resposta.Valor.Status}");
        try
        {
            return Resultado.Ok(RepositorioGitHub.LerPrs(resposta.Valor.Corpo));
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return Resultado.Falha<IReadOnlyList<PullRequest>>("resposta inesperada", ex.Message);
        }
    }

    /// <summary>Sem API: refs/pull/N/head do origin que ainda não estão na main (PR fechado sem merge também aparece).</summary>
    async Task<Resultado<IReadOnlyList<PullRequest>>> ListarPelasRefsAsync(string fontes, CancellationToken cancelar)
    {
        var principal = Configuracao.BranchPrincipal;
        var baixou = await Git_(fontes, cancelar, "fetch", "--quiet", "origin", $"+refs/heads/{principal}:refs/remotes/origin/{principal}", "+refs/pull/*/head:refs/gm-pr/*");
        if (!baixou.EhOk) return Resultado.Falha<IReadOnlyList<PullRequest>>("Não foi possível baixar as refs de PR do origin", baixou.Erro.ToString(), "Confira a rede e o acesso ao GitHub.");
        var lista = new List<PullRequest>();
        var refs = await Git_(fontes, cancelar, "for-each-ref", "--format=%(objectname) %(refname)", "refs/gm-pr/");
        foreach (var linha in refs.EhOk ? refs.Valor.Texto.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [])
        {
            var partes = linha.Split(' ');
            if (partes.Length < 2 || !int.TryParse(partes[1][(partes[1].LastIndexOf('/') + 1)..], out var numero)) continue;
            if ((await Git_(fontes, cancelar, "merge-base", "--is-ancestor", partes[0], $"refs/remotes/origin/{principal}")).EhOk) continue;
            var info = await Git_(fontes, cancelar, "log", "-1", "--format=%s%x1f%an%x1f%cd", "--date=format:%d/%m %H:%M", partes[0]);
            var campos = ((info.EhOk ? info.Valor.Texto.Trim() : "") + "\x1f\x1f").Split('\x1f');
            lista.Add(new PullRequest { Numero = numero, Sha = partes[0], Titulo = campos[0], Autor = campos[1], Atualizado = campos[2] });
        }

        await ApagarRefsAsync(fontes, cancelar);
        return Resultado.Ok<IReadOnlyList<PullRequest>>([.. lista.OrderBy(p => p.Numero)]);
    }

    internal async Task ApagarRefsAsync(string fontes, CancellationToken cancelar = default)
    {
        var refs = await Git_(fontes, cancelar, "for-each-ref", "--format=%(refname)", "refs/gm-pr/");
        if (!refs.EhOk) return;
        foreach (var r in refs.Valor.Texto.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            await Git_(fontes, cancelar, "update-ref", "-d", r);
    }

    /// <summary>Roda o git na pasta; código ≠ 0 vira <see cref="Erro"/> (o chamador decide se importa).</summary>
    internal Task<Resultado<SaidaProcesso>> Git_(string pasta, CancellationToken cancelar, params string[] argumentos)
    {
        var args = Git.Em(pasta).Comando(argumentos[0]);
        foreach (var a in argumentos.Skip(1)) args = args.Argumento(a);
        return git.ExecutarAsync(args, cancelar);
    }

    internal IProcessoRunner Runner => runner;
}
