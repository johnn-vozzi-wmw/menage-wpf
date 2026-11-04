namespace Menage.Pocs.Comum;

public sealed class Argumentos(string[] args)
{
    public string? Valor(string nome)
    {
        var i = Array.IndexOf(args, nome);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    public bool Tem(string nome) => Array.IndexOf(args, nome) >= 0;
}
