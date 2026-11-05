namespace Menage.Core.Projetos;

public interface IModoBeta
{
    bool Ligado { get; }
}

public sealed class ModoBetaFixo(bool ligado = false) : IModoBeta
{
    public bool Ligado { get; } = ligado;
}

/// <summary>Catálogo em uso e os projetos que ele resolve.</summary>
public interface IConfiguracaoDeProjetos : IModoBeta
{
    CatalogoDeProjetos Catalogo { get; }

    IReadOnlyList<Projeto> Projetos { get; }

    /// <summary>Texto para o usuário quando a configuração foi recuperada ou algo não pôde ser lido; vazio se está tudo bem.</summary>
    string Aviso { get; }

    Resultado<Nada> Salvar(CatalogoDeProjetos catalogo);

    Resultado<Nada> SalvarModoBeta(bool ligado);

    /// <summary>Troca a pasta raiz e procura de novo os projetos reconhecidos.</summary>
    Resultado<Nada> SalvarRaiz(string raiz);

    event EventHandler? Alterado;
}

/// <summary>
/// Grava em <c>projetos.json</c> e guarda a versão anterior em <c>projetos-backup.json</c>. Arquivo inválido gera aviso e usa o
/// backup, sem sobrescrever o original.
/// </summary>
public sealed class ConfiguracaoDeProjetos : IConfiguracaoDeProjetos
{
    readonly Armazenamento<CatalogoDeProjetos> _principal;
    readonly Armazenamento<CatalogoDeProjetos> _backup;

    public ConfiguracaoDeProjetos(PastaDeDados pasta)
    {
        _principal = new(pasta, "projetos", padrao: CatalogoDeProjetos.Padrao);
        _backup = new(pasta, "projetos-backup");
        Carregar();
    }

    public event EventHandler? Alterado;

    public CatalogoDeProjetos Catalogo { get; private set; } = new();

    public IReadOnlyList<Projeto> Projetos { get; private set; } = [];

    public string Aviso { get; private set; } = "";

    public bool Ligado => Catalogo.ModoBeta;

    public Resultado<Nada> Salvar(CatalogoDeProjetos catalogo)
    {
        if (catalogo.Validar() is { } invalido) return Resultado.Falha<Nada>(invalido);

        if (File.Exists(_principal.Arquivo) && _principal.Carregar() is { EhOk: true } atual) _backup.Salvar(atual.Valor);
        var gravou = _principal.Salvar(catalogo);
        if (!gravou.EhOk) return gravou;

        Aviso = "";
        Aplicar(catalogo);
        Alterado?.Invoke(this, EventArgs.Empty);
        return gravou;
    }

    public Resultado<Nada> SalvarModoBeta(bool ligado) => Salvar(Catalogo with { ModoBeta = ligado });

    public Resultado<Nada> SalvarRaiz(string raiz) => Salvar(DescobertaDeProjetos.Redescobrir(Catalogo, raiz));

    void Carregar()
    {
        var existia = File.Exists(_principal.Arquivo);
        var lido = _principal.Carregar();
        var problema = lido.EhOk ? lido.Valor.Validar() : lido.Erro;
        var catalogo = problema is null ? lido.Valor : Recuperar(problema);
        var principaisAntes = catalogo.Principais;

        Aplicar(catalogo);

        // Em recuperação nada é gravado sozinho: o original inválido fica para o usuário corrigir.
        if (problema is null && (!existia || MudouPrincipais(principaisAntes))) _principal.Salvar(Catalogo);
    }

    CatalogoDeProjetos Recuperar(Erro problema)
    {
        Aviso = $"Configuração de projetos inválida; usando recuperação. Original preservado. {problema}";
        var backup = _backup.Carregar();
        return backup.EhOk && backup.Valor.Validar() is null ? backup.Valor : CatalogoDeProjetos.Padrao();
    }

    void Aplicar(CatalogoDeProjetos catalogo)
    {
        var resolvidos = ResolvedorDeProjetos.Resolver(catalogo);
        Catalogo = catalogo with { Principais = new Dictionary<string, string>(resolvidos.Principais) };
        Projetos = resolvidos.Projetos;
        if (resolvidos.Avisos.Count > 0) Aviso = string.Join('\n', new[] { Aviso }.Concat(resolvidos.Avisos).Where(a => a.Length > 0));
    }

    bool MudouPrincipais(IReadOnlyDictionary<string, string> antes) =>
        antes.Count != Catalogo.Principais.Count || antes.Any(p => !Catalogo.Principais.TryGetValue(p.Key, out var v) || v != p.Value);
}
