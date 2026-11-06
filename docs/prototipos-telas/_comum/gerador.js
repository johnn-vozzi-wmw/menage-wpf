/* Dados e execução simulada do Gerador de Beta (só protótipo; nada roda de verdade). */
(function () {
  const M = window.Menage, G = M.G = {};
  G.projetos = () => [
    ['framework-util', 'Framework', 'VENDAS-22105_develop', 17], ['framework-service', 'Framework', 'VENDAS-22105_develop', 17],
    ['framework-core', 'Framework', 'VENDAS-22105_develop', 17], ['framework-integrator', 'Framework', 'VENDAS-22105_develop', 17],
    ['framework-integrator-tc', 'Framework', 'VENDAS-22105_develop', 17], ['framework-web', 'Framework', 'VENDAS-22105_develop', 17],
    ['framework-tc', 'Framework', 'VENDAS-24803', 8], ['lavenderepda', 'PDA', 'VENDAS-24803-replicacao-histconfirmape', 8],
    ['lavenderecore', 'Web', 'VENDAS-24803-replicacao-histconfirmape', 17], ['lavenderedados', 'Web', 'VENDAS-24803-replicacao-histconfirmape', 17],
    ['lavendereweb', 'Web', 'VENDAS-24803-replicacao-histconfirmape', 17],
  ].map(([nome, grupo, branch, jdk], i) => ({ id: i, nome, grupo, branch, jdk, sel: false, ordem: 0, status: '', ms: 0, resumo: '', log: [] }));
  G.cmd = (p, modo) => `mvn -B clean ${p.nome === 'lavenderecore' ? 'antrun:run ' : ''}${modo} -Duser.language.format=pt -Dfile.encoding=ISO-8859-1`;
  G.tagGrupo = g => ({ Framework: 'int', PDA: 'app', Web: 'web' }[g]);
  G.fmt = ms => { const s = Math.round(ms / 1000); return String(Math.floor(s / 60)).padStart(2, '0') + ':' + String(s % 60).padStart(2, '0'); };
  G.favoritos = () => [{ nome: 'Web completa', modo: 'install', nomes: ['lavenderecore', 'lavenderedados', 'lavendereweb'] }, { nome: 'PDA', modo: 'install', nomes: ['framework-core', 'lavenderepda'] }, { nome: 'Só o web', modo: 'deploy', nomes: ['lavendereweb'] }];
  const linhas = (p, modo) => {
    const falha = p.nome === 'framework-tc';
    const l = [['', '='.repeat(60)], ['', `Projeto : ${p.nome}`], ['', `Comando : ${G.cmd(p, modo)}`], ['', '='.repeat(60)],
      ['i', '[INFO] Scanning for projects...'], ['i', `[INFO] --- maven-clean-plugin:3.1.0:clean (default-clean) @ ${p.nome} ---`],
      ['i', `[INFO] --- maven-compiler-plugin:3.8.1:compile (default-compile) @ ${p.nome} ---`], ['w', '[WARNING] Using platform encoding (Cp1252) to copy filtered resources']];
    if (falha) l.push(['e', '[ERROR] /src/main/java/br/com/wmw/Exemplo.java:[42,13] cannot find symbol'], ['e', '[ERROR] BUILD FAILURE']);
    else l.push(['', '[INFO] Tests run: 48, Failures: 0, Errors: 0, Skipped: 0'], ['i', `[INFO] --- maven-${modo === 'deploy' ? 'deploy' : 'install'}-plugin @ ${p.nome} ---`], ['ok', '[INFO] BUILD SUCCESS']);
    return { l, falha };
  };
  G.linhaHtml = ([t, x]) => `<div class="ln ${t}">${M.esc(x)}</div>`;
  /* Executa em fila; callbacks: ini(p), linha(p, linha), fim(p, ok), tudo(). Retorna cancelar(). */
  G.executar = (fila, modo, cb) => {
    let parar = false, timers = [];
    const w = ms => new Promise(r => timers.push(setTimeout(r, ms)));
    (async () => {
      for (const p of fila) {
        if (parar) break;
        p.status = 'exec'; p.ms = 0; p.log = []; p.resumo = ''; cb.ini?.(p);
        const { l, falha } = linhas(p, modo), t0 = Date.now();
        const tick = setInterval(() => { p.ms = (Date.now() - t0) * 40; cb.tempo?.(p); }, 250); // tempo acelerado ×40
        for (const x of l) { if (parar) break; await w(260 + Math.random() * 220); p.log.push(x); cb.linha?.(p, x); }
        clearInterval(tick);
        if (parar) { p.status = 'cancel'; p.resumo = 'cancelado'; cb.fim?.(p, false); break; }
        p.status = falha ? 'erro' : 'ok'; p.resumo = falha ? 'cannot find symbol (1 erro)' : 'OK - 48 testes (0 falhas, 0 erros)'; cb.fim?.(p, !falha);
        if (falha && cb.paraNoErro) break;
      }
      cb.tudo?.();
    })();
    return () => { parar = true; timers.forEach(clearTimeout); };
  };
  G.statusHtml = p => ({ '': '<span class="fraco">—</span>', fila: '<span class="tag aviso">Na fila</span>', exec: '<span class="tag info"><span class="ponto pulsa" style="--c:var(--azul)"></span>Executando</span>', ok: '<span class="tag ok">✓ Sucesso</span>', erro: '<span class="tag erro">✕ Falhou</span>', cancel: '<span class="tag aviso">Cancelado</span>' }[p.status]);
})();
(function () {
  const G = Menage.G;
  G.sels = ps => ps.filter(p => p.sel).sort((a, b) => a.ordem - b.ordem);
  G.renum = ps => G.sels(ps).forEach((p, i) => p.ordem = i + 1);
  G.alternar = (ps, p, v = !p.sel) => { if (p.sel === v) return; p.sel = v; p.ordem = v ? 1e6 : 0; G.renum(ps); if (!v) p.ordem = 0; };
  G.grupo = (ps, g) => ps.forEach(p => G.alternar(ps, p, g === 'Todos' ? true : g === 'Nenhum' ? false : p.grupo === g));
  G.favorito = (ps, f) => { ps.forEach(p => { p.sel = false; p.ordem = 0; }); f.nomes.forEach(n => { const p = ps.find(x => x.nome === n); p.sel = true; p.ordem = 1e6; G.renum(ps); }); };
})();
