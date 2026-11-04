using System.Text.RegularExpressions;

namespace Menage.Core.ModoBeta;

/// <summary>Uma linha do ajuste: o projeto, a situação e o que aconteceu.</summary>
public sealed record AjusteDeFramework(Projeto Projeto, Situacao Situacao, string Mensagem);

/// <param name="Ajustes">Por projeto.</param>
/// <param name="Observacoes">Do grupo inteiro (versões diferentes, nenhum repositório).</param>
public sealed record ConclusaoDoBeta(IReadOnlyList<AjusteDeFramework> Ajustes, IReadOnlyList<ObservacaoDoLote> Observacoes);

/// <param name="Branch">Branch dos principais; os frameworks vão para <c>Branch_b</c>.</param>
/// <param name="Criar">Sem a <c>_b</c>, cria a partir da tag em vez de ficar em detached HEAD.</param>
/// <param name="Publicar">Faz push da <c>_b</c> criada.</param>
public sealed record PedidoDeBeta(string Branch, bool Criar = false, bool Publicar = false);

/// <summary>
/// Modo beta: quando os projetos principais trocam de branch, os frameworks acompanham o <c>framework.version</c> do pom.xml deles.
/// </summary>
public sealed class AjustadorDeFrameworks(IGit git, ConsultasGit consultas, IProjetos projetos, IModoBeta modoBeta)
{
    /// <summary>develop e release*: principais e frameworks andam juntos, e o pom aponta para um -SNAPSHOT sem tag.</summary>
    public static bool NaoAgeEm(string? destino) =>
        !string.IsNullOrEmpty(destino) &&
        (destino.Equals("develop", StringComparison.OrdinalIgnoreCase) || destino.StartsWith("release", StringComparison.OrdinalIgnoreCase));

    /// <summary>Só age se há projeto principal na operação; sem nenhum, os frameworks são tratados normalmente.</summary>
    public bool Age(IReadOnlyList<Projeto> alvo, string? destino) =>
        modoBeta.Ligado && !NaoAgeEm(destino) && alvo.Any(p => !p.EhFramework);

    /// <summary>Com o beta agindo, os frameworks saem do lote: quem os ajusta é <see cref="AplicarAsync"/>.</summary>
    public IReadOnlyList<Projeto> SemFrameworks(IReadOnlyList<Projeto> alvo, string? destino) =>
        Age(alvo, destino) ? [.. alvo.Where(p => !p.EhFramework)] : alvo;

    /// <param name="principais">Projetos que trocaram de branch de fato.</param>
    public async Task<ConclusaoDoBeta> AplicarAsync(IEnumerable<Projeto> principais, PedidoDeBeta pedido, CancellationToken cancelar = default)
    {
        var ajustes = new List<AjusteDeFramework>();
        var observacoes = new List<ObservacaoDoLote> { new($"Modo beta - branch dos frameworks: {pedido.Branch}_b") };
        var registro = new RegistroDeVersoes();
        var registrados = 0;
        foreach (var principal in principais.Where(p => !p.EhFramework))
        {
            registrados++;
            if (!registro.Registrar(principal))
                ajustes.Add(new(principal, Situacao.Erro, "framework.version não encontrado no pom.xml"));
        }

        if (registrados == 0) observacoes.Add(new("Nenhum projeto principal foi trocado ou criado: nada a ajustar."));
        foreach (var familia in new[] { Familias.Framework, Familias.FrameworkTc })
            await AjustarFamiliaAsync(registro, familia, pedido, ajustes, observacoes, cancelar);
        return new(ajustes, observacoes);
    }

    async Task AjustarFamiliaAsync(
        RegistroDeVersoes registro, string familia, PedidoDeBeta pedido, List<AjusteDeFramework> ajustes, List<ObservacaoDoLote> observacoes, CancellationToken cancelar)
    {
        if (registro.VersaoDaFamilia(familia) is not { } versao) return;
        if (registro.FamiliaEmConflito(familia))
        {
            observacoes.Add(new($"Grupo {familia} não ajustado - framework.version diferentes: {registro.OrigemDaFamilia(familia)}", Situacao.Erro));
            return;
        }

        Projeto[] repos = [.. projetos.Todos.Where(p => p.Familia == familia)];
        if (repos.Length == 0)
        {
            observacoes.Add(new($"Grupo {familia}: nenhum repositório encontrado", Situacao.Erro));
            return;
        }

        var usos = repos.ToDictionary(r => r.Nome, r => (IReadOnlyCollection<string>)LeitorDePom.LerDependenciasFramework(r.Caminho).Keys, StringComparer.OrdinalIgnoreCase);
        var versoes = registro.VersoesDosFrameworks(repos, versao, usos);
        foreach (var repo in repos)
        {
            if (registro.DependenciaEmConflito(repo.Nome))
                ajustes.Add(new(repo, Situacao.Erro, $"não ajustado - versões diferentes nos pom.xml: {registro.OrigemDaDependencia(repo.Nome)}"));
            else
                ajustes.AddRange(await AjustarAsync(repo, versoes[repo.Nome].Versao, pedido, cancelar));
        }
    }

