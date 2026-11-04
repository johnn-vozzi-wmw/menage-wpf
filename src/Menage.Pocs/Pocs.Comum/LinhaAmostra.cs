using System.Windows.Media;

namespace Menage.Pocs.Comum;

public enum Situacao { Ok, Aviso, Erro, Pendente }

public record Etiqueta(string Texto, Brush Cor);

public record LinhaAmostra(int Id, string Repositorio, string Branch, Situacao Situacao, int Alteracoes, IReadOnlyList<Etiqueta> Etiquetas);

/// <summary>Gera as 200 linhas fixas usadas nas duas telas de teste.</summary>
public sealed class GeradorAmostra
{
    private static readonly (string Texto, string Cor)[] Tags =
    [
        ("beta", "#E8A33D"), ("estavel", "#3FA66B"), ("hotfix", "#D9534F"),
        ("web", "#4A90D9"), ("pda", "#8E6BBF"), ("core", "#2BA3A3")
    ];

    public IReadOnlyList<LinhaAmostra> Gerar(int quantidade = 200)
    {
        var rnd = new Random(42);
        var lista = new List<LinhaAmostra>(quantidade);
        for (var i = 1; i <= quantidade; i++)
        {
            var etiquetas = Enumerable.Range(0, rnd.Next(1, 4))
                .Select(_ => Tags[rnd.Next(Tags.Length)]).Distinct()
                .Select(t => new Etiqueta(t.Texto, Congelado(t.Cor))).ToList();
            lista.Add(new LinhaAmostra(i, $"projeto-{i:000}", $"feature/modulo-{rnd.Next(1, 40)}",
                (Situacao)rnd.Next(4), rnd.Next(0, 120), etiquetas));
        }
        return lista;
    }

    private static Brush Congelado(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}
