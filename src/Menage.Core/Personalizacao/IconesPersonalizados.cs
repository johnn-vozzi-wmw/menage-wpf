namespace Menage.Core.Personalizacao;

/// <summary>Onde um ícone personalizado vale. O ícone do app/janela e o logo do cabeçalho valem sempre.</summary>
public enum AlvoDeIcone { Comando, Grupo, Projeto }

/// <summary>
/// Ícones (imagem ou GIF) escolhidos pelo usuário, em <c>aparencia.json</c>. <see cref="PorTema"/>: caminho por id de tema
/// (a chave <see cref="TodosOsTemas"/> vale para os temas sem ícone próprio). Os mapas de comando, grupo e projeto só
/// valem se a caixa correspondente estiver marcada. Todos os dicionários são trocados por cópias, nunca alterados no lugar.
/// </summary>
public sealed record IconesPersonalizados
{
    public const string TodosOsTemas = "*";

    public const int ZoomMinimo = 50;
    public const int ZoomMaximo = 200;
    public const int ZoomPadrao = 100;

    public Dictionary<string, string> PorTema { get; init; } = [];

    /// <summary>Zoom em % do ícone de cada tema (mesma chave de <see cref="PorTema"/>); ausente = 100%.</summary>
    public Dictionary<string, int> ZoomPorTema { get; init; } = [];

    public bool AplicarNoMenuEPilulas { get; init; }

    public bool AplicarEmGruposEProjetos { get; init; }

    public Dictionary<string, string> Comandos { get; init; } = [];

    public Dictionary<string, string> Grupos { get; init; } = [];

    public Dictionary<string, string> Projetos { get; init; } = [];

    /// <summary>Ícone do app para o tema: o do próprio tema, senão o geral, senão nenhum (vale o "M" padrão).</summary>
    public string? DoApp(string idDoTema) =>
        PorTema.TryGetValue(idDoTema, out var proprio) && proprio.Length > 0 ? proprio
        : PorTema.TryGetValue(TodosOsTemas, out var geral) && geral.Length > 0 ? geral : null;

    /// <summary>Chave de <see cref="PorTema"/> que fornece o ícone do tema: o próprio, senão o geral, senão nulo.</summary>
    public string? OrigemDoApp(string idDoTema) =>
        PorTema.TryGetValue(idDoTema, out var proprio) && proprio.Length > 0 ? idDoTema
        : PorTema.TryGetValue(TodosOsTemas, out var geral) && geral.Length > 0 ? TodosOsTemas : null;

    /// <summary>Zoom (%) do ícone do tema, guardado junto do ícone que vale para ele.</summary>
    public int ZoomDoApp(string idDoTema) =>
        OrigemDoApp(idDoTema) is { } chave && ZoomPorTema.TryGetValue(chave, out var zoom) ? LimitarZoom(zoom) : ZoomPadrao;

    public static int LimitarZoom(int zoom) => Math.Clamp(zoom, ZoomMinimo, ZoomMaximo);

    /// <summary>Define o zoom do tema (limitado a 50-200%); 100% apaga a entrada. Sem ícone próprio, não faz nada.</summary>
    public IconesPersonalizados ComZoomDoApp(string idDoTema, int zoom)
    {
        if (!PorTema.TryGetValue(idDoTema, out var caminho) || caminho.Length == 0) return this;
        var copia = new Dictionary<string, int>(ZoomPorTema);
        var limitado = LimitarZoom(zoom);
        if (limitado == ZoomPadrao) copia.Remove(idDoTema);
        else copia[idDoTema] = limitado;
        return this with { ZoomPorTema = copia };
    }

    /// <summary>Copia o ícone e o zoom do tema <paramref name="origem"/> para todos os outros temas listados (o geral não muda).</summary>
    public IconesPersonalizados UsarEmTodos(string origem, IEnumerable<string> idsDosTemas)
    {
        if (!PorTema.TryGetValue(origem, out var caminho) || caminho.Length == 0) return this;
        var resultado = this;
        var zoom = ZoomPorTema.GetValueOrDefault(origem, ZoomPadrao);
        foreach (var id in idsDosTemas.Where(i => i != origem && i != TodosOsTemas))
            resultado = resultado.ComIconeDoApp(id, caminho).ComZoomDoApp(id, zoom);
        return resultado;
    }

    /// <summary>Ícone de um item, respeitando a caixa de seleção do escopo. Nulo = usar o ícone padrão.</summary>
    public string? De(AlvoDeIcone alvo, string id)
    {
        var vale = alvo == AlvoDeIcone.Comando ? AplicarNoMenuEPilulas : AplicarEmGruposEProjetos;
        if (!vale) return null;
        var mapa = alvo switch { AlvoDeIcone.Comando => Comandos, AlvoDeIcone.Grupo => Grupos, _ => Projetos };
        return mapa.TryGetValue(id, out var caminho) && caminho.Length > 0 ? caminho : null;
    }

    /// <summary>Troca o ícone do tema (o zoom fica); sem caminho, remove o ícone e o zoom dele.</summary>
    public IconesPersonalizados ComIconeDoApp(string idDoTema, string? caminho)
    {
        var novo = this with { PorTema = Trocar(PorTema, idDoTema, caminho) };
        if (!string.IsNullOrWhiteSpace(caminho) || !ZoomPorTema.ContainsKey(idDoTema)) return novo;
        var zoom = new Dictionary<string, int>(ZoomPorTema);
        zoom.Remove(idDoTema);
        return novo with { ZoomPorTema = zoom };
    }

    public IconesPersonalizados ComIconeDe(AlvoDeIcone alvo, string id, string? caminho) => alvo switch
    {
        AlvoDeIcone.Comando => this with { Comandos = Trocar(Comandos, id, caminho) },
        AlvoDeIcone.Grupo => this with { Grupos = Trocar(Grupos, id, caminho) },
        _ => this with { Projetos = Trocar(Projetos, id, caminho) },
    };

    static Dictionary<string, string> Trocar(Dictionary<string, string> mapa, string chave, string? caminho)
    {
        var copia = new Dictionary<string, string>(mapa);
        if (string.IsNullOrWhiteSpace(caminho)) copia.Remove(chave);
        else copia[chave] = caminho;
        return copia;
    }

    /// <summary>Extensões aceitas: imagens comuns, ícone do Windows e GIF animado.</summary>
    public static bool ExtensaoValida(string caminho) =>
        Path.GetExtension(caminho).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".ico" or ".gif";

    public static bool EhGif(string caminho) => Path.GetExtension(caminho).Equals(".gif", StringComparison.OrdinalIgnoreCase);
}
