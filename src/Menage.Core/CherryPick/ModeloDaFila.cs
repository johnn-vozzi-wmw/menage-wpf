namespace Menage.Core.CherryPick;

public enum EstadoDoItem { Pendente, Aplicando, Aplicado, Pulado, Conflito, Erro, Desfeito }

public enum EstadoDaFila { Montando, Rodando, Pausada, Parada, Concluida, Abortada }

/// <summary>Um commit de um projeto na fila. Classe mutável porque o motor muda o estado a cada passo e grava.</summary>
public sealed class ItemDaFila
{
    public string Projeto { get; set; } = "";

    public string Caminho { get; set; } = "";

    public string Hash { get; set; } = "";

    public string Curto { get; set; } = "";

    public string Assunto { get; set; } = "";

    public string Autor { get; set; } = "";

    public string Data { get; set; } = "";

    public string Origem { get; set; } = "";

    public string Detalhe { get; set; } = "";

    public string HeadAntes { get; set; } = "";

    public EstadoDoItem Estado { get; set; } = EstadoDoItem.Pendente;

    public List<string> Arquivos { get; set; } = [];

    /// <summary>Aplicado, pulado ou desfeito: não precisa mais rodar.</summary>
    public bool Resolvido => Estado is EstadoDoItem.Aplicado or EstadoDoItem.Pulado or EstadoDoItem.Desfeito;

    public Projeto ComoProjeto() => new(Projeto, Caminho);
}

/// <summary>Fila de cherry-pick: o que aplicar, onde, e o que anotar para poder voltar atrás.</summary>
public sealed class FilaDeCherryPick
{
    public string Destino { get; set; } = "develop";

    public bool Backup { get; set; } = true;

    public bool AtualizarDestino { get; set; } = true;

    public EstadoDaFila Estado { get; set; } = EstadoDaFila.Montando;

    /// <summary>Índice do item em andamento ou do próximo a rodar.</summary>
    public int Atual { get; set; }

    public List<ItemDaFila> Itens { get; set; } = [];

    /// <summary>Caminho do projeto para o HEAD do destino antes da fila.</summary>
    public Dictionary<string, string> HeadInicial { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Caminho do projeto para a branch de backup criada.</summary>
    public Dictionary<string, string> Backups { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public DateTime Criada { get; set; } = DateTime.Now;

    public int Aplicados => Itens.Count(i => i.Estado == EstadoDoItem.Aplicado);

    public int Pulados => Itens.Count(i => i.Estado == EstadoDoItem.Pulado);

    public int Pendentes => Itens.Count(i => !i.Resolvido);

    public ItemDaFila? ItemAtual => Atual >= 0 && Atual < Itens.Count ? Itens[Atual] : null;

    /// <summary>Tem trabalho a fazer: é o que a janela principal oferece retomar ao abrir.</summary>
    public bool Ativa => Itens.Count > 0 && Pendentes > 0 && Estado is not (EstadoDaFila.Abortada or EstadoDaFila.Concluida);

    public void PosicionarNoPrimeiroPendente() => Atual = Math.Max(0, Itens.FindIndex(i => !i.Resolvido));
}
