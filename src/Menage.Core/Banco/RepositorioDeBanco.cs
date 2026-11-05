namespace Menage.Core.Banco;

/// <summary>
/// <c>contexto-banco.json</c>: caminho do context.xml, perfis e o perfil escolhido. <b>Contém usuários e senhas</b> (já criptografadas);
/// fica em <c>%LOCALAPPDATA%\Menage</c> e nunca deve ser versionado.
/// </summary>
public sealed record ConfiguracaoDoBanco
{
    public const string NomeDoArquivo = "contexto-banco";

    public string Caminho { get; init; } = "";

    public IReadOnlyList<PerfilDeBanco> Perfis { get; init; } = [];

    /// <summary>Id do perfil escolhido no combo da barra lateral.</summary>
    public string Selecionado { get; init; } = "";

    public static ConfiguracaoDoBanco Padrao() => new();
}

/// <summary>Cadastro de perfis: cada alteração grava na hora e avisa quem escuta.</summary>
public sealed class RepositorioDeBanco(IArmazenamento<ConfiguracaoDoBanco> armazenamento)
{
    ConfiguracaoDoBanco? _atual;

    public static string CaminhoPadrao { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "web-framework", "Servers", "lololo-config", "context.xml");

    public ConfiguracaoDoBanco Configuracao => _atual ??= armazenamento.Carregar().ValorOu(new ConfiguracaoDoBanco());

    public IReadOnlyList<PerfilDeBanco> Perfis => Configuracao.Perfis;

    /// <summary>Caminho do context.xml; sem configuração vale o padrão (<c>%USERPROFILE%\web-framework\Servers\lololo-config</c>).</summary>
    public string Caminho => Configuracao.Caminho.Length > 0 ? Configuracao.Caminho : CaminhoPadrao;

    public event Action? Alterado;

    public PerfilDeBanco? PorId(string id) => Perfis.FirstOrDefault(p => p.Id == id);

    public PerfilDeBanco? PorNome(string nome) => Perfis.FirstOrDefault(p => string.Equals(p.Nome, nome, StringComparison.OrdinalIgnoreCase));

    Resultado<Nada> Gravar(ConfiguracaoDoBanco nova)
    {
        var r = armazenamento.Salvar(nova);
        if (r.EhOk) _atual = nova;
        Alterado?.Invoke();
        return r;
    }

    public Resultado<Nada> DefinirCaminho(string caminho) => Gravar(Configuracao with { Caminho = caminho.Trim() });

    public Resultado<Nada> Escolher(string id) => Gravar(Configuracao with { Selecionado = id });

    /// <summary>Inclui ou atualiza (pelo <see cref="PerfilDeBanco.Id"/>). Nome vazio, repetido ou perfil inválido não grava.</summary>
    public Resultado<Nada> Salvar(PerfilDeBanco perfil)
    {
        if (ContextoXml.Validar(perfil) is { } erro) return Resultado.Falha<Nada>(erro);
        if (Perfis.Any(p => p.Id != perfil.Id && string.Equals(p.Nome, perfil.Nome, StringComparison.OrdinalIgnoreCase)))
            return Resultado.Falha<Nada>($"Já existe um perfil chamado \"{perfil.Nome}\"", null, "Escolha outro nome.");

        var lista = Perfis.ToList();
        var i = lista.FindIndex(p => p.Id == perfil.Id);
        if (i >= 0) lista[i] = perfil;
        else lista.Add(perfil);
        return Gravar(Configuracao with { Perfis = lista, Selecionado = Configuracao.Selecionado.Length == 0 ? perfil.Id : Configuracao.Selecionado });
    }

    public Resultado<Nada> Excluir(string id)
    {
        var lista = Perfis.Where(p => p.Id != id).ToList();
        var escolhido = Configuracao.Selecionado == id ? lista.FirstOrDefault()?.Id ?? "" : Configuracao.Selecionado;
        return Gravar(Configuracao with { Perfis = lista, Selecionado = escolhido });
    }

    /// <summary>Cópia com id novo e nome "X (cópia)", "X (cópia 2)"...; devolve o perfil criado.</summary>
    public Resultado<PerfilDeBanco> Duplicar(string id)
    {
        if (PorId(id) is not { } origem) return Resultado.Falha<PerfilDeBanco>("Perfil não encontrado");
        var nome = $"{origem.Nome} (cópia)";
        for (var n = 2; PorNome(nome) is not null; n++) nome = $"{origem.Nome} (cópia {n})";
        var copia = origem with { Id = Guid.NewGuid().ToString("N"), Nome = nome };
        return Salvar(copia).Mapear(_ => copia);
    }

    /// <summary>Nome sugerido para um banco lido do arquivo: tipo + database (Oracle: tipo + usuário); acrescenta número se já existe.</summary>
    public string NomeSugerido(PerfilDeBanco lido)
    {
        var detalhe = lido.Tipo == TipoDeBanco.Oracle ? lido.Web.Usuario : lido.Web.Banco;
        var baseNome = detalhe.Length > 0 ? $"{TipoDeBanco.Nome(lido.Tipo)} {detalhe}" : TipoDeBanco.Nome(lido.Tipo);
        var nome = baseNome;
        for (var n = 2; PorNome(nome) is not null; n++) nome = $"{baseNome} {n}";
        return nome;
    }

    /// <summary>Perfil já cadastrado que descreve a mesma configuração (para marcar como "já existe" na lista de importação).</summary>
    public PerfilDeBanco? Equivalente(PerfilDeBanco lido) => Perfis.FirstOrDefault(p => p.Equivale(lido));

    /// <summary>Cria um perfil para cada banco marcado, com nome sugerido; devolve os criados.</summary>
    public Resultado<IReadOnlyList<PerfilDeBanco>> Importar(IEnumerable<PerfilDeBanco> lidos)
    {
        var criados = new List<PerfilDeBanco>();
        var lista = Perfis.ToList();
        foreach (var lido in lidos)
        {
            var nome = NomeUnico(lido, lista);
            var perfil = lido with { Id = Guid.NewGuid().ToString("N"), Nome = nome };
            if (ContextoXml.Validar(perfil) is { } erro) return Resultado.Falha<IReadOnlyList<PerfilDeBanco>>($"\"{nome}\" não pôde ser importado", erro);
            lista.Add(perfil);
            criados.Add(perfil);
        }

        if (criados.Count == 0) return Resultado.Ok<IReadOnlyList<PerfilDeBanco>>(criados);
        var selecionado = Configuracao.Selecionado.Length == 0 ? criados[0].Id : Configuracao.Selecionado;
        return Gravar(Configuracao with { Perfis = lista, Selecionado = selecionado }).Mapear<IReadOnlyList<PerfilDeBanco>>(_ => criados);
    }

    string NomeUnico(PerfilDeBanco lido, List<PerfilDeBanco> lista)
    {
        var sugerido = lido.Nome.Length > 0 ? lido.Nome : NomeSugerido(lido);
        var nome = sugerido;
        for (var n = 2; lista.Exists(p => string.Equals(p.Nome, nome, StringComparison.OrdinalIgnoreCase)); n++) nome = $"{sugerido} {n}";
        return nome;
    }
}
