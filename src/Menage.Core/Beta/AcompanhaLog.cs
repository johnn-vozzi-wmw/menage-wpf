using System.Text;
using System.Text.RegularExpressions;

namespace Menage.Core.Beta;

public enum TipoDeLinhaDoLog { Normal, Erro, Sucesso, Etapa, Aviso }

public static partial class ClassificadorDeLinha
{
    public static TipoDeLinhaDoLog Classificar(string linha) =>
        Erro().IsMatch(linha) ? TipoDeLinhaDoLog.Erro
        : linha.Contains("BUILD SUCCESS", StringComparison.Ordinal) || linha.Contains("RESULTADO: SUCESSO", StringComparison.Ordinal) ? TipoDeLinhaDoLog.Sucesso
        : linha.StartsWith("[INFO] --- ", StringComparison.Ordinal) ? TipoDeLinhaDoLog.Etapa
        : linha.StartsWith("[WARNING]", StringComparison.Ordinal) || linha.Contains("[AVISO]", StringComparison.Ordinal) ? TipoDeLinhaDoLog.Aviso
        : TipoDeLinhaDoLog.Normal;

    [GeneratedRegex(@"\[ERROR\]|BUILD FAILURE|FAILURE!|<<< FAIL|RESULTADO: ERRO|Tests run:.*(Failures|Errors): [1-9]")]
    private static partial Regex Erro();
}

/// <summary>
/// Lê só o que foi acrescentado ao log desde a última chamada (o Maven ainda está escrevendo): "Acompanhar execução"
/// sem reler o arquivo inteiro a cada segundo. Linha incompleta no fim espera o próximo ciclo.
/// </summary>
public sealed class AcompanhaLog
{
    long _posicao;
    string _resto = "";

    public string? Caminho { get; private set; }

    public void Abrir(string? caminho)
    {
        Caminho = caminho;
        _posicao = 0;
        _resto = "";
    }

    public IReadOnlyList<string> Novas()
    {
        if (Caminho is null || !File.Exists(Caminho)) return [];
        try
        {
            using var fs = new FileStream(Caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length < _posicao) { _posicao = 0; _resto = ""; } // arquivo recriado
            if (fs.Length == _posicao) return [];
            fs.Seek(_posicao, SeekOrigin.Begin);
            using var leitor = new StreamReader(fs, Encoding.Latin1);
            var texto = _resto + leitor.ReadToEnd();
            _posicao = fs.Length;
            var linhas = texto.Split('\n');
            _resto = linhas[^1];
            return [.. linhas[..^1].Select(l => l.TrimEnd('\r'))];
        }
        catch (IOException)
        {
            return [];
        }
    }
}
