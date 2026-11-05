namespace Menage.Core.Personalizacao;

/// <summary>
/// Atalhos efetivos: o padrão de cada comando (declarado no catálogo) mais as trocas do usuário.
/// Um gesto só pode pertencer a um comando; trocar para um gesto em uso devolve o conflito, sem alterar nada.
/// </summary>
public sealed class MapaDeAtalhos(IEnumerable<(string Id, string? Padrao)> comandos, IReadOnlyDictionary<string, string>? trocas = null)
{
    readonly Dictionary<string, string?> _padroes = comandos.ToDictionary(c => c.Id, c => c.Padrao, StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, string> _trocas = new(trocas ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);

    /// <summary>Trocas a gravar nas preferências.</summary>
    public IReadOnlyDictionary<string, string> Trocas => _trocas;

    /// <summary>O gesto do comando hoje; nulo se não tem.</summary>
    public string? Efetivo(string id)
    {
        if (_trocas.TryGetValue(id, out var troca)) return troca.Length == 0 ? null : Canonico(troca);
        return _padroes.TryGetValue(id, out var padrao) && padrao is not null ? Canonico(padrao) : null;
    }

    public bool Personalizado(string id) => _trocas.ContainsKey(id);

    /// <summary>Comando dono do gesto (o primeiro na ordem do catálogo, se o padrão repetir).</summary>
    public string? ComandoDe(Gesto gesto) => _padroes.Keys.FirstOrDefault(id => Efetivo(id) == gesto.ToString());

    /// <summary>Comando que já usa o gesto, fora o próprio <paramref name="id"/>.</summary>
    public string? Conflito(string id, Gesto gesto) =>
        _padroes.Keys.FirstOrDefault(outro => !outro.Equals(id, StringComparison.OrdinalIgnoreCase) && Efetivo(outro) == gesto.ToString());

    /// <summary>Define o gesto; texto vazio tira o atalho do comando.</summary>
    public Resultado<Nada> Definir(string id, string? texto)
    {
        if (!_padroes.ContainsKey(id)) return Resultado.Falha<Nada>($"Comando desconhecido: {id}");
        if (string.IsNullOrWhiteSpace(texto))
        {
            _trocas[id] = "";
            return Resultado.Ok();
        }

        var lido = Gesto.Ler(texto);
        if (!lido.EhOk) return Resultado.Falha<Nada>(lido.Erro);
        if (Conflito(id, lido.Valor) is { } dono)
            return Resultado.Falha<Nada>($"{lido.Valor} já é o atalho de \"{dono}\"", null, "Remova o atalho do outro comando ou escolha outro gesto.");
        _trocas[id] = lido.Valor.ToString();
        return Resultado.Ok();
    }

    /// <summary>Volta ao padrão do catálogo (apaga a troca).</summary>
    public void Restaurar(string id) => _trocas.Remove(id);

    public void RestaurarTodos() => _trocas.Clear();

    static string Canonico(string texto) => Gesto.Ler(texto) is { EhOk: true } r ? r.Valor.ToString() : texto;
}
