namespace Menage.App.Modulos.OperacoesLote.Formulario;

/// <summary>Um campo do formulário genérico dos diálogos de lote. A <c>Chave</c> identifica o valor na resposta.</summary>
public abstract record CampoDeFormulario(string Chave, string Rotulo);

/// <param name="Obrigatorio">Vazio impede confirmar.</param>
public sealed record CampoTexto(string Chave, string Rotulo, string Valor = "", bool VariasLinhas = false, bool Obrigatorio = false, string Dica = "")
    : CampoDeFormulario(Chave, Rotulo);

public sealed record CampoCaixa(string Chave, string Rotulo, bool Valor = false) : CampoDeFormulario(Chave, Rotulo);

/// <summary>Lista editável de texto livre (combo): sugestões e valor digitado.</summary>
public sealed record CampoEscolha(string Chave, string Rotulo, IReadOnlyList<string> Opcoes, string Valor = "") : CampoDeFormulario(Chave, Rotulo);

/// <summary>Só leitura: linhas de informação (prévia por projeto, avisos).</summary>
public sealed record CampoInformacao(string Chave, string Rotulo, IReadOnlyList<string> Linhas) : CampoDeFormulario(Chave, Rotulo);

/// <summary>Lista com uma caixa por item; a resposta devolve os ids dos marcados.</summary>
public sealed record CampoLista(string Chave, string Rotulo, IReadOnlyList<ItemMarcavel> Itens) : CampoDeFormulario(Chave, Rotulo);

public sealed class ItemMarcavel(string id, string texto, bool marcado)
{
    public string Id { get; } = id;

    public string Texto { get; } = texto;

    public bool Marcado { get; set; } = marcado;
}

public sealed record PedidoDeFormulario(
    string Titulo, string Descricao, IReadOnlyList<CampoDeFormulario> Campos, string Confirmar = "OK", double Largura = 640, double Altura = 540);

/// <summary>O que o usuário preencheu, por chave de campo.</summary>
public sealed class RespostaDeFormulario
{
    public Dictionary<string, string> Textos { get; } = [];

    public Dictionary<string, bool> Caixas { get; } = [];

    public Dictionary<string, IReadOnlyList<string>> Marcados { get; } = [];

    public string Texto(string chave) => Textos.GetValueOrDefault(chave, "").Trim();

    public bool Caixa(string chave) => Caixas.GetValueOrDefault(chave);

    public IReadOnlyList<string> Lista(string chave) => Marcados.GetValueOrDefault(chave, []);
}
