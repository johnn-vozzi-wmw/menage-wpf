namespace Menage.Core.CherryPick;

/// <summary>
/// Dono da fila em memória e no disco (<c>Armazenamento&lt;T&gt;</c>): monta a lista (adicionar, remover, reordenar) e grava a cada
/// mudança. Quem executa é <see cref="FilaDeCherryPickEmLote"/>; quem decide depois de uma pausa é <see cref="ControleDaFila"/>.
/// </summary>
public sealed class ServicoDaFila(IArmazenamento<FilaDeCherryPick> armazenamento)
{
    FilaDeCherryPick? _fila;

    /// <summary>A fila mudou (item, estado ou lista): a tela se atualiza.</summary>
    public event Action? Mudou;

    public FilaDeCherryPick Fila => _fila ??= Ler();

    /// <summary>Há fila pendente de uma sessão anterior (ou desta) para retomar.</summary>
    public bool TemFilaPendente => Fila.Ativa;

    public bool Rodando => Fila.Estado == EstadoDaFila.Rodando;

    /// <summary>Adiciona sem repetir (mesmo projeto e mesmo commit ainda não resolvido). Devolve quantos entraram; -1 se a fila está rodando.</summary>
    public int Adicionar(IEnumerable<ItemDaFila> novos)
    {
        var fila = Fila;
        if (fila.Estado == EstadoDaFila.Rodando) return -1;
        if (fila.Estado is EstadoDaFila.Concluida or EstadoDaFila.Abortada)
            (fila.Itens, fila.HeadInicial, fila.Backups, fila.Estado, fila.Atual) = ([], new(StringComparer.OrdinalIgnoreCase), new(StringComparer.OrdinalIgnoreCase), EstadoDaFila.Montando, 0);

        var entrou = 0;
        foreach (var item in novos)
        {
            if (fila.Itens.Any(x => string.Equals(x.Caminho, item.Caminho, StringComparison.OrdinalIgnoreCase) && x.Hash == item.Hash && !x.Resolvido)) continue;
            fila.Itens.Add(item);
            entrou++;
        }

        if (fila.Estado == EstadoDaFila.Montando) fila.PosicionarNoPrimeiroPendente();
        Salvar();
        return entrou;
    }

    /// <summary>Só itens pendentes saem da lista.</summary>
    public bool Remover(ItemDaFila item)
    {
        if (item.Estado != EstadoDoItem.Pendente || Rodando) return false;
        var posicao = Fila.Itens.IndexOf(item);
        if (posicao < 0) return false;
        Fila.Itens.RemoveAt(posicao);
        Reposicionar();
        Salvar();
        return true;
    }

    /// <summary>Troca com o vizinho pendente (<paramref name="direcao"/> -1 sobe, +1 desce).</summary>
    public bool Mover(ItemDaFila item, int direcao)
    {
        var itens = Fila.Itens;
        var de = itens.IndexOf(item);
        if (de < 0 || item.Estado != EstadoDoItem.Pendente || Rodando) return false;
        var para = de + direcao;
        while (para >= 0 && para < itens.Count && itens[para].Estado != EstadoDoItem.Pendente) para += direcao;
        if (para < 0 || para >= itens.Count) return false;
        (itens[de], itens[para]) = (itens[para], itens[de]);
        Reposicionar();
        Salvar();
        return true;
    }

    public void Limpar()
    {
        _fila = new FilaDeCherryPick { Destino = Fila.Destino, Backup = Fila.Backup, AtualizarDestino = Fila.AtualizarDestino };
        Salvar();
    }

    public void Configurar(string destino, bool backup, bool atualizarDestino)
    {
        (Fila.Destino, Fila.Backup, Fila.AtualizarDestino) = (destino.Trim(), backup, atualizarDestino);
        Salvar();
    }

    /// <summary>Grava e avisa a tela. Chamado pelo motor a cada passo.</summary>
    public void Salvar()
    {
        armazenamento.Salvar(Fila);
        Mudou?.Invoke();
    }

    void Reposicionar()
    {
        if (Fila.Estado == EstadoDaFila.Montando) Fila.PosicionarNoPrimeiroPendente();
        else if (Fila.Atual > Fila.Itens.Count) Fila.Atual = Fila.Itens.Count;
    }

    // Programa fechado no meio: "rodando" vira "parada" e o item "aplicando" volta a pendente (se já estava no destino, vira pulado).
    FilaDeCherryPick Ler()
    {
        var lida = armazenamento.Carregar();
        if (!lida.EhOk) return new FilaDeCherryPick();
        var fila = lida.Valor;
        foreach (var item in fila.Itens.Where(i => i.Estado == EstadoDoItem.Aplicando)) item.Estado = EstadoDoItem.Pendente;
        fila.Itens.RemoveAll(i => i.Hash.Length == 0 || i.Caminho.Length == 0);
        fila.HeadInicial = new(fila.HeadInicial, StringComparer.OrdinalIgnoreCase);
        fila.Backups = new(fila.Backups, StringComparer.OrdinalIgnoreCase);
        if (fila.Estado == EstadoDaFila.Rodando) fila.Estado = EstadoDaFila.Parada;
        if (fila.Atual < 0 || fila.Atual > fila.Itens.Count) fila.Atual = 0;
        return fila;
    }
}
