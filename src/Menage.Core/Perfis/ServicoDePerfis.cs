using System.Text.Json;
using Menage.Core.Armazenamento;

namespace Menage.Core.Perfis;

/// <summary>Quem decide se uma funcionalidade está liberada no perfil ativo (a interface e o executor de operações consultam).</summary>
public interface IRegrasDePerfil
{
    bool Permite(string funcao);
}

/// <summary>Conteúdo de <c>perfis.json</c>: o perfil ativo e os perfis editáveis (Desenvolvedor, Tester e os criados pelo usuário).</summary>
public sealed record ConfigDePerfis
{
    public string Ativo { get; init; } = PerfilDefinido.IdGeral;

    public IReadOnlyList<PerfilDefinido> Perfis { get; init; } = PerfilDefinido.Padroes;
}

public sealed record PerfilExportado(string Nome, string? Descricao, PerfilDeUso Base, IReadOnlyList<string> Permitidas);

public sealed record ArquivoDePerfis(string Formato, int Versao, IReadOnlyList<PerfilExportado> Perfis);

/// <summary>
/// Perfis de uso: criar, editar, duplicar, excluir, ativar, importar e exportar. O bloqueio vale só na interface
/// (conveniência e redução de risco, não segurança). O perfil Geral é fixo e nunca fica no arquivo.
/// </summary>
public sealed class ServicoDePerfis : IPerfilAtual, IRegrasDePerfil
{
    public const string FormatoDeArquivo = "menage-perfis";

    static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    readonly IArmazenamento<ConfigDePerfis> _armazenamento;
    ConfigDePerfis _config;

    public ServicoDePerfis(IArmazenamento<ConfigDePerfis> armazenamento)
    {
        _armazenamento = armazenamento;
        _config = Normalizar(armazenamento.Carregar().ValorOu(new ConfigDePerfis()));
    }

    public ServicoDePerfis(PastaDeDados pasta) : this(new Armazenamento<ConfigDePerfis>(pasta, "perfis", padrao: () => new ConfigDePerfis()))
    {
    }

    public event EventHandler? Alterado;

    public PerfilDeUso Atual => Ativo.Base;

    public PerfilDefinido Ativo => Obter(_config.Ativo) ?? PerfilDefinido.Geral;

    /// <summary>Geral primeiro, depois os demais na ordem em que foram criados.</summary>
    public IReadOnlyList<PerfilDefinido> Perfis => [PerfilDefinido.Geral, .. _config.Perfis];

    public PerfilDefinido? Obter(string id) => Perfis.FirstOrDefault(p => p.Id == id);

    public bool Permite(string funcao) => Ativo.Permite(funcao);

    public bool Bloqueada(string funcao) => !Permite(funcao);

    public string Motivo(string funcao) => Ativo.Motivo(funcao, Perfis);

    public Resultado<Nada> Ativar(string id)
    {
        if (Obter(id) is null) return Resultado.Falha<Nada>("Perfil não encontrado", $"Não existe perfil \"{id}\".");
        return Gravar(_config with { Ativo = id });
    }

    /// <summary>Cria (id vazio ou desconhecido) ou atualiza. Devolve o perfil gravado, com o id definitivo.</summary>
    public Resultado<PerfilDefinido> Salvar(PerfilDefinido perfil)
    {
        if (perfil.Id == PerfilDefinido.IdGeral) return Resultado.Falha<PerfilDefinido>("O perfil Geral não pode ser editado", null, "Duplique-o para criar um perfil seu.");
        var nome = perfil.Nome.Trim();
        if (nome.Length == 0) return Resultado.Falha<PerfilDefinido>("Dê um nome ao perfil");
        if (Perfis.Any(p => p.Id != perfil.Id && string.Equals(p.Nome, nome, StringComparison.OrdinalIgnoreCase)))
            return Resultado.Falha<PerfilDefinido>($"Já existe um perfil chamado \"{nome}\"", null, "Escolha outro nome.");
        if (perfil.Permitidas.FirstOrDefault(r => !RegraValida(r)) is { } ruim)
            return Resultado.Falha<PerfilDefinido>($"Funcionalidade desconhecida: \"{ruim}\"", null, "Use os ids do catálogo, * ou -id.");

        var existente = Obter(perfil.Id);
        var id = existente is null ? NovoId(nome) : perfil.Id;
        var gravar = perfil with
        {
            Nome = nome,
            Id = id,
            Permitidas = perfil.Permitidas.Distinct().ToList(),
            Base = existente is { Embutido: true } ? existente.Base : perfil.Base,
        };
        var lista = existente is null
            ? [.. _config.Perfis, gravar]
            : _config.Perfis.Select(p => p.Id == id ? gravar : p).ToList();
        var gravou = Gravar(_config with { Perfis = lista });
        return gravou.EhOk ? Resultado.Ok(Obter(id)!) : Resultado.Falha<PerfilDefinido>(gravou.Erro);
    }

