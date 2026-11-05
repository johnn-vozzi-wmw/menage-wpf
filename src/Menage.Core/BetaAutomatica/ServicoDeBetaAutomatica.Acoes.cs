using System.Globalization;

namespace Menage.Core.BetaAutomatica;

/// <summary>Ações do usuário sobre os cartões: ignorar, reverter, remover, limpar, finalizar, reabrir.</summary>
public sealed partial class ServicoDeBetaAutomatica
{
    public void Ignorar(string chave)
    {
        lock (_trava)
        {
            if (Config.Achar(chave) is { } i && i.Estado is not (EstadoDoItem.Gerando or EstadoDoItem.NaFila or EstadoDoItem.Ignorada or EstadoDoItem.Finalizada))
            {
                i.EstadoAntes = i.Estado;
                i.IgnoradaEm = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                i.Estado = EstadoDoItem.Ignorada;
                RegistrarLog($"IGNORADA {chave} (estava \"{ItemBeta.NomeDoEstado(i.EstadoAntes)}\")");
            }
        }

        Salvar();
        Notificar();
    }

    /// <summary>Desfaz o "Ignorar": volta ao estado de antes (sem alertar de novo) e, se já há branch, confere de novo.</summary>
    public async Task ReverterIgnoradaAsync(string chave, CancellationToken cancelar = default)
    {
        ItemBeta? item;
        lock (_trava)
        {
            item = Config.Achar(chave);
            if (item is not { Estado: EstadoDoItem.Ignorada }) return;
            item.Estado = ItemBeta.EstadoValidoParaVoltar(item.EstadoAntes);
            item.IgnoradaEm = "";
            RegistrarLog($"REVERTIDA {chave} (voltou para \"{ItemBeta.NomeDoEstado(item.Estado)}\")");
        }

        await ReconferirAsync(item, cancelar);
    }

    /// <summary>
    /// Tira atividades da lista (menos as que estão na fila ou gerando). A passagem removida fica em "Removidas": a PRÓXIMA consulta de
    /// qualquer tipo a traz de volta, mesmo que seja anterior à referência do monitor (serve como "limpar e buscar de novo").
    /// </summary>
    public int Remover(IEnumerable<string> chaves)
    {
        var n = 0;
        lock (_trava)
        {
            foreach (var chave in chaves.ToList())
            {
                if (Config.Achar(chave) is not { } i || i.Estado is EstadoDoItem.Gerando or EstadoDoItem.NaFila) continue;
                var marca = $"{i.Chave}|{(i.TransicaoEm.Length > 0 ? i.TransicaoEm : "-")}";
                if (!Config.Removidas.Contains(marca)) Config.Removidas.Add(marca);
                Config.Itens.Remove(i);
                n++;
            }
        }

        if (n > 0) RegistrarLog($"REMOVIDAS {n} atividade(s) da lista");
        Salvar();
        Notificar();
        return n;
    }

    public int Remover(string chave) => Remover([chave]);

    /// <summary>Limpa a lista; as finalizadas ficam (são histórico e não voltam).</summary>
    public int Limpar()
    {
        List<string> todas;
        lock (_trava) todas = [.. Config.Itens.Where(i => i.Estado != EstadoDoItem.Finalizada).Select(i => i.Chave)];
        return Remover(todas);
    }

    /// <summary>Botão "Finalizar" (depois da confirmação do usuário).</summary>
    public bool Finalizar(string chave)
    {
        var item = Achar(chave);
        if (item is null || item.Estado is EstadoDoItem.Finalizada or EstadoDoItem.Gerando or EstadoDoItem.NaFila) return false;
        MarcarFinalizada(item, "manual: confirmado pelo usuário");
        Salvar();
        Notificar();
        return true;
    }

    public async Task ReabrirFinalizadaAsync(string chave, CancellationToken cancelar = default)
    {
        ItemBeta? item;
        lock (_trava)
        {
            item = Config.Achar(chave);
            if (item is not { Estado: EstadoDoItem.Finalizada }) return;
            item.Estado = ItemBeta.EstadoValidoParaVoltar(item.EstadoAntes);
            item.FinalizadaEm = item.FinalizadaMotivo = "";
            RegistrarLog($"REABERTA {chave} (voltou de Finalizada para \"{ItemBeta.NomeDoEstado(item.Estado)}\")");
        }

        await ReconferirAsync(item, cancelar);
    }

    /// <summary>Ignorada ou Falhou volta a Aguardando para ser reavaliada.</summary>
    public void Reabrir(string chave)
    {
        lock (_trava)
        {
            if (Config.Achar(chave) is { Estado: EstadoDoItem.Ignorada or EstadoDoItem.Falhou } i)
            {
                i.Estado = EstadoDoItem.Aguardando;
                i.Erro = "";
            }
        }

        Salvar();
        Notificar();
    }

    async Task ReconferirAsync(ItemBeta item, CancellationToken cancelar)
    {
        if (item.Branch.Length > 0 && item.Estado is EstadoDoItem.Pronta or EstadoDoItem.Problema or EstadoDoItem.Aguardando)
            await VerificarAsync(item, false, cancelar);
        Salvar();
        Notificar();
    }
}
