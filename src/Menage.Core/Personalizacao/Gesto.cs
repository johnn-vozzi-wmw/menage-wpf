namespace Menage.Core.Personalizacao;

/// <summary>Atalho de teclado como texto canônico ("Ctrl+Shift+T", "F5"). Sem WPF: a tela converte a tecla pressionada.</summary>
public readonly record struct Gesto(bool Ctrl, bool Shift, bool Alt, string Tecla)
{
    static readonly Dictionary<string, string> _apelidos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Esc"] = "Esc", ["Escape"] = "Esc", ["Enter"] = "Enter", ["Return"] = "Enter", ["Space"] = "Space", ["Tab"] = "Tab",
        ["Delete"] = "Delete", ["Del"] = "Delete", ["Insert"] = "Insert", ["Home"] = "Home", ["End"] = "End",
        ["PageUp"] = "PageUp", ["PageDown"] = "PageDown", ["Up"] = "Up", ["Down"] = "Down", ["Left"] = "Left", ["Right"] = "Right",
        ["Backspace"] = "Backspace", ["Back"] = "Backspace", [","] = ",", ["."] = ".", ["/"] = "/", ["-"] = "-",
    };

    public override string ToString() =>
        string.Join('+', new[] { Ctrl ? "Ctrl" : null, Shift ? "Shift" : null, Alt ? "Alt" : null, Tecla }.Where(p => p is not null));

    /// <summary>Lê "ctrl+shift+t", "F5", "Ctrl+,". Falha sem tecla, com tecla desconhecida, ou com letra/número sem Ctrl/Alt.</summary>
    public static Resultado<Gesto> Ler(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return Resultado.Falha<Gesto>("Atalho vazio");
        bool ctrl = false, shift = false, alt = false;
        string? tecla = null;
        foreach (var parte in texto.Trim().Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (parte.ToLowerInvariant())
            {
                case "ctrl" or "control": ctrl = true; break;
                case "shift": shift = true; break;
                case "alt": alt = true; break;
                default:
                    if (tecla is not null) return Resultado.Falha<Gesto>($"Atalho com duas teclas: \"{texto}\"");
                    tecla = Normalizar(parte);
                    if (tecla is null) return Resultado.Falha<Gesto>($"Tecla desconhecida: \"{parte}\"");
                    break;
            }
        }

        if (tecla is null) return Resultado.Falha<Gesto>($"O atalho \"{texto}\" não tem tecla", null, "Inclua uma tecla, por exemplo Ctrl+K.");
        var gesto = new Gesto(ctrl, shift, alt, tecla);
        return gesto.Digitavel && !ctrl && !alt
            ? Resultado.Falha<Gesto>($"\"{gesto}\" atrapalharia a digitação", null, "Use Ctrl ou Alt junto.")
            : Resultado.Ok(gesto);
    }

    public static string? Normalizar(string tecla)
    {
        if (_apelidos.TryGetValue(tecla, out var apelido)) return apelido;
        if (tecla.Length == 1 && char.IsLetterOrDigit(tecla[0])) return tecla.ToUpperInvariant();
        return tecla.Length is 2 or 3 && tecla[0] is 'F' or 'f' && int.TryParse(tecla.AsSpan(1), out var n) && n is >= 1 and <= 12 ? "F" + n : null;
    }

    /// <summary>Teclas que escrevem texto: sozinhas atrapalhariam a digitação.</summary>
    bool Digitavel => Tecla.Length == 1 || Tecla is "Space" or "Backspace" or "Delete" or "Enter" or "Tab" or "Insert";
}
