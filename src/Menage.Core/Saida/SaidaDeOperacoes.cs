namespace Menage.Core.Saida;

public enum TipoDeLinha { Normal, Titulo, Ok, Aviso, Erro, Comando, Fraco }

public sealed record LinhaDeSaida(DateTime Hora, string Texto, TipoDeLinha Tipo);

/// <summary>
/// Texto da Saída (console). Fica no Core para qualquer operação escrever sem conhecer a tela; a tela só escuta
/// <see cref="Adicionada"/> e <see cref="Limpa"/>. Pode ser usada de qualquer thread.
/// </summary>
public sealed class SaidaDeOperacoes(TimeProvider? relogio = null)
{
    readonly object _trava = new();
    readonly List<LinhaDeSaida> _linhas = [];
    readonly TimeProvider _relogio = relogio ?? TimeProvider.System;

    public event Action<LinhaDeSaida>? Adicionada;

    public event Action? Limpa;

    public IReadOnlyList<LinhaDeSaida> Linhas
    {
        get { lock (_trava) return [.. _linhas]; }
    }

    public void Escrever(string texto, TipoDeLinha tipo = TipoDeLinha.Normal)
    {
        foreach (var parte in texto.Replace("\r\n", "\n").Split('\n'))
        {
            var linha = new LinhaDeSaida(_relogio.GetLocalNow().DateTime, parte, tipo);
            lock (_trava) _linhas.Add(linha);
            Adicionada?.Invoke(linha);
        }
    }

    public void Limpar()
    {
        lock (_trava) _linhas.Clear();
        Limpa?.Invoke();
    }

    public string Texto() => string.Join(Environment.NewLine, Linhas.Select(l => l.Texto));
}
