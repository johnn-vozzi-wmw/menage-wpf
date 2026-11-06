# Lançar uma versão

`lancar-versao.ps1` (raiz do repositório) integra as branches na `main`, valida, gera o instalador e cria tag/Release.

```powershell
.\lancar-versao.ps1 -Simular                      # só mostra o que faria
.\lancar-versao.ps1                               # pergunta versão e branches
.\lancar-versao.ps1 -Versao 1.3.2 -Branches feature/a,feature/b
```

Parâmetros: `-Versao` (omitida: próximo patch da última tag `v*`), `-Branches` (omitido: lista as remotas não mescladas, exceto `tmp/*`; Enter = todas entram), `-Saida` (padrão `C:\mt\rel<versao sem pontos>`, deve estar vazia ou não existir), `-PastaAnterior` (pacotes anteriores copiados para gerar deltas; padrão: a última `C:\mt\rel*`), `-PularTestes`, `-SemPush`, `-Simular`.

Passos: confere main limpa e sincronizada, `git fetch`, merge `--no-ff` de cada branch (conflito: `git merge --abort` e lista dos arquivos), `dotnet build -warnaserror`, suíte completa, **S/N** e push da main, `publicar.ps1`, instalação de teste em `C:\mt\ti<n>` (abre, confere a versão, fecha só esse processo e desinstala), **S/N**, tag `v<versao>` + push e `gh release create` (só se `gh auth status` ok; senão imprime os passos manuais). No fim mostra o caminho do `Setup.exe`.

Notas: só branches remotas entram (dê `git push` nelas antes). Se parar após os merges, eles ficam locais na `main`: `git reset --hard origin/main` desfaz. Nunca toca em `C:\wmw\GMV*`. Rodar sem `-SemPush` só depois de revisar; o script pede confirmação antes do primeiro push.
