using System.Globalization;
using System.Text;

namespace Menage.Core.BetaAutomatica;

public enum NivelDoConsole { Info, Comando, Saida, Aviso, Erro }

public sealed record RegistroDoConsole(DateTime Quando, NivelDoConsole Nivel, string Origem, string Atividade, string Projeto, string Texto)
{
    public string Linha => string.Join('|', Quando.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), Nivel.ToString().ToUpperInvariant(), Origem, Atividade, Projeto, Texto.Replace("\r", "").Replace("\n", "\\n"));

    public static RegistroDoConsole? Ler(string linha)
    {
        var p = linha.Split('|', 6);
        if (p.Length < 6 || !DateTime.TryParseExact(p[0], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var quando)) return null;
        return Enum.TryParse<NivelDoConsole>(p[1], ignoreCase: true, out var nivel)
            ? new RegistroDoConsole(quando, nivel, p[2], p[3], p[4], p[5].Replace("\\n", "\n"))
            : null;
    }
}

/// <summary>
/// Tudo o que a geração da beta faz (comandos, resultados, fila, verificação), ao vivo. Memória: até 20 mil linhas.
/// Arquivo <c>logs-beta-auto\console-beta.log</c> (data|NIVEL|origem|atividade|projeto|texto); passando de ~6 MB é aparado pela metade.
/// </summary>
public sealed class ConsoleDaBeta
{
    public const int LimiteNaMemoria = 20_000;
    public const long LimiteDoArquivo = 6_000_000;

    readonly object _trava = new();
    readonly List<RegistroDoConsole> _registros = [];
    readonly string? _arquivo;

    public ConsoleDaBeta(PastaDeDados? pasta = null)
    {
        if (pasta is null) return;
        _arquivo = Path.Combine(pasta.Caminho, "logs-beta-auto", "console-beta.log");
        Carregar();
    }

    public string? Arquivo => _arquivo;

    public event Action<RegistroDoConsole>? Registrado;

    public event Action? Limpo;

    public IReadOnlyList<RegistroDoConsole> Registros
    {
        get { lock (_trava) return [.. _registros]; }
    }

    public void Registrar(NivelDoConsole nivel, string origem, string atividade, string projeto, string texto)
    {
        var registro = new RegistroDoConsole(DateTime.Now, nivel, origem, atividade, projeto, texto);
        lock (_trava)
        {
            _registros.Add(registro);
            if (_registros.Count > LimiteNaMemoria) _registros.RemoveRange(0, _registros.Count - LimiteNaMemoria);
            Gravar(registro);
        }

        Registrado?.Invoke(registro);
    }

    /// <summary>Limpa só a tela (memória); o arquivo é o histórico e continua.</summary>
    public void LimparTela()
    {
        lock (_trava) _registros.Clear();
        Limpo?.Invoke();
    }

    /// <summary>Últimas linhas de um arquivo (para mostrar o fim do log do Maven quando falha).</summary>
    public static string Final(string arquivo, int linhas)
    {
        try { return File.Exists(arquivo) ? string.Join(Environment.NewLine, File.ReadLines(arquivo, Encoding.Latin1).TakeLast(linhas)) : ""; }
        catch (IOException) { return ""; }
    }

    void Carregar()
    {
        try
        {
            if (_arquivo is null || !File.Exists(_arquivo)) return;
            var lidos = File.ReadLines(_arquivo, Encoding.UTF8).Select(RegistroDoConsole.Ler).OfType<RegistroDoConsole>();
            _registros.AddRange(lidos.TakeLast(LimiteNaMemoria));
        }
        catch (IOException) { }
    }

    void Gravar(RegistroDoConsole registro)
    {
        if (_arquivo is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_arquivo)!);
            var info = new FileInfo(_arquivo);
            if (info.Exists && info.Length > LimiteDoArquivo)
            {
                var todas = File.ReadAllLines(_arquivo, Encoding.UTF8);
                File.WriteAllLines(_arquivo, todas.Skip(todas.Length / 2), new UTF8Encoding(false));
            }

            File.AppendAllText(_arquivo, registro.Linha + Environment.NewLine, new UTF8Encoding(false));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