    public Resultado<PerfilDefinido> Duplicar(string id)
    {
        if (Obter(id) is not { } origem) return Resultado.Falha<PerfilDefinido>("Perfil não encontrado");
        return Salvar(origem with { Id = "", Nome = NomeLivre($"{origem.Nome} (cópia)") });
    }

    public Resultado<Nada> Excluir(string id)
    {
        if (Obter(id) is not { } perfil) return Resultado.Falha<Nada>("Perfil não encontrado");
        if (perfil.Embutido)
            return Resultado.Falha<Nada>($"O perfil {perfil.Nome} faz parte do Menage e não pode ser excluído", null, "Use \"Restaurar padrão\" para desfazer edições.");
        return Gravar(_config with
        {
            Perfis = _config.Perfis.Where(p => p.Id != id).ToList(),
            Ativo = _config.Ativo == id ? PerfilDefinido.IdGeral : _config.Ativo,
        });
    }

    public Resultado<Nada> RestaurarPadrao(string id)
    {
        if (id is not (PerfilDefinido.IdDesenvolvedor or PerfilDefinido.IdTester))
            return Resultado.Falha<Nada>("Só Desenvolvedor e Tester têm padrão para restaurar");
        return Gravar(_config with { Perfis = _config.Perfis.Select(p => p.Id == id ? PerfilDefinido.Padrao(id) : p).ToList() });
    }

    /// <summary>Grava os perfis escolhidos em um arquivo que outra pessoa pode importar.</summary>
    public Resultado<Nada> Exportar(IEnumerable<string> ids, string caminho)
    {
        var escolhidos = ids.ToHashSet();
        var lista = Perfis.Where(p => escolhidos.Contains(p.Id) && p.Id != PerfilDefinido.IdGeral)
            .Select(p => new PerfilExportado(p.Nome, p.Descricao, p.Base, p.Permitidas)).ToList();
        if (lista.Count == 0) return Resultado.Falha<Nada>("Nada para exportar", null, "Escolha ao menos um perfil que não seja o Geral.");
        try
        {
            File.WriteAllText(caminho, JsonSerializer.Serialize(new ArquivoDePerfis(FormatoDeArquivo, 1, lista), _json));
            return Resultado.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Resultado.Falha<Nada>("Não foi possível gravar o arquivo", ex.Message, "Confira a pasta e as permissões.");
        }
    }

