using System.Windows;
using Microsoft.Win32;

namespace Menage.App.Modulos.Personalizacao;

/// <summary>Pede ao usuário uma imagem ou GIF. Fica atrás de interface para o comando poder ser testado sem diálogo.</summary>
public interface ISeletorDeImagem
{
    /// <summary>Caminho escolhido, ou nulo se cancelar.</summary>
    string? Escolher();
}

/// <summary>Seletor do Windows, filtrado pelas extensões que o app aceita; abre sobre a janela ativa.</summary>
public sealed class SeletorDeImagemWindows : ISeletorDeImagem
{
    public const string Filtro = "Imagens e GIF|*.png;*.jpg;*.jpeg;*.bmp;*.ico;*.gif|Todos os arquivos|*.*";

    public string? Escolher()
    {
        var dialogo = new OpenFileDialog { Title = "Escolher ícone", Filter = Filtro, CheckFileExists = true };
        var dono = Application.Current?.Windows.OfType<Window>().FirstOrDefault(j => j.IsActive);
        var ok = dono is null ? dialogo.ShowDialog() : dialogo.ShowDialog(dono);
        return ok == true ? dialogo.FileName : null;
    }
}
