namespace Menage.Core;

/// <summary>Falha com causa (o que aconteceu), explicação (por quê) e ação sugerida (o que fazer).</summary>
public sealed record Erro(string Causa, string? Explicacao = null, string? AcaoSugerida = null)
{
    public static Erro Cancelado { get; } = new("Cancelado", "A operação foi cancelada antes de terminar.");

    public override string ToString() =>
        string.Join(" ", new[] { Causa, Explicacao, AcaoSugerida }.Where(t => !string.IsNullOrWhiteSpace(t)));
}

/// <summary>Valor "vazio" para operações que só dizem ok ou erro.</summary>
public readonly record struct Nada;

/// <summary>Ok com valor ou erro; os motores devolvem isto em vez de lançar exceção.</summary>
public sealed class Resultado<T>
{
    readonly T? _valor;
    readonly Erro? _erro;

    internal Resultado(T? valor, Erro? erro) => (_valor, _erro) = (valor, erro);

    public bool EhOk => _erro is null;

    public T Valor => _erro is null ? _valor! : throw new InvalidOperationException($"Resultado com erro: {_erro}");

    public Erro Erro => _erro ?? throw new InvalidOperationException("Resultado ok não tem erro.");

    public Resultado<TSaida> Mapear<TSaida>(Func<T, TSaida> f) =>
        _erro is null ? new(f(_valor!), null) : new(default, _erro);

    public Resultado<TSaida> Encadear<TSaida>(Func<T, Resultado<TSaida>> f) =>
        _erro is null ? f(_valor!) : new(default, _erro);

    public T ValorOu(T padrao) => _erro is null ? _valor! : padrao;

    public override string ToString() => _erro is null ? $"Ok({_valor})" : $"Erro({_erro})";
}

public static class Resultado
{
    public static Resultado<T> Ok<T>(T valor) => new(valor, null);

    public static Resultado<Nada> Ok() => new(default, null);

    public static Resultado<T> Falha<T>(Erro erro) => new(default, erro);

    public static Resultado<T> Falha<T>(string causa, string? explicacao = null, string? acao = null) =>
        new(default, new Erro(causa, explicacao, acao));
}
