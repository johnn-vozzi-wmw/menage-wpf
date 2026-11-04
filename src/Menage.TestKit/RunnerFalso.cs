namespace Menage.TestKit;

/// <summary>Runner que não cria processos: guarda os comandos e responde com o que o teste definir.</summary>
public sealed class RunnerFalso(Func<ComandoProcesso, SaidaProcesso>? resposta = null) : IProcessoRunner
{
    public List<ComandoProcesso> Comandos { get; } = [];

    public Task<SaidaProcesso> ExecutarAsync(ComandoProcesso comando, CancellationToken cancelar = default)
    {
        Comandos.Add(comando);
        return Task.FromResult(resposta?.Invoke(comando) ?? Saida(0));
    }

    public static SaidaProcesso Saida(int codigo, string texto = "", string erro = "") =>
        new(codigo, System.Text.Encoding.UTF8.GetBytes(texto), System.Text.Encoding.UTF8.GetBytes(erro), TimeSpan.Zero);
}