    /// <summary>Importa como perfis novos (nome repetido ganha " (2)"). Nada é gravado se o arquivo for inválido. Devolve quantos entraram.</summary>
    public Resultado<int> Importar(string caminho)
    {
        ArquivoDePerfis? arquivo;
        try
        {
            arquivo = JsonSerializer.Deserialize<ArquivoDePerfis>(File.ReadAllText(caminho), _json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return Resultado.Falha<int>("Não foi possível ler o arquivo de perfis", ex.Message, "Escolha um arquivo exportado pelo Menage.");
        }

        if (arquivo is not { Formato: FormatoDeArquivo, Perfis.Count: > 0 })
            return Resultado.Falha<int>("Este arquivo não é uma exportação de perfis do Menage", null, "Escolha um arquivo exportado pelo Menage.");
        if (arquivo.Versao > 1) return Resultado.Falha<int>("Arquivo de uma versão mais nova do Menage", null, "Atualize o Menage.");
        if (arquivo.Perfis.Any(p => string.IsNullOrWhiteSpace(p.Nome) || p.Permitidas is null || p.Permitidas.Any(r => !RegraValida(r))))
            return Resultado.Falha<int>("O arquivo tem perfil inválido", "Nome vazio ou funcionalidade desconhecida.", "Gere o arquivo de novo.");

        var novos = new List<PerfilDefinido>();
        var nomes = Perfis.Select(p => p.Nome).ToList();
        foreach (var p in arquivo.Perfis)
        {
            var nome = NomeLivre(p.Nome.Trim(), nomes);
            nomes.Add(nome);
            novos.Add(new PerfilDefinido(NovoId(nome, novos.Select(n => n.Id)), nome, p.Descricao ?? "", p.Base, p.Permitidas.Distinct().ToList()));
        }

        var gravou = Gravar(_config with { Perfis = [.. _config.Perfis, .. novos] });
        return gravou.EhOk ? Resultado.Ok(novos.Count) : Resultado.Falha<int>(gravou.Erro);
    }

    /// <summary>Usado pela migração: troca o ativo e as listas de Desenvolvedor/Tester de uma vez.</summary>
    internal Resultado<Nada> Aplicar(string? ativo, IReadOnlyDictionary<string, IReadOnlyList<string>> listas)
    {
        var perfis = _config.Perfis.Select(p => listas.TryGetValue(p.Id, out var lista) && p.Embutido ? p with { Permitidas = lista } : p).ToList();
        var novoAtivo = ativo is not null && (ativo == PerfilDefinido.IdGeral || perfis.Any(p => p.Id == ativo)) ? ativo : _config.Ativo;
        return Gravar(_config with { Perfis = perfis, Ativo = novoAtivo });
    }

    static bool RegraValida(string regra)
    {
        if (regra == "*") return true;
        return Funcionalidades.Existe(regra.StartsWith('-') ? regra[1..] : regra);
    }

    string NovoId(string nome, IEnumerable<string>? reservados = null)
    {
        var letras = new string(nome.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD).Where(char.IsAsciiLetterOrDigit).ToArray());
        var baseId = "p-" + (letras.Length == 0 ? "perfil" : letras[..Math.Min(letras.Length, 24)]);
        var usados = Perfis.Select(p => p.Id).Concat(reservados ?? []).ToHashSet();
        var id = baseId;
        for (var n = 2; usados.Contains(id); n++) id = $"{baseId}-{n}";
        return id;
    }

    string NomeLivre(string nome, IEnumerable<string>? usados = null)
    {
        var existentes = (usados ?? Perfis.Select(p => p.Nome)).ToList();
        var candidato = nome;
        for (var n = 2; existentes.Contains(candidato, StringComparer.OrdinalIgnoreCase); n++) candidato = $"{nome} ({n})";
        return candidato;
    }

    Resultado<Nada> Gravar(ConfigDePerfis nova)
    {
        var gravou = _armazenamento.Salvar(nova);
        if (!gravou.EhOk) return gravou;
        _config = nova;
        Alterado?.Invoke(this, EventArgs.Empty);
        return gravou;
    }

    // Garante Desenvolvedor e Tester no arquivo (apagados à mão voltam ao padrão) e um ativo que exista.
    static ConfigDePerfis Normalizar(ConfigDePerfis lida)
    {
        var perfis = lida.Perfis.Where(p => p.Id != PerfilDefinido.IdGeral).ToList();
        foreach (var padrao in PerfilDefinido.Padroes.Reverse())
            if (perfis.All(p => p.Id != padrao.Id)) perfis.Insert(0, padrao);
        var ativo = lida.Ativo == PerfilDefinido.IdGeral || perfis.Any(p => p.Id == lida.Ativo) ? lida.Ativo : PerfilDefinido.IdGeral;
        return new ConfigDePerfis { Ativo = ativo, Perfis = perfis };
    }
}
