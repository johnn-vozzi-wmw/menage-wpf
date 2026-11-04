namespace Menage.TestKit;

/// <summary>
/// Repositório Git de mentira em %TEMP%\mgXXXXXX (caminho curto), com remoto "origin" local (bare),
/// branches <c>develop</c> e <c>feature/exemplo</c> já publicadas e a <c>main</c> em uso.
/// Um por teste; <see cref="Dispose"/> apaga tudo. Nunca toca nos repositórios reais do usuário.
/// </summary>
public sealed class RepositorioDescartavel : IDisposable
{
    static readonly Dictionary<string, string?> _ambiente = new()
    {
        ["GIT_TERMINAL_PROMPT"] = "0",
        ["GIT_EDITOR"] = "true",
        ["GIT_SEQUENCE_EDITOR"] = "true",
        ["GIT_PAGER"] = "cat",
        ["GIT_CONFIG_GLOBAL"] = "NUL",
        ["GIT_CONFIG_NOSYSTEM"] = "1",
        ["GIT_AUTHOR_NAME"] = "Teste",
        ["GIT_AUTHOR_EMAIL"] = "teste@menage.local",
        ["GIT_COMMITTER_NAME"] = "Teste",
        ["GIT_COMMITTER_EMAIL"] = "teste@menage.local",
        ["GIT_SSH_COMMAND"] = null,
    };

    readonly ProcessoRunner _runner = new();

    RepositorioDescartavel(string raiz)
    {
        Raiz = raiz;
        Origin = Path.Combine(raiz, "o.git");
        Caminho = Path.Combine(raiz, "w");
    }

    public string Raiz { get; }

    /// <summary>Remoto "origin" (repositório bare).</summary>
    public string Origin { get; }

    /// <summary>Cópia de trabalho.</summary>
    public string Caminho { get; }

    public string BranchAtual => Rodar("rev-parse", "--abbrev-ref", "HEAD");

    public Projeto ComoProjeto(string nome = "projeto-teste", params string[] grupos) => new(nome, Caminho, grupos);

    public static RepositorioDescartavel Criar()
    {
        var repo = new RepositorioDescartavel(Path.Combine(Path.GetTempPath(), "mg" + Guid.NewGuid().ToString("N")[..6]));
        Directory.CreateDirectory(repo.Raiz);
        repo.RodarEm(repo.Raiz, "init", "--bare", "-b", "main", repo.Origin);
        repo.RodarEm(repo.Raiz, "clone", repo.Origin, repo.Caminho);
        repo.Rodar("checkout", "-b", "main");
        repo.Commitar("README.md", "exemplo\n", "Commit inicial");
        repo.Rodar("push", "-u", "origin", "main");
        repo.CriarBranch("develop");
        repo.CriarBranch("feature/exemplo");
        repo.Rodar("checkout", "main");
        return repo;
    }

    /// <summary>Roda o git na cópia de trabalho; lança se falhar. Devolve a saída sem a quebra final.</summary>
    public string Rodar(params string[] args) => RodarEm(Caminho, args);

    public void Commitar(string arquivo, string conteudo, string mensagem)
    {
        var destino = Path.Combine(Caminho, arquivo);
        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
        File.WriteAllText(destino, conteudo);
        Rodar("add", "--", arquivo);
        Rodar("commit", "-m", mensagem);
    }

    /// <summary>Cria a branch a partir da atual, com um commit próprio, e a publica no origin.</summary>
    public void CriarBranch(string nome)
    {
        Rodar("checkout", "-b", nome);
        Commitar(nome.Replace('/', '-') + ".txt", nome, $"Commit de {nome}");
        Rodar("push", "-u", "origin", nome);
    }

    /// <summary>Simula outra pessoa: faz um commit novo na main do origin sem passar pela cópia de trabalho.</summary>
    public void AvancarOrigin(string arquivo = "remoto.txt")
    {
        var outro = Path.Combine(Raiz, "outro");
        if (!Directory.Exists(outro)) RodarEm(Raiz, "clone", Origin, outro);
        File.WriteAllText(Path.Combine(outro, arquivo), Guid.NewGuid().ToString());
        RodarEm(outro, "add", "--", arquivo);
        RodarEm(outro, "commit", "-m", "Commit remoto");
        RodarEm(outro, "push", "origin", "main");
    }

    public void Dispose() => Apagar(Raiz);

    string RodarEm(string pasta, params string[] args)
    {
        var saida = _runner.ExecutarAsync(new ComandoProcesso("git", args, pasta, _ambiente, TimeSpan.FromSeconds(60))).GetAwaiter().GetResult();
        return saida.Sucesso
            ? saida.Texto.TrimEnd('\r', '\n')
            : throw new InvalidOperationException($"git {string.Join(' ', args)} falhou: {saida.TextoErro} {saida.FalhaAoIniciar}");
    }

    static void Apagar(string pasta)
    {
        if (!Directory.Exists(pasta)) return;
        foreach (var arquivo in Directory.EnumerateFiles(pasta, "*", SearchOption.AllDirectories))
            File.SetAttributes(arquivo, FileAttributes.Normal); // objetos do .git são somente leitura
        Directory.Delete(pasta, recursive: true);
    }
}