    async Task<IReadOnlyList<AjusteDeFramework>> AjustarAsync(Projeto repo, string versao, PedidoDeBeta pedido, CancellationToken cancelar)
    {
        var branch = pedido.Branch + "_b";
        await consultas.AtualizarRefsAsync(repo.Caminho, cancelar);

        var existente = await consultas.ResolverCheckoutAsync(repo.Caminho, branch, cancelar);
        if (existente?.Tipo == TipoDeRef.BranchLocal)
            return [await TrocarAsync(repo, Git.Em(repo.Caminho).Comando("switch").Argumento(branch), $"branch {branch}", cancelar)];
        if (existente?.Tipo == TipoDeRef.BranchEmOrigin)
            return [await TrocarAsync(repo, Git.Em(repo.Caminho).Comando("switch").Opcao("-c").Argumento(branch).Opcao("--track").Argumento(existente.Ref), $"branch {branch} - do origin", cancelar)];

        // a tag é SEMPRE a versão sem -SNAPSHOT (5.050-SNAPSHOT busca a tag 5.050)
        var tag = Regex.Replace(versao, "-SNAPSHOT", "", RegexOptions.IgnoreCase);
        if (!await consultas.RefExisteAsync(repo.Caminho, "refs/tags/" + tag, cancelar))
            return [new(repo, Situacao.Erro, $"não existe a branch {branch} nem a tag {tag}")];

        return pedido.Criar
            ? await CriarDaTagAsync(repo, branch, versao, tag, pedido.Publicar, cancelar)
            : await IrParaATagAsync(repo, versao, tag, cancelar);
    }

    async Task<IReadOnlyList<AjusteDeFramework>> IrParaATagAsync(Projeto repo, string versao, string tag, CancellationToken cancelar)
    {
        var troca = await TrocarAsync(repo, Git.Em(repo.Caminho).Comando("checkout").Argumento("refs/tags/" + tag), $"tag {tag} - detached HEAD", cancelar);
        return troca.Situacao == Situacao.Ok ? [troca, .. ColocarSnapshot(repo, versao, tag)] : [troca];
    }

    async Task<IReadOnlyList<AjusteDeFramework>> CriarDaTagAsync(Projeto repo, string branch, string versao, string tag, bool publicar, CancellationToken cancelar)
    {
        var args = Git.Em(repo.Caminho).Comando("checkout").Opcao("--no-track").Opcao("-b").Argumento(branch).Argumento("refs/tags/" + tag);
        var troca = await TrocarAsync(repo, args, $"branch {branch} criada da tag {tag}", cancelar);
        if (troca.Situacao != Situacao.Ok) return [troca];

        List<AjusteDeFramework> ajustes = [troca, .. ColocarSnapshot(repo, versao, tag)];
        if (publicar)
        {
            var push = await git.ExecutarAsync(Git.Em(repo.Caminho).Comando("push").Opcao("-u").Argumento("origin").Argumento(branch).ComTimeout(ConsultasGit.TempoLongo), cancelar);
            if (!push.EhOk) ajustes.Add(new(repo, Situacao.Erro, $"branch {branch} criada, mas o push falhou"));
        }

        return ajustes;
    }

    async Task<AjusteDeFramework> TrocarAsync(Projeto repo, GitArgs args, string mensagem, CancellationToken cancelar) =>
        (await git.ExecutarAsync(args, cancelar)).EhOk
            ? new(repo, Situacao.Ok, mensagem)
            : new(repo, Situacao.Erro, "falha ao trocar/criar - verifique alterações locais");

    static List<AjusteDeFramework> ColocarSnapshot(Projeto repo, string versao, string tag)
    {
        try
        {
            var (alterados, avisos) = SnapshotNoPom.Aplicar(repo.Caminho, versao, tag);
            var linhas = avisos.Select(a => new AjusteDeFramework(repo, Situacao.Aviso, a)).ToList();
            if (alterados > 0) linhas.Add(new(repo, Situacao.Ok, $"pom.xml com {versao}"));
            return linhas;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [new(repo, Situacao.Erro, $"falha ao colocar -SNAPSHOT no pom.xml: {ex.Message}")];
        }
    }
}
