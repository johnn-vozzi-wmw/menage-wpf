namespace Menage.Core.Interacao;

public enum FaseDoProjeto { Iniciou, Terminou }

/// <summary>Um passo do lote: <c>Indice</c> começa em 1; <c>Situacao</c> só vem preenchida em <c>Terminou</c>.</summary>
public sealed record EventoDeProgresso(
    FaseDoProjeto Fase, int Indice, int Total, string Projeto, Situacao? Situacao = null, string? Mensagem = null);

public interface IProgresso
{
    void Relatar(EventoDeProgresso evento);
}

/// <summary>Descarta o progresso (testes e execuções sem tela).</summary>
public sealed class ProgressoNulo : IProgresso
{
    public void Relatar(EventoDeProgresso evento) { }
}

/// <summary>O que vai acontecer, mostrado antes de confirmar.</summary>
public sealed record Previa(string Titulo, string Mensagem, IReadOnlyList<string> Detalhes);

/// <summary>Único caminho para caixas de diálogo: nada de <c>MessageBox</c> direto.</summary>
public interface IServicoDeDialogos
{
    Task<bool> ConfirmarAsync(Previa previa);

    Task InformarAsync(string titulo, string mensagem);

    Task MostrarErroAsync(Erro erro);

    Task MostrarResumoAsync(IResumoDoLote resumo);
}

/// <summary>
/// Abre janelas pelo id que cada módulo registra, para o Core não conhecer tipos de WPF.
/// </summary>
public interface IServicoDeJanelas
{
    void Abrir(string janelaId, object? parametro = null);

    /// <summary>Abre como diálogo modal e devolve o que a janela entregar ao fechar (nulo se cancelada).</summary>
    Task<object?> AbrirDialogoAsync(string janelaId, object? parametro = null);

    void Fechar(string janelaId);
}
